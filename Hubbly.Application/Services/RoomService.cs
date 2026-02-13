using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Hubbly.Application.Services;

public class RoomService : IRoomService
{
    private readonly ILogger<RoomService> _logger;
    private readonly RoomServiceOptions _options;
    private readonly object _roomLock = new object();

    private static readonly ConcurrentDictionary<Guid, ChatRoom> _rooms = new();
    private static readonly ConcurrentDictionary<Guid, Guid> _userRoomMap = new();

    public RoomService(ILogger<RoomService> logger, IOptions<RoomServiceOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        InitializeFirstRoom();
    }

    #region Инициализация

    private void InitializeFirstRoom()
    {
        if (!_rooms.IsEmpty) return;

        lock (_roomLock)
        {
            if (!_rooms.IsEmpty) return;

            var firstRoom = new ChatRoom("Общая комната #1", _options.DefaultMaxUsers);
            if (_rooms.TryAdd(firstRoom.Id, firstRoom))
            {
                _logger.LogInformation("Initialized first room: {RoomName}", firstRoom.Name);
            }
        }
    }

    #endregion

    #region Публичные методы

    public Task<ChatRoom> GetOrCreateRoomForGuestAsync()
    {
        lock (_roomLock)
        {
            // 1. Ищем активную комнату с местом
            var bestRoom = _rooms.Values
                .Where(r => r.IsActive && !r.IsMarkedForDeletion && r.CurrentUsers < r.MaxUsers)
                .OrderByDescending(r => r.CurrentUsers)
                .FirstOrDefault();

            if (bestRoom != null)
            {
                _logger.LogDebug("Found existing room: {RoomName} ({CurrentUsers}/{MaxUsers})",
                    bestRoom.Name, bestRoom.CurrentUsers, bestRoom.MaxUsers);
                return Task.FromResult(bestRoom);
            }

            // 2. Проверка на максимальное количество комнат
            if (_rooms.Count >= _options.MaxTotalRooms)
            {
                var leastBusy = _rooms.Values
                    .Where(r => r.IsActive)
                    .OrderBy(r => r.CurrentUsers)
                    .FirstOrDefault();

                if (leastBusy != null)
                {
                    _logger.LogDebug("Using least busy room: {RoomName}", leastBusy.Name);
                    return Task.FromResult(leastBusy);
                }
            }

            // 3. Проверка на пустые комнаты
            var emptyRooms = _rooms.Values.Count(r => r.IsEmpty);
            if (emptyRooms >= _options.MaxEmptyRooms)
            {
                var oldestEmpty = _rooms.Values
                    .Where(r => r.IsEmpty)
                    .OrderBy(r => r.LastActiveAt)
                    .First();

                oldestEmpty.UserJoined();
                _logger.LogDebug("Reusing empty room: {RoomName}", oldestEmpty.Name);
                return Task.FromResult(oldestEmpty);
            }

            // 4. Создаем новую комнату
            return Task.FromResult(CreateNewRoom());
        }
    }

    public Task AssignGuestToRoomAsync(Guid userId, Guid roomId)
    {
        lock (_roomLock)
        {
            // Удаляем из старой комнаты
            if (_userRoomMap.TryRemove(userId, out var oldRoomId))
            {
                if (_rooms.TryGetValue(oldRoomId, out var oldRoom))
                {
                    oldRoom.UserLeft();
                    _logger.LogDebug("User {UserId} left room {RoomName}", userId, oldRoom.Name);
                }
            }

            // Добавляем в новую комнату
            if (!_rooms.TryGetValue(roomId, out var newRoom))
            {
                throw new InvalidOperationException($"Room {roomId} not found");
            }

            if (newRoom.CurrentUsers >= newRoom.MaxUsers)
            {
                throw new InvalidOperationException($"Room {newRoom.Name} is full");
            }

            if (!_userRoomMap.TryAdd(userId, roomId))
            {
                throw new InvalidOperationException($"User {userId} already in a room");
            }

            newRoom.UserJoined();
            _logger.LogDebug("User {UserId} joined room {RoomName}", userId, newRoom.Name);

            return Task.CompletedTask;
        }
    }

    public Task RemoveUserFromRoomAsync(Guid userId)
    {
        lock (_roomLock)
        {
            if (_userRoomMap.TryRemove(userId, out var roomId))
            {
                if (_rooms.TryGetValue(roomId, out var room))
                {
                    room.UserLeft();
                    _logger.LogDebug("User {UserId} left room {RoomName}", userId, room.Name);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<ChatRoom?> GetRoomByUserIdAsync(Guid userId)
    {
        if (_userRoomMap.TryGetValue(userId, out var roomId))
        {
            _rooms.TryGetValue(roomId, out var room);
            return Task.FromResult(room);
        }
        return Task.FromResult<ChatRoom?>(null);
    }

    public Task CleanupEmptyRoomsAsync(TimeSpan emptyThreshold)
    {
        var roomsToRemove = new List<Guid>();
        var now = DateTimeOffset.UtcNow;

        lock (_roomLock)
        {
            foreach (var room in _rooms.Values)
            {
                if (room.IsEmpty &&
                    room.LastActiveAt.HasValue &&
                    now - room.LastActiveAt.Value > emptyThreshold &&
                    room != _rooms.Values.FirstOrDefault()) // Не удаляем первую комнату
                {
                    roomsToRemove.Add(room.Id);
                }
            }

            foreach (var roomId in roomsToRemove)
            {
                if (_rooms.TryRemove(roomId, out var removedRoom))
                {
                    _logger.LogInformation("Removed empty room: {RoomName}", removedRoom.Name);
                }
            }
        }

        if (roomsToRemove.Any())
        {
            _logger.LogInformation("Active rooms: {RoomCount}, Total users: {TotalUsers}",
                _rooms.Count, _rooms.Values.Sum(r => r.CurrentUsers));
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Приватные методы

    private ChatRoom CreateNewRoom()
    {
        var roomNumber = _rooms.Count + 1;
        var newRoom = new ChatRoom($"Общая комната #{roomNumber}", _options.DefaultMaxUsers);

        if (_rooms.TryAdd(newRoom.Id, newRoom))
        {
            _logger.LogInformation("Created new room: {RoomName}", newRoom.Name);
            return newRoom;
        }

        // Если не удалось добавить (очень редкий случай), пробуем рекурсивно
        _logger.LogWarning("Failed to add new room, retrying...");
        return GetOrCreateRoomForGuestAsync().GetAwaiter().GetResult();
    }

    #endregion
}

public class RoomServiceOptions
{
    public int MaxTotalRooms { get; set; } = 100;
    public int MaxEmptyRooms { get; set; } = 10;
    public int DefaultMaxUsers { get; set; } = 50;
}
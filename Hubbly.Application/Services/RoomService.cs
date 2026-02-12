using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Hubbly.Application.Services;

public class RoomService : IRoomService
{
    private readonly ILogger<RoomService> _logger;

    private static readonly ConcurrentDictionary<Guid, ChatRoom> _rooms = new();
    private static readonly ConcurrentDictionary<Guid, Guid> _userRoomMap = new();
    
    private const int MAX_TOTAL_ROOMS = 100;      // Не больше 100 комнат всего
    private const int MAX_EMPTY_ROOMS = 10;       // Не больше 10 пустых комнат

    public RoomService(ILogger<RoomService> logger)
    {
        _logger = logger;

        if (_rooms.IsEmpty)
        {
            var firstRoom = new ChatRoom("Общая комната #1");
            if (_rooms.TryAdd(firstRoom.Id, firstRoom))
            {
                _logger.LogInformation("[ROOMS] Initialized {RoomName}", firstRoom.Name);
            }
        }
    }

    public Task<ChatRoom> GetOrCreateRoomForGuestAsync()
    {
        // 1. Ищем активную комнату с местом
        var bestRoom = _rooms.Values
            .Where(r => r.IsActive && !r.IsMarkedForDeletion && r.CurrentUsers < r.MaxUsers)
            .OrderByDescending(r => r.CurrentUsers)
            .FirstOrDefault();

        if (bestRoom != null)
            return Task.FromResult(bestRoom);

        // 2. Проверка на максимальное количество комнат
        if (_rooms.Count >= MAX_TOTAL_ROOMS)
        {
            var leastBusy = _rooms.Values
                .Where(r => r.IsActive)
                .OrderBy(r => r.CurrentUsers)
                .FirstOrDefault();

            if (leastBusy != null)
                return Task.FromResult(leastBusy);
        }

        // 3. Проверка на пустые комнаты
        var emptyRooms = _rooms.Values.Count(r => r.IsEmpty);
        if (emptyRooms >= MAX_EMPTY_ROOMS)
        {
            var oldestEmpty = _rooms.Values
                .Where(r => r.IsEmpty)
                .OrderBy(r => r.LastActiveAt)
                .First();

            oldestEmpty.UserJoined();
            return Task.FromResult(oldestEmpty);
        }

        // 4. Создаем новую комнату
        var roomNumber = _rooms.Count + 1;
        var newRoom = new ChatRoom($"Общая комната #{roomNumber}");
        
        if (_rooms.TryAdd(newRoom.Id, newRoom))
        {
            _logger.LogDebug("[ROOMS] Created new room: {RoomName}", newRoom.Name);
            return Task.FromResult(newRoom);
        }
        else
        {
            // Если не удалось добавить (очень редкий случай), рекурсивно пробуем снова
            return GetOrCreateRoomForGuestAsync();
        }
    }

    public Task AssignGuestToRoomAsync(Guid userId, Guid roomId)
    {
        // 1. Удаляем из старой комнаты
        if (_userRoomMap.TryRemove(userId, out var oldRoomId))
        {
            if (_rooms.TryGetValue(oldRoomId, out var oldRoom))
            {
                oldRoom.UserLeft();
                _logger.LogDebug("[ROOMS] User {UserId} LEFT room {RoomName}",
                    userId, oldRoom.Name);
            }
        }

        // 2. Добавляем в новую комнату
        if (_rooms.TryGetValue(roomId, out var newRoom))
        {
            _userRoomMap[userId] = roomId;
            newRoom.UserJoined();
            _logger.LogDebug("[ROOMS] User {UserId} JOINED room {RoomName}",
                userId, newRoom.Name);
        }

        return Task.CompletedTask;
    }

    public Task RemoveUserFromRoomAsync(Guid userId)
    {
        if (_userRoomMap.TryRemove(userId, out var roomId))
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                room.UserLeft();
                _logger.LogDebug("[ROOMS] User {UserId} LEFT room {RoomName}",
                    userId, room.Name);
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
        var now = DateTimeOffset.UtcNow;
        var roomsToRemove = _rooms.Values
            .Where(r =>
                r.IsEmpty &&
                r.LastActiveAt.HasValue &&
                now - r.LastActiveAt.Value > emptyThreshold &&
                r != _rooms.Values.FirstOrDefault()) // Не удаляем первую комнату
            .ToList();

        foreach (var room in roomsToRemove)
        {
            if (_rooms.TryRemove(room.Id, out var removedRoom))
            {
                _logger.LogInformation("[CLEANUP] Removed empty room: {RoomName}",
                    removedRoom.Name);
            }
        }

        if (roomsToRemove.Any())
        {
            _logger.LogInformation("[CLEANUP] Active rooms: {RoomCount}, Total users: {TotalUsers}",
                _rooms.Count, _rooms.Values.Sum(r => r.CurrentUsers));
        }

        return Task.CompletedTask;
    }
}

using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Threading;

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

    #region Initialization

    private void InitializeFirstRoom()
    {
        if (!_rooms.IsEmpty) return;

        lock (_roomLock)
        {
            if (!_rooms.IsEmpty) return;

            var firstRoom = new ChatRoom("General room #1", _options.DefaultMaxUsers);
            if (_rooms.TryAdd(firstRoom.Id, firstRoom))
            {
                _logger.LogInformation("Initialized first room: {RoomName}", firstRoom.Name);
            }
        }
    }

    #endregion

    #region Public methods

    public Task<ChatRoom> GetOrCreateRoomForGuestAsync()
    {
        lock (_roomLock)
        {
            // 1. Search for active room with space
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

            // 2. Check for maximum number of rooms
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

            // 3. Check for empty rooms
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

            // 4. Create new room
            return Task.FromResult(CreateNewRoom());
        }
    }

    public Task AssignGuestToRoomAsync(Guid userId, Guid roomId)
    {
        lock (_roomLock)
        {
            // Remove from old room
            if (_userRoomMap.TryRemove(userId, out var oldRoomId))
            {
                if (_rooms.TryGetValue(oldRoomId, out var oldRoom))
                {
                    oldRoom.UserLeft();
                    _logger.LogDebug("User {UserId} left room {RoomName}", userId, oldRoom.Name);
                }
            }

            // Add to new room
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
                    room != _rooms.Values.FirstOrDefault()) // Don't delete the first room
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

    public Task<int> GetActiveRoomsCountAsync()
    {
        lock (_roomLock)
        {
            var activeCount = _rooms.Values.Count(r => r.IsActive);
            return Task.FromResult(activeCount);
        }
    }

    #endregion

    #region Private methods

    private ChatRoom CreateNewRoom()
    {
        const int maxAttempts = 3;
        int attempts = 0;

        while (attempts < maxAttempts)
        {
            var roomNumber = _rooms.Count + 1;
            var newRoom = new ChatRoom($"General room #{roomNumber}", _options.DefaultMaxUsers);

            if (_rooms.TryAdd(newRoom.Id, newRoom))
            {
                _logger.LogInformation("Created new room: {RoomName}", newRoom.Name);
                return newRoom;
            }

            attempts++;
            _logger.LogWarning("Failed to add new room (attempt {Attempt}/{MaxAttempts})", attempts, maxAttempts);
            Thread.SpinWait(1000); // Small delay before retry
        }

        _logger.LogError("Failed to create new room after {MaxAttempts} attempts", maxAttempts);
        throw new InvalidOperationException($"Failed to create new room after {maxAttempts} attempts");
    }

    #endregion
}

public class RoomServiceOptions
{
    public int MaxTotalRooms { get; set; } = 100;
    public int MaxEmptyRooms { get; set; } = 10;
    public int DefaultMaxUsers { get; set; } = 50;
}
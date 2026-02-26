using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Hubbly.Api.Services;

/// <summary>
/// Manages user presence, connection tracking, and room assignment logic.
/// Extracted from ChatHub to follow Single Responsibility Principle.
/// </summary>
public class PresenceService : IPresenceService
{
    private readonly IRoomRepository _roomRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRoomService _roomService;
    private readonly ILogger<PresenceService> _logger;

    public PresenceService(
        IRoomRepository roomRepository,
        IUserRepository userRepository,
        IRoomService roomService,
        ILogger<PresenceService> logger)
    {
        _roomRepository = roomRepository ?? throw new ArgumentNullException(nameof(roomRepository));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _roomService = roomService ?? throw new ArgumentNullException(nameof(roomService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles a new user connection: assigns room, tracks connection, and prepares presence data.
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <param name="connectionId">The SignalR connection ID (string)</param>
    /// <returns>
    /// Tuple of (assigned room, user profile, list of existing users in room)
    /// </returns>
    public async Task<(ChatRoom room, User user, List<User> existingUsers)> HandleUserConnectedAsync(
        Guid userId, string connectionId)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["UserId"] = userId,
            ["ConnectionId"] = connectionId
        });

        _logger.LogDebug("PresenceService: Handling user connection");

        // Clean up old connections first
        await CleanupExistingConnectionsAsync(userId);

        // Get user
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            _logger.LogError("User not found! UserId: {UserId}", userId);
            throw new KeyNotFoundException($"User {userId} not found");
        }

        _logger.LogDebug("User {Nickname} (ID: {UserId}) authenticated", user.Nickname, userId);

        // Determine room assignment
        var room = await DetermineAndAssignRoomAsync(userId, user);

        // Track connection in Redis
        _logger.LogDebug("Tracking connection {ConnectionId} for user {UserId} in room {RoomId}",
            connectionId, userId, room.Id);
        await _roomRepository.TrackConnectionAsync(connectionId, userId, room.Id);

        // Get existing users in the room (excluding self)
        var onlineUserIds = await _roomRepository.GetOnlineUserIdsInRoomAsync(room.Id);
        var otherUserIds = onlineUserIds.Where(uid => uid != userId).ToList();
        var existingUsers = await _userRepository.GetByIdsAsync(otherUserIds);

        _logger.LogDebug("User {Nickname} assigned to room {RoomName} (ID: {RoomId}) with {ExistingUsers} other users",
            user.Nickname, room.Name, room.Id, existingUsers.Count());

        return (room, user, existingUsers.ToList());
    }

    /// <summary>
    /// Handles user disconnection: removes connection, leaves room, and notifies if last connection.
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <param name="connectionId">The SignalR connection ID</param>
    /// <returns>
    /// True if user completely disconnected (no remaining connections), false otherwise
    /// </returns>
    public async Task<bool> HandleUserDisconnectedAsync(Guid userId, string connectionId)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["UserId"] = userId,
            ["ConnectionId"] = connectionId
        });

        _logger.LogDebug("PresenceService: Handling user disconnection");

        // Connection already removed from Redis by caller (OnDisconnectedAsync)
        // Just handle room leaving and notifications

        var room = await _roomService.GetRoomByUserIdAsync(userId);
        if (room == null)
        {
            _logger.LogWarning("User {UserId} disconnected but not in any room", userId);
            return false;
        }

        await _roomService.RemoveUserFromRoomAsync(userId);

        // Check if user has any other active connections
        var userConnectionIds = await _roomRepository.GetConnectionIdsByUserIdAsync(userId);

        _logger.LogDebug("User {UserId} has {Count} remaining connections after disconnect",
            userId, userConnectionIds.Count());

        // Return true if this was the user's last connection
        return !userConnectionIds.Any();
    }

    /// <summary>
    /// Gets the room assignment data for a user.
    /// </summary>
    public async Task<RoomAssignmentData> GetRoomAssignmentAsync(ChatRoom room)
    {
        var userCount = await _roomRepository.GetUserCountAsync(room.Id);
        return new RoomAssignmentData
        {
            RoomId = room.Id,
            RoomName = room.Name,
            UsersInRoom = userCount,
            MaxUsers = room.MaxUsers
        };
    }

    private async Task<ChatRoom> DetermineAndAssignRoomAsync(Guid userId, User user)
    {
        ChatRoom room;

        // For authenticated users with a valid last room, try to return to it
        if (user.LastRoomId.HasValue)
        {
            _logger.LogDebug("User {UserId} has LastRoomId: {LastRoomId}, attempting to rejoin",
                userId, user.LastRoomId.Value);
            var lastRoom = await _roomRepository.GetByIdAsync(user.LastRoomId.Value);
            var lastRoomUserCount = lastRoom != null ? await _roomRepository.GetUserCountAsync(lastRoom.Id) : 0;

            if (lastRoom != null && lastRoomUserCount < lastRoom.MaxUsers)
            {
                // Rejoin the last room
                await _roomService.JoinRoomAsync(lastRoom.Id, userId);
                room = lastRoom;
                _logger.LogInformation("User {Nickname} returned to last room: {RoomName} (ID: {RoomId}, Users: {Current}/{Max})",
                    user.Nickname, room.Id, room.Name, lastRoomUserCount + 1, room.MaxUsers);
            }
            else
            {
                // Last room invalid or full, get system room
                room = await _roomService.GetOrCreateRoomForGuestAsync();
                await _roomService.JoinRoomAsync(room.Id, userId);
                _logger.LogInformation("User {Nickname} assigned to new system room (last room full/invalid): {RoomName} (ID: {RoomId})",
                    user.Nickname, room.Name, room.Id);
            }
        }
        else
        {
            // No last room (first time), get system room
            room = await _roomService.GetOrCreateRoomForGuestAsync();
            await _roomService.JoinRoomAsync(room.Id, userId);
            _logger.LogInformation("User {Nickname} assigned to system room (first time): {RoomName} (ID: {RoomId})",
                user.Nickname, room.Name, room.Id);
        }

        return room;
    }

    private async Task CleanupExistingConnectionsAsync(Guid userId)
    {
        _logger.LogDebug("Cleaning up existing connections for user {UserId}", userId);

        var existingConnectionIds = await _roomRepository.GetConnectionIdsByUserIdAsync(userId);

        if (existingConnectionIds.Any())
        {
            _logger.LogInformation("User {UserId} has {Count} existing connections, cleaning up: {ConnectionIds}",
                userId, existingConnectionIds.Count(), string.Join(", ", existingConnectionIds));

            foreach (var connectionId in existingConnectionIds)
            {
                _logger.LogDebug("Removing old connection {ConnectionId} for user {UserId}", connectionId, userId);
                await _roomRepository.RemoveConnectionAsync(connectionId);
            }

            _logger.LogInformation("Cleaned up all existing connections for user {UserId}", userId);
        }
        else
        {
            _logger.LogDebug("No existing connections found for user {UserId}", userId);
        }
    }
}

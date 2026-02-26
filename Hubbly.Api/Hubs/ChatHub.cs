using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;

namespace Hubbly.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IChatService _chatService;
    private readonly IUserService _userService;
    private readonly IUserRepository _userRepository;
    private readonly IRoomService _roomService;
    private readonly IRoomRepository _roomRepository;
    private readonly ILogger<ChatHub> _logger;
    private readonly IMemoryCache _nonceCache;

    private static readonly TimeSpan NonceLifetime = TimeSpan.FromMinutes(2);

    public ChatHub(
        IChatService chatService,
        IUserService userService,
        IUserRepository userRepository,
        IRoomService roomService,
        IRoomRepository roomRepository,
        ILogger<ChatHub> logger,
        IMemoryCache nonceCache)
    {
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _roomService = roomService ?? throw new ArgumentNullException(nameof(roomService));
        _roomRepository = roomRepository ?? throw new ArgumentNullException(nameof(roomRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nonceCache = nonceCache ?? throw new ArgumentNullException(nameof(nonceCache));
    }

    #region Connection/Disconnection

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;

        using (_logger.BeginScope(new Dictionary<string, object> { ["ConnectionId"] = connectionId }))
        {
            _logger.LogDebug("ChatHub.OnConnectedAsync started");

            if (!await ValidateConnectionAsync())
                return;

            var userIdClaim = Context.User!.FindFirst("userId")!;
            var userId = Guid.Parse(userIdClaim.Value);

            try
            {
                // Clean up old connection if exists
                await CleanupExistingConnectionAsync(userId);

                // Get user
                var user = await _userRepository.GetByIdAsync(userId);
                if (user == null)
                {
                    _logger.LogError("User not found! UserId: {UserId}", userId);
                    Context.Abort();
                    return;
                }

                // Determine room assignment
                ChatRoom room;
                
                // For authenticated users with a valid last room, try to return to it
                if (user.LastRoomId.HasValue)
                {
                    var lastRoom = await _roomRepository.GetByIdAsync(user.LastRoomId.Value);
                    var lastRoomUserCount = lastRoom != null ? await _roomRepository.GetUserCountAsync(lastRoom.Id) : 0;
                    if (lastRoom != null && lastRoomUserCount < lastRoom.MaxUsers)
                    {
                        // Rejoin the last room
                        await _roomService.JoinRoomAsync(lastRoom.Id, userId);
                        room = lastRoom;
                        _logger.LogDebug("User returned to last room: {RoomName}", room.Name);
                    }
                    else
                    {
                        // Last room invalid, get system room and join it
                        room = await _roomService.GetOrCreateRoomForGuestAsync();
                        await _roomService.JoinRoomAsync(room.Id, userId);
                        _logger.LogDebug("Assigned user to new system room: {RoomName}", room.Name);
                    }
                }
                else
                {
                    // No last room (first time), get system room and join it
                    room = await _roomService.GetOrCreateRoomForGuestAsync();
                    await _roomService.JoinRoomAsync(room.Id, userId);
                    _logger.LogDebug("Assigned user to system room: {RoomName}", room.Name);
                }

                // Update LastRoomId for the user (already updated in JoinRoomAsync/AssignGuestToRoomAsync)

                // Track connection in Redis (replaces _connectedUsers)
                await _roomRepository.TrackConnectionAsync(Guid.Parse(connectionId), userId, room.Id);
                
                await Groups.AddToGroupAsync(connectionId, room.Id.ToString());

                // Send room information
                await SendRoomAssignmentAsync(room);

                // Send list of existing users (from Redis/DB, not just this instance)
                await SendExistingUsersAsync(room.Id, userId);

                // Notify others about new user
                await NotifyUserJoinedAsync(userId, user.Nickname, user.AvatarConfigJson, room.Id);

                var userCount = await _roomRepository.GetUserCountAsync(room.Id);
                _logger.LogInformation("User {Nickname} (ID: {UserId}) connected to room {RoomName} ({Users}/{Max})",
                    user.Nickname, userId, room.Name, userCount, room.MaxUsers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnConnectedAsync for user {UserId}", userId);
                Context.Abort();
                return;
            }

            await base.OnConnectedAsync();
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;

        using (_logger.BeginScope(new Dictionary<string, object> { ["ConnectionId"] = connectionId }))
        {
            _logger.LogDebug(exception, "ChatHub.OnDisconnectedAsync");

            var userIdClaim = Context.User?.FindFirst("userId");
            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                // Remove connection from Redis first
                await _roomRepository.RemoveConnectionAsync(Guid.Parse(connectionId));
                
                // Then handle disconnect logic (notify others, leave room)
                await HandleDisconnectAsync(userId, connectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }

    #endregion

    #region Message handlers

    public async Task SendMessage(string? content, string? actionType = null, long? timestamp = null, string? nonce = null)
    {
        var userId = Guid.Parse(Context.User!.FindFirst("userId")!.Value);

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["UserId"] = userId,
            ["ContentLength"] = content?.Length ?? 0
        }))
        {
            _logger.LogDebug("SendMessage called");

            // Validation
            if (!timestamp.HasValue)
            {
                _logger.LogWarning("Missing timestamp");
                await Clients.Caller.SendAsync("ReceiveError", "Missing timestamp");
                return;
            }

            if (string.IsNullOrEmpty(nonce) || !IsNonceValid(nonce, timestamp.Value))
            {
                _logger.LogWarning("Invalid message token");
                await Clients.Caller.SendAsync("ReceiveError", "Invalid message token");
                return;
            }

            if (string.IsNullOrEmpty(content))
            {
                _logger.LogWarning("Empty message content");
                await Clients.Caller.SendAsync("ReceiveError", "Message cannot be empty");
                return;
            }

            if (content.Length > 500)
            {
                _logger.LogWarning("Message too long: {Length}", content.Length);
                await Clients.Caller.SendAsync("ReceiveError", "Message too long");
                return;
            }

            try
            {
                var room = await _roomService.GetRoomByUserIdAsync(userId);
                if (room == null)
                {
                    _logger.LogWarning("User not in a room");
                    await Clients.Caller.SendAsync("ReceiveError", "You are not in a room");
                    return;
                }

                var messageDto = await _chatService.SendMessageAsync(userId, content, actionType);
                await Clients.Group(room.Id.ToString()).SendAsync("ReceiveMessage", messageDto);

                _logger.LogInformation("Message sent by {Sender} in {RoomName}",
                    messageDto.SenderNickname, room.Name);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogError(ex, "User not found");
                await Clients.Caller.SendAsync("ReceiveError", "User not found");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid message content");
                await Clients.Caller.SendAsync("ReceiveError", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendMessage error for user {UserId}", userId);
                await Clients.Caller.SendAsync("ReceiveError", "Failed to send message");
            }
        }
    }

    public async Task UserTyping()
    {
        var userId = Guid.Parse(Context.User!.FindFirst("userId")!.Value);

        try
        {
            var room = await _roomService.GetRoomByUserIdAsync(userId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("ReceiveError", "You are not in a room");
                return;
            }

            var user = await _userService.GetUserProfileAsync(userId);
            await Clients.OthersInGroup(room.Id.ToString()).SendAsync("UserTyping", new UserTypingData
            {
                UserId = userId.ToString(),
                Nickname = user.Nickname
            });

            _logger.LogTrace("User {Nickname} is typing", user.Nickname);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UserTyping error for user {UserId}", userId);
        }
    }

    public async Task SendAnimation(string animationType, string? targetUserId = null)
    {
        var callerUserId = Guid.Parse(Context.User!.FindFirst("userId")!.Value);
        var targetUserIdToSend = string.IsNullOrEmpty(targetUserId) ? callerUserId.ToString() : targetUserId;

        try
        {
            var room = await _roomService.GetRoomByUserIdAsync(callerUserId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("ReceiveError", "You are not in a room");
                return;
            }

            // Send to ALL users in the room (including caller)
            await Clients.Group(room.Id.ToString()).SendAsync("UserPlayAnimation",
                targetUserIdToSend, animationType);

            _logger.LogInformation("Animation {Animation} sent by user {CallerUserId} for target {TargetUserId}",
                animationType, callerUserId, targetUserIdToSend);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendAnimation error for user {UserId}", callerUserId);
            await Clients.Caller.SendAsync("ReceiveError", "Failed to send animation");
        }
    }

    public async Task<int> GetOnlineCount()
    {
        var count = await _roomRepository.GetTotalOnlineCountAsync();
        _logger.LogDebug("Online count requested: {Count}", count);
        return count;
    }

    #endregion

    #region Validation

    private Task<bool> ValidateConnectionAsync()
    {
        if (Context.User?.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning("User not authenticated on connect");
            Context.Abort();
            return Task.FromResult(false);
        }

        var userIdClaim = Context.User.FindFirst("userId");
        if (userIdClaim == null)
        {
            _logger.LogWarning("userId claim not found on connect");
            Context.Abort();
            return Task.FromResult(false);
        }

        if (!Guid.TryParse(userIdClaim.Value, out _))
        {
            _logger.LogWarning("Invalid userId format: {UserId}", userIdClaim.Value);
            Context.Abort();
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    #endregion

    #region Private methods

    private async Task CleanupExistingConnectionAsync(Guid userId)
    {
        // Get all connection IDs for this user from Redis
        var existingConnectionIds = await _roomRepository.GetConnectionIdsByUserIdAsync(userId);
        
        if (existingConnectionIds.Any())
        {
            _logger.LogInformation("User {UserId} has {Count} existing connections, cleaning up",
                userId, existingConnectionIds.Count());
            
            // Remove all old connections from Redis
            foreach (var connectionId in existingConnectionIds)
            {
                await _roomRepository.RemoveConnectionAsync(connectionId);
            }
        }
    }

    private async Task SendRoomAssignmentAsync(ChatRoom room)
    {
        var userCount = await _roomRepository.GetUserCountAsync(room.Id);
        await Clients.Caller.SendAsync("AssignedToRoom", new RoomAssignmentData
        {
            RoomId = room.Id,
            RoomName = room.Name,
            UsersInRoom = userCount,
            MaxUsers = room.MaxUsers
        });

        _logger.LogDebug("Sent room assignment: {RoomName}", room.Name);
    }

    private async Task SendExistingUsersAsync(Guid roomId, Guid currentUserId)
    {
        // Get all online user IDs in the room from Redis/DB
        var onlineUserIds = await _roomRepository.GetOnlineUserIdsInRoomAsync(roomId);
        
        // Exclude current user
        var otherUserIds = onlineUserIds.Where(uid => uid != currentUserId).ToList();
        
        if (otherUserIds.Any())
        {
            // Get user profiles from repository
            var users = await _userRepository.GetByIdsAsync(otherUserIds);
            
            var existingUsers = users.Select(u => new UserJoinedData
            {
                UserId = u.Id.ToString(),
                Nickname = u.Nickname,
                AvatarConfigJson = u.AvatarConfigJson,
                JoinedAt = DateTimeOffset.UtcNow // We don't store connection time in DB, use current time
            }).ToList();

            await Clients.Caller.SendAsync("ReceiveInitialPresence", existingUsers);
            _logger.LogDebug("Sent {Count} existing users to new connection", existingUsers.Count);
        }
    }

    private async Task NotifyUserJoinedAsync(Guid userId, string nickname, string avatarConfigJson, Guid roomId)
    {
        await Clients.OthersInGroup(roomId.ToString()).SendAsync("UserJoined", new UserJoinedData
        {
            UserId = userId.ToString(),
            Nickname = nickname,
            AvatarConfigJson = avatarConfigJson,
            JoinedAt = DateTimeOffset.UtcNow
        });
    }

    private async Task HandleDisconnectAsync(Guid userId, string connectionId)
    {
        try
        {
            // Connection already removed from Redis in OnDisconnectedAsync
            // Just handle room leaving and notifications

            var room = await _roomService.GetRoomByUserIdAsync(userId);
            await _roomService.RemoveUserFromRoomAsync(userId);

            if (room != null)
            {
                // Check if user has any other active connections
                var userConnectionIds = await _roomRepository.GetConnectionIdsByUserIdAsync(userId);
                
                // Only send UserLeft if this was the user's last connection overall
                if (!userConnectionIds.Any())
                {
                    // Need to get nickname from database since we don't store it in memory
                    var user = await _userRepository.GetByIdAsync(userId);
                    var nickname = user?.Nickname ?? "User";

                    await Clients.OthersInGroup(room.Id.ToString()).SendAsync("UserLeft", new UserLeftData
                    {
                        UserId = userId.ToString(),
                        Nickname = nickname,
                        LeftAt = DateTimeOffset.UtcNow
                    });
                }

                _logger.LogInformation("User {UserId} left room {RoomName}", userId, room.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling disconnect for user {UserId}", userId);
        }
    }

    private bool IsNonceValid(string nonce, long clientTimestamp)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timeDiff = Math.Abs(now - clientTimestamp);

        if (timeDiff > 300) // Increased from 30s to 5 minutes to account for client-server clock drift
        {
            _logger.LogWarning("Nonce rejected: time diff {TimeDiff}s", timeDiff);
            return false;
        }

        var cacheKey = $"nonce_{nonce}";
        if (_nonceCache.TryGetValue(cacheKey, out _))
        {
            _logger.LogWarning("Nonce rejected: already used");
            return false;
        }

        _nonceCache.Set(cacheKey, true, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = NonceLifetime,
            Size = 1
        });
        return true;
    }

    #endregion
}
using Hubbly.Api.Services;
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
    private readonly IPresenceService _presenceService;
    private readonly IMessageValidationService _messageValidationService;

    public ChatHub(
        IChatService chatService,
        IUserService userService,
        IUserRepository userRepository,
        IRoomService roomService,
        IRoomRepository roomRepository,
        ILogger<ChatHub> logger,
        IPresenceService presenceService,
        IMessageValidationService messageValidationService)
    {
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _roomService = roomService ?? throw new ArgumentNullException(nameof(roomService));
        _roomRepository = roomRepository ?? throw new ArgumentNullException(nameof(roomRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _presenceService = presenceService ?? throw new ArgumentNullException(nameof(presenceService));
        _messageValidationService = messageValidationService ?? throw new ArgumentNullException(nameof(messageValidationService));
    }

    #region Connection/Disconnection

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;

        using (_logger.BeginScope(new Dictionary<string, object> { ["ConnectionId"] = connectionId }))
        {
            _logger.LogInformation("ChatHub.OnConnectedAsync started for connection {ConnectionId}", connectionId);

            // Validate connection (authentication and userId claim)
            if (Context.User?.Identity?.IsAuthenticated != true)
            {
                _logger.LogWarning("User not authenticated on connect");
                Context.Abort();
                return;
            }

            var userIdClaim = Context.User!.FindFirst("userId")!;
            if (!Guid.TryParse(userIdClaim.Value, out var userId))
            {
                _logger.LogWarning("Invalid userId format: {UserId}", userIdClaim.Value);
                Context.Abort();
                return;
            }
            
            _logger.LogDebug("User {UserId} attempting to connect", userId);

            try
            {
                // Use PresenceService to handle connection logic
                var result = await _presenceService.HandleUserConnectedAsync(userId, connectionId);
                ChatRoom room = result.room;
                User user = result.user;
                List<User> existingUsers = result.existingUsers;
                
                // Add connection to SignalR group
                await Groups.AddToGroupAsync(connectionId, room.Id.ToString());
                
                // Send room assignment to the newly connected user
                var roomAssignment = await _presenceService.GetRoomAssignmentAsync(room);
                await Clients.Caller.SendAsync("AssignedToRoom", roomAssignment);
                
                // Send list of existing users in the room
                var existingUsersData = existingUsers.Select(u => new UserJoinedData
                {
                    UserId = u.Id.ToString(),
                    Nickname = u.Nickname,
                    AvatarConfigJson = u.AvatarConfigJson,
                    JoinedAt = DateTimeOffset.UtcNow
                }).ToList();
                await Clients.Caller.SendAsync("ReceiveInitialPresence", existingUsersData);
                
                // Notify others about the new user
                await Clients.OthersInGroup(room.Id.ToString()).SendAsync("UserJoined", new UserJoinedData
                {
                    UserId = userId.ToString(),
                    Nickname = user.Nickname,
                    AvatarConfigJson = user.AvatarConfigJson,
                    JoinedAt = DateTimeOffset.UtcNow
                });

                var userCount = await _roomRepository.GetUserCountAsync(room.Id);
                _logger.LogInformation("User {Nickname} (ID: {UserId}) successfully connected to room {RoomName} (ID: {RoomId}, Users: {Users}/{Max})",
                    user.Nickname, userId, room.Name, room.Id, userCount, room.MaxUsers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnConnectedAsync for user {UserId}", userId);
                Context.Abort();
                return;
            }

            await base.OnConnectedAsync();
            _logger.LogDebug("OnConnectedAsync completed for connection {ConnectionId}", connectionId);
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
                await _roomRepository.RemoveConnectionAsync(connectionId);
                
                // Use PresenceService to handle disconnect logic
                var wasLastConnection = await _presenceService.HandleUserDisconnectedAsync(userId, connectionId);
                
                // If this was the last connection, notify others
                if (wasLastConnection)
                {
                    var room = await _roomService.GetRoomByUserIdAsync(userId);
                    if (room != null)
                    {
                        var user = await _userRepository.GetByIdAsync(userId);
                        var nickname = user?.Nickname ?? "User";
                        await Clients.OthersInGroup(room.Id.ToString()).SendAsync("UserLeft", new UserLeftData
                        {
                            UserId = userId.ToString(),
                            Nickname = nickname,
                            LeftAt = DateTimeOffset.UtcNow
                        });
                    }
                }
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

            // Validate message using validation service
            var validation = _messageValidationService.ValidateMessage(content, timestamp, nonce);
            if (!validation.isValid)
            {
                await Clients.Caller.SendAsync("ReceiveError", validation.errorMessage);
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

    #region Private methods (removed - moved to services)

    // CleanupExistingConnectionAsync -> moved to PresenceService
    // SendRoomAssignmentAsync -> moved to PresenceService
    // SendExistingUsersAsync -> moved to PresenceService
    // NotifyUserJoinedAsync -> inlined in ChatHub
    // HandleDisconnectAsync -> moved to PresenceService
    // IsNonceValid -> moved to MessageValidationService

    #endregion
}
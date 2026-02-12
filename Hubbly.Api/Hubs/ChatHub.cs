using Hubbly.Domain.Dtos;
using Hubbly.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace Hubbly.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IChatService _chatService;
    private readonly IUserService _userService;
    private readonly IUserRepository _userRepository;
    private readonly IRoomService _roomService;
    private readonly ILogger<ChatHub> _logger;

    private static readonly ConcurrentDictionary<string, ConnectedUser> _connectedUsers = new();
    private static readonly HashSet<string> _usedNonces = new();
    private static readonly object _nonceLock = new();

    public ChatHub(
        IChatService chatService,
        IUserService userService,
        IUserRepository userRepository,
        IRoomService roomService,
        ILogger<ChatHub> logger)
    {
        _chatService = chatService;
        _userService = userService;
        _userRepository = userRepository;
        _roomService = roomService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        _logger.LogDebug("ChatHub.OnConnectedAsync: ConnectionId: {ConnectionId}", connectionId);
        
        if (Context.User?.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning("ChatHub: User not authenticated on connect! ConnectionId: {ConnectionId}", connectionId);
            Context.Abort();
            return;
        }
        
        var userIdClaim = Context.User.FindFirst("userId");
        if (userIdClaim == null)
        {
            _logger.LogWarning("ChatHub: userId claim not found on connect! ConnectionId: {ConnectionId}", connectionId);
            Context.Abort();
            return;
        }

        if (!Guid.TryParse(userIdClaim.Value, out var userId))
        {
            _logger.LogWarning("ChatHub: Invalid userId format: {UserId}", userIdClaim.Value);
            Context.Abort();
            return;
        }

        _logger.LogInformation("ChatHub: User {UserId} connecting with ConnectionId: {ConnectionId}",
            userId, connectionId);
        
        if (_connectedUsers.ContainsKey(userId.ToString()))
        {
            _logger.LogInformation("ChatHub: User {UserId} already connected, cleaning up old connection", userId);
            await HandleDisconnectAsync(userId);
        }
        
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            _logger.LogError("ChatHub: User not found! UserId: {UserId}", userId);
            Context.Abort();
            return;
        }
        
        var existingRoom = await _roomService.GetRoomByUserIdAsync(userId);
        if (existingRoom != null)
        {
            _logger.LogInformation("ChatHub: User {UserId} already in room {RoomName}, cleaning up...",
                userId, existingRoom.Name);
            await _roomService.RemoveUserFromRoomAsync(userId);
        }
        
        var room = await _roomService.GetOrCreateRoomForGuestAsync();
        await _roomService.AssignGuestToRoomAsync(userId, room.Id);
        
        var connectedUser = new ConnectedUser
        {
            UserId = userId,
            Nickname = user.Nickname,
            AvatarConfigJson = user.AvatarConfigJson,
            ConnectionId = connectionId,
            ConnectedAt = DateTimeOffset.UtcNow,
            RoomId = room.Id,
            RoomName = room.Name
        };

        _connectedUsers[userId.ToString()] = connectedUser;
        
        await Groups.AddToGroupAsync(connectionId, room.Id.ToString());
        
        await Clients.Caller.SendAsync("AssignedToRoom", new RoomAssignmentData
        {
            RoomId = room.Id,
            RoomName = room.Name,
            UsersInRoom = room.CurrentUsers,
            MaxUsers = room.MaxUsers
        });

        _logger.LogInformation("✅ User {Nickname} (ID: {UserId}) connected to room {RoomName} ({Users}/{Max})",
            user.Nickname, userId, room.Name, room.CurrentUsers, room.MaxUsers);
        
        var existingUsers = _connectedUsers.Values
            .Where(u => u.RoomId == room.Id && u.UserId != userId)
            .Select(u => new UserJoinedData
            {
                UserId = u.UserId.ToString(),
                Nickname = u.Nickname,
                AvatarConfigJson = u.AvatarConfigJson,
                JoinedAt = u.ConnectedAt
            })
            .ToList();

        if (existingUsers.Any())
        {
            await Clients.Caller.SendAsync("ReceiveInitialPresence", existingUsers);
            _logger.LogDebug("Sent {Count} existing users to {Nickname}", existingUsers.Count, user.Nickname);
        }
        
        await Clients.OthersInGroup(room.Id.ToString()).SendAsync("UserJoined", new UserJoinedData
        {
            UserId = userId.ToString(),
            Nickname = user.Nickname,
            AvatarConfigJson = user.AvatarConfigJson,
            JoinedAt = DateTimeOffset.UtcNow
        });

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        _logger.LogDebug("ChatHub.OnDisconnectedAsync: ConnectionId: {ConnectionId}", connectionId);

        var userIdClaim = Context.User?.FindFirst("userId");
        if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
        {
            await HandleDisconnectAsync(userId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task HandleDisconnectAsync(Guid userId)
    {
        try
        {
            _connectedUsers.TryRemove(userId.ToString(), out var disconnectedUser);
            
            var room = await _roomService.GetRoomByUserIdAsync(userId);
            
            await _roomService.RemoveUserFromRoomAsync(userId);
            
            if (room != null && disconnectedUser != null)
            {
                await Clients.OthersInGroup(room.Id.ToString()).SendAsync("UserLeft", new UserLeftData
                {
                    UserId = userId.ToString(),
                    LeftAt = DateTimeOffset.UtcNow
                });

                _logger.LogInformation("👋 User {UserId} left room {RoomName}", userId, room.Name);
            }
            
            if (!string.IsNullOrEmpty(Context.ConnectionId) && room != null)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, room.Id.ToString());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling disconnect for user {UserId}", userId);
        }
    }

    public async Task SendMessage(string content, string? actionType = null, long? timestamp = null, string? nonce = null)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        if (!timestamp.HasValue || Math.Abs(now - timestamp.Value) > 30)
        {
            await Clients.Caller.SendAsync("ReceiveError", "Message expired");
            return;
        }
        
        if (string.IsNullOrEmpty(nonce) || !IsNonceValid(nonce))
        {
            await Clients.Caller.SendAsync("ReceiveError", "Invalid message token");
            return;
        }

        var userId = Guid.Parse(Context.User!.FindFirst("userId")!.Value);

        try
        {
            var room = await _roomService.GetRoomByUserIdAsync(userId);
            if (room == null)
            {
                await Clients.Caller.SendAsync("ReceiveError", "You are not in a room");
                return;
            }

            _logger.LogDebug("SendMessage called by user {UserId}. Content: '{Content}', ActionType: '{ActionType}'",
                userId, content, actionType ?? "null");

            var messageDto = await _chatService.SendMessageAsync(userId, content, actionType);

            await Clients.Group(room.Id.ToString()).SendAsync("ReceiveMessage", messageDto);

            _logger.LogInformation("💬 Message sent by {Sender} in {RoomName}",
                messageDto.SenderNickname, room.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendMessage error for user {UserId}", userId);
            await Clients.Caller.SendAsync("ReceiveError", "Failed to send message");
        }
    }

    public async Task UserTyping()
    {
        var userId = Guid.Parse(Context.User!.FindFirst("userId")!.Value);

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

        _logger.LogTrace("✏️ User {Nickname} is typing", user.Nickname);
    }

    public async Task SendAnimation(string animationType)
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

            var messageDto = await _chatService.SendMessageAsync(
                userId,
                $"[{animationType.ToUpper()}]",
                animationType
            );

            await Clients.Group(room.Id.ToString()).SendAsync("ReceiveMessage", messageDto);

            _logger.LogInformation("🎭 Animation {Animation} sent by user {UserId}", animationType, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendAnimation error for user {UserId}", userId);
            await Clients.Caller.SendAsync("ReceiveError", "Failed to send animation");
        }
    }

    public Task<int> GetOnlineCount()
    {
        return Task.FromResult(_connectedUsers.Count);
    }

    private bool IsNonceValid(string nonce)
    {
        lock (_nonceLock)
        {
            if (_usedNonces.Contains(nonce))
                return false;

            _usedNonces.Add(nonce);
        }

        // Очистка через 5 минут
        _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
        {
            lock (_nonceLock)
            {
                _usedNonces.Remove(nonce);
            }
        });

        return true;
    }
}

/// <summary>
/// Модель подключенного пользователя для внутреннего использования
/// </summary>
public class ConnectedUser
{
    public Guid UserId { get; set; }
    public string Nickname { get; set; } = null!;
    public string AvatarConfigJson { get; set; } = null!;
    public string ConnectionId { get; set; } = null!;
    public DateTimeOffset ConnectedAt { get; set; }
    public Guid RoomId { get; set; }
    public string RoomName { get; set; } = null!;
}
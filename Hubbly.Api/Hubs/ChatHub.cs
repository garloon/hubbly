using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace Hubbly.Api.Hubs;

[Authorize]
public partial class ChatHub : Hub
{
    private readonly IChatService _chatService;
    private readonly IUserService _userService;
    private readonly IUserRepository _userRepository;
    private readonly IRoomService _roomService;
    private readonly ILogger<ChatHub> _logger;
    private readonly IMemoryCache _nonceCache;

    private static readonly ConcurrentDictionary<string, ConnectedUser> _connectedUsers = new();
    private static readonly TimeSpan NonceLifetime = TimeSpan.FromMinutes(5);

    public ChatHub(
        IChatService chatService,
        IUserService userService,
        IUserRepository userRepository,
        IRoomService roomService,
        ILogger<ChatHub> logger,
        IMemoryCache nonceCache)
    {
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _roomService = roomService ?? throw new ArgumentNullException(nameof(roomService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nonceCache = nonceCache ?? throw new ArgumentNullException(nameof(nonceCache));
    }

    #region Подключение/Отключение

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
                // Очищаем старое соединение если есть
                await CleanupExistingConnectionAsync(userId);

                // Получаем пользователя
                var user = await _userRepository.GetByIdAsync(userId);
                if (user == null)
                {
                    _logger.LogError("User not found! UserId: {UserId}", userId);
                    Context.Abort();
                    return;
                }

                // Получаем или создаем комнату
                var room = await _roomService.GetOrCreateRoomForGuestAsync();
                await _roomService.AssignGuestToRoomAsync(userId, room.Id);

                // Сохраняем информацию о подключенном пользователе
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

                // Отправляем информацию о комнате
                await SendRoomAssignmentAsync(room);

                // Отправляем список существующих пользователей
                await SendExistingUsersAsync(room.Id, userId);

                // Уведомляем других о новом пользователе
                await NotifyUserJoinedAsync(userId, user.Nickname, user.AvatarConfigJson, room.Id);

                _logger.LogInformation("User {Nickname} (ID: {UserId}) connected to room {RoomName} ({Users}/{Max})",
                    user.Nickname, userId, room.Name, room.CurrentUsers, room.MaxUsers);
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
                await HandleDisconnectAsync(userId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }

    #endregion

    #region Валидация

    private async Task<bool> ValidateConnectionAsync()
    {
        if (Context.User?.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning("User not authenticated on connect");
            Context.Abort();
            return false;
        }

        var userIdClaim = Context.User.FindFirst("userId");
        if (userIdClaim == null)
        {
            _logger.LogWarning("userId claim not found on connect");
            Context.Abort();
            return false;
        }

        if (!Guid.TryParse(userIdClaim.Value, out _))
        {
            _logger.LogWarning("Invalid userId format: {UserId}", userIdClaim.Value);
            Context.Abort();
            return false;
        }

        return true;
    }

    #endregion

    #region Приватные методы

    private async Task CleanupExistingConnectionAsync(Guid userId)
    {
        if (_connectedUsers.TryGetValue(userId.ToString(), out var existingUser))
        {
            _logger.LogInformation("User {UserId} already connected, cleaning up old connection", userId);
            await HandleDisconnectAsync(userId);
        }
    }

    private async Task SendRoomAssignmentAsync(ChatRoom room)
    {
        await Clients.Caller.SendAsync("AssignedToRoom", new RoomAssignmentData
        {
            RoomId = room.Id,
            RoomName = room.Name,
            UsersInRoom = room.CurrentUsers,
            MaxUsers = room.MaxUsers
        });

        _logger.LogDebug("Sent room assignment: {RoomName}", room.Name);
    }

    private async Task SendExistingUsersAsync(Guid roomId, Guid currentUserId)
    {
        var existingUsers = _connectedUsers.Values
            .Where(u => u.RoomId == roomId && u.UserId != currentUserId)
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

    private async Task HandleDisconnectAsync(Guid userId)
    {
        try
        {
            _connectedUsers.TryRemove(userId.ToString(), out var disconnectedUser);

            var room = await _roomService.GetRoomByUserIdAsync(userId);
            await _roomService.RemoveUserFromRoomAsync(userId);

            if (room != null)
            {
                await Clients.OthersInGroup(room.Id.ToString()).SendAsync("UserLeft", new UserLeftData
                {
                    UserId = userId.ToString(),
                    LeftAt = DateTimeOffset.UtcNow
                });

                if (!string.IsNullOrEmpty(Context.ConnectionId))
                {
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, room.Id.ToString());
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

        if (timeDiff > 30)
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

        _nonceCache.Set(cacheKey, true, NonceLifetime);
        return true;
    }

    #endregion
}
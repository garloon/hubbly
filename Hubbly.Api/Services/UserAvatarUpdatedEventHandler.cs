using Hubbly.Api.Hubs;
using Hubbly.Domain.Dtos;
using Hubbly.Domain.Events;
using Hubbly.Domain.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Hubbly.Api.Services;

public class UserAvatarUpdatedEventHandler : IDomainEventHandler<UserAvatarUpdatedEvent>
{
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserAvatarUpdatedEventHandler> _logger;

    public UserAvatarUpdatedEventHandler(
        IHubContext<ChatHub> hubContext,
        IUserRepository userRepository,
        ILogger<UserAvatarUpdatedEventHandler> logger)
    {
        _hubContext = hubContext;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task HandleAsync(UserAvatarUpdatedEvent @event, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(@event.UserId);
        var roomId = user?.LastRoomId;

        if (roomId.HasValue)
        {
            var avatarData = new UserAvatarUpdatedData
            {
                UserId = @event.UserId.ToString(),
                Nickname = user.Nickname,
                AvatarConfigJson = @event.AvatarConfigJson
            };

            await _hubContext.Clients.Group(roomId.Value.ToString())
                .SendAsync("UserAvatarUpdated", avatarData, cancellationToken);
        }
    }
}

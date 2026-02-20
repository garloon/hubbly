using Hubbly.Domain.Events;
using Microsoft.Extensions.Logging;

namespace Hubbly.Application.Services;

public class LoggingEventHandler : IDomainEventHandler<UserCreatedEvent>,
                                   IDomainEventHandler<UserNicknameUpdatedEvent>,
                                   IDomainEventHandler<UserAvatarUpdatedEvent>,
                                   IDomainEventHandler<MessageSentEvent>
{
    private readonly ILogger<LoggingEventHandler> _logger;

    public LoggingEventHandler(ILogger<LoggingEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(UserCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "User created: UserId={UserId}, DeviceId={DeviceId}, Nickname={Nickname}",
            @event.UserId, @event.DeviceId, @event.Nickname);
        return Task.CompletedTask;
    }

    public Task HandleAsync(UserNicknameUpdatedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "User nickname updated: UserId={UserId}, OldNickname={OldNickname}, NewNickname={NewNickname}",
            @event.UserId, @event.OldNickname, @event.NewNickname);
        return Task.CompletedTask;
    }

    public Task HandleAsync(UserAvatarUpdatedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "User avatar updated: UserId={UserId}, AvatarConfigLength={ConfigLength}",
            @event.UserId, @event.AvatarConfigJson?.Length ?? 0);
        return Task.CompletedTask;
    }

    public Task HandleAsync(MessageSentEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Message sent: SenderId={SenderId}, Content={Content}, ActionType={ActionType}",
            @event.SenderId, @event.Message.Content, @event.Message.ActionType);
        return Task.CompletedTask;
    }
}

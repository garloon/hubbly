namespace Hubbly.Domain.Events;

public class UserCreatedEvent : DomainEvent
{
    public Guid UserId { get; }
    public string DeviceId { get; }
    public string? Nickname { get; }

    public UserCreatedEvent(Guid userId, string deviceId, string? nickname = null)
    {
        UserId = userId;
        DeviceId = deviceId;
        Nickname = nickname;
    }
}

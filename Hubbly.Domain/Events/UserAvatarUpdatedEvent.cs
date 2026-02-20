namespace Hubbly.Domain.Events;

public class UserAvatarUpdatedEvent : DomainEvent
{
    public Guid UserId { get; }
    public string AvatarConfigJson { get; }

    public UserAvatarUpdatedEvent(Guid userId, string avatarConfigJson)
    {
        UserId = userId;
        AvatarConfigJson = avatarConfigJson;
    }
}

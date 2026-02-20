namespace Hubbly.Domain.Events;

public class UserNicknameUpdatedEvent : DomainEvent
{
    public Guid UserId { get; }
    public string OldNickname { get; }
    public string NewNickname { get; }

    public UserNicknameUpdatedEvent(Guid userId, string oldNickname, string newNickname)
    {
        UserId = userId;
        OldNickname = oldNickname;
        NewNickname = newNickname;
    }
}

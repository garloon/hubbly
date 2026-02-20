using Hubbly.Domain.Dtos;

namespace Hubbly.Domain.Events;

public class MessageSentEvent : DomainEvent
{
    public ChatMessageDto Message { get; }
    public Guid SenderId { get; }

    public MessageSentEvent(Guid senderId, ChatMessageDto message)
    {
        SenderId = senderId;
        Message = message;
    }
}

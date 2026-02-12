namespace Hubbly.Domain.Dtos;

public record ChatMessageDto
{
    public Guid Id { get; init; }
    public Guid SenderId { get; init; }
    public string SenderNickname { get; init; } = null!;
    public string Content { get; init; } = null!;
    public DateTimeOffset SentAt { get; init; }
    public string? ActionType { get; init; }
}

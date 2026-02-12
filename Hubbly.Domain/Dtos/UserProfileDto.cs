namespace Hubbly.Domain.Dtos;

public record UserProfileDto
{
    public Guid Id { get; init; }
    public string Nickname { get; init; } = null!;
    public string AvatarConfigJson { get; init; } = "{}";
    public bool IsGuest { get; init; } = true;
}

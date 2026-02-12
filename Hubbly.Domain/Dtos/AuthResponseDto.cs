namespace Hubbly.Domain.Dtos;

public record AuthResponseDto
{
    public string AccessToken { get; init; } = null!;
    public string RefreshToken { get; init; } = null!;
    public UserProfileDto User { get; init; } = null!;
    public string DeviceId { get; init; } = null!;
}

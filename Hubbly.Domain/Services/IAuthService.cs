using Hubbly.Domain.Dtos;

namespace Hubbly.Domain.Services;

public interface IAuthService
{
    Task<AuthResponseDto> AuthenticateGuestAsync(string deviceId, string nickname, string? avatarConfigJson = null);
    Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, string deviceId);
}

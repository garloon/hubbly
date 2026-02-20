using System.Security.Claims;

namespace Hubbly.Domain.Services;

public interface IJwtTokenService
{
    string GenerateAccessToken(Guid userId, string nickname);
    string GenerateRefreshToken();
    Task<(bool isValid, ClaimsPrincipal? principal)> ValidateAccessTokenAsync(string token);
    Guid? GetUserIdFromToken(string token);
}

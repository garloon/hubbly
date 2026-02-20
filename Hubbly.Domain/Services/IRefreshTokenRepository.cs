using Hubbly.Domain.Entities;

namespace Hubbly.Domain.Services;

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token);
    Task UpdateAsync(RefreshToken token);
    Task<RefreshToken?> GetByTokenAsync(string token);
    Task<RefreshToken?> GetByTokenAndDeviceAsync(string token, string deviceId);
    Task CleanupOldDeviceTokensAsync(Guid userId, string deviceId, int keepLast = 3);
    Task RevokeAllForUserAsync(Guid userId);
    Task RevokeAllForDeviceAsync(Guid userId, string deviceId);
    Task<bool> HasActiveRefreshTokensAsync(Guid userId);
}

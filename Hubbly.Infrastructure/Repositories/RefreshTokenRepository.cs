using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Hubbly.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hubbly.Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<RefreshTokenRepository> _logger;

    public RefreshTokenRepository(
        AppDbContext context,
        ILogger<RefreshTokenRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Public methods

    public async Task AddAsync(RefreshToken token)
    {
        _logger.LogDebug("Adding refresh token for user {UserId}", token.UserId);

        await _context.RefreshTokens.AddAsync(token);
        await _context.SaveChangesAsync();

        _logger.LogTrace("Refresh token added successfully");
    }

    public async Task UpdateAsync(RefreshToken token)
    {
        _logger.LogDebug("Updating refresh token {TokenId} for user {UserId}",
            token.Id, token.UserId);

        _context.RefreshTokens.Update(token);
        await _context.SaveChangesAsync();

        _logger.LogTrace("Refresh token updated successfully");
    }

    public async Task<RefreshToken?> GetByTokenAsync(string token)
    {
        _logger.LogDebug("Getting refresh token by value");

        var refreshToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == token);

        _logger.LogDebug(refreshToken != null
            ? "Refresh token found for user {UserId}"
            : "Refresh token not found",
            refreshToken?.UserId);

        return refreshToken;
    }

    public async Task<RefreshToken?> GetByTokenAndDeviceAsync(string token, string deviceId)
    {
        _logger.LogDebug("Getting refresh token by value and device {DeviceId}", deviceId);

        var refreshToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt =>
                rt.Token == token &&
                rt.DeviceId == deviceId &&
                !rt.IsRevoked);

        _logger.LogDebug(refreshToken != null
            ? "Refresh token found for user {UserId}"
            : "Refresh token not found",
            refreshToken?.UserId);

        return refreshToken;
    }

    public async Task CleanupOldDeviceTokensAsync(Guid userId, string deviceId, int keepLast = 3)
    {
        _logger.LogDebug("Cleaning up old tokens for user {UserId}, device {DeviceId}",
            userId, deviceId);

        var deviceTokens = await _context.RefreshTokens
            .Where(rt =>
                rt.UserId == userId &&
                rt.DeviceId == deviceId &&
                !rt.IsRevoked &&
                rt.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(rt => rt.CreatedAt)
            .ToListAsync();

        if (deviceTokens.Count > keepLast)
        {
            var tokensToRevoke = deviceTokens.Skip(keepLast).ToList();
            foreach (var token in tokensToRevoke)
            {
                token.Revoke();
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Revoked {Count} old tokens for user {UserId}",
                tokensToRevoke.Count, userId);
        }
        else
        {
            _logger.LogTrace("No old tokens to clean up for user {UserId}", userId);
        }
    }

    public async Task RevokeAllForUserAsync(Guid userId)
    {
        _logger.LogInformation("Revoking all tokens for user {UserId}", userId);

        var tokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.Revoke();
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Revoked {Count} tokens for user {UserId}",
            tokens.Count, userId);
    }

    public async Task RevokeAllForDeviceAsync(Guid userId, string deviceId)
    {
        _logger.LogInformation("Revoking all tokens for user {UserId}, device {DeviceId}",
            userId, deviceId);

        var tokens = await _context.RefreshTokens
            .Where(rt =>
                rt.UserId == userId &&
                rt.DeviceId == deviceId &&
                !rt.IsRevoked &&
                rt.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.Revoke();
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Revoked {Count} tokens for user {UserId}",
            tokens.Count, userId);
    }

    public async Task<bool> HasActiveRefreshTokensAsync(Guid userId)
    {
        _logger.LogDebug("Checking if user {UserId} has active refresh tokens", userId);

        var hasActive = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId &&
                        !rt.IsRevoked &&
                        rt.ExpiresAt > DateTimeOffset.UtcNow)
            .AnyAsync();

        _logger.LogDebug("User {UserId} has {HasActive} active refresh tokens", userId, hasActive);
        return hasActive;
    }

    #endregion
}
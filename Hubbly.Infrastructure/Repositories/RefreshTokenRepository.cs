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

    #region Публичные методы

    public async Task AddAsync(RefreshToken token)
    {
        try
        {
            _logger.LogDebug("Adding refresh token for user {UserId}", token.UserId);

            await _context.RefreshTokens.AddAsync(token);
            await _context.SaveChangesAsync();

            _logger.LogTrace("Refresh token added successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding refresh token for user {UserId}", token.UserId);
            throw;
        }
    }

    public async Task UpdateAsync(RefreshToken token)
    {
        try
        {
            _logger.LogDebug("Updating refresh token {TokenId} for user {UserId}",
                token.Id, token.UserId);

            _context.RefreshTokens.Update(token);
            await _context.SaveChangesAsync();

            _logger.LogTrace("Refresh token updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating refresh token {TokenId}", token.Id);
            throw;
        }
    }

    public async Task<RefreshToken?> GetByTokenAsync(string token)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting refresh token by value");
            throw;
        }
    }

    public async Task<RefreshToken?> GetByTokenAndDeviceAsync(string token, string deviceId)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting refresh token by token and device");
            throw;
        }
    }

    public async Task CleanupOldDeviceTokensAsync(Guid userId, string deviceId, int keepLast = 3)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up old tokens for user {UserId}", userId);
            throw;
        }
    }

    public async Task RevokeAllForUserAsync(Guid userId)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking all tokens for user {UserId}", userId);
            throw;
        }
    }

    public async Task RevokeAllForDeviceAsync(Guid userId, string deviceId)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking tokens for user {UserId}, device {DeviceId}",
                userId, deviceId);
            throw;
        }
    }

    #endregion
}
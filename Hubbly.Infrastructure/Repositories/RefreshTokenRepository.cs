using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Hubbly.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Hubbly.Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _context;

    public RefreshTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(RefreshToken token)
    {
        await _context.RefreshTokens.AddAsync(token);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(RefreshToken token)
    {
        _context.RefreshTokens.Update(token);
        await _context.SaveChangesAsync();
    }

    public async Task<RefreshToken?> GetByTokenAsync(string token)
    {
        return await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == token);
    }
    
    public async Task<RefreshToken?> GetByTokenAndDeviceAsync(string token, string deviceId)
    {
        return await _context.RefreshTokens
            .FirstOrDefaultAsync(rt =>
                rt.Token == token &&
                rt.DeviceId == deviceId &&
                !rt.IsRevoked);
    }
    
    public async Task CleanupOldDeviceTokensAsync(Guid userId, string deviceId, int keepLast = 3)
    {
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
            foreach (var token in deviceTokens.Skip(keepLast))
            {
                token.Revoke();
            }
            await _context.SaveChangesAsync();
        }
    }

    public async Task RevokeAllForUserAsync(Guid userId)
    {
        var tokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.Revoke();
        }

        await _context.SaveChangesAsync();
    }

    public async Task RevokeAllForDeviceAsync(Guid userId, string deviceId)
    {
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
    }
}

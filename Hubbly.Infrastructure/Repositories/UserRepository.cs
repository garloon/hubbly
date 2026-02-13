using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Hubbly.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hubbly.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(
        AppDbContext context,
        ILogger<UserRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Публичные методы

    public async Task<User?> GetByDeviceIdAsync(string deviceId)
    {
        try
        {
            _logger.LogDebug("Getting user by DeviceId: {DeviceId}", deviceId);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.DeviceId == deviceId);

            _logger.LogDebug(user != null
                ? "User found: {UserId}"
                : "User not found for DeviceId: {DeviceId}",
                user?.Id, deviceId);

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by DeviceId: {DeviceId}", deviceId);
            throw;
        }
    }

    public async Task<User?> GetByIdAsync(Guid userId)
    {
        try
        {
            _logger.LogDebug("Getting user by Id: {UserId}", userId);

            var user = await _context.Users.FindAsync(userId);

            _logger.LogDebug(user != null
                ? "User found"
                : "User not found: {UserId}", userId);

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by Id: {UserId}", userId);
            throw;
        }
    }

    public async Task<User?> GetByNicknameAsync(string nickname)
    {
        try
        {
            _logger.LogDebug("Getting user by nickname: {Nickname}", nickname);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Nickname == nickname);

            _logger.LogDebug(user != null
                ? "User found: {UserId}"
                : "User not found for nickname: {Nickname}",
                user?.Id, nickname);

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by nickname: {Nickname}", nickname);
            throw;
        }
    }

    public async Task<IEnumerable<User>> GetByIdsAsync(IEnumerable<Guid> userIds)
    {
        try
        {
            var userIdList = userIds.ToList();
            _logger.LogDebug("Getting {Count} users by Ids", userIdList.Count);

            var users = await _context.Users
                .Where(u => userIdList.Contains(u.Id))
                .ToListAsync();

            _logger.LogDebug("Found {Count} users", users.Count);

            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting users by Ids");
            throw;
        }
    }

    public async Task AddAsync(User user)
    {
        try
        {
            _logger.LogInformation("Adding new user: {UserId}, DeviceId: {DeviceId}",
                user.Id, user.DeviceId);

            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();

            _logger.LogDebug("User added successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding user: {UserId}", user.Id);
            throw;
        }
    }

    public async Task UpdateAsync(User user)
    {
        try
        {
            _logger.LogDebug("Updating user: {UserId}", user.Id);

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            _logger.LogTrace("User updated successfully");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex, "Concurrency error updating user: {UserId}", user.Id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user: {UserId}", user.Id);
            throw;
        }
    }

    #endregion
}
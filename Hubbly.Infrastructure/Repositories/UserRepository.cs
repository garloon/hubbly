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

    #region Public methods

    public async Task<User?> GetByDeviceIdAsync(string deviceId)
    {
        _logger.LogDebug("Getting user by DeviceId: {DeviceId}", deviceId);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.DeviceId == deviceId);

        _logger.LogDebug(user != null
            ? "User found: {UserId}"
            : "User not found for DeviceId: {DeviceId}",
            user?.Id, deviceId);

        return user;
    }

    public async Task<User?> GetByIdAsync(Guid userId)
    {
        _logger.LogDebug("Getting user by Id: {UserId}", userId);

        var user = await _context.Users.FindAsync(userId);

        _logger.LogDebug(user != null
            ? "User found"
            : "User not found: {UserId}", userId);

        return user;
    }

    public async Task<User?> GetByNicknameAsync(string nickname)
    {
        _logger.LogDebug("Getting user by nickname: {Nickname}", nickname);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Nickname == nickname);

        _logger.LogDebug(user != null
            ? "User found: {UserId}"
            : "User not found for nickname: {Nickname}",
            user?.Id, nickname);

        return user;
    }

    public async Task<IEnumerable<User>> GetByIdsAsync(IEnumerable<Guid> userIds)
    {
        var userIdList = userIds.ToList();
        _logger.LogDebug("Getting {Count} users by Ids", userIdList.Count);

        var users = await _context.Users
            .Where(u => userIdList.Contains(u.Id))
            .ToListAsync();

        _logger.LogDebug("Found {Count} users", users.Count);

        return users;
    }

    public async Task AddAsync(User user)
    {
        _logger.LogInformation("Adding new user: {UserId}, DeviceId: {DeviceId}",
            user.Id, user.DeviceId);

        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();

        _logger.LogDebug("User added successfully");
    }

    public async Task UpdateAsync(User user)
    {
        _logger.LogDebug("Updating user: {UserId}", user.Id);

        _context.Users.Update(user);
        await _context.SaveChangesAsync();

        _logger.LogTrace("User updated successfully");
    }

    public async Task UpdateLastRoomIdAsync(Guid userId, Guid? roomId)
    {
        _logger.LogDebug("Updating LastRoomId for user {UserId} to {RoomId}", userId, roomId);

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            throw new KeyNotFoundException($"User {userId} not found");
        }

        user.LastRoomId = roomId;
        await _context.SaveChangesAsync();

        _logger.LogDebug("LastRoomId updated successfully");
    }

    #endregion
}
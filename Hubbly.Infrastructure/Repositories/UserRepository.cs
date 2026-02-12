using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Hubbly.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Hubbly.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByDeviceIdAsync(string deviceId)
        => await _context.Users.FirstOrDefaultAsync(u => u.DeviceId == deviceId);

    public async Task<User?> GetByIdAsync(Guid userId)
        => await _context.Users.FindAsync(userId);

    public async Task<User?> GetByNicknameAsync(string nickname)
        => await _context.Users.FirstOrDefaultAsync(u => u.Nickname == nickname);

    public async Task<IEnumerable<User>> GetByIdsAsync(IEnumerable<Guid> userIds)
        => await _context.Users.Where(u => userIds.Contains(u.Id)).ToListAsync();

    public async Task AddAsync(User user)
    {
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(User user)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
    }
}

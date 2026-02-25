using Hubbly.Domain.Entities;

namespace Hubbly.Domain.Services;

public interface IUserRepository
{
    Task<User?> GetByDeviceIdAsync(string deviceId);
    Task<User?> GetByIdAsync(Guid userId);
    Task<User?> GetByNicknameAsync(string nickname);
    Task<IEnumerable<User>> GetByIdsAsync(IEnumerable<Guid> userIds);
    Task AddAsync(User user);
    Task UpdateAsync(User user);
    Task UpdateLastRoomIdAsync(Guid userId, Guid? roomId);
}

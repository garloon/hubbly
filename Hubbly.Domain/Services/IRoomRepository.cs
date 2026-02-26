using Hubbly.Domain.Entities;

namespace Hubbly.Domain.Services;

public interface IRoomRepository
{
    // Basic CRUD
    Task<ChatRoom?> GetByIdAsync(Guid roomId);
    Task<IEnumerable<ChatRoom>> GetAllActiveAsync(RoomType? type = null);
    Task<ChatRoom> CreateAsync(ChatRoom room);
    Task UpdateAsync(ChatRoom room);
    Task DeleteAsync(Guid roomId);
    Task<bool> ExistsAsync(Guid roomId);

    // User count operations (atomic)
    Task<int> IncrementUserCountAsync(Guid roomId);
    Task<int> DecrementUserCountAsync(Guid roomId);
    Task<int> GetUserCountAsync(Guid roomId);

    // Room membership
    Task AddUserToRoomAsync(Guid roomId, Guid userId);
    Task RemoveUserFromRoomAsync(Guid roomId, Guid userId);
    Task<IEnumerable<Guid>> GetUsersInRoomAsync(Guid roomId);
    Task<IEnumerable<Guid>> GetOnlineUserIdsInRoomAsync(Guid roomId);

    // User-Room mapping
    Task<Guid?> GetUserRoomAsync(Guid userId);
    Task SetUserRoomAsync(Guid userId, Guid roomId);
    Task RemoveUserRoomAsync(Guid userId);

    // Room selection
    Task<ChatRoom?> GetOptimalRoomAsync(RoomType type, int maxUsers);
    Task<IEnumerable<ChatRoom>> GetRoomsByCreatedByAsync(Guid userId);
    Task<int> GetUserRoomCountAsync(Guid userId);

    // Connection tracking (for scale-out)
    Task TrackConnectionAsync(Guid connectionId, Guid userId, Guid roomId);
    Task RemoveConnectionAsync(Guid connectionId);
    Task<Guid?> GetUserIdByConnectionAsync(Guid connectionId);
    Task<IEnumerable<Guid>> GetConnectionIdsByUserIdAsync(Guid userId);
    Task<int> GetTotalOnlineCountAsync();
}
using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;

namespace Hubbly.Domain.Services;

public interface IRoomService
{
    Task<ChatRoom> GetOrCreateRoomForGuestAsync();
    Task AssignGuestToRoomAsync(Guid userId, Guid roomId);
    Task RemoveUserFromRoomAsync(Guid userId);
    Task<ChatRoom?> GetRoomByUserIdAsync(Guid userId);
    Task CleanupEmptyRoomsAsync(TimeSpan emptyThreshold);
    Task<int> GetActiveRoomsCountAsync();
    Task<IEnumerable<RoomInfoDto>> GetAvailableRoomsAsync(RoomType? type = null, Guid? userId = null);
    Task<RoomAssignmentData> JoinRoomAsync(Guid roomId, Guid userId, string? password = null);
    Task LeaveRoomAsync(Guid userId, Guid roomId);
    Task<RoomInfoDto> CreateUserRoomAsync(string name, string? description, RoomType type, Guid createdBy, int maxUsers);
    Task<IEnumerable<RoomInfoDto>> GetRoomsAsync();
}

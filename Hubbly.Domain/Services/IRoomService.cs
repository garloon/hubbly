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
}

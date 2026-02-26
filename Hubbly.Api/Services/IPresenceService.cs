using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;

namespace Hubbly.Api.Services;

/// <summary>
/// Manages user presence, connection tracking, and room assignment.
/// </summary>
public interface IPresenceService
{
    /// <summary>
    /// Handles a new user connection: assigns room, tracks connection, and prepares presence data.
    /// </summary>
    /// <returns>
    /// Tuple of (assigned room, user profile, list of existing users in room)
    /// </returns>
    Task<(ChatRoom room, User user, List<User> existingUsers)> HandleUserConnectedAsync(
        Guid userId, string connectionId);

    /// <summary>
    /// Handles user disconnection: removes connection, leaves room, and notifies if last connection.
    /// </summary>
    /// <returns>
    /// True if user completely disconnected (no remaining connections), false otherwise
    /// </returns>
    Task<bool> HandleUserDisconnectedAsync(Guid userId, string connectionId);

    /// <summary>
    /// Gets the room assignment data for a user.
    /// </summary>
    Task<RoomAssignmentData> GetRoomAssignmentAsync(ChatRoom room);
}

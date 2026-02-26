using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Hubbly.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hubbly.Infrastructure.Repositories;

using System.Collections.Concurrent;

public class RoomDbRepository : IRoomRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<RoomDbRepository> _logger;
    
    // Fallback in-memory connection tracking (only for single-instance mode)
    private static readonly ConcurrentDictionary<Guid, ConnectionInfo> _connections = new();
    private static readonly ConcurrentDictionary<Guid, HashSet<Guid>> _userConnections = new();
    private static int _onlineCount = 0;

    public RoomDbRepository(AppDbContext context, ILogger<RoomDbRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ChatRoom?> GetByIdAsync(Guid roomId)
    {
        return await _context.ChatRooms.FindAsync(roomId);
    }

    public async Task<IEnumerable<ChatRoom>> GetAllActiveAsync(RoomType? type = null)
    {
        var query = _context.ChatRooms.AsQueryable();

        if (type.HasValue)
        {
            query = query.Where(r => r.Type == type.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<ChatRoom> CreateAsync(ChatRoom room)
    {
        _context.ChatRooms.Add(room);
        await _context.SaveChangesAsync();
        return room;
    }

    public async Task UpdateAsync(ChatRoom room)
    {
        room.UpdatedAt = DateTimeOffset.UtcNow;
        _context.ChatRooms.Update(room);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid roomId)
    {
        var room = await _context.ChatRooms.FindAsync(roomId);
        if (room != null)
        {
            _context.ChatRooms.Remove(room);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(Guid roomId)
    {
        return await _context.ChatRooms.AnyAsync(r => r.Id == roomId);
    }

    public async Task<int> IncrementUserCountAsync(Guid roomId)
    {
        var room = await _context.ChatRooms.FindAsync(roomId);
        if (room == null) return 0;

        room.CurrentUsers++;
        room.UpdatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogDebug("Incremented user count for room {RoomId} to {Count}", roomId, room.CurrentUsers);
        return room.CurrentUsers;
    }

    public async Task<int> DecrementUserCountAsync(Guid roomId)
    {
        var room = await _context.ChatRooms.FindAsync(roomId);
        if (room == null) return 0;

        if (room.CurrentUsers > 0)
        {
            room.CurrentUsers--;
        }
        room.UpdatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogDebug("Decremented user count for room {RoomId} to {Count}", roomId, room.CurrentUsers);
        return room.CurrentUsers;
    }

    public async Task<int> GetUserCountAsync(Guid roomId)
    {
        var room = await _context.ChatRooms.FindAsync(roomId);
        return room?.CurrentUsers ?? 0;
    }

    public Task AddUserToRoomAsync(Guid roomId, Guid userId)
    {
        // No-op в fallback режиме
        _logger.LogWarning("AddUserToRoomAsync called in DB fallback mode - no-op");
        return Task.CompletedTask;
    }

    public Task RemoveUserFromRoomAsync(Guid roomId, Guid userId)
    {
        // No-op в fallback режиме
        _logger.LogWarning("RemoveUserFromRoomAsync called in DB fallback mode - no-op");
        return Task.CompletedTask;
    }

    public Task<IEnumerable<Guid>> GetUsersInRoomAsync(Guid roomId)
    {
        // В fallback режиме не можем получить список пользователей
        _logger.LogWarning("GetUsersInRoomAsync called in DB fallback mode - returning empty");
        return Task.FromResult(Enumerable.Empty<Guid>());
    }

    public Task<IEnumerable<Guid>> GetOnlineUserIdsInRoomAsync(Guid roomId)
    {
        // В fallback режиме не можем получить список онлайн пользователей
        _logger.LogWarning("GetOnlineUserIdsInRoomAsync called in DB fallback mode - returning empty");
        return Task.FromResult(Enumerable.Empty<Guid>());
    }

    public async Task<Guid?> GetUserRoomAsync(Guid userId)
    {
        // Нужно хранить в отдельной таблице или в User.LastRoomId
        var user = await _context.Users.FindAsync(userId);
        return user?.LastRoomId;
    }

    public async Task SetUserRoomAsync(Guid userId, Guid roomId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user != null)
        {
            user.LastRoomId = roomId;
            await _context.SaveChangesAsync();
        }
    }

    public async Task RemoveUserRoomAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user != null)
        {
            user.LastRoomId = null;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<ChatRoom?> GetOptimalRoomAsync(RoomType type, int maxUsers)
    {
        // Fallback режим: возвращаем первую найденную комнату подходящего типа и емкости
        // (сортировка по заполненности невозможна без актуального CurrentUsers)
        var room = await _context.ChatRooms
            .Where(r => r.Type == type && r.MaxUsers >= maxUsers)
            .FirstOrDefaultAsync();

        return room;
    }

    public async Task<IEnumerable<ChatRoom>> GetRoomsByCreatedByAsync(Guid userId)
    {
        return await _context.ChatRooms
            .Where(r => r.CreatedBy == userId)
            .ToListAsync();
    }

    public async Task<int> GetUserRoomCountAsync(Guid userId)
    {
        return await _context.ChatRooms
            .CountAsync(r => r.CreatedBy == userId);
    }

    #region Connection Tracking (Fallback for Single-Instance Mode)

    /// <summary>
    /// Tracks a connection in memory (fallback when Redis is unavailable)
    /// </summary>
    public async Task TrackConnectionAsync(Guid connectionId, Guid userId, Guid roomId)
    {
        var connectionInfo = ConnectionInfo.Create(userId, roomId);
        
        _connections[connectionId] = connectionInfo;
        
        if (!_userConnections.ContainsKey(userId))
            _userConnections[userId] = new HashSet<Guid>();
        
        _userConnections[userId].Add(connectionId);
        
        // Update online count
        Interlocked.Increment(ref _onlineCount);
        
        _logger.LogDebug("Tracked connection {ConnectionId} for user {UserId} in room {RoomId} (DB fallback)",
            connectionId, userId, roomId);
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Removes a connection from memory (fallback when Redis is unavailable)
    /// </summary>
    public async Task RemoveConnectionAsync(Guid connectionId)
    {
        if (_connections.TryRemove(connectionId, out var connectionInfo))
        {
            if (_userConnections.TryGetValue(connectionInfo.UserId, out var userConns))
            {
                userConns.Remove(connectionId);
                if (userConns.Count == 0)
                {
                    _userConnections.TryRemove(connectionInfo.UserId, out _);
                    Interlocked.Decrement(ref _onlineCount);
                }
            }
            
            _logger.LogDebug("Removed connection {ConnectionId} (DB fallback)", connectionId);
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets user ID by connection ID (fallback)
    /// </summary>
    public Task<Guid?> GetUserIdByConnectionAsync(Guid connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var connectionInfo))
        {
            return Task.FromResult<Guid?>(connectionInfo.UserId);
        }
        
        return Task.FromResult<Guid?>(null);
    }

    /// <summary>
    /// Gets all connection IDs for a user (fallback)
    /// </summary>
    public async Task<IEnumerable<Guid>> GetConnectionIdsByUserIdAsync(Guid userId)
    {
        if (_userConnections.TryGetValue(userId, out var connections))
        {
            return await Task.FromResult(connections.ToList());
        }
        
        return await Task.FromResult(Enumerable.Empty<Guid>());
    }

    /// <summary>
    /// Gets total online count (fallback)
    /// </summary>
    public async Task<int> GetTotalOnlineCountAsync()
    {
        return await Task.FromResult(_onlineCount);
    }

    #endregion
}
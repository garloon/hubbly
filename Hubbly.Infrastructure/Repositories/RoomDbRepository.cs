using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Hubbly.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hubbly.Infrastructure.Repositories;

public class RoomDbRepository : IRoomRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<RoomDbRepository> _logger;

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

        return await query.Where(r => r.IsActive).ToListAsync();
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

        // В БД мы не храним CurrentUsers, только для аудита
        // Эта операция только для Redis, но в fallback режиме возвращаем 0
        _logger.LogWarning("IncrementUserCountAsync called in DB fallback mode - no-op");
        return 0;
    }

    public async Task<int> DecrementUserCountAsync(Guid roomId)
    {
        var room = await _context.ChatRooms.FindAsync(roomId);
        if (room == null) return 0;

        _logger.LogWarning("DecrementUserCountAsync called in DB fallback mode - no-op");
        return 0;
    }

    public Task<int> GetUserCountAsync(Guid roomId)
    {
        // В fallback режиме не можем точно определить кол-во пользователей
        _logger.LogWarning("GetUserCountAsync called in DB fallback mode - returning 0");
        return Task.FromResult(0);
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
        // Ищем активную комнату нужного типа с свободными местами
        var rooms = await _context.ChatRooms
            .Where(r => r.IsActive && r.Type == type && r.MaxUsers >= maxUsers)
            .OrderByDescending(r => r.MaxUsers) // Более заполненные в приоритете
            .FirstOrDefaultAsync();

        return rooms;
    }

    public async Task<IEnumerable<ChatRoom>> GetRoomsByCreatedByAsync(Guid userId)
    {
        return await _context.ChatRooms
            .Where(r => r.CreatedBy == userId && r.IsActive)
            .ToListAsync();
    }

    public async Task<int> GetUserRoomCountAsync(Guid userId)
    {
        return await _context.ChatRooms
            .CountAsync(r => r.CreatedBy == userId && r.IsActive);
    }
}
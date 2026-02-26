using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Hubbly.Infrastructure.Repositories;

public class CompositeRoomRepository : IRoomRepository
{
    private readonly RedisRoomRepository _redisRepository;
    private readonly RoomDbRepository _dbRepository;
    private readonly ILogger<CompositeRoomRepository> _logger;

    public CompositeRoomRepository(
        RedisRoomRepository redisRepository,
        RoomDbRepository dbRepository,
        ILogger<CompositeRoomRepository> logger)
    {
        _redisRepository = redisRepository;
        _dbRepository = dbRepository;
        _logger = logger;
    }

    public async Task<ChatRoom?> GetByIdAsync(Guid roomId)
    {
        try
        {
            var room = await _redisRepository.GetByIdAsync(roomId);
            if (room != null) return room;

            // Fallback to DB
            _logger.LogWarning("Redis miss for room {RoomId}, falling back to DB", roomId);
            return await _dbRepository.GetByIdAsync(roomId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB");
            return await _dbRepository.GetByIdAsync(roomId);
        }
    }

    public async Task<IEnumerable<ChatRoom>> GetAllActiveAsync(RoomType? type = null)
    {
        try
        {
            var rooms = await _redisRepository.GetAllActiveAsync(type);
            if (rooms.Any()) return rooms;

            // Fallback to DB
            _logger.LogWarning("Redis returned no active rooms, falling back to DB");
            return await _dbRepository.GetAllActiveAsync(type);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB");
            return await _dbRepository.GetAllActiveAsync(type);
        }
    }

    public async Task<ChatRoom> CreateAsync(ChatRoom room)
    {
        ChatRoom result = null;
        
        // Always try to save to Redis first (primary storage)
        try
        {
            result = await _redisRepository.CreateAsync(room);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error in CreateAsync, will fallback to DB");
        }
        
        // Always also save to DB for persistence and fallback
        try
        {
            var dbResult = await _dbRepository.CreateAsync(room);
            // If Redis failed, use DB result
            if (result == null)
            {
                result = dbResult;
                _logger.LogInformation("Created room {RoomId} in DB only (Redis was unavailable)", dbResult.Id);
            }
            else
            {
                _logger.LogInformation("Created room {RoomId} in both Redis and DB", result.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create room in database");
            // If both failed, throw
            if (result == null)
            {
                throw;
            }
        }
        
        return result;
    }

    public async Task UpdateAsync(ChatRoom room)
    {
        try
        {
            await _redisRepository.UpdateAsync(room);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for UpdateAsync");
            await _dbRepository.UpdateAsync(room);
        }
    }

    public async Task DeleteAsync(Guid roomId)
    {
        try
        {
            await _redisRepository.DeleteAsync(roomId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for DeleteAsync");
            await _dbRepository.DeleteAsync(roomId);
        }
    }

    public async Task<bool> ExistsAsync(Guid roomId)
    {
        try
        {
            var exists = await _redisRepository.ExistsAsync(roomId);
            return exists;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for ExistsAsync");
            return await _dbRepository.ExistsAsync(roomId);
        }
    }

    public async Task<int> IncrementUserCountAsync(Guid roomId)
    {
        try
        {
            var count = await _redisRepository.IncrementUserCountAsync(roomId);
            return count;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for IncrementUserCountAsync");
            return await _dbRepository.IncrementUserCountAsync(roomId);
        }
    }

    public async Task<int> DecrementUserCountAsync(Guid roomId)
    {
        try
        {
            var count = await _redisRepository.DecrementUserCountAsync(roomId);
            return count;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for DecrementUserCountAsync");
            return await _dbRepository.DecrementUserCountAsync(roomId);
        }
    }

    public async Task<int> GetUserCountAsync(Guid roomId)
    {
        try
        {
            var count = await _redisRepository.GetUserCountAsync(roomId);
            return count;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for GetUserCountAsync");
            return await _dbRepository.GetUserCountAsync(roomId);
        }
    }

    public async Task AddUserToRoomAsync(Guid roomId, Guid userId)
    {
        try
        {
            await _redisRepository.AddUserToRoomAsync(roomId, userId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for AddUserToRoomAsync");
            await _dbRepository.AddUserToRoomAsync(roomId, userId);
        }
    }

    public async Task RemoveUserFromRoomAsync(Guid roomId, Guid userId)
    {
        try
        {
            await _redisRepository.RemoveUserFromRoomAsync(roomId, userId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for RemoveUserFromRoomAsync");
            await _dbRepository.RemoveUserFromRoomAsync(roomId, userId);
        }
    }

    public async Task<IEnumerable<Guid>> GetUsersInRoomAsync(Guid roomId)
    {
        try
        {
            var users = await _redisRepository.GetUsersInRoomAsync(roomId);
            return users;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for GetUsersInRoomAsync");
            return await _dbRepository.GetUsersInRoomAsync(roomId);
        }
    }

    public async Task<Guid?> GetUserRoomAsync(Guid userId)
    {
        try
        {
            var roomId = await _redisRepository.GetUserRoomAsync(userId);
            return roomId;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for GetUserRoomAsync");
            return await _dbRepository.GetUserRoomAsync(userId);
        }
    }

    public async Task SetUserRoomAsync(Guid userId, Guid roomId)
    {
        try
        {
            await _redisRepository.SetUserRoomAsync(userId, roomId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for SetUserRoomAsync");
            await _dbRepository.SetUserRoomAsync(userId, roomId);
        }
    }

    public async Task RemoveUserRoomAsync(Guid userId)
    {
        try
        {
            await _redisRepository.RemoveUserRoomAsync(userId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for RemoveUserRoomAsync");
            await _dbRepository.RemoveUserRoomAsync(userId);
        }
    }

    public async Task<ChatRoom?> GetOptimalRoomAsync(RoomType type, int maxUsers)
    {
        try
        {
            var room = await _redisRepository.GetOptimalRoomAsync(type, maxUsers);
            return room;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for GetOptimalRoomAsync");
            return await _dbRepository.GetOptimalRoomAsync(type, maxUsers);
        }
    }

    public async Task<IEnumerable<ChatRoom>> GetRoomsByCreatedByAsync(Guid userId)
    {
        try
        {
            var rooms = await _redisRepository.GetRoomsByCreatedByAsync(userId);
            if (rooms.Any()) return rooms;

            // Fallback to DB
            return await _dbRepository.GetRoomsByCreatedByAsync(userId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for GetRoomsByCreatedByAsync");
            return await _dbRepository.GetRoomsByCreatedByAsync(userId);
        }
    }

    public async Task<int> GetUserRoomCountAsync(Guid userId)
    {
        try
        {
            var count = await _redisRepository.GetUserRoomCountAsync(userId);
            return count;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for GetUserRoomCountAsync");
            return await _dbRepository.GetUserRoomCountAsync(userId);
        }
    }

    public async Task<IEnumerable<Guid>> GetOnlineUserIdsInRoomAsync(Guid roomId)
    {
        try
        {
            var userIds = await _redisRepository.GetOnlineUserIdsInRoomAsync(roomId);
            return userIds;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error, falling back to DB for GetOnlineUserIdsInRoomAsync");
            return await _dbRepository.GetOnlineUserIdsInRoomAsync(roomId);
        }
    }

    public async Task TrackConnectionAsync(string connectionId, Guid userId, Guid roomId)
    {
        try
        {
            await _redisRepository.TrackConnectionAsync(connectionId, userId, roomId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error in TrackConnectionAsync, falling back to DB");
            await _dbRepository.TrackConnectionAsync(connectionId, userId, roomId);
        }
    }

    public async Task RemoveConnectionAsync(string connectionId)
    {
        try
        {
            await _redisRepository.RemoveConnectionAsync(connectionId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error in RemoveConnectionAsync, falling back to DB");
            await _dbRepository.RemoveConnectionAsync(connectionId);
        }
    }

    public async Task<Guid?> GetUserIdByConnectionAsync(string connectionId)
    {
        try
        {
            return await _redisRepository.GetUserIdByConnectionAsync(connectionId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error in GetUserIdByConnectionAsync, falling back to DB");
            return await _dbRepository.GetUserIdByConnectionAsync(connectionId);
        }
    }

    public async Task<IEnumerable<string>> GetConnectionIdsByUserIdAsync(Guid userId)
    {
        try
        {
            return await _redisRepository.GetConnectionIdsByUserIdAsync(userId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error in GetConnectionIdsByUserIdAsync, falling back to DB");
            return await _dbRepository.GetConnectionIdsByUserIdAsync(userId);
        }
    }

    public async Task<int> GetTotalOnlineCountAsync()
    {
        try
        {
            return await _redisRepository.GetTotalOnlineCountAsync();
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex, "Redis connection error in GetTotalOnlineCountAsync, falling back to DB");
            return await _dbRepository.GetTotalOnlineCountAsync();
        }
    }
}
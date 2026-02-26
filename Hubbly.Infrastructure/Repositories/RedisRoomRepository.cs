using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace Hubbly.Infrastructure.Repositories;

public class RedisRoomRepository : IRoomRepository
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisRoomRepository> _logger;
    private const string RoomKeyPrefix = "room:";
    private const string ActiveRoomsPrefix = "room:active:";
    private const string RoomMembersPrefix = "room:members:";
    private const string UserRoomPrefix = "user:room:";
    private const string ConnectionKeyPrefix = "connection:";
    private const string UserConnectionsPrefix = "user:connections:";
    private const string OnlineCountKey = "online_users:count";

    public RedisRoomRepository(IConnectionMultiplexer redis, ILogger<RedisRoomRepository> logger)
    {
        _redis = redis;
        _db = redis.GetDatabase();
        _logger = logger;
    }

    #region Helper Methods

    private string GetRoomKey(Guid roomId) => $"{RoomKeyPrefix}{roomId}";
    private string GetActiveRoomsKey(RoomType? type = null) => type.HasValue ? $"{ActiveRoomsPrefix}{(int)type}" : $"{ActiveRoomsPrefix}all";
    private string GetRoomMembersKey(Guid roomId) => $"{RoomMembersPrefix}{roomId}";
    private string GetUserRoomKey(Guid userId) => $"{UserRoomPrefix}{userId}";

    private HashEntry[] SerializeRoomToHash(ChatRoom room)
    {
        var json = JsonSerializer.Serialize(room);
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

        return dict?.Select(kv => new HashEntry(kv.Key, kv.Value?.ToString() ?? string.Empty)).ToArray() ?? Array.Empty<HashEntry>();
    }

    private ChatRoom? DeserializeRoomFromHash(HashEntry[] entries)
    {
        if (entries == null || entries.Length == 0) return null;

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var key = entry.Name.ToString() ?? string.Empty;
            var value = entry.Value.ToString() ?? string.Empty;

            // Normalize key to proper casing for ChatRoom properties
            var normalizedKey = NormalizeKey(key);

            // Determine the appropriate type based on ChatRoom properties
            switch (normalizedKey)
            {
                case "Id":
                case "CreatedBy":
                    if (Guid.TryParse(value, out var guid))
                        dict[normalizedKey] = guid;
                    else
                        dict[normalizedKey] = null;
                    break;
                case "Type":
                    if (int.TryParse(value, out var typeInt))
                        dict[normalizedKey] = (RoomType)typeInt;
                    else
                        dict[normalizedKey] = RoomType.System; // default fallback
                    break;
                case "MaxUsers":
                    if (int.TryParse(value, out var maxUsers))
                        dict[normalizedKey] = maxUsers;
                    else
                        dict[normalizedKey] = 50; // default fallback
                    break;
                case "CurrentUsers":
                    if (int.TryParse(value, out var currentUsers))
                        dict[normalizedKey] = currentUsers;
                    else
                        dict[normalizedKey] = 0; // default fallback
                    break;
                case "CreatedAt":
                case "LastActiveAt":
                case "UpdatedAt":
                    if (DateTimeOffset.TryParse(value, out var dateTime))
                        dict[normalizedKey] = dateTime;
                    else
                        dict[normalizedKey] = DateTimeOffset.UtcNow; // default fallback
                    break;
                default:
                    // For string properties (Name, Description, PasswordHash)
                    dict[normalizedKey] = string.IsNullOrEmpty(value) ? null : value;
                    break;
            }
        }

        var json = JsonSerializer.Serialize(dict);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        return JsonSerializer.Deserialize<ChatRoom>(json, options);
    }

    private string NormalizeKey(string key)
    {
        // Map common variations to proper property names
        return key.ToLowerInvariant() switch
        {
            "id" => "Id",
            "name" => "Name",
            "description" => "Description",
            "type" => "Type",
            "maxusers" => "MaxUsers",
            "createdby" => "CreatedBy",
            "passwordhash" => "PasswordHash",
            "currentusers" => "CurrentUsers",
            "createdat" => "CreatedAt",
            "lastactiveat" => "LastActiveAt",
            "updatedat" => "UpdatedAt",
            _ => key // Return original if no mapping
        };
    }

    #endregion

    public async Task<ChatRoom?> GetByIdAsync(Guid roomId)
    {
        var key = GetRoomKey(roomId);
        var hash = await _db.HashGetAllAsync(key);

        if (hash.Length == 0) return null;

        var room = DeserializeRoomFromHash(hash);
        return room;
    }

    public async Task<IEnumerable<ChatRoom>> GetAllActiveAsync(RoomType? type = null)
    {
        var activeRoomsKey = GetActiveRoomsKey(type);
        var roomIds = await _db.SortedSetRangeByRankAsync(activeRoomsKey, 0, -1);

        var rooms = new List<ChatRoom>();
        foreach (var roomIdValue in roomIds)
        {
            if (Guid.TryParse(roomIdValue, out var roomId))
            {
                var room = await GetByIdAsync(roomId);
                if (room != null)
                {
                    rooms.Add(room);
                }
            }
        }

        return rooms;
    }

    public async Task<ChatRoom> CreateAsync(ChatRoom room)
    {
        var roomKey = GetRoomKey(room.Id);
        var activeRoomsKey = GetActiveRoomsKey(room.Type);

        // Сохранить комнату в хэш
        var hash = SerializeRoomToHash(room);
        await _db.HashSetAsync(roomKey, hash);

        // Добавить в sorted set активных комнат
        await _db.SortedSetAddAsync(activeRoomsKey, new[] { new SortedSetEntry(room.Id.ToString(), room.LastActiveAt.ToUnixTimeSeconds()) });

        _logger.LogInformation("Created room {RoomId} ({RoomName}) in Redis", room.Id, room.Name);
        return room;
    }

    public async Task UpdateAsync(ChatRoom room)
    {
        var roomKey = GetRoomKey(room.Id);
        var hash = SerializeRoomToHash(room);
        await _db.HashSetAsync(roomKey, hash);

        // Всегда обновлять в sorted set на основе LastActiveAt
        var activeRoomsKey = GetActiveRoomsKey(room.Type);
        await _db.SortedSetAddAsync(activeRoomsKey, new[] { new SortedSetEntry(room.Id.ToString(), room.LastActiveAt.ToUnixTimeSeconds()) });

        _logger.LogDebug("Updated room {RoomId} in Redis", room.Id);
    }

    public async Task DeleteAsync(Guid roomId)
    {
        var room = await GetByIdAsync(roomId);
        if (room == null) return;

        var roomKey = GetRoomKey(roomId);
        var activeRoomsKey = GetActiveRoomsKey(room.Type);
        var membersKey = GetRoomMembersKey(roomId);

        // Удалить хэш комнаты
        await _db.KeyDeleteAsync(roomKey);
        // Удалить из sorted set
        await _db.SortedSetRemoveAsync(activeRoomsKey, roomId.ToString());
        // Удалить список членов
        await _db.KeyDeleteAsync(membersKey);

        _logger.LogInformation("Deleted room {RoomId} from Redis", roomId);
    }

    public async Task<bool> ExistsAsync(Guid roomId)
    {
        var roomKey = GetRoomKey(roomId);
        return await _db.KeyExistsAsync(roomKey);
    }

    public async Task<int> IncrementUserCountAsync(Guid roomId)
    {
        var roomKey = GetRoomKey(roomId);
        var currentUsers = await _db.HashIncrementAsync(roomKey, "currentUsers", 1);
        _logger.LogDebug("Incremented user count for room {RoomId} to {Count}", roomId, currentUsers);
        return (int)currentUsers;
    }

    public async Task<int> DecrementUserCountAsync(Guid roomId)
    {
        var roomKey = GetRoomKey(roomId);
        var currentUsers = await _db.HashDecrementAsync(roomKey, "currentUsers", 1);
        if (currentUsers < 0)
        {
            await _db.HashSetAsync(roomKey, "currentUsers", 0);
            currentUsers = 0;
        }

        _logger.LogDebug("Decremented user count for room {RoomId} to {Count}", roomId, currentUsers);
        return (int)currentUsers;
    }

    public async Task<int> GetUserCountAsync(Guid roomId)
    {
        var roomKey = GetRoomKey(roomId);
        var currentUsers = (int)await _db.HashGetAsync(roomKey, "currentUsers");
        var membersCount = await _db.SetLengthAsync(GetRoomMembersKey(roomId));
        
        // Автокоррекция при значительном расхождении (>2)
        if (Math.Abs(currentUsers - membersCount) > 2)
        {
            _logger.LogWarning("User count mismatch for room {RoomId}: hash={Hash}, set={Set}",
                roomId, currentUsers, membersCount);
            // Синхронизируем с реальным количеством
            await _db.HashSetAsync(roomKey, "currentUsers", membersCount);
            return (int)membersCount;
        }
        return currentUsers;
    }

    public async Task AddUserToRoomAsync(Guid roomId, Guid userId)
    {
        var membersKey = GetRoomMembersKey(roomId);
        await _db.SetAddAsync(membersKey, userId.ToString());
        _logger.LogDebug("Added user {UserId} to room {RoomId}", userId, roomId);
    }

    public async Task RemoveUserFromRoomAsync(Guid roomId, Guid userId)
    {
        var membersKey = GetRoomMembersKey(roomId);
        await _db.SetRemoveAsync(membersKey, userId.ToString());
        _logger.LogDebug("Removed user {UserId} from room {RoomId}", userId, roomId);
    }

    public async Task<IEnumerable<Guid>> GetUsersInRoomAsync(Guid roomId)
    {
        var membersKey = GetRoomMembersKey(roomId);
        var userIds = await _db.SetMembersAsync(membersKey);

        return userIds.Select(uid => Guid.TryParse(uid, out var guid) ? guid : Guid.Empty)
                      .Where(guid => guid != Guid.Empty);
    }

    public async Task<IEnumerable<Guid>> GetOnlineUserIdsInRoomAsync(Guid roomId)
    {
        // For Redis, this is the same as GetUsersInRoomAsync
        // because Redis set contains all users currently in the room
        return await GetUsersInRoomAsync(roomId);
    }

    public async Task<Guid?> GetUserRoomAsync(Guid userId)
    {
        var userRoomKey = GetUserRoomKey(userId);
        var roomIdStr = await _db.StringGetAsync(userRoomKey);

        if (string.IsNullOrEmpty(roomIdStr)) return null;

        return Guid.TryParse(roomIdStr, out var roomId) ? roomId : null;
    }

    public async Task SetUserRoomAsync(Guid userId, Guid roomId)
    {
        var userRoomKey = GetUserRoomKey(userId);
        await _db.StringSetAsync(userRoomKey, roomId.ToString());
        _logger.LogDebug("Set user {UserId} room to {RoomId}", userId, roomId);
    }

    public async Task RemoveUserRoomAsync(Guid userId)
    {
        var userRoomKey = GetUserRoomKey(userId);
        await _db.KeyDeleteAsync(userRoomKey);
        _logger.LogDebug("Removed user {UserId} room mapping", userId);
    }

    public async Task<ChatRoom?> GetOptimalRoomAsync(RoomType type, int maxUsers)
    {
        // Получить все комнаты указанного типа (без фильтра по IsActive)
        var activeRoomsKey = GetActiveRoomsKey(type);
        var roomIds = await _db.SortedSetRangeByRankAsync(activeRoomsKey, 0, -1);

        _logger.LogDebug("GetOptimalRoomAsync: Found {Count} room IDs in active set for type {Type}",
            roomIds.Length, type);

        ChatRoom? bestRoom = null;
        int bestCurrentUsers = -1;

        foreach (var roomIdValue in roomIds)
        {
            if (Guid.TryParse(roomIdValue, out var roomId))
            {
                var room = await GetByIdAsync(roomId);
                if (room != null && room.MaxUsers >= maxUsers)
                {
                    // Получить текущее количество пользователей
                    var currentUsers = await GetUserCountAsync(roomId);

                    _logger.LogDebug("Room {RoomId}: MaxUsers={Max}, CurrentUsers={Current}",
                        roomId, room.MaxUsers, currentUsers);

                    // Искать комнату с максимальным количеством пользователей, но не полную
                    if (currentUsers < room.MaxUsers && currentUsers > bestCurrentUsers)
                    {
                        bestRoom = room;
                        bestCurrentUsers = currentUsers;
                    }
                }
            }
        }

        if (bestRoom != null)
        {
            _logger.LogInformation("GetOptimalRoomAsync: Selected room {RoomId} ({RoomName}) with {Users}/{Max}",
                bestRoom.Id, bestRoom.Name, bestCurrentUsers, bestRoom.MaxUsers);
        }
        else
        {
            _logger.LogWarning("GetOptimalRoomAsync: No suitable room found for type {Type}, maxUsers={MaxUsers}",
                type, maxUsers);
        }

        return bestRoom;
    }

    public async Task<IEnumerable<ChatRoom>> GetRoomsByCreatedByAsync(Guid userId)
    {
        var allRooms = await GetAllActiveAsync();
        return allRooms.Where(r => r.CreatedBy == userId);
    }

    public async Task<int> GetUserRoomCountAsync(Guid userId)
    {
        var rooms = await GetRoomsByCreatedByAsync(userId);
        return rooms.Count();
    }

    #region Connection Tracking (Scale-out Support)

    /// <summary>
    /// Tracks a new connection in Redis
    /// </summary>
    public async Task TrackConnectionAsync(Guid connectionId, Guid userId, Guid roomId)
    {
        var connectionKey = $"{ConnectionKeyPrefix}{connectionId}";
        var userConnectionsKey = $"{UserConnectionsPrefix}{userId}";
        
        var connectionInfo = ConnectionInfo.Create(userId, roomId);
        var json = connectionInfo.ToJson();
        
        // Store connection info with TTL (30 minutes)
        await _db.HashSetAsync(connectionKey, "data", json);
        await _db.KeyExpireAsync(connectionKey, TimeSpan.FromMinutes(30));
        
        // Add to user's connections set
        await _db.SetAddAsync(userConnectionsKey, connectionId.ToString());
        await _db.KeyExpireAsync(userConnectionsKey, TimeSpan.FromMinutes(30));
        
        // Increment global online count
        await _db.StringIncrementAsync(OnlineCountKey, 1);
        
        _logger.LogDebug("Tracked connection {ConnectionId} for user {UserId} in room {RoomId}",
            connectionId, userId, roomId);
    }

    /// <summary>
    /// Removes a connection from Redis
    /// </summary>
    public async Task RemoveConnectionAsync(Guid connectionId)
    {
        var connectionKey = $"{ConnectionKeyPrefix}{connectionId}";
        var connectionData = await _db.HashGetAsync(connectionKey, "data");
        
        if (!connectionData.IsNullOrEmpty)
        {
            var connectionInfo = ConnectionInfo.FromJson(connectionData);
            if (connectionInfo != null)
            {
                var userConnectionsKey = $"{UserConnectionsPrefix}{connectionInfo.UserId}";
                
                // Remove from user's connections set
                await _db.SetRemoveAsync(userConnectionsKey, connectionId.ToString());
                
                // Check if user has other connections
                var remaining = await _db.SetLengthAsync(userConnectionsKey);
                if (remaining == 0)
                {
                    // No more connections for this user, decrement global count
                    await _db.KeyDeleteAsync(userConnectionsKey);
                    await _db.StringDecrementAsync(OnlineCountKey, 1);
                }
            }
        }
        
        // Delete connection key
        await _db.KeyDeleteAsync(connectionKey);
        
        _logger.LogDebug("Removed connection {ConnectionId}", connectionId);
    }

    /// <summary>
    /// Gets user ID by connection ID
    /// </summary>
    public async Task<Guid?> GetUserIdByConnectionAsync(Guid connectionId)
    {
        var connectionKey = $"{ConnectionKeyPrefix}{connectionId}";
        var data = await _db.HashGetAsync(connectionKey, "data");
        
        if (data.IsNullOrEmpty) return null;
        
        var connectionInfo = ConnectionInfo.FromJson(data);
        return connectionInfo?.UserId;
    }

    /// <summary>
    /// Gets all connection IDs for a user
    /// </summary>
    public async Task<IEnumerable<Guid>> GetConnectionIdsByUserIdAsync(Guid userId)
    {
        var userConnectionsKey = $"{UserConnectionsPrefix}{userId}";
        var connectionIds = await _db.SetMembersAsync(userConnectionsKey);
        
        var result = new List<Guid>();
        foreach (var id in connectionIds)
        {
            if (Guid.TryParse(id, out var guid))
            {
                result.Add(guid);
            }
        }
        return result;
    }

    /// <summary>
    /// Gets total online count across all rooms
    /// </summary>
    public async Task<int> GetTotalOnlineCountAsync()
    {
        var count = await _db.StringGetAsync(OnlineCountKey);
        return (int)count;
    }

    #endregion
}
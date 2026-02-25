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

        var dict = new Dictionary<string, string>();
        foreach (var entry in entries)
        {
            var key = entry.Name.ToString() ?? string.Empty;
            var value = entry.Value.ToString() ?? string.Empty;
            dict[key] = value;
        }

        var json = JsonSerializer.Serialize(dict);
        return JsonSerializer.Deserialize<ChatRoom>(json);
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
                if (room != null && room.IsActive)
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

        // Обновить в sorted set, если комната активна
        if (room.IsActive)
        {
            var activeRoomsKey = GetActiveRoomsKey(room.Type);
            await _db.SortedSetAddAsync(activeRoomsKey, new[] { new SortedSetEntry(room.Id.ToString(), room.LastActiveAt.ToUnixTimeSeconds()) });
        }

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
        var currentUsers = await _db.HashGetAsync(roomKey, "currentUsers");
        return (int)currentUsers;
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
        // Получить все активные комнаты указанного типа
        var activeRoomsKey = GetActiveRoomsKey(type);
        var roomIds = await _db.SortedSetRangeByRankAsync(activeRoomsKey, 0, -1);

        ChatRoom? bestRoom = null;
        int bestCurrentUsers = -1;

        foreach (var roomIdValue in roomIds)
        {
            if (Guid.TryParse(roomIdValue, out var roomId))
            {
                var room = await GetByIdAsync(roomId);
                if (room != null && room.IsActive && room.MaxUsers >= maxUsers)
                {
                    // Получить текущее количество пользователей
                    var currentUsers = await GetUserCountAsync(roomId);

                    // Искать комнату с максимальным количеством пользователей, но не полную
                    if (currentUsers < room.MaxUsers && currentUsers > bestCurrentUsers)
                    {
                        bestRoom = room;
                        bestCurrentUsers = currentUsers;
                    }
                }
            }
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
}
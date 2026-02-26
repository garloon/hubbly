using Hubbly.Domain.Entities;
using Hubbly.Domain.Dtos;
using Hubbly.Domain.Services;
using Hubbly.Application.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hubbly.Application.Services;

public class RoomService : IRoomService
{
    private readonly IRoomRepository _roomRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<RoomService> _logger;
    private readonly RoomServiceOptions _options;

    public RoomService(
        IRoomRepository roomRepository,
        IUserRepository userRepository,
        ILogger<RoomService> logger,
        IOptions<RoomServiceOptions> options)
    {
        _roomRepository = roomRepository ?? throw new ArgumentNullException(nameof(roomRepository));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    #region Public methods

    public async Task<ChatRoom> GetOrCreateRoomForGuestAsync()
    {
        _logger.LogDebug("GetOrCreateRoomForGuestAsync called");
        
        // Ищем оптимальную системную комнату
        var room = await _roomRepository.GetOptimalRoomAsync(RoomType.System, _options.DefaultMaxUsers);

        if (room != null)
        {
            _logger.LogInformation("Found existing system room: {RoomName} (ID: {RoomId}, Users: {Current}/{Max})",
                room.Name, room.Id, room.CurrentUsers, room.MaxUsers);
            return room;
        }

        // GetOptimalRoomAsync вернул null - все существующие комнаты полны или их нет
        _logger.LogWarning("No available system room found (all full or none exist), creating new one");
        
        // Генерируем уникальное имя для новой системной комнаты
        var roomName = await GenerateSystemRoomNameAsync();
        _logger.LogInformation("Generated new system room name: {RoomName}", roomName);

        var newRoom = new ChatRoom(
            roomName,
            RoomType.System,
            _options.DefaultMaxUsers
        );

        var createdRoom = await _roomRepository.CreateAsync(newRoom);
        _logger.LogInformation("Created new system room: {RoomName} (ID: {RoomId})",
            createdRoom.Name, createdRoom.Id);
        
        return createdRoom;
    }

    /// <summary>
    /// Генерирует уникальное имя для новой системной комнаты
    /// Формат: "Общая комната #N", где N = максимальный номер + 1
    /// </summary>
    private async Task<string> GenerateSystemRoomNameAsync()
    {
        // Получаем ВСЕ системные комнаты (включая пустые и неактивные) для корректной нумерации
        var allSystemRooms = await _roomRepository.GetAllActiveAsync(RoomType.System);
        int maxNum = 0;

        if (allSystemRooms != null)
        {
            foreach (var room in allSystemRooms)
            {
                if (room.Name.StartsWith("Общая комната #"))
                {
                    var numPart = room.Name.Split('#')[1];
                    if (int.TryParse(numPart, out int num))
                    {
                        maxNum = Math.Max(maxNum, num);
                    }
                }
            }
        }

        return $"Общая комната #{maxNum + 1}";
    }

    public async Task AssignGuestToRoomAsync(Guid userId, Guid roomId)
    {
        var room = await _roomRepository.GetByIdAsync(roomId);
        if (room == null)
        {
            throw new InvalidOperationException($"Room {roomId} not found");
        }

        // Проверить заполненность через репозиторий
        var currentUsers = await _roomRepository.GetUserCountAsync(roomId);
        if (currentUsers >= room.MaxUsers)
        {
            throw new InvalidOperationException($"Room {room.Name} is full");
        }

        // Установить маппинг пользователя → комната
        await _roomRepository.SetUserRoomAsync(userId, roomId);

        // Добавить пользователя в комнату
        await _roomRepository.AddUserToRoomAsync(roomId, userId);

        // Увеличить счетчик
        await _roomRepository.IncrementUserCountAsync(roomId);

        // Обновить LastActiveAt комнаты
        room.UpdateLastActive();
        await _roomRepository.UpdateAsync(room);

        _logger.LogDebug("User {UserId} assigned to room {RoomName}", userId, room.Name);
    }

    public async Task RemoveUserFromRoomAsync(Guid userId)
    {
        var roomId = await _roomRepository.GetUserRoomAsync(userId);
        if (roomId.HasValue)
        {
            await _roomRepository.RemoveUserFromRoomAsync(roomId.Value, userId);
            await _roomRepository.DecrementUserCountAsync(roomId.Value);
            await _roomRepository.RemoveUserRoomAsync(userId);

            _logger.LogDebug("User {UserId} removed from room {RoomId}", userId, roomId.Value);
        }
    }

    public async Task<ChatRoom?> GetRoomByUserIdAsync(Guid userId)
    {
        var roomId = await _roomRepository.GetUserRoomAsync(userId);
        if (roomId.HasValue)
        {
            return await _roomRepository.GetByIdAsync(roomId.Value);
        }
        return null;
    }

    public async Task CleanupEmptyRoomsAsync(TimeSpan emptyThreshold)
    {
        // Очищать только системные комнаты
        var allSystemRooms = await _roomRepository.GetAllActiveAsync(RoomType.System);
        var now = DateTimeOffset.UtcNow;
        var roomsToRemove = new List<Guid>();

        foreach (var room in allSystemRooms)
        {
            // Никогда не удалять "Общая комната #1"
            if (room.Name == "Общая комната #1")
            {
                continue;
            }

            var userCount = await _roomRepository.GetUserCountAsync(room.Id);
            if (userCount == 0 && now - room.LastActiveAt > emptyThreshold)
            {
                roomsToRemove.Add(room.Id);
            }
        }

        foreach (var roomId in roomsToRemove)
        {
            await _roomRepository.DeleteAsync(roomId);
            _logger.LogInformation("Cleaned up empty system room: {RoomId}", roomId);
        }

        if (roomsToRemove.Any())
        {
            var allRooms = await _roomRepository.GetAllActiveAsync();
            _logger.LogInformation("Total rooms after cleanup: {RoomCount}", allRooms.Count());
        }
    }

    public async Task<int> GetActiveRoomsCountAsync()
    {
        var rooms = await _roomRepository.GetAllActiveAsync();
        return rooms.Count();
    }

    public async Task<RoomInfoDto> CreateUserRoomAsync(string name, string? description, RoomType type, Guid createdBy, int maxUsers)
    {
        if (type == RoomType.System)
        {
            throw new InvalidOperationException("Cannot create system rooms manually");
        }

        // Проверить лимит комнат на пользователя (макс 5)
        var userRoomCount = await _roomRepository.GetUserRoomCountAsync(createdBy);
        if (userRoomCount >= 5)
        {
            throw new InvalidOperationException("Room limit exceeded (max 5 rooms per user)");
        }

        var room = new ChatRoom(name, type, maxUsers, createdBy, description);
        var createdRoom = await _roomRepository.CreateAsync(room);

        // Возвращаем DTO
        return new RoomInfoDto
        {
            RoomId = createdRoom.Id,
            RoomName = createdRoom.Name,
            Description = createdRoom.Description,
            Type = createdRoom.Type,
            CurrentUsers = 0, // Новая комната, пользователей пока нет
            MaxUsers = createdRoom.MaxUsers,
            IsPrivate = createdRoom.Type == RoomType.Private
        };
    }

    public async Task<RoomAssignmentData> JoinRoomAsync(Guid roomId, Guid userId, string? password = null)
    {
        var room = await _roomRepository.GetByIdAsync(roomId);
        if (room == null)
        {
            throw new KeyNotFoundException($"Room {roomId} not found");
        }

        // Проверить пароль для приватных комнат
        if (room.Type == RoomType.Private && !string.IsNullOrEmpty(room.PasswordHash))
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new UnauthorizedAccessException("Password required for private room");
            }

            // TODO: Implement password verification
            // if (!BCrypt.Verify(password, room.PasswordHash))
            //     throw new UnauthorizedAccessException("Invalid password");
        }

        // Проверить, не в комнате ли уже пользователь
        var currentRoomId = await _roomRepository.GetUserRoomAsync(userId);
        if (currentRoomId.HasValue && currentRoomId.Value == roomId)
        {
            // Уже в этой комнате
            return new RoomAssignmentData
            {
                RoomId = room.Id,
                RoomName = room.Name,
                UsersInRoom = (int)await _roomRepository.GetUserCountAsync(roomId),
                MaxUsers = room.MaxUsers
            };
        }

        // Если пользователь уже в другой комнате — выйти
        if (currentRoomId.HasValue)
        {
            await RemoveUserFromRoomAsync(userId);
        }

        // Назначить пользователя в комнату
        await AssignGuestToRoomAsync(userId, roomId);

        // Обновить LastRoomId у пользователя (для возврата)
        await _userRepository.UpdateLastRoomIdAsync(userId, roomId);

        return new RoomAssignmentData
        {
            RoomId = room.Id,
            RoomName = room.Name,
            UsersInRoom = (int)await _roomRepository.GetUserCountAsync(roomId),
            MaxUsers = room.MaxUsers
        };
    }

    public async Task LeaveRoomAsync(Guid userId, Guid roomId)
    {
        var currentRoomId = await _roomRepository.GetUserRoomAsync(userId);
        if (currentRoomId.HasValue && currentRoomId.Value == roomId)
        {
            await RemoveUserFromRoomAsync(userId);
        }
    }

    public async Task<IEnumerable<RoomInfoDto>> GetAvailableRoomsAsync(RoomType? type = null, Guid? userId = null)
    {
        // Получить все комнаты (без фильтра по IsActive)
        var rooms = await _roomRepository.GetAllActiveAsync(type);

        // Если указан userId, исключить приватные комнаты, в которых он не состоит
        if (userId.HasValue)
        {
            var userRoomId = await _roomRepository.GetUserRoomAsync(userId.Value);
            rooms = rooms.Where(r =>
                r.Type != RoomType.Private || // Публичные и системные — все
                r.CreatedBy == userId.Value || // Свои приватные
                r.Id == userRoomId // Или уже в этой комнате
            );
        }

        var result = new List<RoomInfoDto>();

        foreach (var room in rooms)
        {
            var currentUsers = await _roomRepository.GetUserCountAsync(room.Id);

            // Показывать только комнаты, где есть свободные места (или пользователь уже в них)
            // Если пользователь уже в комнате, показываем ее даже если заполнена
            var isUserInRoom = userId.HasValue && (await _roomRepository.GetUserRoomAsync(userId.Value)) == room.Id;
            if (currentUsers >= room.MaxUsers && !isUserInRoom)
            {
                continue; // Пропустить заполненную комнату
            }

            result.Add(new RoomInfoDto
            {
                RoomId = room.Id,
                RoomName = room.Name,
                Description = room.Description,
                Type = room.Type,
                CurrentUsers = currentUsers,
                MaxUsers = room.MaxUsers,
                IsPrivate = room.Type == RoomType.Private
            });
        }

        // Сортировка: сначала System, затем Public, затем Private
        // Внутри каждой группы — по алфавиту
        return result.OrderBy(r => r.Type).ThenBy(r => r.RoomName);
    }

    #endregion

    public async Task<IEnumerable<RoomInfoDto>> GetRoomsAsync()
    {
        // Получить все активные комнаты (и системные, и публичные)
        return await GetAvailableRoomsAsync(null, null);
    }
}
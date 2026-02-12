namespace Hubbly.Domain.Dtos;

/// <summary>
/// Данные о назначении пользователя в комнату
/// </summary>
public class RoomAssignmentData
{
    public Guid RoomId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public int UsersInRoom { get; set; }
    public int MaxUsers { get; set; }
}

/// <summary>
/// Данные о подключившемся пользователе
/// </summary>
public class UserJoinedData
{
    public string UserId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string AvatarConfigJson { get; set; } = "{}";
    public DateTimeOffset JoinedAt { get; set; }
}

/// <summary>
/// Данные о вышедшем пользователе
/// </summary>
public class UserLeftData
{
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset LeftAt { get; set; }
}

/// <summary>
/// Данные о печатающем пользователе
/// </summary>
public class UserTypingData
{
    public string UserId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
}

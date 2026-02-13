namespace Hubbly.Api.Hubs;

/// <summary>
/// Модель подключенного пользователя для внутреннего использования
/// </summary>
public class ConnectedUser
{
    public Guid UserId { get; set; }
    public string Nickname { get; set; } = null!;
    public string AvatarConfigJson { get; set; } = null!;
    public string ConnectionId { get; set; } = null!;
    public DateTimeOffset ConnectedAt { get; set; }
    public Guid RoomId { get; set; }
    public string RoomName { get; set; } = null!;
}
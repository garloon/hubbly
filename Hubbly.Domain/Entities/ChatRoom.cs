using System.Text.Json.Serialization;

namespace Hubbly.Domain.Entities;

public enum RoomType
{
    System = 0,
    Public = 1,
    Private = 2
}

public class ChatRoom
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public RoomType Type { get; set; }
    public int MaxUsers { get; set; }
    public Guid? CreatedBy { get; set; } // null для системных комнат
    public string? PasswordHash { get; set; } // только для Private комнат
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActiveAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Конструктор для EF Core
    private ChatRoom() { }

    public ChatRoom(string name, RoomType type, int maxUsers = 50, Guid? createdBy = null, string? description = null)
    {
        Id = Guid.NewGuid();
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
        MaxUsers = maxUsers > 0 ? maxUsers : throw new ArgumentException("MaxUsers must be positive", nameof(maxUsers));
        CreatedBy = createdBy;
        Description = description;
        CreatedAt = DateTimeOffset.UtcNow;
        LastActiveAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
        IsActive = true;
    }

    public void UpdateLastActive()
    {
        LastActiveAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAsInactive()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateDetails(string? name = null, string? description = null, int? maxUsers = null)
    {
        if (name != null) Name = name;
        if (description != null) Description = description;
        if (maxUsers.HasValue && maxUsers.Value > 0) MaxUsers = maxUsers.Value;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

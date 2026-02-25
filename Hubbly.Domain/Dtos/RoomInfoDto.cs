using Hubbly.Domain.Entities;

namespace Hubbly.Domain.Dtos;

public class RoomInfoDto
{
    public Guid RoomId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public RoomType Type { get; set; }
    public int CurrentUsers { get; set; }
    public int MaxUsers { get; set; }
    public bool IsPrivate { get; set; } // Password protected
}
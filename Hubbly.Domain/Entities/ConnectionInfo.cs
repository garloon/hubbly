using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hubbly.Domain.Entities;

/// <summary>
/// Connection information stored in Redis for tracking active connections
/// </summary>
public class ConnectionInfo
{
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }
    
    [JsonPropertyName("roomId")]
    public Guid RoomId { get; set; }
    
    [JsonPropertyName("connectedAt")]
    public DateTimeOffset ConnectedAt { get; set; }
    
    public static ConnectionInfo Create(Guid userId, Guid roomId)
    {
        return new ConnectionInfo
        {
            UserId = userId,
            RoomId = roomId,
            ConnectedAt = DateTimeOffset.UtcNow
        };
    }
    
    public string ToJson()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        return JsonSerializer.Serialize(this, options);
    }
    
    public static ConnectionInfo? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
            
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            return JsonSerializer.Deserialize<ConnectionInfo>(json, options);
        }
        catch
        {
            return null;
        }
    }
}
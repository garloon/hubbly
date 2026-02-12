namespace Hubbly.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Token { get; private set; } = null!;
    public string DeviceId { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }

    private RefreshToken() { }
    
    public RefreshToken(Guid userId, string token, string deviceId, int expirationDays)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Token = token;
        DeviceId = deviceId;
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(expirationDays);
        IsRevoked = false;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void Revoke()
    {
        IsRevoked = true;
    }
    
    public void MarkAsUsed()
    {
        LastUsedAt = DateTimeOffset.UtcNow;
    }

    public bool IsActive()
    {
        return !IsRevoked && ExpiresAt > DateTimeOffset.UtcNow;
    }
}
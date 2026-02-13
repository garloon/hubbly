using Hubbly.Domain.Services;
using System.Text.Json;

namespace Hubbly.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string DeviceId { get; private set; } = null!;
    public string Nickname { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public string AvatarConfigJson { get; private set; } = "{}";
    public List<string> OwnedAssetIds { get; private set; } = new();

    // Навигационные свойства
    public ICollection<RefreshToken> RefreshTokens { get; private set; } = new List<RefreshToken>();

    private User() { } // Для EF Core

    public User(string deviceId, string nickname, string? avatarConfigJson = null)
    {
        Id = Guid.NewGuid();
        DeviceId = deviceId;
        Nickname = nickname;
        CreatedAt = DateTimeOffset.UtcNow;

        // Инициализируем аватар
        AvatarConfigJson = InitializeAvatarConfig(avatarConfigJson);
    }

    #region Публичные методы

    public void UpdateNickname(string newNickname)
    {
        if (string.IsNullOrWhiteSpace(newNickname))
            throw new ArgumentException("Nickname cannot be empty.");

        if (newNickname.Length > 50)
            throw new ArgumentException("Nickname cannot exceed 50 characters.");

        Nickname = newNickname;
    }

    public void UpdateAvatarConfig(string newConfigJson, IAvatarValidator? validator = null)
    {
        if (string.IsNullOrWhiteSpace(newConfigJson))
            throw new ArgumentException("Avatar config cannot be empty.");

        var config = ValidateAndParseAvatarConfig(newConfigJson, validator);
        AvatarConfigJson = config.ToJson();
    }

    public AvatarConfig GetAvatarConfig() =>
        AvatarConfig.FromJson(AvatarConfigJson);

    public void AddOwnedAsset(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
            throw new ArgumentException("Asset ID cannot be empty.");

        if (!OwnedAssetIds.Contains(assetId))
            OwnedAssetIds.Add(assetId);
    }

    public void UpdateDeviceId(string newDeviceId)
    {
        if (string.IsNullOrWhiteSpace(newDeviceId))
            throw new ArgumentException("DeviceId cannot be empty.");

        DeviceId = newDeviceId;
    }

    #endregion

    #region Приватные методы

    private string InitializeAvatarConfig(string? avatarConfigJson)
    {
        if (string.IsNullOrWhiteSpace(avatarConfigJson))
        {
            return AvatarConfig.DefaultMale.ToJson();
        }

        try
        {
            var config = AvatarConfig.FromJson(avatarConfigJson);
            return config.IsValid() ? config.ToJson() : AvatarConfig.DefaultMale.ToJson();
        }
        catch
        {
            return AvatarConfig.DefaultMale.ToJson();
        }
    }

    private AvatarConfig ValidateAndParseAvatarConfig(string configJson, IAvatarValidator? validator)
    {
        try
        {
            var config = AvatarConfig.FromJson(configJson);

            if (validator != null)
            {
                if (!validator.IsValidConfig(configJson, OwnedAssetIds))
                    throw new InvalidOperationException("Invalid avatar configuration.");
            }
            else
            {
                if (!config.IsValid())
                    throw new InvalidOperationException("Invalid avatar configuration.");
            }

            return config;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Invalid JSON format for avatar config.", ex);
        }
    }

    #endregion
}
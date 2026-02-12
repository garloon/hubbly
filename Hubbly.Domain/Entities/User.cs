using Hubbly.Domain.Services;
using System.Text.Json;

namespace Hubbly.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string DeviceId { get; private set; } = null!;
    public string Nickname { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    // ИЗМЕНЕНИЕ: Добавляем поле для конфига аватара
    public string AvatarConfigJson { get; private set; } = "{}";

    public List<string> OwnedAssetIds { get; private set; } = new();

    private User() { } // Для EF Core

    // ИЗМЕНЕНИЕ: Обновляем конструктор - принимаем avatarConfigJson
    public User(string deviceId, string nickname, string? avatarConfigJson = null)
    {
        Id = Guid.NewGuid();
        DeviceId = deviceId;
        Nickname = nickname;
        CreatedAt = DateTimeOffset.UtcNow;

        // Инициализируем аватар
        if (string.IsNullOrWhiteSpace(avatarConfigJson))
        {
            // Дефолтный мужской аватар
            AvatarConfigJson = AvatarConfig.DefaultMale.ToJson();
        }
        else
        {
            try
            {
                // Валидируем JSON перед сохранением
                var config = AvatarConfig.FromJson(avatarConfigJson);
                AvatarConfigJson = config.ToJson();
            }
            catch
            {
                // Если JSON невалидный - дефолтный
                AvatarConfigJson = AvatarConfig.DefaultMale.ToJson();
            }
        }

        OwnedAssetIds = new List<string>();
    }

    public void UpdateNickname(string newNickname)
    {
        if (string.IsNullOrWhiteSpace(newNickname))
            throw new ArgumentException("Nickname cannot be empty.");
        Nickname = newNickname;
    }

    // ИЗМЕНЕНИЕ: Добавляем метод для обновления аватара
    public void UpdateAvatarConfig(string newConfigJson, IAvatarValidator? validator = null)
    {
        if (string.IsNullOrWhiteSpace(newConfigJson))
            throw new ArgumentException("Avatar config cannot be empty.");

        try
        {
            // Парсим и валидируем конфиг
            var config = AvatarConfig.FromJson(newConfigJson);

            // Если есть валидатор - используем его
            if (validator != null)
            {
                // Для MVP валидация простая
                if (!config.IsValid())
                    throw new InvalidOperationException("Invalid avatar configuration.");
            }
            else
            {
                // Базовая валидация
                if (!config.IsValid())
                    throw new InvalidOperationException("Invalid avatar configuration.");
            }

            AvatarConfigJson = config.ToJson();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Invalid JSON format for avatar config.", ex);
        }
    }

    // ИЗМЕНЕНИЕ: Добавляем метод для получения конфига
    public AvatarConfig GetAvatarConfig() =>
        AvatarConfig.FromJson(AvatarConfigJson);

    public void AddOwnedAsset(string assetId)
    {
        if (!OwnedAssetIds.Contains(assetId))
            OwnedAssetIds.Add(assetId);
    }

    public void UpdateDeviceId(string newDeviceId)
    {
        if (string.IsNullOrWhiteSpace(newDeviceId))
            throw new ArgumentException("DeviceId cannot be empty.");

        DeviceId = newDeviceId;
    }
}
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hubbly.Domain.Entities;

/// <summary>
/// Конфигурация 3D аватара пользователя
/// </summary>
public class AvatarConfig
{
    /// <summary>
    /// Пол аватара: "male" или "female"
    /// </summary>
    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "male";

    /// <summary>
    /// ID базовой 3D модели (например: "male_base" или "female_base")
    /// </summary>
    [JsonPropertyName("baseModelId")]
    public string BaseModelId { get; set; } = "male_base";

    /// <summary>
    /// Поза аватара. Для MVP только "standing"
    /// </summary>
    [JsonPropertyName("pose")]
    public string Pose { get; set; } = "standing";

    /// <summary>
    /// Дополнительные компоненты (одежда, аксессуары и т.д.)
    /// Ключ: тип компонента (например, "hair", "shirt")
    /// Значение: ID компонента
    /// </summary>
    [JsonPropertyName("components")]
    public Dictionary<string, string> Components { get; set; } = new();

    // Статические методы для создания дефолтных конфигов

    /// <summary>
    /// Дефолтный мужской аватар
    /// </summary>
    public static AvatarConfig DefaultMale => new AvatarConfig
    {
        Gender = "male",
        BaseModelId = "male_base",
        Pose = "standing",
        Components = new Dictionary<string, string>()
    };

    /// <summary>
    /// Дефолтный женский аватар
    /// </summary>
    public static AvatarConfig DefaultFemale => new AvatarConfig
    {
        Gender = "female",
        BaseModelId = "female_base",
        Pose = "standing",
        Components = new Dictionary<string, string>()
    };

    /// <summary>
    /// Создает дефолтный конфиг на основе пола
    /// </summary>
    public static AvatarConfig DefaultForGender(string gender) =>
        gender?.ToLower() == "female" ? DefaultFemale : DefaultMale;

    // Сериализация/десериализация

    /// <summary>
    /// Конвертирует конфиг в JSON строку
    /// </summary>
    public string ToJson()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(this, options);
    }

    /// <summary>
    /// Создает конфиг из JSON строки
    /// </summary>
    public static AvatarConfig FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return DefaultMale;

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var config = JsonSerializer.Deserialize<AvatarConfig>(json, options);
            return config ?? DefaultMale;
        }
        catch (JsonException)
        {
            // Если JSON поврежден, возвращаем дефолтный
            return DefaultMale;
        }
    }

    /// <summary>
    /// Валидирует конфиг
    /// </summary>
    public bool IsValid()
    {
        // Проверяем обязательные поля
        if (string.IsNullOrWhiteSpace(Gender)) return false;
        if (string.IsNullOrWhiteSpace(BaseModelId)) return false;
        if (string.IsNullOrWhiteSpace(Pose)) return false;

        // Проверяем допустимые значения
        var validGenders = new[] { "male", "female" };
        var validPoses = new[] { "standing", "sitting", "lean", "handsonhips", "armscrossed" };

        if (!validGenders.Contains(Gender.ToLower())) return false;
        if (!validPoses.Contains(Pose.ToLower())) return false;

        // Базовая модель должна соответствовать полу
        if (Gender == "male" && !BaseModelId.Contains("male", StringComparison.OrdinalIgnoreCase))
            return false;
        if (Gender == "female" && !BaseModelId.Contains("female", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// Возвращает путь к 3D модели на основе конфига
    /// </summary>
    public string GetModelPath()
    {
        // Формируем путь к модели
        // В MVP: "assets/avatars/{BaseModelId}.glb"
        // В будущем: URL к CDN
        return $"assets/avatars/{BaseModelId}.glb";
    }

    /// <summary>
    /// Возвращает Emoji для предпросмотра
    /// </summary>
    public string GetPreviewEmoji() =>
        Gender.ToLower() == "female" ? "👩" : "👨";
}

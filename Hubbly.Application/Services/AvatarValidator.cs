using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Hubbly.Application.Services;

public class AvatarValidator : IAvatarValidator
{
    private readonly ILogger<AvatarValidator> _logger;
    private static readonly HashSet<string> ValidGenders = new() { "male", "female" };
    private static readonly HashSet<string> ValidPoses = new() { "standing", "sitting", "lean", "handsonhips", "armscrossed" };

    public AvatarValidator(ILogger<AvatarValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsValidConfig(string configJson, List<string> ownedAssetIds)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AvatarConfig>(configJson);
            if (config == null)
            {
                _logger.LogWarning("Avatar config deserialization failed");
                return false;
            }

            // Базовая валидация
            if (!config.IsValid())
            {
                _logger.LogWarning("Avatar config validation failed");
                return false;
            }

            // TODO: Когда появится система ассетов - добавить проверку ownedAssetIds
            // Сейчас просто пропускаем

            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error validating avatar config");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating avatar config");
            return false;
        }
    }
}
using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using System.Text.Json;

namespace Hubbly.Application.Services;

public class AvatarValidator : IAvatarValidator
{
    private readonly IAssetService _assetService;

    public AvatarValidator(IAssetService assetService)
    {
        _assetService = assetService;
    }

    public bool IsValidConfig(string configJson, List<string> ownedAssetIds)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AvatarConfig>(configJson);
            if (config == null) return false;

            // Проверяем, что все компоненты в конфиге принадлежат пользователю
            foreach (var component in config.Components)
            {
                if (!ownedAssetIds.Contains(component.Value))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}

namespace Hubbly.Domain.Services;

public interface IAvatarValidator
{
    bool IsValidConfig(string configJson, List<string> ownedAssetIds);
}

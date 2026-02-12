namespace Hubbly.Domain.Services;

public interface IAssetService
{
    Task<bool> IsAssetValidAsync(string assetId);
    Task<bool> IsAssetOwnedByUserAsync(Guid userId, string assetId);
}

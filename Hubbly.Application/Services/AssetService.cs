using Hubbly.Domain.Services;

namespace Hubbly.Application.Services;

public class AssetService : IAssetService
{
    private readonly IUserRepository _userRepository;

    public AssetService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<bool> IsAssetValidAsync(string assetId)
    {
        // TODO: Проверка, существует ли ассет в базе данных ассетов
        // Например, через IAssetRepository
        return true; // Заглушка
    }

    public async Task<bool> IsAssetOwnedByUserAsync(Guid userId, string assetId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        return user?.OwnedAssetIds.Contains(assetId) ?? false;
    }
}

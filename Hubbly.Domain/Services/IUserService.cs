using Hubbly.Domain.Dtos;

namespace Hubbly.Domain.Services;

public interface IUserService
{
    Task<UserProfileDto> GetUserProfileAsync(Guid userId);
    Task UpdateUserNicknameAsync(Guid userId, string newNickname);
    Task UpdateUserAvatarAsync(Guid userId, string newAvatarConfigJson);
    Task AddOwnedAssetAsync(Guid userId, string assetId);
}

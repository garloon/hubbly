using Hubbly.Domain.Dtos;
using Hubbly.Domain.Services;

namespace Hubbly.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<UserProfileDto> GetUserProfileAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new KeyNotFoundException("User not found.");

        return new UserProfileDto
        {
            Id = user.Id,
            Nickname = user.Nickname,
            AvatarConfigJson = user.AvatarConfigJson
        };
    }

    public async Task UpdateUserNicknameAsync(Guid userId, string newNickname)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new KeyNotFoundException("User not found.");

        user.UpdateNickname(newNickname);
        await _userRepository.UpdateAsync(user);
    }

    public async Task UpdateUserAvatarAsync(Guid userId, string newAvatarConfigJson)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new KeyNotFoundException("User not found.");
        
        user.UpdateAvatarConfig(newAvatarConfigJson);
        await _userRepository.UpdateAsync(user);
    }

    public async Task AddOwnedAssetAsync(Guid userId, string assetId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new KeyNotFoundException("User not found.");

        user.AddOwnedAsset(assetId);
        await _userRepository.UpdateAsync(user);
    }
}

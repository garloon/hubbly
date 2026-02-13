using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Hubbly.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepository,
        ILogger<UserService> logger)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Публичные методы

    public async Task<UserProfileDto> GetUserProfileAsync(Guid userId)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["UserId"] = userId }))
        {
            _logger.LogDebug("GetUserProfileAsync started");

            try
            {
                var user = await GetUserAsync(userId);
                var profile = CreateUserProfile(user);

                _logger.LogInformation("Retrieved profile for user {UserId}", userId);
                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get profile for user {UserId}", userId);
                throw;
            }
        }
    }

    public async Task UpdateUserNicknameAsync(Guid userId, string newNickname)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["UserId"] = userId,
            ["NewNickname"] = newNickname
        }))
        {
            _logger.LogInformation("UpdateUserNicknameAsync started");

            try
            {
                ValidateNickname(newNickname);

                var user = await GetUserAsync(userId);

                user.UpdateNickname(newNickname);
                await _userRepository.UpdateAsync(user);

                _logger.LogInformation("Updated nickname for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update nickname for user {UserId}", userId);
                throw;
            }
        }
    }

    public async Task UpdateUserAvatarAsync(Guid userId, string newAvatarConfigJson)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["UserId"] = userId,
            ["AvatarConfigLength"] = newAvatarConfigJson?.Length ?? 0
        }))
        {
            _logger.LogInformation("UpdateUserAvatarAsync started");

            try
            {
                if (string.IsNullOrWhiteSpace(newAvatarConfigJson))
                    throw new ArgumentException("Avatar config cannot be empty");

                ValidateAvatarConfig(newAvatarConfigJson);

                var user = await GetUserAsync(userId);

                user.UpdateAvatarConfig(newAvatarConfigJson);
                await _userRepository.UpdateAsync(user);

                _logger.LogInformation("Updated avatar for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update avatar for user {UserId}", userId);
                throw;
            }
        }
    }

    public async Task AddOwnedAssetAsync(Guid userId, string assetId)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["UserId"] = userId,
            ["AssetId"] = assetId
        }))
        {
            _logger.LogInformation("AddOwnedAssetAsync started");

            try
            {
                ValidateAssetId(assetId);

                var user = await GetUserAsync(userId);

                user.AddOwnedAsset(assetId);
                await _userRepository.UpdateAsync(user);

                _logger.LogInformation("Added asset {AssetId} to user {UserId}", assetId, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add asset {AssetId} to user {UserId}", assetId, userId);
                throw;
            }
        }
    }

    #endregion

    #region Приватные методы

    private async Task<User> GetUserAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User not found: {UserId}", userId);
            throw new KeyNotFoundException($"User with id {userId} not found.");
        }
        return user;
    }

    private void ValidateNickname(string nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            throw new ArgumentException("Nickname cannot be empty.");
        }

        if (nickname.Length > 50)
        {
            throw new ArgumentException("Nickname cannot exceed 50 characters.");
        }
    }

    private void ValidateAvatarConfig(string avatarConfig)
    {
        if (string.IsNullOrWhiteSpace(avatarConfig))
        {
            throw new ArgumentException("Avatar config cannot be empty.");
        }

        if (avatarConfig.Length > 2000)
        {
            throw new ArgumentException("Avatar config cannot exceed 2000 characters.");
        }
    }

    private void ValidateAssetId(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            throw new ArgumentException("Asset ID cannot be empty.");
        }
    }

    private UserProfileDto CreateUserProfile(User user)
    {
        return new UserProfileDto
        {
            Id = user.Id,
            Nickname = user.Nickname,
            AvatarConfigJson = user.AvatarConfigJson,
            IsGuest = true // По умолчанию все гости, потом будет меняться
        };
    }

    #endregion
}
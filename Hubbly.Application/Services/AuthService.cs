using Hubbly.Domain.Common;
using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Hubbly.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly JwtTokenService _jwtTokenService;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        JwtTokenService jwtTokenService,
        JwtSettings jwtSettings,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _refreshTokenRepository = refreshTokenRepository ?? throw new ArgumentNullException(nameof(refreshTokenRepository));
        _jwtTokenService = jwtTokenService ?? throw new ArgumentNullException(nameof(jwtTokenService));
        _jwtSettings = jwtSettings ?? throw new ArgumentNullException(nameof(jwtSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Публичные методы

    public async Task<AuthResponseDto> AuthenticateGuestAsync(string clientDeviceId, string? avatarConfigJson = null)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["DeviceId"] = clientDeviceId }))
        {
            _logger.LogInformation("Starting guest authentication");

            try
            {
                ValidateDeviceId(clientDeviceId);

                var user = await GetOrCreateUserAsync(clientDeviceId, avatarConfigJson);

                await _refreshTokenRepository.RevokeAllForDeviceAsync(user.Id, clientDeviceId);

                var (accessToken, refreshToken) = await GenerateTokenPairAsync(user, clientDeviceId);

                _logger.LogInformation("Guest authenticated successfully: UserId {UserId}", user.Id);

                return CreateAuthResponse(user, accessToken, refreshToken, clientDeviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authentication failed for device {DeviceId}", clientDeviceId);
                throw;
            }
        }
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, string deviceId)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["DeviceId"] = deviceId }))
        {
            _logger.LogInformation("Starting token refresh");

            try
            {
                ValidateRefreshRequest(refreshToken, deviceId);

                var storedToken = await GetAndValidateStoredTokenAsync(refreshToken, deviceId);
                var user = await GetUserAsync(storedToken.UserId);

                await RevokeOldTokenAsync(storedToken);
                var (newAccessToken, newRefreshToken) = await GenerateTokenPairAsync(user, deviceId);

                await _refreshTokenRepository.CleanupOldDeviceTokensAsync(user.Id, deviceId, keepLast: 3);

                _logger.LogInformation("Token refreshed successfully for user {UserId}", user.Id);

                return CreateAuthResponse(user, newAccessToken, newRefreshToken, deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token refresh failed");
                throw;
            }
        }
    }

    #endregion

    #region Приватные методы

    private void ValidateDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("DeviceId is required");
    }

    private void ValidateRefreshRequest(string refreshToken, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("Refresh token is required");

        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("DeviceId is required");
    }

    private async Task<User> GetOrCreateUserAsync(string deviceId, string? avatarConfigJson)
    {
        var existingUser = await _userRepository.GetByDeviceIdAsync(deviceId);

        if (existingUser != null)
        {
            await UpdateUserAvatarIfNeededAsync(existingUser, avatarConfigJson);
            return existingUser;
        }

        return await CreateNewUserAsync(deviceId, avatarConfigJson);
    }

    private async Task UpdateUserAvatarIfNeededAsync(User user, string? avatarConfigJson)
    {
        if (!string.IsNullOrEmpty(avatarConfigJson) && user.AvatarConfigJson != avatarConfigJson)
        {
            user.UpdateAvatarConfig(avatarConfigJson);
            await _userRepository.UpdateAsync(user);
            _logger.LogDebug("Updated avatar for user {UserId}", user.Id);
        }
    }

    private async Task<User> CreateNewUserAsync(string deviceId, string? avatarConfigJson)
    {
        var nickname = await GenerateGuestNicknameAsync();
        var user = new User(deviceId, nickname, avatarConfigJson);
        await _userRepository.AddAsync(user);
        _logger.LogInformation("Created new user: UserId {UserId}, Nickname {Nickname}", user.Id, user.Nickname);
        return user;
    }

    private async Task<(string accessToken, string refreshToken)> GenerateTokenPairAsync(User user, string deviceId)
    {
        var accessToken = _jwtTokenService.GenerateAccessToken(user.Id, user.Nickname);
        var refreshToken = _jwtTokenService.GenerateRefreshToken();

        var refreshTokenEntity = new RefreshToken(
            user.Id,
            refreshToken,
            deviceId,
            _jwtSettings.RefreshTokenExpirationDays
        );

        await _refreshTokenRepository.AddAsync(refreshTokenEntity);

        _logger.LogDebug("Generated tokens for user {UserId}", user.Id);

        return (accessToken, refreshToken);
    }

    private async Task<RefreshToken> GetAndValidateStoredTokenAsync(string refreshToken, string deviceId)
    {
        var storedToken = await _refreshTokenRepository.GetByTokenAndDeviceAsync(refreshToken, deviceId);

        if (storedToken == null)
        {
            _logger.LogWarning("Refresh token not found");
            throw new SecurityTokenException("Refresh token not found");
        }

        if (!storedToken.IsActive())
        {
            _logger.LogWarning("Refresh token is not active - UserId: {UserId}", storedToken.UserId);
            throw new SecurityTokenException("Refresh token expired or revoked");
        }

        return storedToken;
    }

    private async Task<User> GetUserAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            _logger.LogError("User not found for refresh token - UserId: {UserId}", userId);
            throw new KeyNotFoundException("User not found");
        }
        return user;
    }

    private async Task RevokeOldTokenAsync(RefreshToken token)
    {
        token.Revoke();
        token.MarkAsUsed();
        await _refreshTokenRepository.UpdateAsync(token);
        _logger.LogDebug("Revoked old refresh token for user {UserId}", token.UserId);
    }

    private AuthResponseDto CreateAuthResponse(User user, string accessToken, string refreshToken, string deviceId)
    {
        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = CreateUserProfile(user, isGuest: true),
            DeviceId = deviceId
        };
    }

    private UserProfileDto CreateUserProfile(User user, bool isGuest = true)
    {
        return new UserProfileDto
        {
            Id = user.Id,
            Nickname = user.Nickname,
            AvatarConfigJson = user.AvatarConfigJson,
            IsGuest = isGuest
        };
    }

    private async Task<string> GenerateGuestNicknameAsync()
    {
        var random = new Random();
        var attempts = 0;
        const int maxAttempts = 10;

        while (attempts < maxAttempts)
        {
            var suffix = random.Next(1000, 9999);
            var candidate = $"Guest_{suffix}";

            var existing = await _userRepository.GetByNicknameAsync(candidate);
            if (existing == null)
            {
                return candidate;
            }

            attempts++;
        }

        // Крайне маловероятно, но на всякий случай
        return $"Guest_{Guid.NewGuid():N}"[..12];
    }

    #endregion
}
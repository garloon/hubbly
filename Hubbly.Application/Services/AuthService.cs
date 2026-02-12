using Hubbly.Domain.Common;
using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.AspNetCore.Http;
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
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _jwtTokenService = jwtTokenService;
        _jwtSettings = jwtSettings;
        _logger = logger;
    }

    /// <summary>
    /// Аутентификация гостя по DeviceId
    /// </summary>
    /// <param name="clientDeviceId">DeviceId, сгенерированный клиентом</param>
    /// <param name="nickname">Желаемый никнейм</param>
    /// <param name="avatarConfigJson">JSON конфигурации аватара (опционально)</param>
    public async Task<AuthResponseDto> AuthenticateGuestAsync(
        string clientDeviceId,
        string? avatarConfigJson = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(clientDeviceId))
                throw new ArgumentException("DeviceId is required");
            
            var existingUser = await _userRepository.GetByDeviceIdAsync(clientDeviceId);

            User user;
            bool isNewUser = false;

            if (existingUser == null)
            {
                var nickname = await GenerateGuestNicknameAsync();
                
                user = new User(clientDeviceId, nickname, avatarConfigJson);
                await _userRepository.AddAsync(user);
                isNewUser = true;

                _logger.LogInformation("Created new user - UserId: {UserId}, DeviceId: {DeviceId}, Nickname: {Nickname}",
                    user.Id, clientDeviceId, user.Nickname);
            }
            else
            {
                user = existingUser;
                
                if (!string.IsNullOrEmpty(avatarConfigJson) &&
                    user.AvatarConfigJson != avatarConfigJson)
                {
                    user.UpdateAvatarConfig(avatarConfigJson);
                    await _userRepository.UpdateAsync(user);
                }
            }
            
            await _refreshTokenRepository.RevokeAllForDeviceAsync(user.Id, clientDeviceId);
            
            var accessToken = _jwtTokenService.GenerateAccessToken(user.Id, user.Nickname);
            var refreshToken = _jwtTokenService.GenerateRefreshToken();

            var refreshTokenEntity = new RefreshToken(
                user.Id,
                refreshToken,
                clientDeviceId,
                _jwtSettings.RefreshTokenExpirationDays
            );

            await _refreshTokenRepository.AddAsync(refreshTokenEntity);

            _logger.LogDebug("Generated tokens for user {UserId} - Access token expires in {AccessExpiration}min, Refresh token expires in {RefreshExpiration}days",
                user.Id, _jwtSettings.AccessTokenExpirationMinutes, _jwtSettings.RefreshTokenExpirationDays);
            
            return new AuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                User = new UserProfileDto
                {
                    Id = user.Id,
                    Nickname = user.Nickname,
                    AvatarConfigJson = user.AvatarConfigJson,
                    IsGuest = true
                },
                DeviceId = clientDeviceId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed for device {DeviceId}", clientDeviceId);
            throw;
        }
    }

    /// <summary>
    /// Обновление access token'а по refresh token'у
    /// </summary>
    public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, string deviceId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new ArgumentException("Refresh token is required");

            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentException("DeviceId is required");


            var storedToken = await _refreshTokenRepository.GetByTokenAndDeviceAsync(refreshToken, deviceId);
            
            if (storedToken == null)
            {
                _logger.LogWarning("Refresh token not found: {RefreshToken}", refreshToken.Substring(0, 10) + "...");
                throw new SecurityTokenException("Refresh token not found");
            }

            if (!storedToken.IsActive())
            {
                _logger.LogWarning("Refresh token is not active - UserId: {UserId}, Expired: {ExpiresAt}, Revoked: {IsRevoked}",
                    storedToken.UserId, storedToken.ExpiresAt, storedToken.IsRevoked);
                throw new SecurityTokenException("Refresh token expired or revoked");
            }
            
            var user = await _userRepository.GetByIdAsync(storedToken.UserId);
            if (user == null)
            {
                _logger.LogError("User not found for refresh token - UserId: {UserId}", storedToken.UserId);
                throw new KeyNotFoundException("User not found");
            }
            
            storedToken.Revoke();
            storedToken.MarkAsUsed();
            await _refreshTokenRepository.UpdateAsync(storedToken);

            _logger.LogDebug("Revoked old refresh token for user {UserId}", user.Id);
            
            var newAccessToken = _jwtTokenService.GenerateAccessToken(user.Id, user.Nickname);
            var newRefreshToken = _jwtTokenService.GenerateRefreshToken();
            
            var newTokenEntity = new RefreshToken(
                user.Id,
                newRefreshToken,
                deviceId,
                _jwtSettings.RefreshTokenExpirationDays
            );

            await _refreshTokenRepository.AddAsync(newTokenEntity);
            
            await _refreshTokenRepository.CleanupOldDeviceTokensAsync(user.Id, deviceId, keepLast: 3);

            _logger.LogInformation("Successfully refreshed tokens for user {UserId}", user.Id);
            
            return new AuthResponseDto
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                User = new UserProfileDto
                {
                    Id = user.Id,
                    Nickname = user.Nickname,
                    AvatarConfigJson = user.AvatarConfigJson,
                    IsGuest = true
                },
                DeviceId = deviceId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh failed");
            throw;
        }
    }
    
    private async Task<string> GenerateGuestNicknameAsync()
    {
        var random = new Random();
        int attempts = 0;

        while (attempts < 10)
        {
            var suffix = random.Next(1000, 9999);
            var candidate = $"Guest_{suffix}";

            var existing = await _userRepository.GetByNicknameAsync(candidate);
            if (existing == null)
                return candidate;

            attempts++;
        }

        // Крайне маловероятно, но на всякий случай
        return $"Guest_{Guid.NewGuid():N}"[..12];
    }
}
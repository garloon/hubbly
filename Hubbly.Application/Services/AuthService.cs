using Hubbly.Domain.Common;
using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Hubbly.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly JwtTokenService _jwtTokenService;
    private readonly JwtSettings _jwtSettings;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthService> _logger;

    private static readonly ConcurrentDictionary<string, DateTime> _recentRegistrations = new();
    private const int MAX_REGISTRATIONS_PER_IP_PER_HOUR = 5;
    private const int DEVICE_ID_HASH_LENGTH = 32;

    public AuthService(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        JwtTokenService jwtTokenService,
        JwtSettings jwtSettings,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _jwtTokenService = jwtTokenService;
        _jwtSettings = jwtSettings;
        _httpContextAccessor = httpContextAccessor;
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
        string nickname,
        string? avatarConfigJson = null)
    {
        try
        {
            // 1. Валидация входных данных
            if (string.IsNullOrWhiteSpace(clientDeviceId))
                throw new ArgumentException("DeviceId is required");

            if (string.IsNullOrWhiteSpace(nickname))
                throw new ArgumentException("Nickname is required");

            // 2. Проверка лимитов для IP (защита от спама)
            var ip = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
            if (!await IsIpAllowedToRegisterAsync(ip))
            {
                _logger.LogWarning("Too many registrations from IP: {IP}", ip);
                throw new UnauthorizedAccessException("Too many registration attempts. Please try again later.");
            }

            // 3. Генерируем серверный DeviceId (безопасный, детерминированный)
            var serverDeviceId = await GenerateServerDeviceIdAsync(clientDeviceId);

            _logger.LogDebug("Auth attempt - Client DeviceId: {ClientDeviceId}, Server DeviceId: {ServerDeviceId}",
                clientDeviceId, serverDeviceId);

            // 4. Ищем существующего пользователя по серверному DeviceId
            var existingUser = await _userRepository.GetByDeviceIdAsync(serverDeviceId);

            User user;
            bool isNewUser = false;

            if (existingUser == null)
            {
                // 5. Создаём нового пользователя
                var finalNickname = await GenerateUniqueNicknameAsync(nickname);
                user = new User(serverDeviceId, finalNickname, avatarConfigJson);
                await _userRepository.AddAsync(user);
                isNewUser = true;

                _logger.LogInformation("Created new user - UserId: {UserId}, DeviceId: {DeviceId}, Nickname: {Nickname}",
                    user.Id, serverDeviceId, user.Nickname);
            }
            else
            {
                user = existingUser;

                _logger.LogInformation("Found existing user - UserId: {UserId}, DeviceId: {DeviceId}, Nickname: {Nickname}",
                    user.Id, serverDeviceId, user.Nickname);

                // 6. Обновляем никнейм, если изменился
                if (user.Nickname != nickname)
                {
                    var nicknameAvailable = await IsNicknameAvailableAsync(nickname, user.Id);
                    if (nicknameAvailable)
                    {
                        user.UpdateNickname(nickname);
                        await _userRepository.UpdateAsync(user);
                        _logger.LogInformation("Updated nickname for user {UserId}: {NewNickname}", user.Id, nickname);
                    }
                    else
                    {
                        var uniqueNickname = await GenerateUniqueNicknameAsync(nickname);
                        user.UpdateNickname(uniqueNickname);
                        await _userRepository.UpdateAsync(user);
                        _logger.LogInformation("Generated unique nickname for user {UserId}: {NewNickname}",
                            user.Id, uniqueNickname);
                    }
                }

                // 7. Обновляем аватар, если передан
                if (!string.IsNullOrEmpty(avatarConfigJson) &&
                    user.AvatarConfigJson != avatarConfigJson)
                {
                    user.UpdateAvatarConfig(avatarConfigJson);
                    await _userRepository.UpdateAsync(user);
                    _logger.LogInformation("Updated avatar for user {UserId}", user.Id);
                }
            }

            // 8. Отзываем все старые refresh token'ы для этого устройства
            await _refreshTokenRepository.RevokeAllForDeviceAsync(user.Id, serverDeviceId);

            // 9. Генерируем новые токены
            var accessToken = _jwtTokenService.GenerateAccessToken(user.Id, user.Nickname);
            var refreshToken = _jwtTokenService.GenerateRefreshToken();

            var refreshTokenEntity = new RefreshToken(
                user.Id,
                refreshToken,
                serverDeviceId,
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
    public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, string clientDeviceId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new ArgumentException("Refresh token is required");

            if (string.IsNullOrWhiteSpace(clientDeviceId))
                throw new ArgumentException("DeviceId is required");

            // 1. Генерируем серверный DeviceId ТАК ЖЕ, как при аутентификации
            var serverDeviceId = await GenerateServerDeviceIdAsync(clientDeviceId);

            _logger.LogDebug("Refresh attempt - Client DeviceId: {ClientDeviceId}, Server DeviceId: {ServerDeviceId}",
                clientDeviceId, serverDeviceId);

            // 2. Ищем токен по серверному DeviceId
            var storedToken = await _refreshTokenRepository.GetByTokenAndDeviceAsync(refreshToken, serverDeviceId);

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

            // 3. Получаем пользователя
            var user = await _userRepository.GetByIdAsync(storedToken.UserId);
            if (user == null)
            {
                _logger.LogError("User not found for refresh token - UserId: {UserId}", storedToken.UserId);
                throw new KeyNotFoundException("User not found");
            }

            // 4. Отзываем старый refresh token
            storedToken.Revoke();
            storedToken.MarkAsUsed();
            await _refreshTokenRepository.UpdateAsync(storedToken);

            _logger.LogDebug("Revoked old refresh token for user {UserId}", user.Id);

            // 5. Генерируем новые токены
            var newAccessToken = _jwtTokenService.GenerateAccessToken(user.Id, user.Nickname);
            var newRefreshToken = _jwtTokenService.GenerateRefreshToken();

            // 6. Сохраняем новый refresh token
            var newTokenEntity = new RefreshToken(
                user.Id,
                newRefreshToken,
                serverDeviceId,
                _jwtSettings.RefreshTokenExpirationDays
            );

            await _refreshTokenRepository.AddAsync(newTokenEntity);

            // 7. Очищаем старые токены (оставляем только последние 3 для этого устройства)
            await _refreshTokenRepository.CleanupOldDeviceTokensAsync(user.Id, serverDeviceId, keepLast: 3);

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
                DeviceId = clientDeviceId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh failed");
            throw;
        }
    }

    /// <summary>
    /// Генерирует серверный DeviceId на основе клиентского ID, IP и UserAgent
    /// </summary>
    private Task<string> GenerateServerDeviceIdAsync(string clientDeviceId)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var ip = httpContext?.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";
        var userAgent = httpContext?.Request.Headers["User-Agent"].ToString() ?? "unknown";

        // Соль для дополнительной безопасности (должна храниться в конфиге)
        const string pepper = "hubbly-secure-device-pepper-2026";

        // Комбинируем факторы для создания уникального, но детерминированного ID
        var combined = $"{ip}_{userAgent}_{clientDeviceId}_{pepper}";

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));

        // URL-safe base64 (без символов / и +)
        var serverDeviceId = Convert.ToBase64String(hash)
            .Replace("/", "_")
            .Replace("+", "-")
            [..DEVICE_ID_HASH_LENGTH];  // Обрезаем до разумной длины

        return Task.FromResult(serverDeviceId);
    }

    /// <summary>
    /// Генерирует уникальный никнейм, если запрошенный уже занят
    /// </summary>
    private async Task<string> GenerateUniqueNicknameAsync(string baseNickname)
    {
        // Очищаем никнейм от недопустимых символов
        baseNickname = SanitizeNickname(baseNickname);

        // Если ник свободен - возвращаем как есть
        if (await IsNicknameAvailableAsync(baseNickname, null))
            return baseNickname;

        var random = new Random();
        int attempts = 0;

        while (attempts < 10)
        {
            var suffix = random.Next(1000, 9999);
            var candidate = $"{baseNickname}{suffix}";

            if (candidate.Length > 20)
                candidate = $"{baseNickname[..15]}{suffix}";

            if (await IsNicknameAvailableAsync(candidate, null))
                return candidate;

            attempts++;
        }

        // Если все варианты заняты - генерируем случайный
        return $"Guest{Guid.NewGuid():N}"[..20];
    }

    /// <summary>
    /// Очищает никнейм от недопустимых символов
    /// </summary>
    private string SanitizeNickname(string nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
            return "Guest";

        // Удаляем недопустимые символы, оставляем буквы, цифры и _
        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            nickname.Trim(),
            @"[^\w\u0400-\u04FF]",
            "");

        // Ограничиваем длину
        if (sanitized.Length > 20)
            sanitized = sanitized[..20];

        // Если после очистки ничего не осталось
        if (string.IsNullOrWhiteSpace(sanitized))
            return $"Guest{Guid.NewGuid():N}"[..10];

        return sanitized;
    }

    /// <summary>
    /// Проверяет, доступен ли никнейм
    /// </summary>
    private async Task<bool> IsNicknameAvailableAsync(string nickname, Guid? excludeUserId)
    {
        var userWithSameNick = await _userRepository.GetByNicknameAsync(nickname);

        if (userWithSameNick == null)
            return true;

        if (excludeUserId.HasValue && userWithSameNick.Id == excludeUserId.Value)
            return true;

        return false;
    }

    /// <summary>
    /// Проверяет лимиты регистраций для IP
    /// </summary>
    private Task<bool> IsIpAllowedToRegisterAsync(string? ip)
    {
        if (string.IsNullOrEmpty(ip) || ip == "0.0.0.0" || ip == "::1")
            return Task.FromResult(true);

        var cutoff = DateTime.UtcNow.AddHours(-1);
        var count = _recentRegistrations.Count(x =>
            x.Key == ip && x.Value > cutoff);

        if (count >= MAX_REGISTRATIONS_PER_IP_PER_HOUR)
        {
            return Task.FromResult(false);
        }

        _recentRegistrations[ip] = DateTime.UtcNow;

        // Очистка старых записей
        var toRemove = _recentRegistrations
            .Where(x => x.Value < cutoff)
            .Select(x => x.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _recentRegistrations.TryRemove(key, out _);
        }

        return Task.FromResult(true);
    }
}
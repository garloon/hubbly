using Hubbly.Domain.Common;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Hubbly.Application.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<JwtTokenService> _logger;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IMemoryCache _cache;
    private const string CacheKeyPrefix = "jwt_user_active_";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public JwtTokenService(
        JwtSettings jwtSettings,
        ILogger<JwtTokenService> logger,
        IRefreshTokenRepository refreshTokenRepository,
        IMemoryCache cache)
    {
        _jwtSettings = jwtSettings ?? throw new ArgumentNullException(nameof(jwtSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _refreshTokenRepository = refreshTokenRepository ?? throw new ArgumentNullException(nameof(refreshTokenRepository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    #region Public methods

    public string GenerateAccessToken(Guid userId, string nickname)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["UserId"] = userId,
            ["Nickname"] = nickname
        }))
        {
            _logger.LogDebug("Generating access token");

            try
            {
                var claims = CreateClaims(userId, nickname);
                var key = CreateSigningKey();
                var credentials = CreateSigningCredentials(key);
                var token = CreateToken(claims, credentials);

                var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

                _logger.LogDebug("Access token generated successfully");
                return tokenString;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate access token");
                throw;
            }
        }
    }

    public string GenerateRefreshToken()
    {
        try
        {
            var randomNumber = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            var refreshToken = Convert.ToBase64String(randomNumber);

            _logger.LogTrace("Refresh token generated");
            return refreshToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate refresh token");
            throw;
        }
    }

    public async Task<(bool isValid, ClaimsPrincipal? principal)> ValidateAccessTokenAsync(string token)
    {
        ClaimsPrincipal? principal = null;

        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_jwtSettings.Secret);

            principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _jwtSettings.Issuer,
                ValidAudience = _jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero
            }, out _);

            _logger.LogTrace("Token validated successfully");

            // Extract userId and check for active refresh tokens
            var userId = GetUserIdFromToken(token);
            if (userId.HasValue)
            {
                var cacheKey = $"{CacheKeyPrefix}{userId.Value}";
                if (!_cache.TryGetValue(cacheKey, out bool hasActiveTokens))
                {
                    // Cache miss - query database
                    hasActiveTokens = await _refreshTokenRepository.HasActiveRefreshTokensAsync(userId.Value);
                    _cache.Set(cacheKey, hasActiveTokens, CacheDuration);
                    _logger.LogDebug("Cached active token status for user {UserId}: {HasActive}", userId.Value, hasActiveTokens);
                }
                else
                {
                    _logger.LogDebug("Cache hit for user {UserId} active token status: {HasActive}", userId.Value, hasActiveTokens);
                }

                if (!hasActiveTokens)
                {
                    _logger.LogWarning("User {UserId} has no active refresh tokens - invalidating access token", userId.Value);
                    return (false, null);
                }
            }
            else
            {
                _logger.LogWarning("Could not extract userId from token for revocation check");
                return (false, null);
            }

            return (true, principal);
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogDebug("Token expired");
            return (false, null);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return (false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating token");
            return (false, null);
        }
    }

    public Guid? GetUserIdFromToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadJwtToken(token);

            var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "userId");
            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                _logger.LogTrace("Extracted userId from token: {UserId}", userId);
                return userId;
            }

            _logger.LogWarning("userId claim not found in token");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract userId from token");
            return null;
        }
    }

    #endregion

    #region Private methods

    private Claim[] CreateClaims(Guid userId, string nickname)
    {
        return new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, nickname),
            new Claim("userId", userId.ToString())
        };
    }

    private SymmetricSecurityKey CreateSigningKey()
    {
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
    }

    private SigningCredentials CreateSigningCredentials(SymmetricSecurityKey key)
    {
        return new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    private JwtSecurityToken CreateToken(Claim[] claims, SigningCredentials credentials)
    {
        return new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            signingCredentials: credentials
        );
    }

    #endregion
}
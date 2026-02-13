using Hubbly.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Hubbly.Application.Services;

public class JwtTokenService
{
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<JwtTokenService> _logger;

    public JwtTokenService(
        JwtSettings jwtSettings,
        ILogger<JwtTokenService> logger)
    {
        _jwtSettings = jwtSettings ?? throw new ArgumentNullException(nameof(jwtSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Публичные методы

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

    public bool ValidateAccessToken(string token, out ClaimsPrincipal? principal)
    {
        principal = null;

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
            return true;
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogDebug("Token expired");
            return false;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating token");
            return false;
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

    #region Приватные методы

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
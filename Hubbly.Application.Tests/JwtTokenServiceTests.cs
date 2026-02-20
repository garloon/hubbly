using Hubbly.Application.Services;
using Hubbly.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Hubbly.Application.Tests;

public class JwtTokenServiceTests
{
    private readonly JwtSettings _jwtSettings;
    private readonly JwtTokenService _jwtTokenService;

    public JwtTokenServiceTests()
    {
        _jwtSettings = new JwtSettings
        {
            Secret = "test-secret-key-that-is-at-least-32-characters-long",
            Issuer = "HubblyTest",
            Audience = "HubblyUsersTest",
            AccessTokenExpirationMinutes = 15
        };

        var loggerMock = new Mock<ILogger<JwtTokenService>>();
        _jwtTokenService = new JwtTokenService(_jwtSettings, loggerMock.Object);
    }

    [Fact]
    public void GenerateAccessToken_ShouldReturnValidJwtToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var nickname = "TestUser";

        // Act
        var token = _jwtTokenService.GenerateAccessToken(userId, nickname);

        // Assert
        Assert.NotNull(token);
        Assert.NotEmpty(token);
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturnNonEmptyToken()
    {
        // Act
        var token = _jwtTokenService.GenerateRefreshToken();

        // Assert
        Assert.NotNull(token);
        Assert.NotEmpty(token);
        // 32 bytes base64 encoded = 44 characters
        Assert.Equal(44, token.Length);
    }

    [Fact]
    public void ValidateAccessToken_WithValidToken_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var nickname = "TestUser";
        var token = _jwtTokenService.GenerateAccessToken(userId, nickname);

        // Act
        var result = _jwtTokenService.ValidateAccessToken(token, out var principal);

        // Assert
        Assert.True(result);
        Assert.NotNull(principal);
        Assert.Equal(userId.ToString(), principal.FindFirst("userId")?.Value);
        Assert.Equal(nickname, principal.FindFirst(ClaimTypes.Name)?.Value);
    }

    [Fact]
    public void ValidateAccessToken_WithInvalidToken_ReturnsFalse()
    {
        // Act
        var result = _jwtTokenService.ValidateAccessToken("invalid-token", out var principal);

        // Assert
        Assert.False(result);
        Assert.Null(principal);
    }

    [Fact]
    public void ValidateAccessToken_WithNullToken_ReturnsFalse()
    {
        // Act
        var result = _jwtTokenService.ValidateAccessToken(null!, out var principal);

        // Assert
        Assert.False(result);
        Assert.Null(principal);
    }

    [Fact]
    public void ValidateAccessToken_WithEmptyToken_ReturnsFalse()
    {
        // Act
        var result = _jwtTokenService.ValidateAccessToken("", out var principal);

        // Assert
        Assert.False(result);
        Assert.Null(principal);
    }

    [Fact]
    public void GetUserIdFromToken_WithValidToken_ReturnsUserId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var token = _jwtTokenService.GenerateAccessToken(userId, "TestUser");

        // Act
        var extractedUserId = _jwtTokenService.GetUserIdFromToken(token);

        // Assert
        Assert.NotNull(extractedUserId);
        Assert.Equal(userId, extractedUserId);
    }

    [Fact]
    public void GetUserIdFromToken_WithInvalidToken_ReturnsNull()
    {
        // Act
        var extractedUserId = _jwtTokenService.GetUserIdFromToken("invalid-token");

        // Assert
        Assert.Null(extractedUserId);
    }

    [Fact]
    public void GenerateAccessToken_TokenContainsCorrectClaims()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var nickname = "TestUser";

        // Act
        var token = _jwtTokenService.GenerateAccessToken(userId, nickname);
        _jwtTokenService.ValidateAccessToken(token, out var principal);

        // Assert
        Assert.NotNull(principal);
        // Check for our custom claims
        Assert.Contains(principal.Claims, c => c.Type == "userId" && c.Value == userId.ToString());
        Assert.Contains(principal.Claims, c => c.Type == ClaimTypes.Name && c.Value == nickname);
        // Jti claim is preserved as is
        Assert.Contains(principal.Claims, c => c.Type == JwtRegisteredClaimNames.Jti);
        // Sub claim gets mapped to ClaimTypes.NameIdentifier by JWT handler
        Assert.Contains(principal.Claims, c => c.Type == ClaimTypes.NameIdentifier && c.Value == userId.ToString());
    }
}
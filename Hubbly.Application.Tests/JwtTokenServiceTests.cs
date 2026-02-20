using FluentAssertions;
using Hubbly.Application.Services;
using Hubbly.Domain.Common;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock;
    private readonly IMemoryCache _memoryCache;

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
        _refreshTokenRepositoryMock = new Mock<IRefreshTokenRepository>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());

        _jwtTokenService = new JwtTokenService(
            _jwtSettings,
            loggerMock.Object,
            _refreshTokenRepositoryMock.Object,
            _memoryCache);
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
    public async Task ValidateAccessToken_WithValidToken_AndActiveRefreshToken_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var nickname = "TestUser";
        var token = _jwtTokenService.GenerateAccessToken(userId, nickname);

        // Mock repository to return true (user has active refresh tokens)
        _refreshTokenRepositoryMock
            .Setup(r => r.HasActiveRefreshTokensAsync(userId))
            .ReturnsAsync(true);

        // Act
        var (isValid, principal) = await _jwtTokenService.ValidateAccessTokenAsync(token);

        // Assert
        Assert.True(isValid);
        Assert.NotNull(principal);
        Assert.Equal(userId.ToString(), principal.FindFirst("userId")?.Value);
        Assert.Equal(nickname, principal.FindFirst(ClaimTypes.Name)?.Value);

        // Verify repository was called
        _refreshTokenRepositoryMock.Verify(
            r => r.HasActiveRefreshTokensAsync(userId),
            Times.Once);
    }

    [Fact]
    public async Task ValidateAccessToken_WithValidToken_ButNoActiveRefreshToken_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var nickname = "TestUser";
        var token = _jwtTokenService.GenerateAccessToken(userId, nickname);

        // Mock repository to return false (user has no active refresh tokens)
        _refreshTokenRepositoryMock
            .Setup(r => r.HasActiveRefreshTokensAsync(userId))
            .ReturnsAsync(false);

        // Act
        var (isValid, principal) = await _jwtTokenService.ValidateAccessTokenAsync(token);

        // Assert
        Assert.False(isValid);
        Assert.Null(principal);

        // Verify repository was called
        _refreshTokenRepositoryMock.Verify(
            r => r.HasActiveRefreshTokensAsync(userId),
            Times.Once);
    }

    [Fact]
    public async Task ValidateAccessToken_CachesResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var nickname = "TestUser";
        var token = _jwtTokenService.GenerateAccessToken(userId, nickname);

        // Use a separate cache instance to test caching behavior
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        
        var service = new JwtTokenService(
            _jwtSettings,
            Mock.Of<ILogger<JwtTokenService>>(),
            _refreshTokenRepositoryMock.Object,
            memoryCache);

        _refreshTokenRepositoryMock
            .Setup(r => r.HasActiveRefreshTokensAsync(userId))
            .ReturnsAsync(true);

        // Act - first call (cache miss)
        var (isValid1, _) = await service.ValidateAccessTokenAsync(token);
        Assert.True(isValid1);

        // Reset the mock to verify second call uses cache
        _refreshTokenRepositoryMock
            .Setup(r => r.HasActiveRefreshTokensAsync(userId))
            .ReturnsAsync(false); // Should not be called if cache is used

        // Act - second call (should hit cache)
        var (isValid2, _) = await service.ValidateAccessTokenAsync(token);
        Assert.True(isValid2);

        // Verify repository was called only once (second call used cache)
        _refreshTokenRepositoryMock.Verify(
            r => r.HasActiveRefreshTokensAsync(userId),
            Times.Once);
    }

    [Fact]
    public async Task ValidateAccessToken_WithInvalidToken_ReturnsFalse()
    {
        // Act
        var (isValid, principal) = await _jwtTokenService.ValidateAccessTokenAsync("invalid-token");

        // Assert
        Assert.False(isValid);
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateAccessToken_WithNullToken_ReturnsFalse()
    {
        // Act
        var (isValid, principal) = await _jwtTokenService.ValidateAccessTokenAsync(null!);

        // Assert
        Assert.False(isValid);
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateAccessToken_WithEmptyToken_ReturnsFalse()
    {
        // Act
        var (isValid, principal) = await _jwtTokenService.ValidateAccessTokenAsync("");

        // Assert
        Assert.False(isValid);
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateAccessToken_WithValidToken_ButCannotExtractUserId_ReturnsFalse()
    {
        // Arrange
        var token = "invalid-token-that-cannot-be-parsed";

        // Act
        var (isValid, principal) = await _jwtTokenService.ValidateAccessTokenAsync(token);

        // Assert
        Assert.False(isValid);
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
    public async Task GenerateAccessToken_TokenContainsCorrectClaims()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var nickname = "TestUser";
        var token = _jwtTokenService.GenerateAccessToken(userId, nickname);

        // Mock repository to return true (user has active refresh tokens)
        _refreshTokenRepositoryMock
            .Setup(r => r.HasActiveRefreshTokensAsync(userId))
            .ReturnsAsync(true);

        // Act
        var (isValid, principal) = await _jwtTokenService.ValidateAccessTokenAsync(token);

        // Assert
        Assert.True(isValid);
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
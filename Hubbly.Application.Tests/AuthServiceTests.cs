using FluentAssertions;
using Hubbly.Application.Services;
using Hubbly.Domain.Common;
using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System.IdentityModel.Tokens.Jwt;

namespace Hubbly.Application.Tests;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock;
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly Mock<ILogger<AuthService>> _loggerMock;
    private readonly JwtSettings _jwtSettings;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _refreshTokenRepositoryMock = new Mock<IRefreshTokenRepository>();
        _jwtTokenServiceMock = new Mock<IJwtTokenService>();
        _loggerMock = new Mock<ILogger<AuthService>>();

        _jwtSettings = new JwtSettings
        {
            Secret = "TestSecretKeyThatIsAtLeast32CharactersLongForHS256",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        };

        _authService = new AuthService(
            _userRepositoryMock.Object,
            _refreshTokenRepositoryMock.Object,
            _jwtTokenServiceMock.Object,
            _jwtSettings,
            _loggerMock.Object);
    }

    [Fact]
    public async Task AuthenticateGuestAsync_WithValidDeviceId_ReturnsAuthResponse()
    {
        // Arrange
        var deviceId = "test-device-123";
        var avatarConfigJson = "{\"test\":\"config\"}";
        var user = new User(deviceId, "Guest_1234", avatarConfigJson);

        _userRepositoryMock
            .Setup(r => r.GetByDeviceIdAsync(deviceId))
            .ReturnsAsync((User?)null);

        _userRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<User>()))
            .Callback<User>(u => user = u);

        _jwtTokenServiceMock
            .Setup(s => s.GenerateAccessToken(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns("test-access-token");

        _jwtTokenServiceMock
            .Setup(s => s.GenerateRefreshToken())
            .Returns("test-refresh-token");

        _refreshTokenRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<RefreshToken>()));

        // Act
        var result = await _authService.AuthenticateGuestAsync(deviceId, avatarConfigJson);

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().Be("test-access-token");
        result.RefreshToken.Should().Be("test-refresh-token");
        result.DeviceId.Should().Be(deviceId);
        result.User.Should().NotBeNull();
        result.User.IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task AuthenticateGuestAsync_WithExistingUser_ReturnsExistingUser()
    {
        // Arrange
        var deviceId = "existing-device";
        var existingUser = new User(deviceId, "ExistingUser", null);

        _userRepositoryMock
            .Setup(r => r.GetByDeviceIdAsync(deviceId))
            .ReturnsAsync(existingUser);

        _jwtTokenServiceMock
            .Setup(s => s.GenerateAccessToken(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns("test-access-token");

        _jwtTokenServiceMock
            .Setup(s => s.GenerateRefreshToken())
            .Returns("test-refresh-token");

        _refreshTokenRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<RefreshToken>()));

        // Act
        var result = await _authService.AuthenticateGuestAsync(deviceId);

        // Assert
        result.Should().NotBeNull();
        result.User.Id.Should().Be(existingUser.Id);
        _userRepositoryMock.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task AuthenticateGuestAsync_WithEmptyDeviceId_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _authService.AuthenticateGuestAsync(string.Empty);

        // Assert
        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("DeviceId is required");
    }

    [Fact]
    public async Task AuthenticateGuestAsync_WithNullDeviceId_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _authService.AuthenticateGuestAsync(null!);

        // Assert
        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("DeviceId is required");
    }

    [Fact]
    public async Task RefreshTokenAsync_WithValidTokens_ReturnsNewTokens()
    {
        // Arrange
        var refreshToken = "valid-refresh-token";
        var deviceId = "test-device";
        var user = new User(deviceId, "TestUser", null);
        var storedToken = new RefreshToken(user.Id, refreshToken, deviceId, 7);

        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenAndDeviceAsync(refreshToken, deviceId))
            .ReturnsAsync(storedToken);

        _refreshTokenRepositoryMock
            .Setup(r => r.CleanupOldDeviceTokensAsync(user.Id, deviceId, 3))
            .Returns(Task.CompletedTask);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(user.Id))
            .ReturnsAsync(user);

        _jwtTokenServiceMock
            .Setup(s => s.GenerateAccessToken(user.Id, user.Nickname))
            .Returns("new-access-token");

        _jwtTokenServiceMock
            .Setup(s => s.GenerateRefreshToken())
            .Returns("new-refresh-token");

        // Act
        var result = await _authService.RefreshTokenAsync(refreshToken, deviceId);

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().Be("new-access-token");
        result.RefreshToken.Should().Be("new-refresh-token");
        result.User.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithInvalidToken_ThrowsSecurityTokenException()
    {
        // Arrange
        var refreshToken = "invalid-token";
        var deviceId = "test-device";

        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenAndDeviceAsync(refreshToken, deviceId))
            .ReturnsAsync((RefreshToken?)null);

        // Act
        var act = async () => await _authService.RefreshTokenAsync(refreshToken, deviceId);

        // Assert
        act.Should().ThrowAsync<SecurityTokenException>()
            .WithMessage("Refresh token not found");
    }

    [Fact]
    public async Task RefreshTokenAsync_WithRevokedToken_ThrowsSecurityTokenException()
    {
        // Arrange
        var refreshToken = "revoked-token";
        var deviceId = "test-device";
        var storedToken = new RefreshToken(Guid.NewGuid(), refreshToken, deviceId, 7);
        storedToken.Revoke();

        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenAndDeviceAsync(refreshToken, deviceId))
            .ReturnsAsync(storedToken);

        // Act
        var act = async () => await _authService.RefreshTokenAsync(refreshToken, deviceId);

        // Assert
        act.Should().ThrowAsync<SecurityTokenException>()
            .WithMessage("Refresh token expired or revoked");
    }

    [Fact]
    public async Task RefreshTokenAsync_WithEmptyRefreshToken_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _authService.RefreshTokenAsync(string.Empty, "deviceId");

        // Assert
        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Refresh token is required");
    }

    [Fact]
    public async Task RefreshTokenAsync_WithEmptyDeviceId_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _authService.RefreshTokenAsync("token", string.Empty);

        // Assert
        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("DeviceId is required");
    }

    [Fact]
    public async Task GenerateGuestNicknameAsync_GeneratesUniqueNickname()
    {
        // Arrange
        var existingUser = new User("device", "Guest_1234", null);

        _userRepositoryMock
            .Setup(r => r.GetByNicknameAsync(It.IsAny<string>()))
            .ReturnsAsync((User?)null);

        // Use reflection to call private method
        var method = typeof(AuthService).GetMethod("GenerateGuestNicknameAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act - async method returns Task<string>, need to await it
        var task = (Task<string>)method!.Invoke(_authService, null)!;
        var nickname = await task;

        // Assert
        nickname.Should().NotBeNullOrEmpty();
        nickname.Should().StartWith("Guest_");
    }
}

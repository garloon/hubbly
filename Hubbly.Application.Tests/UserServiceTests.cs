using FluentAssertions;
using Hubbly.Application.Services;
using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Events;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Hubbly.Application.Tests;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ILogger<UserService>> _loggerMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly UserService _userService;

    public UserServiceTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _loggerMock = new Mock<ILogger<UserService>>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();

        _userService = new UserService(
            _userRepositoryMock.Object,
            _loggerMock.Object,
            _eventDispatcherMock.Object);
    }

    [Fact]
    public async Task GetUserProfileAsync_WithExistingUser_ReturnsProfile()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User("device-id", "TestUser", "{\"test\":\"avatar\"}");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(user);

        // Act
        var result = await _userService.GetUserProfileAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(user.Id);
        result.Nickname.Should().Be(user.Nickname);
        result.AvatarConfigJson.Should().Be(user.AvatarConfigJson);
        result.IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task GetUserProfileAsync_WithNonExistingUser_ThrowsKeyNotFoundException()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync((User?)null);

        // Act
        var act = async () => await _userService.GetUserProfileAsync(userId);

        // Assert
        act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("User not found");
    }

    [Fact]
    public async Task UpdateUserNicknameAsync_WithValidNickname_UpdatesSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User("device-id", "OldNickname", null);
        var newNickname = "NewNickname";

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(user);

        _userRepositoryMock
            .Setup(r => r.UpdateAsync(user));

        // Act
        await _userService.UpdateUserNicknameAsync(userId, newNickname);

        // Assert
        user.Nickname.Should().Be(newNickname);
        _userRepositoryMock.Verify(r => r.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task UpdateUserNicknameAsync_WithEmptyNickname_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _userService.UpdateUserNicknameAsync(Guid.NewGuid(), string.Empty);

        // Assert
        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Nickname cannot be empty or whitespace*");
    }

    [Fact]
    public async Task UpdateUserNicknameAsync_WithWhitespaceNickname_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _userService.UpdateUserNicknameAsync(Guid.NewGuid(), "   ");

        // Assert
        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Nickname cannot be empty or whitespace*");
    }

    [Fact]
    public async Task UpdateUserAvatarAsync_WithValidConfig_UpdatesSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User("device-id", "TestUser", "old-config");
        var newAvatarConfig = "{\"gender\":\"male\",\"baseModelId\":\"male_base\",\"pose\":\"standing\",\"components\":{}}";

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(user);

        _userRepositoryMock
            .Setup(r => r.UpdateAsync(user));

        // Act
        await _userService.UpdateUserAvatarAsync(userId, newAvatarConfig);

        // Assert
        user.AvatarConfigJson.Should().Be(newAvatarConfig);
        _userRepositoryMock.Verify(r => r.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task UpdateUserAvatarAsync_WithEmptyConfig_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _userService.UpdateUserAvatarAsync(Guid.NewGuid(), string.Empty);

        // Assert
        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Avatar config cannot be empty");
    }

    [Fact]
    public async Task AddOwnedAssetAsync_WithValidAssetId_AddsSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User("device-id", "TestUser", null);
        var assetId = "asset-123";

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(user);

        _userRepositoryMock
            .Setup(r => r.UpdateAsync(user));

        // Act
        await _userService.AddOwnedAssetAsync(userId, assetId);

        // Assert
        user.OwnedAssetIds.Should().Contain(assetId);
        _userRepositoryMock.Verify(r => r.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task AddOwnedAssetAsync_WithEmptyAssetId_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _userService.AddOwnedAssetAsync(Guid.NewGuid(), string.Empty);

        // Assert
        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("AssetId cannot be empty");
    }

    [Fact]
    public async Task AddOwnedAssetAsync_WithNullAssetId_ThrowsArgumentException()
    {
        // Act
        var act = async () => await _userService.AddOwnedAssetAsync(Guid.NewGuid(), null!);

        // Assert
        act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("AssetId cannot be empty");
    }

    [Fact]
    public async Task AddOwnedAssetAsync_WithDuplicateAssetId_DoesNotAdd()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User("device-id", "TestUser", null);
        user.AddOwnedAsset("existing-asset");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId))
            .ReturnsAsync(user);

        _userRepositoryMock
            .Setup(r => r.UpdateAsync(user));

        // Act
        await _userService.AddOwnedAssetAsync(userId, "existing-asset");

        // Assert
        user.OwnedAssetIds.Count.Should().Be(1);
        _userRepositoryMock.Verify(r => r.UpdateAsync(user), Times.Never);
    }
}

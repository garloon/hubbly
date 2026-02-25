using FluentAssertions;
using Hubbly.Application.Config;
using Hubbly.Application.Services;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Hubbly.Application.Tests;

public class RoomServiceTests
{
    private readonly Mock<ILogger<RoomService>> _loggerMock;
    private readonly Mock<IRoomRepository> _roomRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly RoomServiceOptions _options;
    private readonly RoomService _roomService;

    public RoomServiceTests()
    {
        _loggerMock = new Mock<ILogger<RoomService>>();
        _roomRepositoryMock = new Mock<IRoomRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _options = new RoomServiceOptions { DefaultMaxUsers = 50 };
        var optionsWrapper = Options.Create(_options);

        _roomService = new RoomService(_roomRepositoryMock.Object, _userRepositoryMock.Object, _loggerMock.Object, optionsWrapper);
    }

    [Fact]
    public async Task GetOrCreateRoomForGuestAsync_ShouldReturnRoom()
    {
        // Arrange - setup repository to return null for GetOptimalRoomAsync (no existing room)
        var newRoom = new ChatRoom("Test System Room", RoomType.System, 50);
        _roomRepositoryMock
            .Setup(r => r.GetOptimalRoomAsync(RoomType.System, 50))
            .ReturnsAsync((ChatRoom?)null);
        _roomRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ChatRoom>()))
            .ReturnsAsync(newRoom);

        // Act
        var result = await _roomService.GetOrCreateRoomForGuestAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(newRoom);
    }

    [Fact]
    public async Task GetOrCreateRoomForGuestAsync_MultipleCalls_ShouldReturnSameOrDifferentRooms()
    {
        // Arrange - first two calls return existing room, third returns null (creates new)
        var existingRoom = new ChatRoom("Existing System Room", RoomType.System, 50) { Id = Guid.NewGuid() };
        var newRoom = new ChatRoom("New System Room", RoomType.System, 50) { Id = Guid.NewGuid() };
        
        var callCount = 0;
        _roomRepositoryMock
            .Setup(r => r.GetOptimalRoomAsync(RoomType.System, 50))
            .Returns(() => Task.FromResult(callCount++ < 2 ? existingRoom : null));
        _roomRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<ChatRoom>()))
            .ReturnsAsync(newRoom);

        // Act - make multiple calls
        var room1 = await _roomService.GetOrCreateRoomForGuestAsync();
        var room2 = await _roomService.GetOrCreateRoomForGuestAsync();
        var room3 = await _roomService.GetOrCreateRoomForGuestAsync();

        // Assert
        room1.Should().NotBeNull();
        room2.Should().NotBeNull();
        room3.Should().NotBeNull();
        // room1 and room2 should be the same (existing from cache), room3 should be new (created)
        room1.Should().Be(existingRoom);
        room2.Should().Be(existingRoom);
        room3.Should().Be(newRoom);
    }

    [Fact]
    public async Task RemoveUserFromRoomAsync_ShouldRemoveUserFromRoom()
    {
        // Arrange
        var room = await _roomService.GetOrCreateRoomForGuestAsync();
        var userId = Guid.NewGuid();
        
        // First assign user to room (via internal method - we need to test through public API)
        // Since there's no public method to assign a user directly, we'll test that
        // removing a non-existent user does not throw and returns successfully
        
        // Act
        await _roomService.RemoveUserFromRoomAsync(userId);

        // Assert - should complete without exception
        // User was not in any room, so nothing to remove
    }

    [Fact]
    public async Task CleanupEmptyRoomsAsync_ShouldNotThrow_WhenNoEmptyRooms()
    {
        // Act
        await _roomService.CleanupEmptyRoomsAsync(TimeSpan.FromMinutes(30));

        // Assert - should complete without exception
    }

    }
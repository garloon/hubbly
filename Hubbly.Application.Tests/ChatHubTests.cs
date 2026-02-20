using Hubbly.Api.Hubs;
using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Concurrent;

namespace Hubbly.Application.Tests;

public class ChatHubTests : IDisposable
{
    private readonly Mock<IChatService> _chatServiceMock;
    private readonly Mock<IUserService> _userServiceMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IRoomService> _roomServiceMock;
    private readonly Mock<ILogger<ChatHub>> _loggerMock;
    private readonly Mock<IMemoryCache> _memoryCacheMock;
    private readonly Mock<HubCallerContext> _contextMock;
    private readonly ChatHub _chatHub;
    private readonly Guid _userId;
    private readonly User _testUser;
    private readonly ChatRoom _testRoom;

    public ChatHubTests()
    {
        _chatServiceMock = new Mock<IChatService>();
        _userServiceMock = new Mock<IUserService>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _roomServiceMock = new Mock<IRoomService>();
        _loggerMock = new Mock<ILogger<ChatHub>>();
        _memoryCacheMock = new Mock<IMemoryCache>();
        _contextMock = new Mock<HubCallerContext>();

        _userId = Guid.NewGuid();
        _testUser = new User("device-id", "TestUser", "{\"test\":\"avatar\"}");
        _testRoom = new ChatRoom("Test Room", 10);

        // Setup memory cache mock to return false for nonce checks
        var cacheEntryMock = new Mock<ICacheEntry>();
        _memoryCacheMock
            .Setup(m => m.CreateEntry(It.IsAny<object>()))
            .Returns(cacheEntryMock.Object);

        _chatHub = new ChatHub(
            _chatServiceMock.Object,
            _userServiceMock.Object,
            _userRepositoryMock.Object,
            _roomServiceMock.Object,
            _loggerMock.Object,
            _memoryCacheMock.Object);

        // Setup context with user claims
        var claims = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("userId", _userId.ToString())
        });
        _contextMock.Setup(c => c.User).Returns(new System.Security.Claims.ClaimsPrincipal(claims));
        _chatHub.Context = _contextMock.Object;
    }

    [Fact]
    public async Task OnConnectedAsync_WithValidUser_ConnectsSuccessfully()
    {
        // Arrange
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(_userId))
            .ReturnsAsync(_testUser);

        _roomServiceMock
            .Setup(r => r.GetOrCreateRoomForGuestAsync())
            .ReturnsAsync(_testRoom);

        _roomServiceMock
            .Setup(r => r.AssignGuestToRoomAsync(_userId, _testRoom.Id))
            .Returns(Task.CompletedTask);

        _chatServiceMock
            .Setup(s => s.SendMessageAsync(_userId, It.IsAny<string>(), "connected"))
            .ReturnsAsync(new ChatMessageDto { Id = Guid.NewGuid() });

        // Act
        await _chatHub.OnConnectedAsync();

        // Assert
        _userRepositoryMock.Verify(r => r.GetByIdAsync(_userId), Times.Once);
        _roomServiceMock.Verify(r => r.GetOrCreateRoomForGuestAsync(), Times.Once);
        _roomServiceMock.Verify(r => r.AssignGuestToRoomAsync(_userId, _testRoom.Id), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_WithNonExistingUser_AbortsConnection()
    {
        // Arrange
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(_userId))
            .ReturnsAsync((User?)null);

        // Act
        await _chatHub.OnConnectedAsync();

        // Assert
        _contextMock.Verify(c => c.Abort(), Times.Once);
    }

    [Fact]
    public async Task SendMessage_WithValidContent_SendsSuccessfully()
    {
        // Arrange
        var message = "Hello, world!";
        var chatMessageDto = new ChatMessageDto
        {
            Id = Guid.NewGuid(),
            SenderId = _userId,
            SenderNickname = _testUser.Nickname,
            Content = message,
            Timestamp = DateTimeOffset.UtcNow
        };

        _chatServiceMock
            .Setup(s => s.SendMessageAsync(_userId, message, null))
            .ReturnsAsync(chatMessageDto);

        // Act
        await _chatHub.SendMessage(message, null);

        // Assert
        _chatServiceMock.Verify(s => s.SendMessageAsync(_userId, message, null), Times.Once);
    }

    [Fact]
    public async Task SendMessage_WithEmptyContent_DoesNotSend()
    {
        // Arrange
        var message = "";

        // Act
        await _chatHub.SendMessage(message, null);

        // Assert
        _chatServiceMock.Verify(s => s.SendMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendMessage_WithNullContent_DoesNotSend()
    {
        // Act
        await _chatHub.SendMessage(null, null);

        // Assert
        _chatServiceMock.Verify(s => s.SendMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UserTyping_SendsTypingIndicator()
    {
        // Arrange
        _chatServiceMock
            .Setup(s => s.SendMessageAsync(_userId, string.Empty, "typing"))
            .ReturnsAsync(new ChatMessageDto { Id = Guid.NewGuid() });

        // Act
        await _chatHub.UserTyping();

        // Assert
        _chatServiceMock.Verify(s => s.SendMessageAsync(_userId, string.Empty, "typing"), Times.Once);
    }

    [Fact]
    public async Task SendAnimation_WithValidAnimation_SendsSuccessfully()
    {
        // Arrange
        var animationType = "clap";

        _chatServiceMock
            .Setup(s => s.SendMessageAsync(_userId, null, animationType))
            .ReturnsAsync(new ChatMessageDto { Id = Guid.NewGuid() });

        // Act
        await _chatHub.SendAnimation(animationType);

        // Assert
        _chatServiceMock.Verify(s => s.SendMessageAsync(_userId, null, animationType), Times.Once);
    }

    [Fact]
    public async Task SendAnimation_WithEmptyAnimation_DoesNotSend()
    {
        // Act
        await _chatHub.SendAnimation("");

        // Assert
        _chatServiceMock.Verify(s => s.SendMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetOnlineCount_ReturnsConnectedUsersCount()
    {
        // Arrange
        // Clear static dictionary first
        var field = typeof(ChatHub).GetField("_connectedUsers", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var connectedUsers = (ConcurrentDictionary<string, ConnectedUser>)field!.GetValue(null)!;
        connectedUsers.Clear();

        var user1 = new ConnectedUser { UserId = Guid.NewGuid(), Nickname = "User1" };
        var user2 = new ConnectedUser { UserId = Guid.NewGuid(), Nickname = "User2" };
        connectedUsers[user1.UserId.ToString()] = user1;
        connectedUsers[user2.UserId.ToString()] = user2;

        // Act
        var result = await _chatHub.GetOnlineCount();

        // Assert
        result.Should().Be(2);
    }

    [Fact]
    public async Task OnDisconnectedAsync_RemovesUserFromConnectedUsers()
    {
        // Arrange
        var connectionId = _contextMock.Object.ConnectionId;
        var connectedUser = new ConnectedUser
        {
            UserId = _userId,
            Nickname = "TestUser",
            ConnectionId = connectionId,
            RoomId = _testRoom.Id
        };

        var field = typeof(ChatHub).GetField("_connectedUsers", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var connectedUsers = (ConcurrentDictionary<string, ConnectedUser>)field!.GetValue(null)!;
        connectedUsers.Clear();
        connectedUsers[_userId.ToString()] = connectedUser;

        _roomServiceMock
            .Setup(r => r.RemoveUserFromRoomAsync(_userId))
            .Returns(Task.CompletedTask);

        // Act
        await _chatHub.OnDisconnectedAsync(null);

        // Assert
        connectedUsers.Should().NotContainKey(_userId.ToString());
        _roomServiceMock.Verify(r => r.RemoveUserFromRoomAsync(_userId), Times.Once);
    }

    public void Dispose()
    {
        // Clean up static dictionary after tests
        var field = typeof(ChatHub).GetField("_connectedUsers", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var connectedUsers = (ConcurrentDictionary<string, ConnectedUser>)field!.GetValue(null)!;
        connectedUsers.Clear();
    }
}

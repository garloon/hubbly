using Hubbly.Domain.Dtos;
using Hubbly.Domain.Entities;
using Hubbly.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hubbly.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RoomController : ControllerBase
{
    private readonly IRoomService _roomService;
    private readonly IRoomRepository _roomRepository;
    private readonly ILogger<RoomController> _logger;

    public RoomController(
        IRoomService roomService,
        IRoomRepository roomRepository,
        ILogger<RoomController> logger)
    {
        _roomService = roomService ?? throw new ArgumentNullException(nameof(roomService));
        _roomRepository = roomRepository ?? throw new ArgumentNullException(nameof(roomRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get list of available rooms
    /// Guests see: system + public rooms
    /// Authenticated users see: system + public + their own private + private rooms they're already in
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RoomInfoDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRooms()
    {
        using (_logger.BeginScope(new Dictionary<string, object>()))
        {
            _logger.LogInformation("GetRooms requested");

            try
            {
                Guid? userId = null;
                if (User.Identity?.IsAuthenticated == true)
                {
                    userId = GetCurrentUserId();
                }

                var rooms = await _roomService.GetAvailableRoomsAsync(userId: userId);
                return Ok(rooms);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting rooms");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Internal server error" });
            }
        }
    }

    /// <summary>
    /// Create a new room (authenticated users only)
    /// </summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(RoomInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request)
    {
        var userId = GetCurrentUserId();

        using (_logger.BeginScope(new Dictionary<string, object> { ["UserId"] = userId }))
        {
            _logger.LogInformation("CreateRoom requested: {RoomName}, Type: {RoomType}", request.Name, request.Type);

            try
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return BadRequest(new { error = "Room name is required" });
                }

                if (request.Type == RoomType.System)
                {
                    return BadRequest(new { error = "Cannot create system rooms" });
                }

                var roomDto = await _roomService.CreateUserRoomAsync(
                    request.Name,
                    request.Description,
                    request.Type,
                    userId,
                    request.MaxUsers
                );

                return Ok(roomDto);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Room creation failed");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating room");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Internal server error" });
            }
        }
    }

    /// <summary>
    /// Join a room (guests and authenticated users)
    /// </summary>
    [HttpPost("{roomId:guid}/join")]
    [ProducesResponseType(typeof(RoomAssignmentData), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> JoinRoom([FromRoute] Guid roomId, [FromBody] JoinRoomRequest? request = null)
    {
        Guid? userId = null;
        if (User.Identity?.IsAuthenticated == true)
        {
            userId = GetCurrentUserId();
        }

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["RoomId"] = roomId,
            ["UserId"] = userId ?? Guid.Empty
        }))
        {
            _logger.LogInformation("JoinRoom requested for room {RoomId}", roomId);

            try
            {
                string? password = null;
                if (request != null)
                {
                    password = request.Password;
                }

                var assignment = await _roomService.JoinRoomAsync(roomId, userId!.Value, password);

                return Ok(assignment);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Room not found");
                return NotFound(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access to room");
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Room join failed");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining room");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Internal server error" });
            }
        }
    }

    /// <summary>
    /// Leave a room (authenticated users only)
    /// </summary>
    [HttpPost("{roomId:guid}/leave")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LeaveRoom([FromRoute] Guid roomId)
    {
        var userId = GetCurrentUserId();

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["RoomId"] = roomId,
            ["UserId"] = userId
        }))
        {
            _logger.LogInformation("LeaveRoom requested for room {RoomId}", roomId);

            try
            {
                await _roomService.LeaveRoomAsync(userId, roomId);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Room leave failed");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leaving room");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Internal server error" });
            }
        }
    }

    /// <summary>
    /// Get room details
    /// </summary>
    [HttpGet("{roomId:guid}")]
    [ProducesResponseType(typeof(RoomInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRoom([FromRoute] Guid roomId)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["RoomId"] = roomId }))
        {
            _logger.LogInformation("GetRoom requested for room {RoomId}", roomId);

            try
            {
                var room = await _roomService.GetRoomByUserIdAsync(roomId);
                if (room == null)
                {
                    return NotFound(new { error = "Room not found" });
                }

                var userCount = await _roomRepository.GetUserCountAsync(room.Id);

                var roomDto = new RoomInfoDto
                {
                    RoomId = room.Id,
                    RoomName = room.Name,
                    Description = room.Description,
                    Type = room.Type,
                    CurrentUsers = userCount,
                    MaxUsers = room.MaxUsers,
                    IsPrivate = room.Type == RoomType.Private
                };

                return Ok(roomDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting room");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Internal server error" });
            }
        }
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("userId") ?? User.FindFirst("sub");
        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            throw new InvalidOperationException("User ID not found in claims");
        }
        return userId;
    }
}

public class CreateRoomRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public RoomType Type { get; set; }
    public int MaxUsers { get; set; } = 50;
}

public class JoinRoomRequest
{
    public string? Password { get; set; }
}

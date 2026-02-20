using Hubbly.Domain.Dtos;
using Hubbly.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hubbly.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UserController> _logger;

    public UserController(
        IUserService userService,
        ILogger<UserController> logger)
    {
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Public methods

    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile()
    {
        var userId = GetCurrentUserId();

        using (_logger.BeginScope(new Dictionary<string, object> { ["UserId"] = userId }))
        {
            _logger.LogInformation("GetProfile requested");

            try
            {
                var profile = await _userService.GetUserProfileAsync(userId);
                return Ok(profile);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "User not found");
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting profile");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }

    [HttpPut("nickname")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateNickname([FromBody] UpdateNicknameRequest request)
    {
       var userId = GetCurrentUserId();

       using (_logger.BeginScope(new Dictionary<string, object>
       {
           ["UserId"] = userId,
           ["NewNickname"] = request.NewNickname
       }))
       {
           _logger.LogInformation("UpdateNickname requested");

           try
           {
               await _userService.UpdateUserNicknameAsync(userId, request.NewNickname);

               _logger.LogInformation("Nickname updated successfully");
               return NoContent();
           }
           catch (KeyNotFoundException ex)
           {
               _logger.LogWarning(ex, "User not found");
               return NotFound(new { error = ex.Message });
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Error updating nickname");
               return StatusCode(500, new { error = "Internal server error" });
           }
       }
   }

    [HttpPut("avatar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAvatar([FromBody] UpdateAvatarRequest request)
    {
        var userId = GetCurrentUserId();

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["UserId"] = userId,
            ["AvatarConfigLength"] = request?.AvatarConfigJson?.Length ?? 0
        }))
        {
            _logger.LogInformation("UpdateAvatar requested");

            try
            {
                await _userService.UpdateUserAvatarAsync(userId, request!.AvatarConfigJson);

                _logger.LogInformation("Avatar updated successfully");
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "User not found");
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating avatar");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }

    #endregion

    #region Private methods

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("userId")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogWarning("Invalid or missing userId claim");
            throw new UnauthorizedAccessException("Invalid user identity");
        }
        return userId;
    }

    #endregion
}

#region Request Records

public record UpdateNicknameRequest
{
    public string NewNickname { get; init; } = null!;
}

public record UpdateAvatarRequest
{
    public string AvatarConfigJson { get; init; } = null!;
}

#endregion
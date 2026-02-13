using Hubbly.Domain.Dtos;
using Hubbly.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hubbly.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        ILogger<AuthController> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Публичные методы

    [HttpPost("guest")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> AuthenticateGuest([FromBody] GuestAuthRequest request)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["DeviceId"] = request?.DeviceId,
            ["HasAvatar"] = !string.IsNullOrEmpty(request?.AvatarConfigJson)
        }))
        {
            _logger.LogInformation("Guest authentication requested");

            try
            {
                ValidateGuestRequest(request);

                var response = await _authService.AuthenticateGuestAsync(
                    request!.DeviceId,
                    request.AvatarConfigJson);

                _logger.LogInformation("Guest authenticated successfully: UserId {UserId}", response.User.Id);

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid guest request");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Guest authentication failed");
                return StatusCode(500, new { error = "Authentication failed" });
            }
        }
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["DeviceId"] = request?.DeviceId,
            ["HasRefreshToken"] = !string.IsNullOrEmpty(request?.RefreshToken)
        }))
        {
            _logger.LogInformation("Token refresh requested");

            try
            {
                ValidateRefreshRequest(request);

                var response = await _authService.RefreshTokenAsync(
                    request!.RefreshToken,
                    request.DeviceId);

                _logger.LogInformation("Token refreshed successfully for user {UserId}", response.User.Id);

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid refresh request");
                return BadRequest(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized refresh attempt");
                return Unauthorized(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token refresh failed");
                return StatusCode(500, new { error = "Token refresh failed" });
            }
        }
    }

    #endregion

    #region Приватные методы

    private void ValidateGuestRequest(GuestAuthRequest? request)
    {
        if (request == null)
        {
            throw new ArgumentException("Request cannot be null");
        }

        if (string.IsNullOrWhiteSpace(request.DeviceId))
        {
            throw new ArgumentException("DeviceId is required");
        }
    }

    private void ValidateRefreshRequest(RefreshTokenRequest? request)
    {
        if (request == null)
        {
            throw new ArgumentException("Request cannot be null");
        }

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new ArgumentException("Refresh token is required");
        }

        if (string.IsNullOrWhiteSpace(request.DeviceId))
        {
            throw new ArgumentException("DeviceId is required");
        }
    }

    #endregion
}

#region Request Records

public record GuestAuthRequest
{
    public string DeviceId { get; init; } = null!;
    public string? AvatarConfigJson { get; init; }
}

public record RefreshTokenRequest
{
    public string RefreshToken { get; init; } = null!;
    public string DeviceId { get; init; } = null!;
}

#endregion
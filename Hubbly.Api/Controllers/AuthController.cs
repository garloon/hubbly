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

    #region Public methods

    [HttpPost("guest")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> AuthenticateGuest([FromBody] GuestAuthRequest request)
    {
       using (_logger.BeginScope(new Dictionary<string, object>
       {
           ["DeviceId"] = request.DeviceId,
           ["HasAvatar"] = !string.IsNullOrEmpty(request.AvatarConfigJson)
       }))
       {
           _logger.LogInformation("Guest authentication requested");

           try
           {
               var response = await _authService.AuthenticateGuestAsync(
                   request.DeviceId,
                   request.AvatarConfigJson);

               _logger.LogInformation("Guest authenticated successfully: UserId {UserId}", response.User.Id);

               return Ok(response);
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
           ["DeviceId"] = request.DeviceId,
           ["HasRefreshToken"] = !string.IsNullOrEmpty(request.RefreshToken)
       }))
       {
           _logger.LogInformation("Token refresh requested");

           try
           {
               var response = await _authService.RefreshTokenAsync(
                   request.RefreshToken,
                   request.DeviceId);

               _logger.LogInformation("Token refreshed successfully for user {UserId}", response.User.Id);

               return Ok(response);
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

   #region Guest Conversion

   [HttpPost("convert-guest")]
   [ProducesResponseType(StatusCodes.Status200OK)]
   [ProducesResponseType(StatusCodes.Status400BadRequest)]
   public async Task<IActionResult> ConvertGuestToUser([FromBody] ConvertGuestRequest request)
   {
       _logger.LogInformation("Convert guest to user requested for GuestUserId: {GuestUserId}", request.GuestUserId);

       try
       {
           // TODO: Реализовать полноценную конвертацию:
           // 1. Создать запись пользователя на основе данных гостя
           // 2. Сохранить аватар, настройки, комнату
           // 3. Связать deviceId с новым пользователем
           // 4. Вернуть новые токены

           // Заглушка: просто возвращаем успех
           return Ok(new
           {
               message = "Guest converted to user (stub implementation)",
               userId = request.GuestUserId,
               note = "Real implementation needed"
           });
       }
       catch (Exception ex)
       {
           _logger.LogError(ex, "Guest conversion failed for GuestUserId: {GuestUserId}", request.GuestUserId);
           return StatusCode(500, new { error = "Conversion failed" });
       }
   }

   #endregion
}

#region Request/Response Records

public record ConvertGuestRequest
{
   public Guid GuestUserId { get; init; }
}

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
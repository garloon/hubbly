using Hubbly.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hubbly.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("guest")]
    public async Task<IActionResult> AuthenticateGuest([FromBody] GuestAuthRequest request)
    {
        var response = await _authService.AuthenticateGuestAsync(request.DeviceId, request.AvatarConfigJson);
        return Ok(response);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var response = await _authService.RefreshTokenAsync(request.RefreshToken, request.DeviceId);
        return Ok(response);
    }
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
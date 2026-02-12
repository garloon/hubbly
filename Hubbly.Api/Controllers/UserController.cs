using Hubbly.Application.Services;
using Hubbly.Domain.Services;
using Hubbly.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Hubbly.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetProfile()
    {
        var userId = Guid.Parse(User.FindFirst("userId")!.Value);
        var profile = await _userService.GetUserProfileAsync(userId);
        return Ok(profile);
    }

    [HttpPut("nickname")]
    public async Task<IActionResult> UpdateNickname([FromBody] UpdateNicknameRequest request)
    {
        var userId = Guid.Parse(User.FindFirst("userId")!.Value);
        await _userService.UpdateUserNicknameAsync(userId, request.NewNickname);
        return NoContent();
    }

    [HttpPut("avatar")]
    public async Task<IActionResult> UpdateAvatar([FromBody] UpdateAvatarRequest request)
    {
        var userId = Guid.Parse(User.FindFirst("userId")!.Value);
        await _userService.UpdateUserAvatarAsync(userId, request.AvatarConfigJson);
        return NoContent();
    }
}

public record UpdateNicknameRequest
{
    public string NewNickname { get; init; } = null!;
}

public record UpdateAvatarRequest
{
    public string AvatarConfigJson { get; init; } = null!;
}
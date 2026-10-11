using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Abstractions;
using SupportDesk.Presentation.Authentication;

namespace SupportDesk.Presentation.Controllers;

public sealed record LoginRequest([Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(4096)] string Password);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAgentAuthentication authentication, JwtTokens tokens) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var agent = await authentication.VerifyAsync(request.Email, request.Password, ct);
        return agent is null
            ? Unauthorized(new ProblemDetails { Status = 401, Title = "Unauthorized", Detail = "Invalid email or password." })
            : Ok(tokens.Create(agent));
    }

    [HttpGet("me")]
    public async Task<ActionResult<AuthAgent>> Me(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue("sub"), out var id)) return Unauthorized();
        var agent = await authentication.GetActiveAsync(id, ct);
        return agent is null ? Unauthorized() : Ok(agent);
    }
}

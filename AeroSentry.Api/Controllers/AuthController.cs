using AeroSentry.Api.DTOs;
using AeroSentry.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AeroSentry.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;

    public AuthController(AuthService auth)
    {
        _auth = auth;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var result = await _auth.RegisterAsync(request);
        if (result is null)
            return Problem(statusCode: 409, title: "Email already registered");

        return StatusCode(StatusCodes.Status201Created, result);
    }

[HttpPost("login")]
public async Task<IActionResult> Login(LoginRequest request)
{
    var result = await _auth.LoginAsync(request);

    return result.Status switch
    {
        LoginStatus.Success => Ok(result.Response),
        LoginStatus.Suspended => Problem(statusCode: 403, title: "Account suspended"),
        _ => Problem(statusCode: 401, title: "Invalid email or password")
    };
}
}

namespace DeveloperOverwatch.API.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/auth")]
[Authorize]
public sealed class AuthController : ControllerBase
{
    [HttpGet("me")]
    public IActionResult GetCurrentUser()
    {
        var user = HttpContext.User;
        return Ok(new
        {
            id = user.FindFirstValue(ClaimTypes.NameIdentifier),
            name = user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue("name"),
            email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email"),
            roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
            authMethod = user.FindFirstValue("auth_method") ?? user.Identity?.AuthenticationType
        });
    }
}

namespace DeveloperOverwatch.API.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/auth")]
[Authorize]
[Produces("application/json")]
[Tags("Authentication")]
public sealed class AuthController : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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

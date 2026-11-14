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
    public async Task<IActionResult> GetCurrentUser([FromServices] IAuthorizationService authorization)
    {
        var user = HttpContext.User;
        // The policy itself decides, so the dashboard never re-implements who may edit.
        var canEditBudgets = (await authorization.AuthorizeAsync(user, AuthPolicies.BudgetEdit)).Succeeded;
        return Ok(new
        {
            id = user.FindFirstValue(ClaimTypes.NameIdentifier),
            name = user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue("name"),
            email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email"),
            roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
            authMethod = user.FindFirstValue("auth_method") ?? user.Identity?.AuthenticationType,
            canEditBudgets
        });
    }
}

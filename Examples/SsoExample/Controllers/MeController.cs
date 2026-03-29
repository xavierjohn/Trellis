using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trellis.Authorization;

namespace SsoExample.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class MeController(IActorProvider actorProvider) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var actor = await actorProvider.GetCurrentActorAsync(ct);
        return Ok(new
        {
            actor.Id,
            Permissions = actor.Permissions.ToList(),
            Attributes = actor.Attributes
        });
    }
}

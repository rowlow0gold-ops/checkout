using Microsoft.AspNetCore.Mvc;
using StoreServer.Services;

namespace StoreServer.Controllers;

/// <summary>
/// Called by terminals when they receive a CatalogUpdate event from the emulator.
/// Forces an immediate catalog refresh from the cloud instead of waiting 10 minutes.
/// After refresh, SignalR pushes CatalogUpdated to all connected terminals.
/// </summary>
[ApiController]
[Route("catalog")]
public class CatalogController(CatalogRefreshService refreshService) : ControllerBase
{
    [HttpPost("refresh")]
    public async Task<IActionResult> ForceRefresh()
    {
        await refreshService.TriggerNowAsync();
        return Ok(new { message = "Catalog refresh triggered" });
    }
}

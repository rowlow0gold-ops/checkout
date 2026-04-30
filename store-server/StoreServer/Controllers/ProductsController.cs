using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StoreServer.Hubs;
using StoreServer.Models;

namespace StoreServer.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController(StoreDbContext db, IHubContext<CheckoutHub> hub) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await db.Products.ToListAsync());

    [HttpGet("barcode/{barcode}")]
    public async Task<IActionResult> GetByBarcode(string barcode)
    {
        var p = await db.Products.FirstOrDefaultAsync(x => x.Barcode == barcode);
        return p is null ? NotFound() : Ok(p);
    }

    /// <summary>
    /// Updates price in local SQLite and broadcasts PriceChanged via SignalR to all terminals.
    /// Called by a terminal when it receives a price_change event from the emulator.
    /// </summary>
    [HttpPatch("barcode/{barcode}/price")]
    public async Task<IActionResult> UpdatePrice(string barcode, [FromBody] PriceUpdateDto dto)
    {
        var product = await db.Products.FirstOrDefaultAsync(x => x.Barcode == barcode);
        if (product is null) return NotFound();

        product.Price     = dto.Price;
        product.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Broadcast to all connected terminals via SignalR
        await hub.Clients.All.SendAsync("PriceChanged", barcode, dto.Price);

        return Ok(new { barcode, price = dto.Price });
    }
}

public record PriceUpdateDto(decimal Price);

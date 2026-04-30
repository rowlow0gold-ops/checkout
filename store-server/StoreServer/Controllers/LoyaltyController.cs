using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreServer.Models;

namespace StoreServer.Controllers;

[ApiController]
[Route("api/loyalty")]
public class LoyaltyController(StoreDbContext db) : ControllerBase
{
    /// <summary>
    /// Look up a member by phone number or card ID.
    /// Phone lookups require ?pin=. Card/staff lookups bypass PIN.
    /// </summary>
    /// <summary>
    /// Direct lookup — no PIN required. Used by hardware events (card scan, phone from emulator).
    /// </summary>
    [HttpGet("{identifier}")]
    public async Task<IActionResult> Lookup(string identifier)
    {
        var m = await db.LoyaltyMembers
            .FirstOrDefaultAsync(x => x.Phone == identifier || x.CardId == identifier);
        if (m is null) return NotFound();
        return Ok(m);
    }

    /// <summary>
    /// PIN-verified lookup — used by manual phone entry on the terminal touchscreen.
    /// </summary>
    [HttpGet("{identifier}/verify")]
    public async Task<IActionResult> LookupWithPin(string identifier, [FromQuery] string? pin)
    {
        var m = await db.LoyaltyMembers
            .FirstOrDefaultAsync(x => x.Phone == identifier || x.CardId == identifier);
        if (m is null) return NotFound();

        if (string.IsNullOrEmpty(pin))
            return Unauthorized(new { error = "pin_required" });
        if (m.Pin != pin)
            return Unauthorized(new { error = "wrong_pin" });

        return Ok(m);
    }

    /// <summary>
    /// Add (or subtract) points by member Id — always the same row regardless of
    /// whether the customer identified by phone or by card.
    /// </summary>
    [HttpPatch("{id:int}/points")]
    public async Task<IActionResult> UpdatePoints(int id, [FromBody] UpdatePointsDto dto)
    {
        var m = await db.LoyaltyMembers.FindAsync(id);
        if (m is null) return NotFound();

        m.Points = Math.Max(0, m.Points + dto.Delta);
        await db.SaveChangesAsync();
        return Ok(m);
    }

    /// <summary>
    /// Reset a member's pattern PIN — staff-authorized flow on the terminal.
    /// Looks up by phone or cardId, replaces the Pin column.
    /// </summary>
    [HttpPatch("{identifier}/reset-pin")]
    public async Task<IActionResult> ResetPin(string identifier, [FromBody] ResetPinDto dto)
    {
        var m = await db.LoyaltyMembers
            .FirstOrDefaultAsync(x => x.Phone == identifier || x.CardId == identifier);
        if (m is null) return NotFound();

        m.Pin = dto.NewPin;
        await db.SaveChangesAsync();
        return Ok(new { message = "PIN reset successfully" });
    }
}

public record UpdatePointsDto(int Delta);
public record ResetPinDto(string NewPin);

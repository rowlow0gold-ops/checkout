using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreServer.Models;

namespace StoreServer.Controllers;

[ApiController]
[Route("api/cash-drawer/{terminalId:int}")]
public class CashDrawerController(StoreDbContext db) : ControllerBase
{
    // Seed defaults — mirrors the values in Program.cs
    private static readonly Dictionary<string, int> _defaults = new()
    {
        ["0.01"]      = 200,
        ["0.05"]      = 200,
        ["0.10"]      = 300,
        ["0.25"]      = 250,
        ["0.50"]      = 50,
        ["1.00_coin"] = 75,
        ["1.00_bill"] = 100,
        ["2.00"]      = 30,
        ["5.00"]      = 75,
        ["10.00"]     = 80,
        ["20.00"]     = 100,
        ["50.00"]     = 20,
        ["100.00"]    = 10,
    };

    /// <summary>Returns the full drawer inventory for a terminal, coins first then bills lowest to highest.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(int terminalId)
    {
        var entries = (await db.CashDrawer.Where(e => e.TerminalId == terminalId).ToListAsync())
            .OrderByDescending(e => e.IsCoin)
            .ThenBy(e => e.Denomination)
            .ToList();
        return Ok(entries);
    }

    /// <summary>Adds one unit. Returns isFull=true when count reaches MaxCount.</summary>
    [HttpPost("deposit/{key}")]
    public async Task<IActionResult> Deposit(int terminalId, string key)
    {
        var entry = await db.CashDrawer.FirstOrDefaultAsync(e => e.TerminalId == terminalId && e.Key == key);
        if (entry is null) return NotFound($"Unknown denomination key '{key}' for terminal {terminalId}");
        entry.Count++;
        await db.SaveChangesAsync();
        bool isFull = entry.MaxCount > 0 && entry.Count >= entry.MaxCount;
        return Ok(new { entry.Key, entry.Count, isFull, entry.Label });
    }

    /// <summary>Returns whether any denomination is at or over MaxCount for a terminal.</summary>
    [HttpGet("is-full")]
    public async Task<IActionResult> IsFull(int terminalId)
    {
        var entries = await db.CashDrawer.Where(e => e.TerminalId == terminalId).ToListAsync();
        var fullSlots = entries
            .Where(e => e.MaxCount > 0 && e.Count >= e.MaxCount)
            .Select(e => e.Label)
            .ToList();
        return Ok(new { isFull = fullSlots.Count > 0, fullSlots });
    }

    /// <summary>Removes one unit (management withdrawal).</summary>
    [HttpPost("withdraw/{key}")]
    public async Task<IActionResult> Withdraw(int terminalId, string key)
    {
        var entry = await db.CashDrawer.FirstOrDefaultAsync(e => e.TerminalId == terminalId && e.Key == key);
        if (entry is null) return NotFound($"Unknown denomination key '{key}' for terminal {terminalId}");
        if (entry.Count <= 0) return BadRequest("No units to withdraw");
        entry.Count--;
        await db.SaveChangesAsync();
        return Ok(new { entry.Key, entry.Count });
    }

    /// <summary>
    /// Dispenses change: greedy algorithm largest→smallest, decrements counts.
    /// Returns the breakdown of denominations dispensed.
    /// </summary>
    [HttpPost("dispense")]
    public async Task<IActionResult> Dispense(int terminalId, [FromBody] DispenseDto dto)
    {
        if (dto.Amount <= 0) return Ok(new { breakdown = Array.Empty<object>() });

        var entries = (await db.CashDrawer.Where(e => e.TerminalId == terminalId).ToListAsync())
            .OrderByDescending(e => e.Denomination)
            .ToList();

        long remaining = (long)Math.Round(dto.Amount * 100);
        var breakdown  = new List<object>();

        foreach (var e in entries)
        {
            if (remaining <= 0) break;
            long denomCents = (long)Math.Round(e.Denomination * 100);
            if (denomCents <= 0 || e.Count <= 0) continue;
            long use = Math.Min(remaining / denomCents, e.Count);
            if (use <= 0) continue;
            remaining  -= use * denomCents;
            e.Count    -= (int)use;
            breakdown.Add(new { e.Label, Count = use });
        }

        await db.SaveChangesAsync();

        return Ok(new
        {
            breakdown,
            shortfall = remaining > 0 ? remaining / 100m : 0m,
            success   = remaining == 0
        });
    }

    /// <summary>Restores all counts to the seeded default values.</summary>
    [HttpPost("reset-to-default")]
    public async Task<IActionResult> ResetToDefault(int terminalId)
    {
        foreach (var (key, defaultCount) in _defaults)
        {
            var entry = await db.CashDrawer.FirstOrDefaultAsync(e => e.TerminalId == terminalId && e.Key == key);
            if (entry is not null) entry.Count = defaultCount;
        }
        await db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>Directly sets a denomination's count to an exact value.</summary>
    [HttpPost("set/{key}/{count}")]
    public async Task<IActionResult> SetCount(int terminalId, string key, int count)
    {
        if (count < 0) return BadRequest("Count cannot be negative");
        var entry = await db.CashDrawer.FirstOrDefaultAsync(e => e.TerminalId == terminalId && e.Key == key);
        if (entry is null) return NotFound($"Unknown denomination key '{key}' for terminal {terminalId}");
        entry.Count = count;
        await db.SaveChangesAsync();
        return Ok(new { entry.Key, entry.Count });
    }

    /// <summary>Zeros all counts for a terminal.</summary>
    [HttpPost("reset")]
    public async Task<IActionResult> Reset(int terminalId)
    {
        await db.CashDrawer
            .Where(e => e.TerminalId == terminalId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Count, 0));
        return Ok();
    }
}

public record DispenseDto(decimal Amount);

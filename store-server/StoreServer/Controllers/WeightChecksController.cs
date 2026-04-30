using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreServer.Models;

namespace StoreServer.Controllers;

[ApiController]
[Route("api/weight-checks")]
public class WeightChecksController(StoreDbContext db) : ControllerBase
{
    /// <summary>
    /// Terminal posts a weight check result after every scale comparison.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] WeightCheckRequest req)
    {
        // risk = deviation × item_price
        // deviation: 0 if pass, 1.0 if no reading, otherwise |actual-expected|/expected
        double deviation = req.Result == "pass" ? 0.0
            : req.ActualGrams == 0              ? 1.0
            : Math.Abs(req.ActualGrams - req.ExpectedGrams) / (double)req.ExpectedGrams;
        double riskScore = Math.Round(deviation * (double)req.ItemPrice, 3);

        var log = new WeightCheckLog
        {
            TerminalId    = req.TerminalId,
            Barcode       = req.Barcode,
            ProductName   = req.ProductName,
            ExpectedGrams = req.ExpectedGrams,
            ActualGrams   = req.ActualGrams,
            Result        = req.Result,
            ItemPrice     = req.ItemPrice,
            RiskScore     = riskScore,
            CheckedAt     = DateTime.UtcNow,
        };
        db.WeightCheckLogs.Add(log);
        await db.SaveChangesAsync();
        return Ok(new { log.Id, riskScore });
    }

    /// <summary>
    /// Returns the most recent 500 weight check logs (newest first).
    /// Optional filters: ?terminalId=1 &amp;result=fail
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int?    terminalId = null,
        [FromQuery] string? result     = null)
    {
        var q = db.WeightCheckLogs.AsQueryable();
        if (terminalId.HasValue) q = q.Where(w => w.TerminalId == terminalId.Value);
        if (!string.IsNullOrEmpty(result)) q = q.Where(w => w.Result == result);

        var logs = await q
            .OrderByDescending(w => w.CheckedAt)
            .Take(500)
            .Select(w => new
            {
                w.Id,
                w.TerminalId,
                w.Barcode,
                w.ProductName,
                w.ExpectedGrams,
                w.ActualGrams,
                w.Result,
                w.ItemPrice,
                w.RiskScore,
                w.CheckedAt,
            })
            .ToListAsync();

        return Ok(logs);
    }
}

public record WeightCheckRequest(
    int     TerminalId,
    string  Barcode,
    string  ProductName,
    int     ExpectedGrams,
    int     ActualGrams,
    string  Result,
    decimal ItemPrice
);

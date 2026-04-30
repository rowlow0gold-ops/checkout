using Microsoft.AspNetCore.Mvc;
using StoreServer.Models;
using StoreServer.Services;

namespace StoreServer.Controllers;

[ApiController]
[Route("api/transactions")]
public class TransactionsController(StoreDbContext db, SyncService sync) : ControllerBase
{
    public record ItemDto(int ProductId, int Quantity, decimal UnitPrice);
    public record TransactionDto(int TerminalId, string PaymentMethod, List<ItemDto> Items);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TransactionDto dto)
    {
        var tx = new Transaction
        {
            TerminalId    = dto.TerminalId,
            PaymentMethod = dto.PaymentMethod,
            TotalAmount   = dto.Items.Sum(i => i.UnitPrice * i.Quantity),
            Status        = "pending",
            Items         = dto.Items.Select(i => new TransactionItem
            {
                ProductId = i.ProductId,
                Quantity  = i.Quantity,
                UnitPrice = i.UnitPrice,
                Subtotal  = i.UnitPrice * i.Quantity,
            }).ToList(),
        };
        db.Transactions.Add(tx);
        await db.SaveChangesAsync();

        // Wake the sync loop immediately — no concurrent sync, no Task.Run needed
        await sync.TriggerNowAsync();

        return Ok(new { tx.Id });
    }
}

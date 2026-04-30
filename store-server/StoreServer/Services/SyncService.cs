using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StoreServer.Hubs;
using StoreServer.Models;

namespace StoreServer.Services;

/// <summary>
/// Periodically syncs pending transactions to the cloud FastAPI.
/// Runs as a background service — if cloud is unreachable, retries next cycle.
/// Call TriggerNowAsync() to wake the loop immediately (no 30-second wait).
/// </summary>
public class SyncService(IServiceScopeFactory scopeFactory, IHttpClientFactory httpFactory, IConfiguration config, ILogger<SyncService> logger)
    : BackgroundService
{
    // Channel used to signal an on-demand sync without spawning a concurrent sync
    private readonly System.Threading.Channels.Channel<bool> _trigger =
        System.Threading.Channels.Channel.CreateBounded<bool>(1);

    /// <summary>Called by TransactionsController after each new transaction is saved.</summary>
    public ValueTask TriggerNowAsync() => _trigger.Writer.TryWrite(true)
        ? ValueTask.CompletedTask
        : ValueTask.CompletedTask;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Flush anything left over from a previous session on startup
        await SyncPendingTransactions(ct);

        while (!ct.IsCancellationRequested)
        {
            // Wake up on either a trigger signal OR the 30-second fallback timer
            var delay     = Task.Delay(TimeSpan.FromSeconds(30), ct);
            var triggered = _trigger.Reader.ReadAsync(ct).AsTask();

            await Task.WhenAny(delay, triggered);
            if (ct.IsCancellationRequested) break;

            await SyncPendingTransactions(ct);
        }
    }

    public async Task SyncNowAsync() =>
        await SyncPendingTransactions(CancellationToken.None);

    private async Task SyncPendingTransactions(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        var pending = await db.Transactions
            .Include(t => t.Items)
            .ThenInclude(i => i.Product)
            .Where(t => t.Status == "pending" || t.Status == "failed")
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        var http    = httpFactory.CreateClient("cloud");
        var storeId = config.GetValue<int>("StoreId");

        foreach (var tx in pending)
        {
            try
            {
                var payload = new
                {
                    terminal_id    = tx.TerminalId,
                    store_id       = storeId,
                    payment_method = tx.PaymentMethod,
                    created_at     = tx.CreatedAt.ToUniversalTime().ToString("o"),
                    items          = tx.Items.Select(i => new
                    {
                        barcode    = i.Product?.Barcode ?? "",
                        quantity   = i.Quantity,
                        unit_price = i.UnitPrice,
                    }),
                };
                var res = await http.PostAsJsonAsync("transactions/sync", payload, ct);
                tx.Status = res.IsSuccessStatusCode ? "synced" : "failed";
                logger.LogInformation("Synced tx {Id}: {Status}", tx.Id, tx.Status);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Sync failed for tx {Id}: {Msg}", tx.Id, ex.Message);
            }
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Refreshes product catalog from cloud every 10 minutes,
/// OR immediately when TriggerNowAsync() is called (e.g. from the /catalog/refresh endpoint).
/// After refreshing, pushes SignalR CatalogUpdated to all connected terminals.
/// </summary>
public class CatalogRefreshService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    IHubContext<CheckoutHub> hub,
    ILogger<CatalogRefreshService> logger)
    : BackgroundService
{
    // Channel used to signal an on-demand refresh without interrupting the timer loop
    private readonly System.Threading.Channels.Channel<bool> _trigger =
        System.Threading.Channels.Channel.CreateBounded<bool>(1);

    /// <summary>Called by the API controller to force an immediate refresh.</summary>
    public ValueTask TriggerNowAsync() => _trigger.Writer.TryWrite(true)
        ? ValueTask.CompletedTask
        : ValueTask.CompletedTask;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Initial refresh on startup
        await RefreshCatalog(ct);

        while (!ct.IsCancellationRequested)
        {
            // Wait for either a manual trigger OR the 10-minute timer
            var delay = Task.Delay(TimeSpan.FromMinutes(10), ct);
            var triggered = _trigger.Reader.ReadAsync(ct).AsTask();

            await Task.WhenAny(delay, triggered);
            if (ct.IsCancellationRequested) break;

            await RefreshCatalog(ct);
        }
    }

    private async Task RefreshCatalog(CancellationToken ct)
    {
        try
        {
            var http     = httpFactory.CreateClient("cloud");
            var products = await http.GetFromJsonAsync<List<ProductDto>>("products/", ct);
            if (products is null) return;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();

            foreach (var p in products)
            {
                var existing = await db.Products.FirstOrDefaultAsync(x => x.Barcode == p.barcode, ct);
                if (existing is null)
                    db.Products.Add(new Product { Barcode = p.barcode, Name = p.name, Price = (decimal)p.price, Category = p.category });
                else
                {
                    existing.Name     = p.name;
                    existing.Price    = (decimal)p.price;
                    existing.Category = p.category;
                }
            }
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Catalog refreshed: {Count} products", products.Count);

            // Push SignalR notification to all connected terminals
            await hub.Clients.All.SendAsync("CatalogUpdated", ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Catalog refresh failed: {Msg}", ex.Message);
        }
    }

    private record ProductDto(int id, string barcode, string name, double price, string? category);
}

using Microsoft.AspNetCore.SignalR;

namespace StoreServer.Hubs;

/// <summary>
/// SignalR hub — terminals connect here.
/// Store server pushes real-time price/catalog updates to all terminals instantly.
/// </summary>
public class CheckoutHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var terminalId = Context.GetHttpContext()?.Request.Query["terminalId"];
        await Groups.AddToGroupAsync(Context.ConnectionId, $"terminal:{terminalId}");
        await base.OnConnectedAsync();
    }

    // Called by store server to push catalog update to all terminals
    public async Task NotifyCatalogUpdated()
        => await Clients.All.SendAsync("CatalogUpdated");

    // Called by store server to push price change to all terminals
    public async Task NotifyPriceChanged(string barcode, decimal newPrice)
        => await Clients.All.SendAsync("PriceChanged", new { barcode, newPrice });
}

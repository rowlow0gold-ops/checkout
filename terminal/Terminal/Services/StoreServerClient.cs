using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Terminal.Models;
using System.Text.Json;

namespace Terminal.Services;

/// <summary>
/// Talks to the local store server over LAN.
/// Uses SignalR for real-time updates, REST for lookups and transaction submission.
/// </summary>
public class StoreServerClient : IAsyncDisposable
{
    private readonly HttpClient   _http;
    private readonly HubConnection _hub;

    public event Action?                     OnCatalogUpdated;
    public event Action<string, decimal>?    OnPriceChanged;
    public event Action<bool>?               OnServerConnectionChanged;

    public bool IsServerConnected => _hub.State == HubConnectionState.Connected;

    public StoreServerClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _hub  = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/hub?terminalId={Config.TerminalId}")
            .WithAutomaticReconnect()
            .Build();

        _hub.On("CatalogUpdated", () => OnCatalogUpdated?.Invoke());
        _hub.On<string, decimal>("PriceChanged", (b, p) => OnPriceChanged?.Invoke(b, p));

        _hub.Reconnecting  += _ => { OnServerConnectionChanged?.Invoke(false); return Task.CompletedTask; };
        _hub.Reconnected   += _ => { OnServerConnectionChanged?.Invoke(true);  return Task.CompletedTask; };
        _hub.Closed        += _ => { OnServerConnectionChanged?.Invoke(false); return Task.CompletedTask; };
    }

    public async Task ConnectAsync()
    {
        try
        {
            await _hub.StartAsync();
            OnServerConnectionChanged?.Invoke(true);
        }
        catch
        {
            OnServerConnectionChanged?.Invoke(false);
        }
    }

    public async Task<Product?> LookupBarcodeAsync(string barcode)
    {
        try
        {
            return await _http.GetFromJsonAsync<Product>(
                $"/api/products/barcode/{barcode}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    /// <summary>Persists a price change to the store server SQLite DB.</summary>
    public async Task UpdatePriceAsync(string barcode, decimal newPrice)
    {
        try { await _http.PatchAsJsonAsync($"/api/products/barcode/{barcode}/price", new { price = newPrice }); }
        catch { /* offline — ignore */ }
    }

    /// <summary>
    /// Tells the store server to immediately re-pull the catalog from the cloud
    /// instead of waiting for the next 10-minute cycle.
    /// Store server then pushes SignalR CatalogUpdated to all terminals.
    /// </summary>
    public async Task TriggerCatalogRefreshAsync()
    {
        try { await _http.PostAsync("/catalog/refresh", null); }
        catch { /* store server unreachable — ignore */ }
    }

    public async Task<bool> SubmitTransactionAsync(int terminalId, string paymentMethod, IEnumerable<CartItem> items)
    {
        var payload = new
        {
            TerminalId    = terminalId,
            PaymentMethod = paymentMethod,
            Items = items.Select(i => new
            {
                ProductId = i.ProductId,
                Quantity  = i.Quantity,
                UnitPrice = i.UnitPrice,
            }),
        };
        var res = await _http.PostAsJsonAsync("/api/transactions", payload);
        return res.IsSuccessStatusCode;
    }

    // ── Loyalty ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Looks up a loyalty member by phone number or card ID.
    /// Returns null if not found or server is unreachable.
    /// </summary>
    /// <summary>Card reader / card-ID lookup — no PIN required.</summary>
    public async Task<LoyaltyMemberInfo?> LookupLoyaltyAsync(string phoneOrCard)
    {
        try
        {
            var resp = await _http.GetAsync($"/api/loyalty/{Uri.EscapeDataString(phoneOrCard)}");
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<LoyaltyMemberInfo>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    public enum PinResult { Ok, WrongPin, NotFound, Error }

    /// <summary>Phone-number lookup — PIN is required and validated server-side.</summary>
    public async Task<(LoyaltyMemberInfo? member, PinResult result)> LookupLoyaltyWithPinAsync(string phone, string pin)
    {
        try
        {
            var url  = $"/api/loyalty/{Uri.EscapeDataString(phone)}/verify?pin={Uri.EscapeDataString(pin)}";
            var resp = await _http.GetAsync(url);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (null, PinResult.NotFound);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return (null, PinResult.WrongPin);
            if (!resp.IsSuccessStatusCode)
                return (null, PinResult.Error);
            var member = await resp.Content.ReadFromJsonAsync<LoyaltyMemberInfo>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return (member, PinResult.Ok);
        }
        catch { return (null, PinResult.Error); }
    }

    /// <summary>
    /// Adds (positive) or subtracts (negative) points from a loyalty account.
    /// Uses member Id so phone-lookup and card-lookup always update the same row.
    /// </summary>
    /// <summary>
    /// Resets a loyalty member's pattern PIN — staff-authorized flow.
    /// Returns true on success, false if member not found or server unreachable.
    /// </summary>
    public async Task<bool> ResetLoyaltyPatternAsync(string phoneOrCard, string newPin)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync(
                $"/api/loyalty/{Uri.EscapeDataString(phoneOrCard)}/reset-pin",
                new { newPin });
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task AddLoyaltyPointsAsync(int memberId, int delta)
    {
        try
        {
            await _http.PatchAsJsonAsync($"/api/loyalty/{memberId}/points", new { delta });
        }
        catch { /* offline — ignore */ }
    }

    /// <summary>
    /// Silently records a scale weight-check result in the store server DB.
    /// Fire-and-forget safe — swallows all exceptions.
    /// </summary>
    public async Task SubmitWeightCheckAsync(
        int     terminalId,
        string  barcode,
        string  productName,
        int     expectedGrams,
        int     actualGrams,
        string  result,
        decimal itemPrice)
    {
        try
        {
            await _http.PostAsJsonAsync("/api/weight-checks", new
            {
                terminalId,
                barcode,
                productName,
                expectedGrams,
                actualGrams,
                result,
                itemPrice,
            });
        }
        catch { /* offline — ignore */ }
    }

    /// <summary>
    /// Fetches the current store-wide staff settings PIN from the server.
    /// Returns null if the server is unreachable (caller should fall back to local default).
    /// </summary>
    public async Task<string?> GetStaffPinAsync()
    {
        try
        {
            var resp = await _http.GetAsync("/api/settings/staff-pin");
            if (!resp.IsSuccessStatusCode) return null;
            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return doc.GetProperty("pin").GetString();
        }
        catch { return null; }
    }

    /// <summary>
    /// Saves one inserted cash denomination. Returns the label of the slot if it just hit MaxCount, null otherwise.
    /// </summary>
    public async Task<string?> DepositCashAsync(string key)
    {
        try
        {
            var resp = await _http.PostAsync($"/api/cash-drawer/{Config.TerminalId}/deposit/{key}", null);
            if (!resp.IsSuccessStatusCode) return null;
            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (doc.TryGetProperty("isFull", out var full) && full.GetBoolean())
                return doc.TryGetProperty("label", out var lbl) ? lbl.GetString() : key;
            return null;
        }
        catch { return null; }
    }

    /// <summary>Checks whether any denomination in this terminal's drawer is at or over MaxCount.</summary>
    public async Task<List<string>> CheckDrawerFullSlotsAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"/api/cash-drawer/{Config.TerminalId}/is-full");
            if (!resp.IsSuccessStatusCode) return [];
            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (doc.TryGetProperty("isFull", out var full) && full.GetBoolean())
            {
                var slots = new List<string>();
                foreach (var s in doc.GetProperty("fullSlots").EnumerateArray())
                    slots.Add(s.GetString() ?? "");
                return slots;
            }
            return [];
        }
        catch { return []; }
    }

    /// <summary>
    /// Dispenses change from this terminal's drawer (greedy, largest denom first).
    /// Returns a formatted breakdown string like "1×$10  2×$1  1×25¢", or null on failure.
    /// </summary>
    public async Task<string?> DispenseCashAsync(decimal changeAmount)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync($"/api/cash-drawer/{Config.TerminalId}/dispense", new { amount = changeAmount });
            if (!resp.IsSuccessStatusCode) return null;
            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var breakdown = doc.GetProperty("breakdown");
            var parts = new List<string>();
            foreach (var item in breakdown.EnumerateArray())
            {
                var label = item.GetProperty("label").GetString() ?? "";
                var count = item.GetProperty("count").GetInt64();
                parts.Add($"{count}×{label}");
            }
            return parts.Count > 0 ? string.Join("  ", parts) : "—";
        }
        catch { return null; }
    }

    public async ValueTask DisposeAsync()
        => await _hub.DisposeAsync();
}

public static class Config
{
    public static int    TerminalId      { get; set; } = 1;
    public static int    EmulatorPort    { get; set; } = 9876;  // 9875 + TerminalId
    public static string StoreServerUrl  { get; set; } = "http://localhost:5100";
    public static string DashboardUrl    { get; set; } = "https://checkout.minhojan-world.site";
    /// <summary>Fallback PIN used when the store server is unreachable.</summary>
    public const  string StaffSettingsPin = "4312";
}

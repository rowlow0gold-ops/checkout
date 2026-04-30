using System.Net.Http;
using System.Text.Json;
using Emulator.Models;

namespace Emulator.Services;

public class CashDrawerService(string serverUrl)
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(serverUrl), Timeout = TimeSpan.FromSeconds(5) };

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private static string Base(int terminalId) => $"api/cash-drawer/{terminalId}";

    /// <summary>Fetches drawer counts for a terminal and applies them to the given slots.</summary>
    public async Task RefreshAsync(int terminalId, IEnumerable<CashSlot> slots)
    {
        try
        {
            var resp = await _http.GetAsync(Base(terminalId));
            if (!resp.IsSuccessStatusCode) return;
            var entries = JsonSerializer.Deserialize<List<DrawerEntry>>(
                await resp.Content.ReadAsStringAsync(), _json) ?? [];
            var map = entries.ToDictionary(e => e.Key, e => e.Count);
            foreach (var slot in slots)
                if (map.TryGetValue(slot.Key, out var count))
                    slot.Count = count;
        }
        catch { /* server offline — counts stay at last known value */ }
    }

    public async Task DepositAsync(int terminalId, string key)
    {
        try { await _http.PostAsync($"{Base(terminalId)}/deposit/{key}", null); }
        catch { }
    }

    public async Task WithdrawAsync(int terminalId, string key)
    {
        try { await _http.PostAsync($"{Base(terminalId)}/withdraw/{key}", null); }
        catch { }
    }

    public async Task ResetToDefaultAsync(int terminalId)
    {
        try { await _http.PostAsync($"{Base(terminalId)}/reset-to-default", null); }
        catch { }
    }

    public async Task SetCountAsync(int terminalId, string key, int count)
    {
        try { await _http.PostAsync($"{Base(terminalId)}/set/{key}/{count}", null); }
        catch { }
    }

    /// <summary>Pings using terminal 1's drawer — just checks server reachability.</summary>
    public async Task<bool> PingAsync()
    {
        try
        {
            var resp = await _http.GetAsync(Base(1));
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private record DrawerEntry(string Key, string Label, decimal Denomination, bool IsCoin, int Count);
}

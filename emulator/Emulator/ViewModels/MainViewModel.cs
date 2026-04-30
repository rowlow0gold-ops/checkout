using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emulator.Models;
using Emulator.Protocol;
using Emulator.Services;

namespace Emulator.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string ServerUrl = "http://localhost:5100/";
    private readonly CashDrawerService _cashDrawer = new(ServerUrl);

    // ── Terminals ─────────────────────────────────────────────────────────────
    public ObservableCollection<TerminalEntry> Terminals { get; } =
    [
        new() { Id = 1, Label = "Terminal #1", Port = 9876, IsTargeted = true },
        new() { Id = 2, Label = "Terminal #2", Port = 9877 },
        new() { Id = 3, Label = "Terminal #3", Port = 9878 },
    ];

    // ── Cash drawer ───────────────────────────────────────────────────────────
    // Denomination buttons — used only to send TCP cash_insert events to terminals
    public ObservableCollection<CashSlot> DrawerCoins { get; } =
    [
        new() { Key = "0.01",      Label = "1¢",      Amount = "0.01", IsCoin = true,  DefaultCount = 1000 },
        new() { Key = "0.05",      Label = "5¢",      Amount = "0.05", IsCoin = true,  DefaultCount = 1000 },
        new() { Key = "0.10",      Label = "10¢",     Amount = "0.10", IsCoin = true,  DefaultCount = 1500 },
        new() { Key = "0.25",      Label = "25¢",     Amount = "0.25", IsCoin = true,  DefaultCount = 1250 },
        new() { Key = "0.50",      Label = "50¢",     Amount = "0.50", IsCoin = true,  DefaultCount = 250  },
        new() { Key = "1.00_coin", Label = "$1 coin", Amount = "1.00", IsCoin = true,  DefaultCount = 375  },
    ];
    public ObservableCollection<CashSlot> DrawerBills { get; } =
    [
        new() { Key = "1.00_bill", Label = "$1",   Amount = "1.00",   IsCoin = false, DefaultCount = 500 },
        new() { Key = "2.00",      Label = "$2",   Amount = "2.00",   IsCoin = false, DefaultCount = 150 },
        new() { Key = "5.00",      Label = "$5",   Amount = "5.00",   IsCoin = false, DefaultCount = 375 },
        new() { Key = "10.00",     Label = "$10",  Amount = "10.00",  IsCoin = false, DefaultCount = 400 },
        new() { Key = "20.00",     Label = "$20",  Amount = "20.00",  IsCoin = false, DefaultCount = 500 },
        new() { Key = "50.00",     Label = "$50",  Amount = "50.00",  IsCoin = false, DefaultCount = 100 },
        new() { Key = "100.00",    Label = "$100", Amount = "100.00", IsCoin = false, DefaultCount = 50  },
    ];

    public IEnumerable<CashSlot> AllSlots => DrawerCoins.Concat(DrawerBills);

    // ── Server connection status ──────────────────────────────────────────────
    [ObservableProperty] private bool   _serverConnected  = false;
    [ObservableProperty] private string _serverStatusText = "Connecting…";

    // ── Selected terminal (cash drawer scope) ─────────────────────────────────
    [ObservableProperty] private TerminalEntry? _selectedTerminal;
    private int SelectedTerminalId => SelectedTerminal?.Id ?? 1;

    [RelayCommand]
    private void SelectTerminal(TerminalEntry terminal)
    {
        foreach (var t in Terminals) t.IsSelected = false;
        terminal.IsSelected  = true;
        SelectedTerminal     = terminal;
        foreach (var slot in AllSlots) slot.Count = slot.DefaultCount;
        _ = RefreshDrawerAsync();
        AppendLog($"📺 Selected {terminal.Label} — drawer counts loaded");
    }

    // ── Drawer management popup ───────────────────────────────────────────────
    [ObservableProperty] private bool _showDrawerManagement = false;

    public bool AnyConnected => Terminals.Any(t => t.IsConnected);

    // ── Scan ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _barcodeInput = "1234567890";

    // ── Hardware status ───────────────────────────────────────────────────────
    [ObservableProperty] private bool _scannerOk    = true;
    [ObservableProperty] private bool _printerOk    = true;
    [ObservableProperty] private bool _cardReaderOk = true;
    [ObservableProperty] private bool _scaleOk      = true;

    // ── Wrong weight simulation (scanner area toggle) ─────────────────────────
    // When ON: preset scans send gray-zone weight (~73% of expected) → logged as fail in DB
    [ObservableProperty] private bool _simulateWrongWeight = false;

    partial void OnSimulateWrongWeightChanged(bool value)
    {
        if (value) SimulateNotPlaced = false; // mutually exclusive
        AppendLog(value ? "⚖ Wrong-weight mode ON  (gray-zone weights)" : "⚖ Wrong-weight mode OFF");
    }

    // ── Not placed simulation (scanner area toggle) ───────────────────────────
    // When ON: preset scans send 0 g → customer didn't place item on scale
    [ObservableProperty] private bool _simulateNotPlaced = false;

    partial void OnSimulateNotPlacedChanged(bool value)
    {
        if (value) SimulateWrongWeight = false; // mutually exclusive
        AppendLog(value ? "⚖ Not-placed mode ON  (scans will send 0 g)" : "⚖ Not-placed mode OFF");
    }

    // Barcode of the last weighted item scanned while placement popup is showing.
    // Cleared when the item is placed (PlaceItemCommand) or Apply Status resolves it.
    private string _pendingWeightBarcode = "";
    public bool HasPendingItem => !string.IsNullOrEmpty(_pendingWeightBarcode);
    private void SetPendingBarcode(string value)
    {
        _pendingWeightBarcode = value;
        OnPropertyChanged(nameof(HasPendingItem));
        // Re-enable Place Item button whenever a new pending item is registered
        if (!string.IsNullOrEmpty(value)) PlaceItemReady = true;
    }

    // Controls the Place Item button — true when a scale check is expected on the terminal.
    // Disabled after clicking, re-enabled when a new pending barcode arrives or manually reset.
    [ObservableProperty] private bool _placeItemReady = false;

    // ── Product weights — auto-sent after each preset scan ────────────────────
    private static readonly Dictionary<string, int> _productWeights = new()
    {
        ["1234567890"]    = 182,   // Apple
        ["9780201379624"] = 450,   // Programming Book
        ["5901234123457"] = 100,   // Dark Chocolate
        ["0012000001086"] = 528,   // Pepsi 500ml
        ["5000112546415"] = 200,   // Cadbury Dairy Milk
        ["8801062573158"] = 120,   // Shin Ramyun
        ["0038000845031"] = 500,   // Kellogg's Corn Flakes
    };

    // ── Wrong weights — ~73% of real weight, just below the 75% pass floor ───
    private static readonly Dictionary<string, int> _wrongWeights = new()
    {
        ["1234567890"]    = 133,   // Apple        (real 182g, floor 137g → sends 133g)
        ["9780201379624"] = 328,   // Book         (real 450g, floor 338g → sends 328g)
        ["5901234123457"] = 74,    // Dark Choc    (real 100g, floor  75g → sends  74g)
        ["0012000001086"] = 385,   // Pepsi 500ml  (real 528g, floor 396g → sends 385g)
        ["5000112546415"] = 146,   // Cadbury      (real 200g, floor 150g → sends 146g)
        ["8801062573158"] = 89,    // Shin Ramyun  (real 120g, floor  90g → sends  89g)
        ["0038000845031"] = 374,   // Corn Flakes  (real 500g, floor 375g → sends 374g)
    };

    // ── Items-on-scale count (used by PlaceItem for cumulative weight) ────────
    [ObservableProperty] private int _scaleItemCount = 1;

    // ── Payment ───────────────────────────────────────────────────────────────

    // ── Price change ──────────────────────────────────────────────────────────
    [ObservableProperty] private string _priceChangeBarcode = "1234567890";
    [ObservableProperty] private string _priceChangeValue   = "9.99";

    // ── Loyalty card ─────────────────────────────────────────────────────────
    [ObservableProperty] private string _loyaltyCardInput = "";
    [ObservableProperty] private string _phoneInput       = "";

    // ── Log ───────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _log = "";

    public MainViewModel()
    {
        // Seed counts from defaults immediately so the UI is never all-zero
        foreach (var slot in AllSlots) slot.Count = slot.DefaultCount;

        // Auto-select Terminal 1
        _selectedTerminal        = Terminals[0];
        Terminals[0].IsSelected  = true;

        // Auto-connect to all terminals on startup
        foreach (var terminal in Terminals)
            _ = AutoConnectAsync(terminal);

        // Sync counts from server, then start the server status ping loop
        _ = RefreshDrawerAsync();
        _ = PingServerLoopAsync();
    }

    private async Task PingServerLoopAsync()
    {
        while (true)
        {
            var ok = await _cashDrawer.PingAsync();
            ServerConnected  = ok;
            ServerStatusText = ok ? "Connected" : "Offline";
            // Keep badge counts in sync — reflects change dispensed by terminal
            if (ok && !ShowDrawerManagement)
                await RefreshDrawerAsync();
            await Task.Delay(3000);
        }
    }

    private async Task RefreshDrawerAsync()
    {
        await _cashDrawer.RefreshAsync(SelectedTerminalId, AllSlots);
        foreach (var slot in AllSlots.Where(s => s.IsAtMax))
            AppendLog($"⚠️ DRAWER FULL — {SelectedTerminal?.Label} · {slot.Label} at max ({slot.DefaultCount})");
    }

    private async Task AutoConnectAsync(TerminalEntry terminal)
    {
        while (!terminal.IsConnected)
        {
            await terminal.ConnectAsync();
            if (!terminal.IsConnected)
                await Task.Delay(2000);
        }
        AppendLog($"Auto-connected to {terminal.Label}");
    }

    // ── Terminal toggle ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task TogglePower(TerminalEntry terminal)
    {
        if (terminal.IsConnected)
        {
            // Shut down
            await terminal.SendAsync(new EmulatorMessage { Type = "shutdown", Payload = "" });
            await Task.Delay(200);
            terminal.Disconnect();
            AppendLog($"⏻ Shutdown {terminal.Label}");
            OnPropertyChanged(nameof(AnyConnected));
        }
        else
        {
            // Launch then auto-connect
            var exeDir   = AppContext.BaseDirectory;
            var checkout = Path.GetFullPath(Path.Combine(exeDir, "../../../../.."));
            var projDir  = Path.Combine(checkout, "terminal", "Terminal");
            var args     = terminal.Id == 1 ? "" : $"-- --id {terminal.Id}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName         = "dotnet",
                Arguments        = $"run --no-build {args}".Trim(),
                WorkingDirectory = projDir,
                UseShellExecute  = false,
            });
            terminal.StatusText = "Starting…";
            AppendLog($"▶ Launching {terminal.Label}…");
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                if (await terminal.ConnectAsync())
                {
                    AppendLog($"✓ Connected to {terminal.Label}");
                    OnPropertyChanged(nameof(AnyConnected));
                    return;
                }
            }
            AppendLog($"✗ {terminal.Label} did not respond — try Connect manually");
        }
    }

    [RelayCommand]
    private async Task ToggleTerminal(TerminalEntry terminal)
    {
        await terminal.ToggleAsync();
        AppendLog(terminal.IsConnected
            ? $"Connected to {terminal.Label} :{terminal.Port}"
            : $"Disconnected from {terminal.Label}");
        OnPropertyChanged(nameof(AnyConnected));
    }

    // ── Scan ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SendScan()
    {
        if (string.IsNullOrWhiteSpace(BarcodeInput)) return;
        var barcode = BarcodeInput.Trim();
        await Broadcast(new EmulatorMessage { Type = "scan", Payload = barcode });
        await MaybeSendScaleWeightAsync(barcode);
    }

    /// <summary>
    /// Preset buttons call this directly with the barcode as a parameter,
    /// bypassing the BarcodeInput field to avoid any binding-timing race.
    /// Also auto-fires the expected scale weight for weighted products.
    /// </summary>
    [RelayCommand]
    private async Task SendPresetScan(string barcode)
    {
        await Broadcast(new EmulatorMessage { Type = "scan", Payload = barcode });
        await MaybeSendScaleWeightAsync(barcode);
    }

    [RelayCommand]
    private async Task SendRandomScans()
    {
        string[] pool = ["1234567890", "9780201379624", "5901234123457", "0012000001086"];
        var rng = new Random();
        var picks = pool.OrderBy(_ => rng.Next()).Take(3).ToList();
        foreach (var b in picks)
        {
            await Broadcast(new EmulatorMessage { Type = "scan", Payload = b });
            await MaybeSendScaleWeightAsync(b);
            await Task.Delay(200);
        }
    }

    /// <summary>
    /// If the barcode maps to a product with a known weight, waits 600ms then sends a scale reading:
    ///   SimulateWrongWeight ON  → 50 g (logs as fail in DB, item still added)
    ///   ScaleOk OFF (Error)     → 0 g  (terminal loops prompting customer to place item)
    ///                             and stores the barcode so toggling Scale → OK auto-resolves it
    ///   Normal                  → real expected weight
    /// </summary>
    private async Task MaybeSendScaleWeightAsync(string barcode)
    {
        if (!_productWeights.TryGetValue(barcode, out var grams)) return;
        await Task.Delay(600);

        if (SimulateNotPlaced)
        {
            // Customer ignored the bagging area — don't send any reading.
            // Terminal waits at the placement prompt; resolve via "Place Item on Scale".
            SetPendingBarcode(barcode);
        }
        else if (SimulateWrongWeight)
        {
            // Gray-zone wrong weight — ~73% of expected, just below the fail floor
            var wrong = _wrongWeights.TryGetValue(barcode, out var w) ? w : (int)(grams * 0.73);
            await Broadcast(new EmulatorMessage { Type = "scale", Payload = wrong.ToString() });
            SetPendingBarcode("");
        }
        else if (!ScaleOk)
        {
            await Broadcast(new EmulatorMessage { Type = "scale", Payload = "0" });
            SetPendingBarcode(barcode);
        }
        else
        {
            await Broadcast(new EmulatorMessage { Type = "scale", Payload = grams.ToString() });
            SetPendingBarcode("");
        }
    }

    [RelayCommand]
    private async Task SendUnknownBarcode()
        => await Broadcast(new EmulatorMessage { Type = "scan", Payload = "UNKNOWN_BARCODE_XYZ" });

    /// <summary>
    /// Resolves any active scale-placement prompt on the terminal.
    /// Sends singleItemWeight × ScaleItemCount so the terminal can validate cumulative weight
    /// (e.g. 3 apples = 3 × 182 g = 546 g).
    /// Works for both initial-scan prompts and + / − debounce prompts.
    /// </summary>
    [RelayCommand]
    private async Task PlaceItem()
    {
        PlaceItemReady = false;  // disable immediately after click

        int single = 200;
        if (!string.IsNullOrEmpty(_pendingWeightBarcode)
            && _productWeights.TryGetValue(_pendingWeightBarcode, out var known))
            single = known;

        int total = single * Math.Max(1, ScaleItemCount);
        await Broadcast(new EmulatorMessage { Type = "scale", Payload = total.ToString() });
        AppendLog($"⚖ Placed {ScaleItemCount}× item on scale → {total} g");
        SetPendingBarcode("");
    }

    /// <summary>Re-enables the Place Item button (e.g. for a + debounce check).</summary>
    [RelayCommand] private void ReadyPlaceItem() => PlaceItemReady = true;

    [RelayCommand] private void IncreaseScaleItemCount() => ScaleItemCount = Math.Min(ScaleItemCount + 1, 99);
    [RelayCommand] private void DecreaseScaleItemCount() => ScaleItemCount = Math.Max(ScaleItemCount - 1, 1);

    // ── Hardware status — auto-broadcast on each toggle ───────────────────────

    partial void OnScannerOkChanged(bool value)
        => _ = Broadcast(new EmulatorMessage { Type = "status", Payload = $"scanner:{(value ? "ok" : "error")}" });

    partial void OnPrinterOkChanged(bool value)
        => _ = Broadcast(new EmulatorMessage { Type = "status", Payload = $"printer:{(value ? "ok" : "error")}" });


    partial void OnCardReaderOkChanged(bool value)
        => _ = Broadcast(new EmulatorMessage { Type = "status", Payload = $"cardreader:{(value ? "ok" : "error")}" });

    /// <summary>
    /// Auto-broadcasts the scale status change. When scale comes back OK while a weighted
    /// item is pending (customer just placed it), auto-sends the real weight so the terminal
    /// placement popup resolves.
    /// </summary>
    partial void OnScaleOkChanged(bool value)
    {
        _ = Broadcast(new EmulatorMessage { Type = "status", Payload = $"scale:{(value ? "ok" : "error")}" });

        if (value && !string.IsNullOrEmpty(_pendingWeightBarcode)
            && _productWeights.TryGetValue(_pendingWeightBarcode, out var grams))
        {
            var barcode = _pendingWeightBarcode;
            _ = Task.Run(async () =>
            {
                await Task.Delay(200); // let status message arrive first
                await Broadcast(new EmulatorMessage { Type = "scale", Payload = grams.ToString() });
                AppendLog($"⚖ Sent {grams} g for pending item ({barcode}) — popup will dismiss");
                SetPendingBarcode("");
            });
        }

        AppendLog(value ? "⚖ Scale → OK" : "⚖ Scale → ERROR");
    }

    [RelayCommand]
    private void DisconnectAllHardware()
    {
        ScannerOk    = false;
        PrinterOk    = false;
        CardReaderOk = false;
        ScaleOk      = false;
        AppendLog("💥 All hardware → disconnected");
    }

    [RelayCommand]
    private void ReconnectAllHardware()
    {
        ScannerOk    = true;
        PrinterOk    = true;
        CardReaderOk = true;
        ScaleOk      = true;
        AppendLog("✓ All hardware → reconnected");
    }


    // ── Payment ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ApprovePayment()
    {
        await Broadcast(new EmulatorMessage { Type = "payment", Payload = "success" });
        AppendLog("Payment APPROVED");
    }

    [RelayCommand]
    private async Task DeclinePayment()
    {
        await Broadcast(new EmulatorMessage { Type = "payment", Payload = "fail" });
        AppendLog("Payment DECLINED");
    }

    // ── Cash denominations ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SendCashInsert(CashSlot slot)
    {
        await Broadcast(new EmulatorMessage { Type = "cash_insert", Payload = slot.Key });
        // Deposit is handled by the terminal's OnCashKeyDepositedAsync, which guards
        // against wrong screen state.  The emulator only simulates the hardware signal.
        AppendLog($"💵 Cash inserted: {slot.Label}");
        await Task.Delay(300);   // let terminal finish its async deposit before we refresh
        await RefreshDrawerAsync();                  // update the total display
    }

    // ── Drawer management popup ───────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenDrawerManagement()
    {
        await RefreshDrawerAsync();
        foreach (var slot in AllSlots) slot.EditCountText = slot.Count.ToString();
        ShowDrawerManagement = true;
    }

    [RelayCommand]
    private async Task SaveDrawerManagement()
    {
        foreach (var slot in AllSlots)
        {
            var parsed   = int.TryParse(slot.EditCountText, out var v) ? v : slot.Count;
            var newCount = Math.Max(0, Math.Min(parsed, slot.DefaultCount));
            if (newCount != slot.Count)
            {
                await _cashDrawer.SetCountAsync(SelectedTerminalId, slot.Key, newCount);
                slot.Count = newCount;
            }
            slot.EditCountText = slot.Count.ToString();
        }
        ShowDrawerManagement = false;
        AppendLog("💰 Drawer counts saved");
    }

    [RelayCommand]
    private void CloseDrawerManagement()
    {
        foreach (var slot in AllSlots) slot.EditCountText = slot.Count.ToString();
        ShowDrawerManagement = false;
    }

    /// <summary>Increments the popup edit value by 1 (capped at DefaultCount).</summary>
    [RelayCommand]
    private void IncreaseEditCount(CashSlot slot)
    {
        var current = int.TryParse(slot.EditCountText, out var v) ? v : 0;
        if (slot.DefaultCount > 0 && current >= slot.DefaultCount) return;
        slot.EditCountText = (current + 1).ToString();
    }

    /// <summary>Decrements the popup edit value by 1 (floor 0).</summary>
    [RelayCommand]
    private void DecreaseEditCount(CashSlot slot)
    {
        var current = int.TryParse(slot.EditCountText, out var v) ? v : 0;
        if (current <= 0) return;
        slot.EditCountText = (current - 1).ToString();
    }

    /// <summary>Pour one unit into the drawer (management top-up, no TCP message). Capped at DefaultCount.</summary>
    [RelayCommand]
    private async Task PourCash(CashSlot slot)
    {
        if (slot.DefaultCount > 0 && slot.Count >= slot.DefaultCount)
        {
            AppendLog($"⚠ {slot.Label} already at max ({slot.DefaultCount})");
            return;
        }
        await _cashDrawer.DepositAsync(SelectedTerminalId, slot.Key);
        slot.Count++;
        AppendLog($"➕ Poured 1× {slot.Label} into drawer");
    }

    /// <summary>Withdraw one unit from the drawer (management action, no TCP message).</summary>
    [RelayCommand]
    private async Task WithdrawCash(CashSlot slot)
    {
        if (slot.Count <= 0) return;
        await _cashDrawer.WithdrawAsync(SelectedTerminalId, slot.Key);
        slot.Count--;
        AppendLog($"➖ Withdrew 1× {slot.Label} from drawer");
    }

    /// <summary>Resets all counts to seed defaults.</summary>
    [RelayCommand]
    private async Task ResetToDefault()
    {
        await _cashDrawer.ResetToDefaultAsync(SelectedTerminalId);
        await RefreshDrawerAsync();
        AppendLog("↺ Drawer reset to default values");
    }

    // ── Network ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SimulateTimeSkip()
    {
        await Broadcast(new EmulatorMessage { Type = "time_skip", Payload = "" });
        AppendLog("⏩ Time skip → inactivity warning triggered");
    }

    [RelayCommand]
    private async Task SimulateSlowNetwork()
    {
        await Broadcast(new EmulatorMessage { Type = "network", Payload = "latency:3000" });
        AppendLog("3s latency applied");
    }

    [RelayCommand]
    private async Task ResetNetworkLatency()
    {
        await Broadcast(new EmulatorMessage { Type = "network", Payload = "latency:0" });
        AppendLog("Latency reset");
    }

    [RelayCommand]
    private async Task SimulateNetworkDown()
    {
        await Broadcast(new EmulatorMessage { Type = "network", Payload = "down" });
        AppendLog("⚠ Network set to DOWN");
    }

    [RelayCommand]
    private async Task SimulateNetworkUp()
    {
        await Broadcast(new EmulatorMessage { Type = "network", Payload = "up" });
        AppendLog("✓ Network set to UP");
    }

    // ── Real-time events ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SimulatePriceChange()
    {
        var barcode = PriceChangeBarcode.Trim();
        var price   = PriceChangeValue.Trim();
        if (string.IsNullOrEmpty(barcode) || string.IsNullOrEmpty(price)) return;
        await Broadcast(new EmulatorMessage { Type = "price_change", Payload = $"{barcode}:{price}" });
    }

    [RelayCommand]
    private async Task SimulateCatalogUpdate()
        => await Broadcast(new EmulatorMessage { Type = "catalog_update", Payload = "" });

    // ── Loyalty card ─────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SendLoyaltyScan()
    {
        var input = LoyaltyCardInput.Trim();
        if (string.IsNullOrWhiteSpace(input)) return;
        // Only accept 13-digit card numbers — phone numbers belong to the phone section
        if (input.Length != 13 || !input.All(char.IsDigit))
        {
            AppendLog("✕ Insert Card only accepts 13-digit card numbers");
            return;
        }
        await Broadcast(new EmulatorMessage { Type = "loyalty", Payload = input });
    }

    /// <summary>
    /// Preset loyalty buttons call this directly with the card/phone value,
    /// avoiding any binding-timing race on LoyaltyCardInput.
    /// </summary>
    [RelayCommand]
    private async Task SendPresetLoyalty(string cardId)
        => await Broadcast(new EmulatorMessage { Type = "loyalty", Payload = cardId });

    [RelayCommand]
    private async Task SendPhoneLookup()
    {
        if (string.IsNullOrWhiteSpace(PhoneInput)) return;
        await Broadcast(new EmulatorMessage { Type = "loyalty", Payload = PhoneInput.Trim() });
    }

    /// <summary>
    /// Phone preset buttons call this directly with the number as parameter,
    /// avoiding binding-timing race on PhoneInput.
    /// </summary>
    [RelayCommand]
    private async Task SendPresetPhone(string phone)
        => await Broadcast(new EmulatorMessage { Type = "loyalty", Payload = phone });

    [RelayCommand]
    private async Task SendUnknownLoyaltyCard()
        => await Broadcast(new EmulatorMessage { Type = "loyalty", Payload = "UNKNOWN_CARD_XYZ" });

    [RelayCommand]
    private async Task SendLoyaltyError()
        => await Broadcast(new EmulatorMessage { Type = "loyalty_error", Payload = "" });

    // ── Staff card ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SendStaffCard(string cardId)
        => await Broadcast(new EmulatorMessage { Type = "loyalty", Payload = cardId });

    [RelayCommand]
    private async Task SendForceLockout(string target)
    {
        await Broadcast(new EmulatorMessage { Type = "force_lockout", Payload = target });
        AppendLog($"🔒 Forced {target} lockout → call-staff screen");
    }


    // ── Log ───────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ClearLog() => Log = "";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task Broadcast(EmulatorMessage msg)
    {
        var targets = Terminals.Where(t => t.IsConnected && t.IsTargeted).ToList();
        if (targets.Count == 0)
        {
            AppendLog("No targeted terminals — check the target toggles");
            return;
        }
        foreach (var t in targets)
            await t.SendAsync(msg);
        var names = string.Join(", ", targets.Select(t => t.Label));
        AppendLog($"→ [{msg.Type}] {msg.Payload}  ({names})");
    }

    private void AppendLog(string line)
        => Log = $"[{DateTime.Now:HH:mm:ss}] {line}\n" + Log;
}

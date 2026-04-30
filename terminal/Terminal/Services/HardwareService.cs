using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Terminal.Services;

/// <summary>
/// Hardware abstraction layer.
/// In real deployment: wires up actual serial/USB devices.
/// In emulator mode: listens on TCP :9876 for JSON messages from the emulator app.
/// Message format: { "type": "scan|payment|cash_insert|status|network|price_change|catalog_update|loyalty|loyalty_error",
///                   "payload": "..." }
/// </summary>
public class HardwareService : IAsyncDisposable
{
    public event Action<string>?          BarcodeScanned;
    public event Action<string>?          LoyaltyScanned;
    public event Action?                  LoyaltyReaderError;
    public event Action?                  HardwareStatusChanged;
    public event Action<string, decimal>? PriceChanged;
    public event Action?                  CatalogUpdated;
    public event Action?                  NetworkStateChanged;
    public event Action<decimal>?         CashInserted;      // fires for each bill/coin inserted
    public event Action<string>?          CashKeyDeposited;  // fires with the denomination key for DB deposit
    public event Action<int>?             ScaleWeightChanged; // fires with grams when item placed on scale
    public event Action?                  TimeSkipRequested;  // fires when emulator sends time_skip
    public event Action?                  ShutdownRequested;  // fires when emulator sends shutdown
    public event Action<string>?          LockoutForced;      // "pin" | "pattern" — emulator forces call-staff screen

    public bool NetworkDown      { get; private set; } = false;
    public int  NetworkLatencyMs { get; private set; } = 0;

    public HardwareStatus ScannerStatus     { get; private set; } = HardwareStatus.Connected;
    public HardwareStatus PrinterStatus     { get; private set; } = HardwareStatus.Connected;
    public HardwareStatus PaymentStatus     { get; private set; } = HardwareStatus.Connected;
    public HardwareStatus ScaleStatus       { get; private set; } = HardwareStatus.Connected;
    public HardwareStatus CardReaderStatus  { get; private set; } = HardwareStatus.Connected;

    // TCS fulfilled when emulator explicitly approves or declines card/mobile payment
    private TaskCompletionSource<bool>? _paymentTcs;

    private TcpListener?     _listener;
    private CancellationTokenSource _cts = new();

    public HardwareService()
    {
        _ = StartListenerAsync();
    }

    // ── Emulator TCP listener ─────────────────────────────────────────────

    private async Task StartListenerAsync()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Config.EmulatorPort);
            _listener.Start();

            while (!_cts.Token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = HandleClientAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        catch { /* listener shut down */ }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        var buffer = new byte[4096];
        var sb     = new StringBuilder();

        try
        {
            int n;
            while ((n = await stream.ReadAsync(buffer, _cts.Token)) > 0)
            {
                sb.Append(Encoding.UTF8.GetString(buffer, 0, n));

                // Messages are newline-delimited
                string? raw;
                while ((raw = ExtractLine(sb)) is not null)
                    DispatchMessage(raw);
            }
        }
        catch { /* client disconnected */ }
        finally { client.Dispose(); }
    }

    private static string? ExtractLine(StringBuilder sb)
    {
        var s = sb.ToString();
        var idx = s.IndexOf('\n');
        if (idx < 0) return null;
        sb.Remove(0, idx + 1);
        return s[..idx].Trim();
    }

    private void DispatchMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var doc     = JsonDocument.Parse(json);
            string type    = doc.RootElement.GetProperty("type").GetString()    ?? "";
            string payload = doc.RootElement.GetProperty("payload").GetString() ?? "";

            switch (type)
            {
                case "scan":
                    BarcodeScanned?.Invoke(payload);
                    break;

                case "payment":
                    // Emulator explicitly approves or declines card/mobile payment
                    bool approved = payload == "success";
                    _paymentTcs?.TrySetResult(approved);
                    break;

                case "cash_insert":
                    // payload: denomination key e.g. "10.00", "1.00_coin", "1.00_bill"
                    var denomAmt = KeyToAmount(payload);
                    if (denomAmt > 0)
                    {
                        CashInserted?.Invoke(denomAmt);
                        CashKeyDeposited?.Invoke(payload);
                    }
                    break;

                case "status":
                    // payload: "scanner:ok" | "scanner:error" | "scanner:disconnected" | etc.
                    ApplyStatusPayload(payload);
                    break;

                case "network":
                    switch (payload)
                    {
                        case "down":
                            NetworkDown      = true;
                            NetworkLatencyMs = 0;
                            break;
                        case "up":
                            NetworkDown      = false;
                            NetworkLatencyMs = 0;
                            break;
                        default:
                            // e.g. "latency:3000" or "latency:0"
                            if (payload.StartsWith("latency:") &&
                                int.TryParse(payload[8..], out var ms))
                            {
                                NetworkDown      = false;
                                NetworkLatencyMs = ms;
                            }
                            break;
                    }
                    NetworkStateChanged?.Invoke();
                    break;

                case "price_change":
                    // payload: "barcode:price"  e.g. "1234567890:9.99"
                    var parts = payload.Split(':');
                    if (parts.Length == 2 && decimal.TryParse(
                            parts[1],
                            System.Globalization.NumberStyles.Number,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var price))
                        PriceChanged?.Invoke(parts[0], price);
                    break;

                case "scale":
                    if (int.TryParse(payload, out var grams))
                        ScaleWeightChanged?.Invoke(grams);
                    break;

                case "loyalty":
                    LoyaltyScanned?.Invoke(payload);
                    break;

                case "loyalty_error":
                    LoyaltyReaderError?.Invoke();
                    break;

                case "catalog_update":
                    CatalogUpdated?.Invoke();
                    break;

                case "time_skip":
                    TimeSkipRequested?.Invoke();
                    break;

                case "shutdown":
                    ShutdownRequested?.Invoke();
                    break;

                case "force_lockout":
                    // payload: "pin" | "pattern"
                    LockoutForced?.Invoke(payload);
                    break;
            }
        }
        catch { /* malformed JSON — ignore */ }
    }

    private void ApplyStatusPayload(string payload)
    {
        var parts = payload.Split(':');
        if (parts.Length != 2) return;

        var device = parts[0] switch
        {
            "scanner"    => (HardwareDevice?)HardwareDevice.Scanner,
            "printer"    => (HardwareDevice?)HardwareDevice.Printer,
            "cash"       => (HardwareDevice?)HardwareDevice.Payment,
            "payment"    => (HardwareDevice?)HardwareDevice.Payment,  // legacy alias
            "scale"      => (HardwareDevice?)HardwareDevice.Scale,
            "cardreader" => (HardwareDevice?)HardwareDevice.CardReader,
            _            => null
        };
        if (device is null) return;

        var status = parts[1] switch
        {
            "ok"           => HardwareStatus.Connected,
            "disconnected" => HardwareStatus.Disconnected,
            _              => HardwareStatus.Error,
        };

        SetStatus(device.Value, status);
    }

    // ── Public API ────────────────────────────────────────────────────────

    private static decimal KeyToAmount(string key) => key switch
    {
        "0.01"      => 0.01m,
        "0.05"      => 0.05m,
        "0.10"      => 0.10m,
        "0.25"      => 0.25m,
        "0.50"      => 0.50m,
        "1.00_coin" => 1.00m,
        "1.00_bill" => 1.00m,
        "2.00"      => 2.00m,
        "5.00"      => 5.00m,
        "10.00"     => 10.00m,
        "20.00"     => 20.00m,
        "50.00"     => 50.00m,
        "100.00"    => 100.00m,
        _           => 0m
    };

    public void SetStatus(HardwareDevice device, HardwareStatus status)
    {
        switch (device)
        {
            case HardwareDevice.Scanner:    ScannerStatus    = status; break;
            case HardwareDevice.Printer:    PrinterStatus    = status; break;
            case HardwareDevice.Payment:    PaymentStatus    = status; break;
            case HardwareDevice.Scale:      ScaleStatus      = status; break;
            case HardwareDevice.CardReader: CardReaderStatus = status; break;
        }
        HardwareStatusChanged?.Invoke();
    }

    /// <summary>
    /// Waits for the emulator to send an explicit "payment:success" or "payment:fail".
    /// Times out after 120 seconds (returns false).
    /// </summary>
    public async Task<bool> ProcessPaymentAsync(decimal amount, string method)
    {
        if (PaymentStatus != HardwareStatus.Connected)
            return false;

        _paymentTcs = new TaskCompletionSource<bool>();
        var timeout = Task.Delay(TimeSpan.FromSeconds(120), _cts.Token);
        var winner  = await Task.WhenAny(_paymentTcs.Task, timeout);
        if (winner == timeout) return false; // timed out — treat as declined
        return _paymentTcs.Task.Result;
    }

    /// <summary>
    /// Cancels an in-progress card/mobile payment wait (e.g., customer cancels).
    /// </summary>
    public void CancelPaymentWait()
    {
        _paymentTcs?.TrySetResult(false);
        _paymentTcs = null;
    }

    public void PrintReceipt(string content)
    {
        if (PrinterStatus != HardwareStatus.Connected)
            throw new InvalidOperationException("Printer disconnected");
        // Real impl: send ESC/POS commands to thermal printer
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener?.Stop();
        await Task.CompletedTask;
    }
}

public enum HardwareStatus { Connected, Disconnected, Error }
public enum HardwareDevice { Scanner, Printer, Payment, Scale, CardReader }

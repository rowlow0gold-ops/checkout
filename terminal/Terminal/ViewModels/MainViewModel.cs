using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Terminal.Models;
using Terminal.Services;

namespace Terminal.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly StoreServerClient _server;
    private readonly HardwareService   _hardware;

    public ObservableCollection<CartItem>          Cart           { get; } = [];
    public ObservableCollection<ReceiptHistoryEntry> ReceiptHistory { get; } = [];

    [ObservableProperty] private ReceiptHistoryEntry? _selectedHistory;
    [ObservableProperty] private bool                 _showHistoryDetail = false;

    [ObservableProperty] private string  _statusMessage = "Connecting...";
    [ObservableProperty] private string  _barcodeInput  = "";
    [ObservableProperty] private bool    _isProcessing  = false;
    [ObservableProperty] private string  _screenMode    = "idle";

    // Loyalty
    [ObservableProperty] private string             _loyaltyInput      = "";
    [ObservableProperty] private string             _loyaltyInputError = "";
    [ObservableProperty] private string             _loyaltyName       = "";
    [ObservableProperty] private bool               _loyaltyApplied    = false;
    [ObservableProperty] private LoyaltyMemberInfo? _currentMember;
    [ObservableProperty] private decimal            _loyaltyDiscount   = 0;
    [ObservableProperty] private bool               _loyaltyManualMode = false;
    private int _loyaltyPointsRedeemed = 0;

    // Hardware status
    [ObservableProperty] private bool _scannerOk    = true;
    [ObservableProperty] private bool _printerOk    = true;
    [ObservableProperty] private bool _paymentOk    = true;
    [ObservableProperty] private bool _cardReaderOk = true;
    [ObservableProperty] private bool _scaleOk      = true;

    // Weight check
    [ObservableProperty] private bool   _showWeightCheckPrompt   = false;
    [ObservableProperty] private string _weightCheckItemName     = "";
    [ObservableProperty] private bool   _showScalePlacementPrompt = false; // immediate popup when scale reads 0
    [ObservableProperty] private bool   _showWeightMismatchAlert  = false;
    [ObservableProperty] private string _weightMismatchInfo      = "";
    private TaskCompletionSource<int>?  _scaleWeightTcs;
    private int?                        _pendingScaleReading; // buffers a reading that arrived before TCS was ready

    // Tracks whether the last cash_insert was accepted by OnCashInsertedAsync.
    // OnCashKeyDepositedAsync (always runs after on the UI thread) checks this before depositing.
    private bool _lastInsertAccepted = false;

    // Quantity debounce — fires a scale check 5 s after the last +/− click
    private CancellationTokenSource? _quantityDebounceCts;
    private CartItem?                _debounceItem;
    [ObservableProperty] private bool _quantityDebouncePending = false;

    // Quantity change guard — only active when scale is Error (no weight verification)
    private bool _quantityChangedWhileScaleError = false;
    [ObservableProperty] private bool _showQuantityChangeAlert = false;

    // Scale went offline while items were in the cart — blocks +/- and payment until staff clears it
    [ObservableProperty] private bool _showScaleOfflineAlert = false;
    private bool _prevScaleOk = true;          // tracks last known scale state to detect transitions
    private bool _scaleOfflineApproved = false; // set when staff approves — suppresses re-trigger for this session


    // Server connection
    [ObservableProperty] private bool   _serverOk             = false;
    [ObservableProperty] private string _serverDownSinceText  = "";
    [ObservableProperty] private bool   _showGoHomeFromOfflineConfirm = false;
    private bool _serverHubConnected = false;

    // Payment state
    [ObservableProperty] private string  _paymentMethod  = "";
    [ObservableProperty] private decimal _cashInserted   = 0;
    [ObservableProperty] private bool    _cashComplete   = false;

    public bool    CanRedeemPoints     => CurrentMember?.CanRedeem == true;
    public int     UpdatedMemberPoints => (CurrentMember?.Points ?? 0) + PointsEarned;
    public bool    IsCardPayment    => PaymentMethod == "card";
    public bool    IsMobilePayment  => PaymentMethod == "mobile";
    public bool    IsCashPayment    => PaymentMethod == "cash";
    public decimal CashChange       => Math.Max(0, CashInserted - Total);
    public bool    CashSufficient   => CashInserted >= Total && Total > 0;

    // Receipt overlay
    [ObservableProperty] private string _receiptText       = "";
    [ObservableProperty] private string _receiptMethod     = "";
    [ObservableProperty] private string _printerStatusNote = "";
    [ObservableProperty] private bool   _receiptPrinted    = false;
    [ObservableProperty] private int    _pointsEarned      = 0;
    private CancellationTokenSource?    _receiptDismissCts;

    // Settings overlay — unlocked via staff card scan or 4-digit PIN
    [ObservableProperty] private bool   _showSettingsOverlay = false;
    [ObservableProperty] private bool   _settingsUnlocked    = false;
    [ObservableProperty] private bool   _isDarkMode          = true;
    [ObservableProperty] private bool   _showSettingsPinInput = false;
    [ObservableProperty] private string _settingsPinInput     = "";
    [ObservableProperty] private string _settingsPinError     = "";
    public string SettingsPinDisplay => _settingsPinInput.Length switch
    {
        0 => "○  ○  ○  ○",
        1 => "●  ○  ○  ○",
        2 => "●  ●  ○  ○",
        3 => "●  ●  ●  ○",
        _ => "●  ●  ●  ●",
    };

    // ── Popup / overlay flags ─────────────────────────────────────────────────
    [ObservableProperty] private bool _showLoyaltyConfirmOverlay   = false;
    [ObservableProperty] private bool   _showLoyaltyNotFoundOverlay  = false;
    [ObservableProperty] private string _loyaltyNotFoundName         = "";
    [ObservableProperty] private bool _showBonusRedeemOverlay      = false;
    [ObservableProperty] private bool _showPointsOnlyAlert         = false;
    [ObservableProperty] private bool _pointsOnlyMode              = false;
    [ObservableProperty] private bool _showPrinterErrorAlert       = false;
    [ObservableProperty] private bool _showLoyaltyErrorOverlay     = false;
    [ObservableProperty] private bool _showEmptyCartPopup          = false;
    [ObservableProperty] private bool _showClearCartConfirm        = false;
    [ObservableProperty] private bool _showCancelSessionConfirm    = false;
    [ObservableProperty] private bool _showRestartConfirm          = false;

    // Price update toast
    [ObservableProperty] private string _priceUpdateNotice     = "";
    [ObservableProperty] private bool   _showPriceUpdateNotice = false;

    // Drawer maintenance overlay
    [ObservableProperty] private bool   _showDrawerMaintenanceOverlay = false;
    [ObservableProperty] private string _drawerMaintenanceSlots       = "";

    // ── 9-dot pattern lock (1–9, 3×3 grid) ──────────────────────────────────
    public ObservableCollection<PatternNode> PatternNodes { get; }
        = new(Enumerable.Range(1, 9).Select(i => new PatternNode { Index = i }));

    private readonly List<int> _patternSequence = new();

    /// <summary>Raised when the pattern is reset so the view can clear drawn lines.</summary>
    public event Action? PatternReset;
    public event Action? CloseRequested;

    [ObservableProperty] private bool   _showPatternLockOverlay = false;
    [ObservableProperty] private string _patternLockError       = "";

    // ── Brute-force lockout — Pattern ─────────────────────────────────────
    private int  _patternFailCount    = 0;
    private bool _patternPostCooldown = false;   // true after first 5-fail cooldown expires
    [ObservableProperty] private bool _showPatternCooldown  = false;
    [ObservableProperty] private int  _patternCooldownSecs  = 30;
    [ObservableProperty] private bool _showPatternCallStaff = false;

    // ── Forgot-pattern / PIN-reset flow ───────────────────────────────────
    // Phase: "verify" | "await_staff" | "new_pattern" | "confirm_pattern"
    [ObservableProperty] private bool _patternPhaseVerify  = true;
    [ObservableProperty] private bool _patternPhaseAwaitStaff = false;
    [ObservableProperty] private bool _patternPhaseNew     = false;
    [ObservableProperty] private bool _patternPhaseConfirm = false;
    private string _patternResetFirst = "";   // stores first new-pattern entry during confirm step

    private void SetPatternPhase(string phase)
    {
        PatternPhaseVerify     = phase == "verify";
        PatternPhaseAwaitStaff = phase == "await_staff";
        PatternPhaseNew        = phase == "new_pattern";
        PatternPhaseConfirm    = phase == "confirm_pattern";
    }

    // ── Brute-force lockout — Staff PIN ───────────────────────────────────
    private int  _pinFailCount    = 0;
    private bool _pinPostCooldown = false;
    [ObservableProperty] private bool _showPinCooldown  = false;
    [ObservableProperty] private int  _pinCooldownSecs  = 30;
    [ObservableProperty] private bool _showPinCallStaff = false;

    // Bonus points (redeemed on payment select screen)
    [ObservableProperty] private bool   _bonusApplied     = false;
    [ObservableProperty] private string _bonusPointsInput = "";

    public int     BonusPointsToUse   => ParseBonusPoints();
    public decimal BonusSavingPreview => BonusPointsToUse / 100m;   // 1 pt = $0.01 — no floor
    public bool    BonusInputValid    => int.TryParse(BonusPointsInput, out _);
    public string  BonusApplyLabel    => BonusPointsToUse > 0
        ? $"Apply  −${BonusSavingPreview:F2}"
        : "Remove Discount";
    // Raw subtotal before any loyalty discount — used as the points cap base
    private decimal CartSubtotal => Cart.Sum(i => i.Subtotal);

    // Max redeemable for this order = lesser of member balance and raw cart subtotal in pts
    public int     MaxRedeemableForOrder => Math.Min(
        CurrentMember?.RedeemablePoints ?? 0,
        (int)(CartSubtotal * 100));

    private int ParseBonusPoints()
    {
        if (!int.TryParse(BonusPointsInput, out var pts)) return 0;
        var memberMax = CurrentMember?.RedeemablePoints ?? 0;
        pts = Math.Clamp(pts, 0, memberMax);
        // Cap so saving never exceeds raw cart subtotal (1 pt = $0.01)
        var maxPtsByTotal = (int)(CartSubtotal * 100);
        pts = Math.Min(pts, maxPtsByTotal);
        return pts;
    }

    partial void OnBonusPointsInputChanged(string _)
    {
        OnPropertyChanged(nameof(BonusPointsToUse));
        OnPropertyChanged(nameof(BonusSavingPreview));
        OnPropertyChanged(nameof(BonusInputValid));
        OnPropertyChanged(nameof(BonusApplyLabel));
        OnPropertyChanged(nameof(MaxRedeemableForOrder));
    }

    // ── Change dispensed breakdown ────────────────────────────────────────────

    // ── Server down banner ────────────────────────────────────────────────────
    [ObservableProperty] private bool _showServerDownBanner = false;

    // Inactivity timeout
    [ObservableProperty] private bool _showTimeoutWarning = false;
    [ObservableProperty] private int  _timeoutCountdown   = 30;
    private CancellationTokenSource? _inactivityCts;
    // Popup appears at exactly 2 minutes; 30s countdown before auto-reset
    private const int TotalTimeoutSecs = 300;
    private const int WarningLeadSecs  = 30;

    public decimal Total     => Math.Max(0, Cart.Sum(i => i.Subtotal) - LoyaltyDiscount);
    public int     ItemCount => Cart.Sum(i => i.Quantity);

    // Screen visibility
    public bool ShowIdleScreen          => ScreenMode == "idle";
    public bool ShowLoyaltyScreen       => ScreenMode == "loyalty";
    public bool ShowPaymentSelectScreen => ScreenMode == "payment_select";
    public bool ShowPaymentOverlay      => ScreenMode == "payment";
    public bool ShowReceiptOverlay      => ScreenMode == "receipt";
    public bool IsCheckoutMode          => ScreenMode == "checkout";

    partial void OnScreenModeChanged(string value)
    {
        OnPropertyChanged(nameof(ShowIdleScreen));
        OnPropertyChanged(nameof(ShowLoyaltyScreen));
        OnPropertyChanged(nameof(ShowPaymentSelectScreen));
        OnPropertyChanged(nameof(ShowPaymentOverlay));
        OnPropertyChanged(nameof(ShowReceiptOverlay));
        OnPropertyChanged(nameof(IsCheckoutMode));
        if (value == "loyalty") LoyaltyManualMode = false;
        if (value == "idle")
        {
            _inactivityCts?.Cancel();
            ShowTimeoutWarning = false;
        }
        else
        {
            ResetInactivityTimer();
        }
    }

    partial void OnCurrentMemberChanged(LoyaltyMemberInfo? _)
    {
        OnPropertyChanged(nameof(CanRedeemPoints));
        OnPropertyChanged(nameof(UpdatedMemberPoints));
    }

    partial void OnPointsEarnedChanged(int _)
        => OnPropertyChanged(nameof(UpdatedMemberPoints));

    partial void OnLoyaltyDiscountChanged(decimal _)
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(CashChange));
        OnPropertyChanged(nameof(CashSufficient));
    }

    partial void OnPaymentMethodChanged(string _)
    {
        OnPropertyChanged(nameof(IsCardPayment));
        OnPropertyChanged(nameof(IsMobilePayment));
        OnPropertyChanged(nameof(IsCashPayment));
    }

    partial void OnCashInsertedChanged(decimal _)
    {
        OnPropertyChanged(nameof(CashChange));
        OnPropertyChanged(nameof(CashSufficient));
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    // ── Inactivity timer ──────────────────────────────────────────────────────

    public void ResetInactivityTimer()
    {
        if (ScreenMode == "idle") return;
        _inactivityCts?.Cancel();
        _inactivityCts = new CancellationTokenSource();
        ShowTimeoutWarning = false;
        _ = RunInactivityAsync(_inactivityCts.Token, skipWait: false);
    }

    private async Task RunInactivityAsync(CancellationToken ct, bool skipWait = false)
    {
        try
        {
            if (!skipWait)
                await Task.Delay(TimeSpan.FromSeconds(TotalTimeoutSecs - WarningLeadSecs), ct);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                TimeoutCountdown   = WarningLeadSecs;
                ShowTimeoutWarning = true;
            });
            for (int remaining = WarningLeadSecs; remaining > 0; remaining--)
            {
                await Task.Delay(1000, ct);
                var snap = remaining - 1;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    TimeoutCountdown = snap);
            }
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                ShowTimeoutWarning = false;
                CancelSession();
            });
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    private void ContinueSession() => ResetInactivityTimer();

    /// <summary>
    /// Emulator "Skip 5 min" — jumps straight to the 30-second warning countdown.
    /// Does nothing on the idle screen.
    /// </summary>
    private void OnTimeSkipRequested()
    {
        if (ScreenMode == "idle") return;
        _inactivityCts?.Cancel();
        _inactivityCts = new CancellationTokenSource();
        _ = RunInactivityAsync(_inactivityCts.Token, skipWait: true);
    }

    private Task OnLockoutForced(string target)
    {
        if (target == "pin")
        {
            // Force the settings PIN directly into the call-staff hard-lock state.
            // Also open the settings overlay so the banner is visible.
            _pinFailCount        = 5;
            _pinPostCooldown     = true;
            ShowPinCooldown      = false;
            ShowPinCallStaff     = true;
            SettingsPinError     = "";
            ShowSettingsOverlay  = true;
            SettingsUnlocked     = false;
            ShowSettingsPinInput = true;
        }
        else if (target == "pattern")
        {
            // Force the pattern lock into the call-staff hard-lock state.
            // The pattern overlay must already be open (user in loyalty flow).
            _patternFailCount    = 5;
            _patternPostCooldown = true;
            ShowPatternCooldown  = false;
            ShowPatternCallStaff = true;
            PatternLockError     = "";
        }
        return Task.CompletedTask;
    }

    public MainViewModel()
    {
        _server   = new StoreServerClient(Config.StoreServerUrl);
        _hardware = new HardwareService();

        _hardware.ScaleWeightChanged += w => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_scaleWeightTcs != null)
                _scaleWeightTcs.TrySetResult(w);
            else
                _pendingScaleReading = w;   // arrived before TCS was ready — buffer it
        });
        _hardware.BarcodeScanned       += b    => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => OnBarcodeScanned(b));
        _hardware.LoyaltyScanned       += c    => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _ = OnLoyaltyScannedAsync(c));
        _hardware.LoyaltyReaderError   += ()   => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(OnLoyaltyReaderError);
        _hardware.HardwareStatusChanged += ()  => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(RefreshHardwareStatus);
        _hardware.PriceChanged         += (b, p) => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _ = OnPriceChangedAsync(b, p));
        _hardware.CatalogUpdated       += ()   => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(OnCatalogUpdateRequested);
        _hardware.NetworkStateChanged  += ()   => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(OnNetworkStateChangedAsync);
        _hardware.CashInserted         += amt  => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _ = OnCashInsertedAsync(amt));
        _hardware.CashKeyDeposited     += key  => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _ = OnCashKeyDepositedAsync(key));
        _hardware.TimeSkipRequested    += ()   => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(OnTimeSkipRequested);
        _hardware.ShutdownRequested    += ()   => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { CloseRequested?.Invoke(); return Task.CompletedTask; });
        _hardware.LockoutForced        += tgt  => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => OnLockoutForced(tgt));

        _server.OnCatalogUpdated          += () => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => StatusMessage = "✓ Catalog refreshed");
        // OnPriceChanged from SignalR is intentionally NOT subscribed here.
        // Price changes arrive via the hardware TCP channel (_hardware.PriceChanged above).
        // The terminal itself pushes the change to the server inside OnPriceChangedAsync,
        // which causes the server to broadcast SignalR to OTHER terminals — not this one.
        _server.OnServerConnectionChanged += connected =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _serverHubConnected = connected;
                ServerOk = connected && !_hardware.NetworkDown;
            });

        _ = InitialConnectAsync();
    }

    // ── Server connection ─────────────────────────────────────────────────────

    private async Task InitialConnectAsync()
    {
        // Keep retrying indefinitely until we connect
        while (!_serverHubConnected)
        {
            await _server.ConnectAsync();
            if (_serverHubConnected) break;
            await Task.Delay(3000);
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            StatusMessage = "Ready");
    }

    private void OnNetworkStateChangedAsync()
    {
        // Network flag changed — reflect in the server dot.
        // Don't call ConnectAsync here: if the hub is already connected, StartAsync throws
        // and the catch incorrectly sets ServerOk = false.
        bool down = _hardware.NetworkDown;
        if (down && !ShowServerDownBanner)
            ServerDownSinceText = DateTime.Now.ToString("HH:mm:ss");
        ServerOk             = _server.IsServerConnected && !down;
        ShowServerDownBanner = down;
    }

    [RelayCommand]
    private void RequestGoHomeFromOffline() => ShowGoHomeFromOfflineConfirm = true;

    [RelayCommand]
    private void ConfirmGoHomeFromOffline()
    {
        ShowGoHomeFromOfflineConfirm = false;
        CancelSession();
    }

    [RelayCommand]
    private void DismissGoHomeFromOffline() => ShowGoHomeFromOfflineConfirm = false;

    // ── Session flow ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void StartSession()
    {
        Cart.Clear();
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(ItemCount));
        LoyaltyInput           = "";
        LoyaltyName            = "";
        LoyaltyApplied         = false;
        LoyaltyDiscount        = 0;
        _loyaltyPointsRedeemed = 0;
        CurrentMember          = null;
        ScreenMode             = "loyalty";
    }

    partial void OnLoyaltyInputChanged(string value) => LoyaltyInputError = "";

    [RelayCommand]
    private void SwitchToManualInput() => LoyaltyManualMode = true;

    [RelayCommand]
    private void SwitchToCardReader() => LoyaltyManualMode = false;

    // Called when user submits phone/card on the loyalty screen
    [RelayCommand]
    private async Task SubmitLoyalty()
    {
        var input = LoyaltyInput.Trim();
        if (string.IsNullOrEmpty(input)) return;
        LoyaltyInputError = "";

        bool isCard  = input.Length == 13 && input.All(char.IsDigit);
        bool isPhone = !isCard && input.All(char.IsDigit)
                       && input.Length is 10 or 11
                       && input.StartsWith('0');

        if (isCard)
        {
            // 13-digit card → direct lookup, no PIN required
            IsProcessing = true;
            var member = await _server.LookupLoyaltyAsync(input);
            IsProcessing = false;
            if (member is null) { LoyaltyNotFoundName = input; ShowLoyaltyNotFoundOverlay = true; }
            else { CurrentMember = member; ShowLoyaltyConfirmOverlay = true; }
            return;
        }

        if (isPhone)
        {
            // Korean phone number → pattern lock
            ResetPattern();
            PatternLockError       = "";
            ShowPatternLockOverlay = true;
            return;
        }

        // Anything else (credit card, random input, etc.) → show inline error
        LoyaltyInputError = "Enter a 13-digit loyalty card number or phone number.";
    }

    // ── 9-dot pattern lock ────────────────────────────────────────────────────

    /// <summary>Called by the view when the pointer enters a dot during drawing (1-based index).</summary>
    public void EnterPatternNode(int index)
    {
        var node = PatternNodes[index - 1];
        if (node.IsSelected) return;
        _patternSequence.Add(index);
        node.Order      = _patternSequence.Count;
        node.IsSelected = true;
        PatternLockError = "";
    }

    [RelayCommand]
    private void ClearPatternInput()
    {
        ResetPattern();
        PatternLockError = "";
    }

    [RelayCommand]
    private void CancelPattern()
    {
        ShowPatternLockOverlay = false;
        ResetPattern();
        PatternLockError    = "";
        _patternResetFirst  = "";
        SetPatternPhase("verify");
    }

    private void ResetPattern()
    {
        _patternSequence.Clear();
        foreach (var n in PatternNodes) { n.IsSelected = false; n.Order = 0; }
        PatternReset?.Invoke();
    }

    [RelayCommand]
    private async Task ConfirmPattern()
    {
        // Hard-locked or cooldown — ignore
        if (ShowPatternCallStaff || ShowPatternCooldown) return;

        // ── Phase: await_staff — nothing to do until staff taps card ────
        if (PatternPhaseAwaitStaff) return;

        if (_patternSequence.Count < 4)
        {
            PatternLockError = "Connect at least 4 dots.";
            await Task.Delay(1000);
            ResetPattern();
            PatternLockError = "";
            return;
        }

        var patternStr = string.Concat(_patternSequence);

        // ── Phase: new_pattern — store first entry, ask to confirm ──────
        if (PatternPhaseNew)
        {
            _patternResetFirst = patternStr;
            ResetPattern();
            PatternLockError = "";
            SetPatternPhase("confirm_pattern");
            return;
        }

        // ── Phase: confirm_pattern — compare and save ────────────────────
        if (PatternPhaseConfirm)
        {
            if (patternStr != _patternResetFirst)
            {
                PatternLockError = "Patterns don't match — draw again.";
                await Task.Delay(1200);
                ResetPattern();
                PatternLockError = "";
                SetPatternPhase("new_pattern");
                _patternResetFirst = "";
                return;
            }

            // Patterns match — persist to DB
            IsProcessing = true;
            bool ok = await _server.ResetLoyaltyPatternAsync(LoyaltyInput.Trim(), patternStr);
            IsProcessing = false;

            ResetPattern();
            PatternLockError = "";
            _patternResetFirst = "";
            SetPatternPhase("verify");
            ResetPatternLockout();

            if (ok)
            {
                // Close overlay and resume normal loyalty flow
                ShowPatternLockOverlay = false;
                PatternLockError = "";
                StatusMessage = "Pattern updated — please log in with your new pattern.";
            }
            else
            {
                PatternLockError = "Could not save — server offline. Try again.";
                await Task.Delay(2000);
                PatternLockError = "";
            }
            return;
        }

        // ── Phase: verify (default) ──────────────────────────────────────
        IsProcessing = true;
        var (member, result) = await _server.LookupLoyaltyWithPinAsync(LoyaltyInput.Trim(), patternStr);
        IsProcessing = false;

        if (result == StoreServerClient.PinResult.WrongPin)
        {
            _patternFailCount++;
            int remaining = 5 - _patternFailCount;

            if (_patternFailCount >= 5)
            {
                ResetPattern();
                PatternLockError = "";
                if (_patternPostCooldown)
                    ShowPatternCallStaff = true;
                else
                {
                    ShowPatternCooldown = true;
                    _ = RunPatternCooldownAsync();
                }
            }
            else
            {
                PatternLockError = remaining == 1
                    ? "Wrong pattern — 1 attempt left!"
                    : $"Wrong pattern — {remaining} attempts left.";
                await Task.Delay(1000);
                ResetPattern();
                PatternLockError = "";
            }
            return;
        }

        // ── Success ──────────────────────────────────────────────────────
        _patternFailCount      = 0;
        _patternPostCooldown   = false;
        ShowPatternLockOverlay = false;
        ResetPattern();
        PatternLockError = "";

        if (member is null) { LoyaltyNotFoundName = LoyaltyInput.Trim(); ShowLoyaltyNotFoundOverlay = true; }
        else { CurrentMember = member; ShowLoyaltyConfirmOverlay = true; }
    }

    [RelayCommand]
    private void ForgotPattern()
    {
        ResetPattern();
        PatternLockError = "";
        SetPatternPhase("await_staff");
    }

    private async Task RunPatternCooldownAsync()
    {
        for (int i = 30; i > 0; i--)
        {
            PatternCooldownSecs = i;
            await Task.Delay(1000);
        }
        ShowPatternCooldown  = false;
        _patternFailCount    = 0;
        _patternPostCooldown = true;   // next 5 failures → call staff
        PatternLockError     = "5 attempts remaining.";
    }

    private void ResetPatternLockout()
    {
        _patternFailCount    = 0;
        _patternPostCooldown = false;
        ShowPatternCooldown  = false;
        ShowPatternCallStaff = false;
        PatternCooldownSecs  = 30;
        PatternLockError     = "";
        _patternResetFirst   = "";
        SetPatternPhase("verify");
    }

    // ── Staff card routing ────────────────────────────────────────────────────

    private async Task OnLoyaltyScannedAsync(string card)
    {
        bool isStaff = card.StartsWith("STAFF-", StringComparison.OrdinalIgnoreCase);

        // Staff card → clear scale-offline alert and mark all current items as approved
        if (isStaff && ShowScaleOfflineAlert)
        {
            ShowScaleOfflineAlert    = false;
            _scaleOfflineApproved    = true;  // suppresses re-trigger for rest of session
            StatusMessage            = "Scale offline — all items approved by staff";
            return;
        }

        // Staff card → approve scale placement (override weight check)
        if (isStaff && ShowScalePlacementPrompt)
        {
            _scaleWeightTcs?.TrySetResult(-2);  // -2 = staff approved
            _scaleWeightTcs = null;
            return;
        }

        // Staff card → reset PIN hard-lock ("call staff" screen)
        if (isStaff && ShowPinCallStaff)
        {
            ResetPinLockout();
            StatusMessage = "PIN lockout cleared by staff";
            return;
        }

        // Staff card → reset pattern hard-lock ("call staff" screen)
        if (isStaff && ShowPatternCallStaff)
        {
            ResetPatternLockout();
            StatusMessage = "Pattern lockout cleared by staff";
            return;
        }

        // Staff card → authorize forgot-pattern reset
        if (isStaff && ShowPatternLockOverlay && PatternPhaseAwaitStaff)
        {
            // Clear any active lockout / cooldown banners
            _patternFailCount    = 0;
            _patternPostCooldown = false;
            ShowPatternCooldown  = false;
            ShowPatternCallStaff = false;
            PatternCooldownSecs  = 30;
            PatternLockError     = "";
            _patternResetFirst   = "";
            ResetPattern();
            SetPatternPhase("new_pattern");
            StatusMessage = "Staff authorized — draw a new pattern";
            return;
        }

        // Staff card → unlock settings
        if (isStaff && ShowSettingsOverlay && !SettingsUnlocked)
        {
            SettingsUnlocked = true;
            StatusMessage    = "Settings unlocked by staff";
            return;
        }

        // Card/phone inserted on the idle screen → jump straight into the loyalty flow
        if (ScreenMode == "idle" && !isStaff)
            StartSession();

        if (ScreenMode != "loyalty") return;
        if (isStaff) return; // staff cards don't act as loyalty cards

        bool isPhysicalCard = card.Length == 13 && card.All(char.IsDigit);
        bool isPhone        = !isPhysicalCard && card.All(char.IsDigit)
                              && card.Length is 10 or 11 && card.StartsWith('0');

        if (isPhone)
        {
            // Phone number sent from hardware (e.g. emulator phone preset) → pattern lock
            LoyaltyInput           = card;
            LoyaltyManualMode      = true;
            ResetPattern();
            PatternLockError       = "";
            ShowPatternLockOverlay = true;
            return;
        }

        if (!isPhysicalCard)
        {
            // Unrecognized format (credit card, random scan, etc.) → not found
            LoyaltyNotFoundName = card;
            ShowLoyaltyNotFoundOverlay = true;
            return;
        }

        // 13-digit loyalty card → direct lookup, no PIN required
        LoyaltyInput  = card;
        IsProcessing  = true;
        var member    = await _server.LookupLoyaltyAsync(card);
        IsProcessing  = false;

        if (member is null) { LoyaltyNotFoundName = card; ShowLoyaltyNotFoundOverlay = true; }
        else { CurrentMember = member; ShowLoyaltyConfirmOverlay = true; }
    }

    private void OnLoyaltyReaderError()
    {
        if (ScreenMode == "loyalty")
            ShowLoyaltyErrorOverlay = true;
    }

    // ── Loyalty confirm / deny ────────────────────────────────────────────────

    [RelayCommand]
    private void ConfirmLoyaltyMember()
    {
        ShowLoyaltyConfirmOverlay = false;
        EnterCheckout();
    }

    [RelayCommand]
    private void DenyLoyaltyMember()
    {
        ShowLoyaltyConfirmOverlay = false;
        CurrentMember = null;
        LoyaltyInput  = "";
    }

    [RelayCommand]
    private void OpenBonusOverlay()
    {
        // If already applied, re-open with the current redeemed amount; otherwise start at 0
        BonusPointsInput = BonusApplied ? _loyaltyPointsRedeemed.ToString() : "0";
        ShowBonusRedeemOverlay = true;
    }

    [RelayCommand]
    private void UseBonus()
    {
        if (CurrentMember is not null && CurrentMember.CanRedeem)
        {
            if (BonusPointsToUse > 0)
            {
                _loyaltyPointsRedeemed = BonusPointsToUse;
                LoyaltyDiscount        = BonusSavingPreview;
                BonusApplied           = true;
            }
            else
            {
                // 0 pts entered — remove any previously applied discount
                _loyaltyPointsRedeemed = 0;
                LoyaltyDiscount        = 0;
                BonusApplied           = false;
            }
        }
        ShowBonusRedeemOverlay = false;

        // If bonus now covers the entire bill, ask if they want to pay by points only
        if (BonusApplied && Total <= 0)
            ShowPointsOnlyAlert = true;
        else
            PointsOnlyMode = false; // partial points — re-enable all payment methods
    }

    [RelayCommand]
    private async Task ConfirmPointsOnly()
    {
        ShowPointsOnlyAlert = false;
        await FinalizeTransactionAsync("loyalty");
    }

    [RelayCommand]
    private void DismissPointsOnly()
    {
        ShowPointsOnlyAlert = false;
        PointsOnlyMode = true; // block card/cash/mobile until user adjusts points
    }

    [RelayCommand]
    private void SetBonusPoints(string value)
    {
        var max = CurrentMember?.RedeemablePoints ?? 0;
        if (value == "all")
            BonusPointsInput = max.ToString();
        else if (int.TryParse(value, out var pts))
            BonusPointsInput = Math.Min(pts, max).ToString();
    }

    [RelayCommand]
    private void SkipBonus() => ShowBonusRedeemOverlay = false;

    [RelayCommand]
    private void CloseLoyaltyNotFound() => ShowLoyaltyNotFoundOverlay = false;

    [RelayCommand]
    private void CloseLoyaltyError() => ShowLoyaltyErrorOverlay = false;

    private void EnterCheckout()
    {
        // Always reset bonus state when entering checkout — prevents stale discount from previous session
        BonusApplied           = false;
        LoyaltyDiscount        = 0;
        _loyaltyPointsRedeemed = 0;
        if (CurrentMember is not null)
        {
            LoyaltyName    = CurrentMember.Name;
            LoyaltyApplied = true;
        }
        ScreenMode    = "checkout";
        StatusMessage = LoyaltyApplied
            ? $"Welcome back, {LoyaltyName}! Ready to scan."
            : "Ready — scan a barcode";
    }

    [RelayCommand]
    private void SkipLoyalty()
    {
        ShowLoyaltyNotFoundOverlay = false;
        LoyaltyApplied    = false;
        LoyaltyName       = "";
        LoyaltyInput      = "";
        LoyaltyManualMode = false;
        CurrentMember     = null;
        ScreenMode        = "checkout";
        StatusMessage     = "Ready — scan a barcode";
    }

    [RelayCommand]
    private async Task ProceedToPayment()
    {
        if (ShowScaleOfflineAlert)    return;
        if (ShowScalePlacementPrompt) return;
        if (QuantityDebouncePending)  { StatusMessage = "⚠ Place items on scale before proceeding"; return; }
        if (Cart.Count == 0) { ShowEmptyCartPopup = true; return; }
        ShowEmptyCartPopup = false;
        ScreenMode         = "payment_select";
    }

    [RelayCommand]
    private void CloseEmptyCartPopup() => ShowEmptyCartPopup = false;

    [RelayCommand]
    private void CancelPaymentSelect()
    {
        PointsOnlyMode = false;
        ScreenMode     = "checkout";
    }

    [RelayCommand]
    private void RequestCancelSession() => ShowCancelSessionConfirm = true;

    [RelayCommand]
    private void DismissCancelSessionConfirm() => ShowCancelSessionConfirm = false;

    [RelayCommand]
    private void CancelSession()
    {
        ShowCancelSessionConfirm      = false;
        ShowGoHomeFromOfflineConfirm  = false;
        ShowScalePlacementPrompt        = false;
        ShowWeightCheckPrompt           = false;
        ShowQuantityChangeAlert         = false;
        _quantityChangedWhileScaleError = false;
        ShowScaleOfflineAlert           = false;
        _scaleOfflineApproved           = false;
        _scaleWeightTcs?.TrySetResult(-1);
        _scaleWeightTcs               = null;
        _pendingScaleReading          = null;
        ShowPatternLockOverlay    = false;
        ShowPointsOnlyAlert       = false;
        PointsOnlyMode            = false;
        ShowPrinterErrorAlert     = false;
        ResetPattern();
        _hardware.CancelPaymentWait();
        Cart.Clear();
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(ItemCount));
        ShowLoyaltyConfirmOverlay  = false;
        ShowLoyaltyNotFoundOverlay = false;
        LoyaltyApplied             = false;
        LoyaltyName                = "";
        LoyaltyInput               = "";
        LoyaltyManualMode          = false;
        LoyaltyDiscount            = 0;
        _loyaltyPointsRedeemed     = 0;
        BonusApplied               = false;
        CurrentMember              = null;
        PaymentMethod          = "";
        CashInserted           = 0;
        CashComplete           = false;
        _lastInsertAccepted    = false;
        CancelQuantityDebounce();
        ShowEmptyCartPopup     = false;
        ScreenMode             = "idle";
        IsProcessing           = false;
    }

    [RelayCommand]
    private void RequestClearCart()
    {
        if (Cart.Count == 0) { ShowEmptyCartPopup = true; return; }
        ShowClearCartConfirm = true;
    }

    [RelayCommand]
    private void DismissClearCartConfirm() => ShowClearCartConfirm = false;

    [RelayCommand]
    private void ClearCart()
    {
        ShowClearCartConfirm   = false;
        LoyaltyDiscount        = 0;
        _loyaltyPointsRedeemed = 0;
        Cart.Clear();
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(ItemCount));
        StatusMessage = "Cart cleared";
    }

    // ── Catalog / price events ────────────────────────────────────────────────

    private async void OnCatalogUpdateRequested()
    {
        StatusMessage = "⟳ Refreshing catalog from cloud...";
        await _server.TriggerCatalogRefreshAsync();
    }

    private async Task OnPriceChangedAsync(string barcode, decimal newPrice)
    {
        // Update the server DB first
        await _server.UpdatePriceAsync(barcode, newPrice);

        // Apply to cart items — but only if the new price is LOWER (price integrity policy).
        // If the price went up mid-transaction, the customer keeps the price they scanned at.
        var affected = Cart.Where(i => i.Barcode == barcode && newPrice < i.UnitPrice).ToList();
        if (affected.Count > 0)
        {
            foreach (var item in affected)
                item.UnitPrice = newPrice;

            OnPropertyChanged(nameof(Total));
        }

        // Silent — no customer-facing notification
    }

    private void RefreshHardwareStatus()
    {
        ScannerOk    = _hardware.ScannerStatus    == HardwareStatus.Connected;
        PrinterOk    = _hardware.PrinterStatus    == HardwareStatus.Connected;
        PaymentOk    = _hardware.PaymentStatus    == HardwareStatus.Connected;
        CardReaderOk = _hardware.CardReaderStatus == HardwareStatus.Connected;
        bool scaleNowOk = _hardware.ScaleStatus == HardwareStatus.Connected;
        ScaleOk = scaleNowOk;

        // Scale just went offline while items are in the bagging area → block and wait for staff
        // Suppressed if staff already approved this session
        if (_prevScaleOk && !scaleNowOk && Cart.Count > 0 && ScreenMode == "checkout" && !_scaleOfflineApproved)
            ShowScaleOfflineAlert = true;

        // Scale came back online → clear the alert automatically
        if (!_prevScaleOk && scaleNowOk)
            ShowScaleOfflineAlert = false;

        _prevScaleOk = scaleNowOk;
    }

    // ── Scanning ──────────────────────────────────────────────────────────────

    private void OnBarcodeScanned(string barcode)
    {
        if (ScreenMode == "loyalty")
        {
            LoyaltyInput = barcode;
            _ = SubmitLoyalty();
            return;
        }
        if (ScreenMode != "checkout") return;
        if (_hardware.ScannerStatus != HardwareStatus.Connected)
        {
            StatusMessage = "⚠ Scanner disconnected";
            return;
        }
        _ = AddItemByBarcodeAsync(barcode);
    }

    [RelayCommand]
    private async Task ScanBarcode()
    {
        if (string.IsNullOrWhiteSpace(BarcodeInput)) return;
        await AddItemByBarcodeAsync(BarcodeInput.Trim());
        BarcodeInput = "";
    }

    private async Task AddItemByBarcodeAsync(string barcode)
    {
        if (ShowScalePlacementPrompt || ShowWeightCheckPrompt)
        {
            StatusMessage = "⚠ Place the current item on the scale before scanning another";
            return;
        }
        if (QuantityDebouncePending)
        {
            StatusMessage = "⚠ Place items on scale before scanning a new item";
            return;
        }

        // Scale is Error and customer bumped quantity without any weight check — alert them
        if (_quantityChangedWhileScaleError && _hardware.ScaleStatus == HardwareStatus.Error)
        {
            ShowQuantityChangeAlert = true;
            return;
        }

        IsProcessing         = true;
        _pendingScaleReading = null;  // discard any stale reading from a previous scan
        if (_hardware.NetworkDown)
        {
            StatusMessage = "⚠ Server unreachable (network down)";
            IsProcessing  = false;
            return;
        }
        if (_hardware.NetworkLatencyMs > 0)
            await Task.Delay(_hardware.NetworkLatencyMs);

        StatusMessage = $"Looking up {barcode}...";
        var product = await _server.LookupBarcodeAsync(barcode);
        if (product is null)
        {
            StatusMessage = $"⚠ Product not found: {barcode}";
            IsProcessing  = false;
            return;
        }

        // ── Weight check ──────────────────────────────────────────────────────
        // Connected/Error → run check (Error sends 0 until customer places item)
        // Disconnected    → no scale present, skip
        // < 150g          → too small/light to reliably detect; skip for convenience
        const int ScaleCheckMinGrams = 150;
        if (product.WeightGrams >= ScaleCheckMinGrams && _hardware.ScaleStatus != HardwareStatus.Disconnected)
        {
            WeightCheckItemName   = product.Name;
            ShowWeightCheckPrompt = true;
            StatusMessage         = $"Place \"{product.Name}\" in the bagging area…";

            int actual = 0;

            // Wait indefinitely — no timeout.
            // Only exits when: item placed (>0), session cancelled (-1), or staff override (-2).
            while (true)
            {
                var tcs         = new TaskCompletionSource<int>();
                _scaleWeightTcs = tcs;
                // Flush any reading that arrived before TCS was ready
                if (_pendingScaleReading.HasValue)
                {
                    tcs.TrySetResult(_pendingScaleReading.Value);
                    _pendingScaleReading = null;
                }
                actual = await tcs.Task;
                _scaleWeightTcs = null;

                if (actual == -1) { actual = 0; break; }  // session cancelled
                if (actual == -2) break;                   // staff approved
                if (actual > 0)   break;                   // valid weight reading

                // Got 0 — customer hasn't placed item, keep waiting
                ShowWeightCheckPrompt    = false;
                ShowScalePlacementPrompt = true;
            }

            ShowWeightCheckPrompt    = false;
            ShowScalePlacementPrompt = false;
            _scaleWeightTcs          = null;

            if (actual == -2)
            {
                // Staff approved — log override, continue to add item
                _ = _server.SubmitWeightCheckAsync(Config.TerminalId, product.Barcode, product.Name, product.WeightGrams, 0, "staff_override", product.Price);
            }
            else if (actual == 0)
            {
                // Session was cancelled mid-wait — don't add item, return silently
                IsProcessing = false;
                return;
            }
            else
            {
                double ratio  = (double)actual / product.WeightGrams;
                bool   passed = ratio >= 0.75 && ratio <= 1.25;
                _ = _server.SubmitWeightCheckAsync(Config.TerminalId, product.Barcode, product.Name, product.WeightGrams, actual, passed ? "pass" : "fail", product.Price);
            }
        }

        var existing = Cart.FirstOrDefault(i => i.Barcode == barcode);
        if (existing is not null)
            existing.Quantity++;
        else
            Cart.Add(new CartItem
            {
                ProductId   = product.Id,
                Barcode     = product.Barcode,
                Name        = product.Name,
                UnitPrice   = product.Price,
                WeightGrams = product.WeightGrams,
            });

        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(ItemCount));
        StatusMessage                   = $"Added: {product.Name}";
        _quantityChangedWhileScaleError = false;
        IsProcessing                    = false;
    }

    [RelayCommand]
    private void DismissWeightMismatch()
    {
        ShowWeightMismatchAlert = false;
        StatusMessage           = "⚠ Item not added — please rescan or call staff.";
    }

    /// <summary>
    /// Customer or staff cancels the scale placement wait.
    /// Sends -1 so the weight loop exits cleanly without adding the item.
    /// </summary>
    [RelayCommand]
    private void CancelScalePlacement()
    {
        _scaleWeightTcs?.TrySetResult(-1);
        _scaleWeightTcs = null;
    }

    // ── Payment ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Pay(string method)
    {
        if (Cart.Count == 0) return;
        if (_hardware.PaymentStatus != HardwareStatus.Connected)
        {
            StatusMessage = "⚠ Payment terminal disconnected";
            ScreenMode    = "payment_select";
            return;
        }

        PaymentMethod = method;
        CashInserted  = 0;
        CashComplete  = false;
        ScreenMode    = "payment";

        if (method == "cash")
        {
            StatusMessage = "Waiting for cash...";
            return;
        }

        IsProcessing  = true;
        StatusMessage = method == "card" ? "Awaiting card..." : "Awaiting mobile payment...";
        var success   = await _hardware.ProcessPaymentAsync(Total, method);
        IsProcessing  = false;

        if (!success)
        {
            StatusMessage = "✗ Payment declined — try again";
            ScreenMode    = "payment_select";
            return;
        }

        await FinalizeTransactionAsync(method);
    }

    private async Task OnCashInsertedAsync(decimal amount)
    {
        if (ScreenMode != "payment" || PaymentMethod != "cash") { _lastInsertAccepted = false; return; }
        if (CashInserted >= Total)                              { _lastInsertAccepted = false; return; }
        CashInserted += amount;
        _lastInsertAccepted = true;
        if (CashInserted >= Total && !CashComplete)
            CashComplete = true;
    }

    private async Task OnCashKeyDepositedAsync(string key)
    {
        // Only deposit when the paired OnCashInsertedAsync actually accepted the amount.
        // Both handlers are queued in order on the UI thread so this flag is always set first.
        if (!_lastInsertAccepted) return;
        _lastInsertAccepted = false;  // consume immediately — prevents double-deposit on repeat events
        var fullLabel = await _server.DepositCashAsync(key);
        if (fullLabel is not null)
            await TriggerDrawerMaintenanceAsync();
    }

    private async Task TriggerDrawerMaintenanceAsync()
    {
        var slots = await _server.CheckDrawerFullSlotsAsync();
        if (slots.Count == 0) return;
        DrawerMaintenanceSlots       = string.Join(", ", slots);
        ShowDrawerMaintenanceOverlay = true;
        _ = PollDrawerClearAsync();
    }

    private async Task PollDrawerClearAsync()
    {
        while (ShowDrawerMaintenanceOverlay)
        {
            await Task.Delay(5000);
            var slots = await _server.CheckDrawerFullSlotsAsync();
            if (slots.Count == 0)
                ShowDrawerMaintenanceOverlay = false;
            else
                DrawerMaintenanceSlots = string.Join(", ", slots);
        }
    }

    [RelayCommand]
    private async Task CompleteCash()
    {
        if (PaymentMethod != "cash" || !CashSufficient) return;
        // Subtract change from the drawer (fire-and-forget — UI doesn't need to wait)
        if (CashChange > 0)
            _ = _server.DispenseCashAsync(CashChange);
        await FinalizeTransactionAsync("cash");
    }

    [RelayCommand]
    private void CancelPayment()
    {
        _hardware.CancelPaymentWait();
        IsProcessing  = false;
        ScreenMode    = "payment_select";
        StatusMessage = "Payment cancelled";
    }

    private async Task FinalizeTransactionAsync(string method)
    {
        IsProcessing = true;
        await _server.SubmitTransactionAsync(Config.TerminalId, method, Cart);

        if (CurrentMember is not null)
        {
            int earned   = (int)Math.Floor(Total);
            int netDelta = earned - _loyaltyPointsRedeemed;
            PointsEarned = Math.Max(0, netDelta);
            await _server.AddLoyaltyPointsAsync(CurrentMember.Id, netDelta);
        }
        else
        {
            PointsEarned = 0;
        }

        ReceiptText       = BuildReceipt(method);
        ReceiptMethod     = method;
        ReceiptPrinted    = false;
        PrinterStatusNote = "";

        // Save to in-session history (most recent first)
        ReceiptHistory.Insert(0, new ReceiptHistoryEntry
        {
            Timestamp   = DateTime.Now,
            ReceiptText = ReceiptText,
            Total       = Total,
            Method      = method,
            Member      = CurrentMember?.Name ?? "",
        });

        // Let the customer choose — receipt screen shows the choice
        ScreenMode    = "receipt";
        StatusMessage = "✓ Payment successful";
        IsProcessing  = false;
        StartReceiptAutoDismiss();
    }

    [RelayCommand]
    private async Task PrintReceipt()
    {
        if (_hardware.PrinterStatus != HardwareStatus.Connected)
        {
            ShowPrinterErrorAlert = true;
            return;
        }
        try
        {
            _hardware.PrintReceipt(ReceiptText);
            ReceiptPrinted    = true;
            PrinterStatusNote = "✓ Receipt printed.";
            await Task.Delay(1500);
            await DismissReceiptCommand.ExecuteAsync(null);
        }
        catch
        {
            ShowPrinterErrorAlert = true;
        }
    }

    [RelayCommand]
    private async Task DismissPrinterError()
    {
        ShowPrinterErrorAlert = false;
        await DismissReceiptCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task DismissReceipt()
    {
        _receiptDismissCts?.Cancel();
        _receiptDismissCts = null;
        ScreenMode = "idle";
        await Task.Delay(100);
        Cart.Clear();
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(ItemCount));
        LoyaltyApplied         = false;
        LoyaltyName            = "";
        LoyaltyInput           = "";
        LoyaltyDiscount        = 0;
        _loyaltyPointsRedeemed = 0;
        CurrentMember          = null;
        IsProcessing           = false;
        ReceiptPrinted         = false;
        PointsEarned           = 0;
        PointsOnlyMode         = false;
    }

    // ── Receipt History ───────────────────────────────────────────────────────

    [RelayCommand]
    private void ViewHistoryEntry(ReceiptHistoryEntry entry)
    {
        SelectedHistory   = entry;
        ShowHistoryDetail = true;
    }

    [RelayCommand]
    private void CloseHistoryDetail() => ShowHistoryDetail = false;

    [RelayCommand]
    private async Task ReprintReceipt(ReceiptHistoryEntry entry)
    {
        if (_hardware.PrinterStatus != HardwareStatus.Connected)
        {
            ShowHistoryDetail = true;
            SelectedHistory   = entry;
            return;
        }
        try
        {
            _hardware.PrintReceipt(entry.ReceiptText);
            // Brief pause so staff can see the print started, then close everything and go home
            await Task.Delay(1000);
        }
        catch { /* silent — staff can see printer LED */ }

        ShowHistoryDetail   = false;
        ShowSettingsOverlay = false;
        SettingsUnlocked    = false;
        CancelSession();
    }

    private void StartReceiptAutoDismiss()
    {
        _receiptDismissCts?.Cancel();
        _receiptDismissCts = new CancellationTokenSource();
        var token = _receiptDismissCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ScreenMode == "receipt")
                        _ = DismissReceiptCommand.ExecuteAsync(null);
                });
            }
            catch (TaskCanceledException) { }
        });
    }

    private string BuildReceipt(string method)
    {
        var methodLabel = method switch
        {
            "card"    => "CARD",
            "cash"    => "CASH",
            "mobile"  => "MOBILE PAY",
            "loyalty" => "LOYALTY POINTS",
            _         => method.ToUpper()
        };
        var lines = new List<string> { "=== J MART RECEIPT ===", "" };
        if (LoyaltyApplied && CurrentMember is not null)
        {
            lines.Add($"Member: {CurrentMember.Name}  [{CurrentMember.Tier}]");
            lines.Add($"Points: {CurrentMember.Points:N0} pts");
            if (_loyaltyPointsRedeemed > 0)
                lines.Add($"Redeemed: {_loyaltyPointsRedeemed:N0} pts  (-${LoyaltyDiscount:F2})");
        }
        lines.Add($"Date: {DateTime.Now:yyyy-MM-dd HH:mm}");
        lines.Add("");
        foreach (var item in Cart)
            lines.Add($"{item.Name,-20} x{item.Quantity}  ${item.Subtotal:F2}");
        if (LoyaltyDiscount > 0)
            lines.Add($"{"Points Redeemed",-20}       -${LoyaltyDiscount:F2}");
        lines.Add(new string('-', 36));
        lines.Add($"TOTAL: ${Total:F2}   [{methodLabel}]");
        if (method == "cash" && CashInserted > 0)
        {
            lines.Add($"Cash Tendered:  ${CashInserted:F2}");
            lines.Add($"Change Due:     ${CashChange:F2}");
        }
        lines.Add("");
        lines.Add("Thank you for shopping at J Mart!");
        return string.Join("\n", lines);
    }

    // ── Cart item quantity ────────────────────────────────────────────────────

    [RelayCommand]
    private void IncreaseQuantity(CartItem item)
    {
        if (ShowScaleOfflineAlert || ShowScalePlacementPrompt || ShowWeightCheckPrompt) return;

        item.Quantity++;
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(ItemCount));

        if (_hardware.ScaleStatus == HardwareStatus.Error)
            _quantityChangedWhileScaleError = true;

        ScheduleQuantityDebounce(item);
    }

    [RelayCommand]
    private void DecreaseQuantity(CartItem item)
    {
        if (ShowScaleOfflineAlert || ShowScalePlacementPrompt || ShowWeightCheckPrompt) return;

        if (item.Quantity > 1)
            item.Quantity--;
        else
            Cart.Remove(item);

        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(ItemCount));

        if (_hardware.ScaleStatus == HardwareStatus.Error)
            _quantityChangedWhileScaleError = true;

        // Decrease: cancel any pending check — item was removed or reduced
        CancelQuantityDebounce();
    }

    private void ScheduleQuantityDebounce(CartItem item)
    {
        const int ScaleCheckMinGrams = 150;
        if (item.WeightGrams < ScaleCheckMinGrams || _hardware.ScaleStatus == HardwareStatus.Disconnected)
            return; // item doesn't need scale check

        _quantityDebounceCts?.Cancel();
        _quantityDebounceCts  = new CancellationTokenSource();
        _debounceItem         = item;
        QuantityDebouncePending = true;

        var cts = _quantityDebounceCts;
        _ = Task.Delay(5000, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _ = RunQuantityScaleCheckAsync(item));
        }, TaskScheduler.Default);
    }

    private void CancelQuantityDebounce()
    {
        _quantityDebounceCts?.Cancel();
        _quantityDebounceCts    = null;
        _debounceItem           = null;
        QuantityDebouncePending = false;
    }

    private async Task RunQuantityScaleCheckAsync(CartItem item)
    {
        QuantityDebouncePending = false;
        _quantityDebounceCts    = null;
        _debounceItem           = null;

        if (!Cart.Contains(item)) return;
        if (_hardware.ScaleStatus == HardwareStatus.Disconnected) return;

        int expectedGrams = item.WeightGrams * item.Quantity;

        // Give the scale ~500 ms to deliver any buffered reading — no prompt shown,
        // no blocking beyond what the 5-second debounce already covered.
        if (!_pendingScaleReading.HasValue)
            await Task.Delay(500);

        int actual = _pendingScaleReading ?? 0;
        _pendingScaleReading = null;

        if (actual > 0)
        {
            double ratio  = (double)actual / expectedGrams;
            bool   passed = ratio >= 0.75 && ratio <= 1.25;
            _ = _server.SubmitWeightCheckAsync(Config.TerminalId, item.Barcode, item.Name,
                    expectedGrams, actual, passed ? "pass" : "fail", item.UnitPrice);
        }
        // No reading arrived → silently skip (scale check is best-effort for qty changes)
    }

    [RelayCommand]
    private void DismissQuantityChangeAlert()
    {
        ShowQuantityChangeAlert         = false;
        _quantityChangedWhileScaleError = false;
    }

    [RelayCommand]
    private void RemoveItem(CartItem item)
    {
        Cart.Remove(item);
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(ItemCount));
    }

    // ── Settings (staff card unlock) ──────────────────────────────────────────

    [RelayCommand]
    private void OpenSettings()
    {
        SettingsUnlocked     = false;
        ShowSettingsPinInput = false;
        SettingsPinInput     = "";
        SettingsPinError     = "";
        ShowSettingsOverlay  = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        ShowSettingsOverlay  = false;
        SettingsUnlocked     = false;
        ShowSettingsPinInput = false;
        SettingsPinInput     = "";
        SettingsPinError     = "";
    }

    [RelayCommand]
    private void ShowPinInput()
    {
        ShowSettingsPinInput = true;
        SettingsPinInput     = "";
        SettingsPinError     = "";
    }

    [RelayCommand]
    private async Task AppendSettingsPin(string digit)
    {
        if (SettingsPinInput.Length >= 4) return;
        SettingsPinInput += digit;
        SettingsPinError  = "";
        OnPropertyChanged(nameof(SettingsPinDisplay));
        if (SettingsPinInput.Length == 4) await SubmitSettingsPin();
    }

    [RelayCommand]
    private void ClearSettingsPin()
    {
        if (SettingsPinInput.Length > 0)
            SettingsPinInput = SettingsPinInput[..^1];
        SettingsPinError = "";
        OnPropertyChanged(nameof(SettingsPinDisplay));
    }

    [RelayCommand]
    private async Task SubmitSettingsPin()
    {
        if (SettingsPinInput.Length < 4) return;
        // Hard-locked or in cooldown — ignore input
        if (ShowPinCallStaff || ShowPinCooldown) return;

        // Fetch from server; fall back to local hardcoded default if offline
        var correctPin = await _server.GetStaffPinAsync() ?? Config.StaffSettingsPin;
        if (SettingsPinInput == correctPin)
        {
            ResetPinLockout();
            SettingsUnlocked     = true;
            ShowSettingsPinInput = false;
            SettingsPinInput     = "";
            StatusMessage        = "Settings unlocked by PIN";
        }
        else
        {
            _pinFailCount++;
            int remaining = 5 - _pinFailCount;
            SettingsPinInput = "";
            OnPropertyChanged(nameof(SettingsPinDisplay));

            if (_pinFailCount >= 5)
            {
                if (_pinPostCooldown)
                {
                    // Second round exhausted → hard lock
                    ShowPinCallStaff  = true;
                    ShowSettingsPinInput = false;
                    SettingsPinError  = "";
                }
                else
                {
                    // First round → 30 s cooldown
                    ShowPinCooldown = true;
                    SettingsPinError = "";
                    _ = RunPinCooldownAsync();
                }
            }
            else
            {
                SettingsPinError = remaining == 1
                    ? "Wrong PIN — 1 attempt left!"
                    : $"Wrong PIN — {remaining} attempts left";
            }
        }
    }

    private async Task RunPinCooldownAsync()
    {
        for (int i = 30; i > 0; i--)
        {
            PinCooldownSecs = i;
            await Task.Delay(1000);
        }
        ShowPinCooldown  = false;
        _pinFailCount    = 0;
        _pinPostCooldown = true;
        SettingsPinError = "5 attempts remaining.";
    }

    private void ResetPinLockout()
    {
        _pinFailCount    = 0;
        _pinPostCooldown = false;
        ShowPinCooldown  = false;
        ShowPinCallStaff = false;
        PinCooldownSecs  = 30;
        SettingsPinError = "";
    }

    [RelayCommand]
    private void RequestRestartTerminal()
    {
        ShowSettingsOverlay = false;
        ShowRestartConfirm  = true;
    }

    [RelayCommand]
    private void DismissRestartConfirm()
    {
        ShowRestartConfirm  = false;
        ShowSettingsOverlay = true;
    }

    [RelayCommand]
    private void RestartTerminal()
    {
        var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (exe is null) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Arguments       = $"--id {Config.TerminalId}"
        });
        Environment.Exit(0);
    }

    [ObservableProperty] private bool _showCloseTerminalConfirm = false;

    [RelayCommand]
    private void RequestCloseTerminal()
    {
        ShowSettingsOverlay        = false;
        ShowCloseTerminalConfirm   = true;
    }

    [RelayCommand]
    private void DismissCloseTerminalConfirm()
    {
        ShowCloseTerminalConfirm = false;
        ShowSettingsOverlay      = true;
    }

    [RelayCommand]
    private void CloseTerminal()
    {
        Environment.Exit(0);
    }
}

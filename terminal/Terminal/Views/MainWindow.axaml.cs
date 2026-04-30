using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Terminal.Services;
using Terminal.ViewModels;

namespace Terminal.Views;

public partial class MainWindow : Window
{
    private MainViewModel _vm = null!;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsDarkMode))
                ApplyTheme(_vm.IsDarkMode);
        };

        _vm.PatternReset    += ClearPatternLines;
        _vm.CloseRequested  += () => Close();

        ApplyTheme(_vm.IsDarkMode);
    }

    // ── Window positioning ────────────────────────────────────────────────────

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Terminal 1 → top-left (0, 0) — already set in XAML Position="0,0"
        // Terminal 2 → bottom-right of primary screen
        if (Config.TerminalId == 2)
        {
            var screen = Screens.Primary;
            if (screen is not null)
            {
                var wa      = screen.WorkingArea;
                var scale   = screen.Scaling;
                var winW    = (int)(Width  * scale);
                var winH    = (int)(Height * scale);
                Position = new PixelPoint(
                    wa.X + wa.Width  - winW,
                    wa.Y + wa.Height - winH
                );
            }
        }
    }

    // ── Drag to move ─────────────────────────────────────────────────────────

    private void OnTopBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    // ── Inactivity reset ──────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _vm.ResetInactivityTimer();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _vm.ResetInactivityTimer();
    }

    private static void ApplyTheme(bool dark)
    {
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant =
                dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    // ── Idle screen — tap anywhere to start ──────────────────────────────────

    private void OnIdleScreenTapped(object? sender, TappedEventArgs e)
    {
        // Walk up from the tapped source — if it's inside the settings button, ignore
        var src = e.Source as Visual;
        while (src is not null)
        {
            if (src is Button btn && btn.Name == "IdleSettingsButton") return;
            src = src.GetVisualParent();
        }
        _vm.StartSessionCommand.Execute(null);
    }

    // ── Pattern lock — drag-to-draw ───────────────────────────────────────────

    // Dot center coordinates inside PatternLineCanvas (matches the 3×3 UniformGrid cells of 96×96).
    // Layout:  1(48,48)  2(144,48)  3(240,48)
    //          4(48,144) 5(144,144) 6(240,144)
    //          7(48,240) 8(144,240) 9(240,240)
    private static readonly Point[] DotCenters =
    [
        new(48,  48), new(144,  48), new(240,  48),   // 1 2 3
        new(48, 144), new(144, 144), new(240, 144),   // 4 5 6
        new(48, 240), new(144, 240), new(240, 240),   // 7 8 9
    ];
    private const double HitRadius = 34;

    private bool  _drawing    = false;
    private int   _lastDot    = -1;
    private Line? _tailLine;
    private readonly List<Line> _permLines = [];

    private static readonly IBrush LineBrush =
        new SolidColorBrush(Color.FromArgb(200, 59, 130, 246));
    private static readonly IBrush TailBrush =
        new SolidColorBrush(Color.FromArgb(100, 59, 130, 246));

    private void PatternCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_vm.ShowPatternLockOverlay) return;
        var pos = e.GetPosition(PatternLineCanvas);
        _drawing = true;
        _lastDot = -1;

        var dot = HitTest(pos);
        if (dot >= 0)
        {
            _vm.EnterPatternNode(dot + 1);
            _lastDot = dot;
        }
        UpdateTailLine(pos);
        e.Handled = true;
    }

    private void PatternCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_drawing) return;
        var pos = e.GetPosition(PatternLineCanvas);
        UpdateTailLine(pos);

        var dot = HitTest(pos);
        if (dot >= 0 && !_vm.PatternNodes[dot].IsSelected)
        {
            // Commit a permanent line from the previous dot
            if (_lastDot >= 0)
            {
                RemoveTailLine();
                AddPermLine(DotCenters[_lastDot], DotCenters[dot]);
            }
            _vm.EnterPatternNode(dot + 1);
            _lastDot = dot;
            UpdateTailLine(pos);
        }
    }

    private void PatternCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_drawing) return;
        _drawing = false;
        RemoveTailLine();
        // Auto-submit when the user lifts their finger/mouse
        _ = _vm.ConfirmPatternCommand.ExecuteAsync(null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private int HitTest(Point p)
    {
        for (int i = 0; i < DotCenters.Length; i++)
        {
            var dx = p.X - DotCenters[i].X;
            var dy = p.Y - DotCenters[i].Y;
            if (Math.Sqrt(dx * dx + dy * dy) <= HitRadius)
                return i;
        }
        return -1;
    }

    private void AddPermLine(Point from, Point to)
    {
        var line = new Line
        {
            StartPoint     = from,
            EndPoint       = to,
            Stroke         = LineBrush,
            StrokeThickness = 3,
            StrokeLineCap  = PenLineCap.Round,
        };
        PatternLineCanvas.Children.Add(line);
        _permLines.Add(line);
    }

    private void UpdateTailLine(Point to)
    {
        if (_lastDot < 0) { RemoveTailLine(); return; }
        if (_tailLine is null)
        {
            _tailLine = new Line
            {
                StartPoint      = DotCenters[_lastDot],
                EndPoint        = to,
                Stroke          = TailBrush,
                StrokeThickness = 2,
                StrokeLineCap   = PenLineCap.Round,
                StrokeDashArray = new AvaloniaList<double> { 5, 4 },
            };
            PatternLineCanvas.Children.Add(_tailLine);
        }
        else
        {
            _tailLine.StartPoint = DotCenters[_lastDot];
            _tailLine.EndPoint   = to;
        }
    }

    private void RemoveTailLine()
    {
        if (_tailLine is null) return;
        PatternLineCanvas.Children.Remove(_tailLine);
        _tailLine = null;
    }

    private void ClearPatternLines()
    {
        PatternLineCanvas.Children.Clear();
        _permLines.Clear();
        _tailLine   = null;
        _lastDot    = -1;
        _drawing    = false;
    }
}

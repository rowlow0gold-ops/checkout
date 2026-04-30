using CommunityToolkit.Mvvm.ComponentModel;

namespace Terminal.Models;

/// <summary>
/// One dot in the 9-dot Android-style pattern lock.
/// Indices 1–9 map left-to-right, top-to-bottom:
///   1 2 3
///   4 5 6
///   7 8 9
/// </summary>
public partial class PatternNode : ObservableObject
{
    public int Index { get; init; }

    /// <summary>True once the user taps/drags through this dot.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>Tap order in the current pattern (1-based). 0 = not yet selected.</summary>
    [ObservableProperty] private int _order;
}

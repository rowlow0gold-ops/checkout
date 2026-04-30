using CommunityToolkit.Mvvm.ComponentModel;

namespace Emulator.Models;

public partial class CashSlot : ObservableObject
{
    public string  Key          { get; init; } = "";   // unique key matching server DB, e.g. "1.00_bill"
    public string  Label        { get; init; } = "";   // display label, e.g. "$1"
    public string  Amount       { get; init; } = "";   // TCP payload, e.g. "1.00"
    public bool    IsCoin       { get; init; }
    public int     DefaultCount { get; init; } = 0;    // seed max — PourCash is capped here

    [ObservableProperty] private int    _count         = 0;
    [ObservableProperty] private string _editCountText = "0";  // string bound to TextBox in the management popup

    public bool IsAtMax => DefaultCount > 0 && _count >= DefaultCount;

    partial void OnCountChanged(int _) => OnPropertyChanged(nameof(IsAtMax));
}

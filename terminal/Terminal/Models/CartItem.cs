using CommunityToolkit.Mvvm.ComponentModel;

namespace Terminal.Models;

public partial class CartItem : ObservableObject
{
    public int    ProductId   { get; init; }
    public string Barcode     { get; init; } = "";
    public string Name        { get; init; } = "";
    public int    WeightGrams { get; init; } = 0;

    // Mutable so live price-change events can update in-cart items
    [ObservableProperty]
    private decimal _unitPrice;

    [ObservableProperty]
    private int _quantity = 1;

    /// <summary>
    /// "" = no weight check needed (WeightGrams == 0)
    /// "ok" = scale check passed
    /// "skipped" = scale was offline/error when this item was scanned
    /// </summary>
    [ObservableProperty]
    private string _weightStatus = "";

    public decimal Subtotal           => UnitPrice * Quantity;
    public bool    IsWeightUnverified => WeightStatus == "skipped";

    partial void OnQuantityChanged(int value)    => OnPropertyChanged(nameof(Subtotal));
    partial void OnUnitPriceChanged(decimal _)   => OnPropertyChanged(nameof(Subtotal));
    partial void OnWeightStatusChanged(string _) => OnPropertyChanged(nameof(IsWeightUnverified));
}

public record Product(int Id, string Barcode, string Name, decimal Price, string? Category, int WeightGrams = 0);

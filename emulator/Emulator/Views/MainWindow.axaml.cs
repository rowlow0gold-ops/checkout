using Avalonia.Controls;
using Avalonia.Interactivity;
using Emulator.ViewModels;

namespace Emulator.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    // Quick-preset barcode buttons — pass payload directly to avoid binding-timing race
    private void OnPresetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string barcode
            && DataContext is MainViewModel vm)
            _ = vm.SendPresetScanCommand.ExecuteAsync(barcode);
    }

    // Loyalty card preset buttons — pass payload directly
    private void OnLoyaltyPresetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string cardId
            && DataContext is MainViewModel vm)
            _ = vm.SendPresetLoyaltyCommand.ExecuteAsync(cardId);
    }

    // Phone number preset buttons — sets PhoneInput and sends
    private void OnPhonePresetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string phone
            && DataContext is MainViewModel vm)
            _ = vm.SendPresetPhoneCommand.ExecuteAsync(phone);
    }
}

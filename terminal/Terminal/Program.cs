using Avalonia;
using Terminal.Services;

namespace Terminal;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Usage: dotnet run --project Terminal -- --id 2
        // Terminal ID determines both the emulator listen port (9875 + id)
        // and the terminal identity sent to the store server.
        var idArg = Array.IndexOf(args, "--id");
        if (idArg >= 0 && idArg + 1 < args.Length && int.TryParse(args[idArg + 1], out var id))
        {
            Config.TerminalId = id;
            Config.EmulatorPort = 9875 + id;   // Terminal 1 → 9876, Terminal 2 → 9877, etc.
        }

        // Ensure Ctrl+C kills the process even if the TCP listener is still running
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = false;
            Environment.Exit(0);
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        Environment.Exit(0); // force-exit after window closes — kills any lingering background threads
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .WithInterFont()
                     .LogToTrace();
}

using System.Net.Sockets;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Emulator.Protocol;

namespace Emulator.Models;

/// <summary>
/// Represents one checkout terminal the emulator can connect to.
/// Each entry manages its own TCP socket independently.
/// </summary>
public partial class TerminalEntry : ObservableObject
{
    public int    Id    { get; init; }
    public string Label { get; init; } = "";
    public string Host  { get; init; } = "127.0.0.1";
    public int    Port  { get; init; }

    [ObservableProperty] private bool   _isConnected = false;
    [ObservableProperty] private bool   _isTargeted  = false;  // receives broadcasts when connected
    [ObservableProperty] private bool   _isSelected  = false;  // cash drawer scope in emulator
    [ObservableProperty] private string _statusText  = "Disconnected";

    public string ConnectLabel => IsConnected ? "Disconnect" : "Connect";

    partial void OnIsConnectedChanged(bool _) => OnPropertyChanged(nameof(ConnectLabel));

    private TcpClient?          _client;
    private NetworkStream?      _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task<bool> ConnectAsync()
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(Host, Port);
            _stream      = _client.GetStream();
            IsConnected  = true;
            StatusText   = "Connected";
            return true;
        }
        catch
        {
            IsConnected = false;
            StatusText  = "Unreachable";
            return false;
        }
    }

    public void Disconnect()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream     = null;
        _client     = null;
        IsConnected = false;
        StatusText  = "Disconnected";
    }

    public async Task ToggleAsync()
    {
        if (IsConnected) Disconnect();
        else
        {
            StatusText = "Connecting…";
            await ConnectAsync();
        }
    }

    public async Task SendAsync(EmulatorMessage msg)
    {
        if (_stream is null || !IsConnected) return;
        await _writeLock.WaitAsync();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(msg.ToJson() + "\n");
            await _stream.WriteAsync(bytes);
        }
        catch
        {
            IsConnected = false;
            StatusText  = "Disconnected";
        }
        finally
        {
            _writeLock.Release();
        }
    }
}

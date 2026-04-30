using System.Net.Sockets;
using System.Text;
using Emulator.Protocol;

namespace Emulator.Services;

/// <summary>
/// Connects to the terminal app's emulator listener on localhost:9876
/// and sends JSON messages to inject hardware events.
/// </summary>
public class EmulatorSocket : IAsyncDisposable
{
    private const string Host = "127.0.0.1";
    private const int    Port = 9876;

    private TcpClient?    _client;
    private NetworkStream? _stream;

    public bool IsConnected => _client?.Connected == true;

    public event Action<bool>? ConnectionChanged;

    public async Task<bool> ConnectAsync()
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(Host, Port);
            _stream = _client.GetStream();
            ConnectionChanged?.Invoke(true);
            return true;
        }
        catch
        {
            ConnectionChanged?.Invoke(false);
            return false;
        }
    }

    public async Task SendAsync(EmulatorMessage msg)
    {
        if (_stream is null) return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(msg.ToJson() + "\n");
            await _stream.WriteAsync(bytes);
        }
        catch
        {
            ConnectionChanged?.Invoke(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null) await _stream.DisposeAsync();
        _client?.Dispose();
    }
}

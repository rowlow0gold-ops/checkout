using System.Text.Json;
using System.Text.Json.Serialization;

namespace Emulator.Protocol;

/// <summary>
/// Simple JSON messages sent over TCP to the terminal.
/// Terminal's HardwareService listens on port 9876 for these.
/// </summary>
public class EmulatorMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";   // scan | payment | status | network

    [JsonPropertyName("payload")]
    public string Payload { get; set; } = ""; // barcode / "success|fail" / "scanner:error" / etc.

    public string ToJson() => JsonSerializer.Serialize(this);

    public static EmulatorMessage? FromJson(string json)
        => JsonSerializer.Deserialize<EmulatorMessage>(json);
}

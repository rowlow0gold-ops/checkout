namespace Terminal.Models;

public class ReceiptHistoryEntry
{
    public DateTime  Timestamp   { get; init; }
    public string    ReceiptText { get; init; } = "";
    public decimal   Total       { get; init; }
    public string    Method      { get; init; } = "";
    public string    Member      { get; init; } = "";   // empty if no loyalty member

    public string Label => $"{Timestamp:HH:mm:ss}  ${Total:F2}  [{Method.ToUpper()}]"
                         + (string.IsNullOrEmpty(Member) ? "" : $"  · {Member}");
}

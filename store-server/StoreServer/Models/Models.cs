using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StoreServer.Models;

public class Product
{
    public int Id { get; set; }
    [Required] public string Barcode { get; set; } = "";
    [Required] public string Name    { get; set; } = "";
    [Column(TypeName = "decimal(10,2)")] public decimal Price { get; set; }
    public string? Category    { get; set; }
    public int     WeightGrams { get; set; } = 0;   // 0 = no weight check required
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Transaction
{
    public int      Id            { get; set; }
    public int      TerminalId    { get; set; }
    public decimal  TotalAmount   { get; set; }
    public string   PaymentMethod { get; set; } = "";
    public string   Status        { get; set; } = "pending";  // pending | synced | failed
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
    public List<TransactionItem> Items { get; set; } = [];
}

public class TransactionItem
{
    public int     Id            { get; set; }
    public int     TransactionId { get; set; }
    public int     ProductId     { get; set; }
    public int     Quantity      { get; set; }
    public decimal UnitPrice     { get; set; }
    public decimal Subtotal      { get; set; }
    public Product? Product      { get; set; }
}

/// <summary>
/// Loyalty / bonus card member.
/// One row per person — they can have a phone number, a physical card, or both.
/// Lookup works by either; points always update the same row via Id.
/// </summary>
public class LoyaltyMember
{
    public int      Id        { get; set; }
    public string?  Phone     { get; set; }          // e.g. "01012345678"  — null if phone not registered
    public string?  CardId    { get; set; }          // e.g. "CARD-001"     — null if no physical card
    [Required] public string Name { get; set; } = "";
    public int      Points    { get; set; } = 0;
    public string   Pin       { get; set; } = "";    // pattern string for phone login; empty for card-only / staff
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Computed — not stored in DB
    [NotMapped] public string  Tier         => Points >= 5000 ? "Gold"   : Points >= 1000 ? "Silver" : "Bronze";
    [NotMapped] public string  TierLabel    => Points >= 5000 ? "★ Gold" : Points >= 1000 ? "◈ Silver" : "◇ Bronze";
    [NotMapped] public bool    CanRedeem    => Points >= 1000;
    [NotMapped] public int     RedeemablePoints => Math.Min(Points, 1_000_000);
    [NotMapped] public decimal RedeemableSaving => RedeemablePoints / 100m;
    /// <summary>Canonical display identifier (phone preferred, fall back to card).</summary>
    [NotMapped] public string  PhoneOrCard  => Phone ?? CardId ?? "";
}

/// <summary>
/// Cash drawer inventory — one row per denomination.
/// Counts increase when cash is inserted by customers.
/// </summary>
public class CashDrawerEntry
{
    public int     Id           { get; set; }
    public int     TerminalId   { get; set; } = 1;   // which terminal owns this drawer row
    /// <summary>Unique key, e.g. "0.01", "1.00_coin", "1.00_bill", "20.00"</summary>
    [Required] public string  Key         { get; set; } = "";
    [Required] public string  Label       { get; set; } = "";   // "1¢", "$1 bill", "$20"
    [Column(TypeName = "decimal(10,2)")] public decimal Denomination { get; set; }
    public bool    IsCoin       { get; set; }
    public int     Count        { get; set; } = 0;
    public int     MaxCount     { get; set; } = 0;   // 0 = no limit
}

/// <summary>
/// Generic key-value store for server-side configuration (e.g. StaffPin).
/// </summary>
public class StoreSetting
{
    public int    Id    { get; set; }
    public string Key   { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>
/// Records every scale weight-check performed at a terminal.
/// Result: "pass" | "fail" | "timeout"
/// </summary>
public class WeightCheckLog
{
    public int      Id            { get; set; }
    public int      TerminalId    { get; set; }
    public string   Barcode       { get; set; } = "";
    public string   ProductName   { get; set; } = "";
    public int      ExpectedGrams { get; set; }
    public int      ActualGrams   { get; set; }   // 0 when result = "timeout"
    public string   Result        { get; set; } = ""; // pass | fail | timeout | staff_override
    [Column(TypeName = "decimal(10,2)")] public decimal ItemPrice  { get; set; }
    public double   RiskScore     { get; set; }   // deviation × item_price
    public DateTime CheckedAt     { get; set; } = DateTime.UtcNow;
}

namespace Terminal.Models;

/// <summary>DTO received from the store server /api/loyalty endpoint.</summary>
public class LoyaltyMemberInfo
{
    public int     Id           { get; set; }
    public string  PhoneOrCard  { get; set; } = "";
    public string  Name         { get; set; } = "";
    public int     Points       { get; set; }
    public string  Tier         { get; set; } = "Bronze";
    public string  TierLabel    { get; set; } = "◇ Bronze";
    public bool    CanRedeem    { get; set; }
    public int     RedeemablePoints { get; set; }
    public decimal RedeemableSaving { get; set; }
}

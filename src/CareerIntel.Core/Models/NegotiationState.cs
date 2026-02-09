namespace CareerIntel.Core.Models;

public sealed class NegotiationState
{
    public int Id { get; set; }
    public string Company { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public decimal OfferedSalary { get; set; }
    public string OfferedCurrency { get; set; } = "USD";
    public string EngagementType { get; set; } = string.Empty; // "Employment", "B2B", "Contract"
    public decimal? CounterOffer { get; set; }
    public string Status { get; set; } = "Pending"; // "Pending", "Negotiating", "Accepted", "Rejected", "Expired"
    public List<string> Leverage { get; set; } = []; // competing offers, rare skills, etc.
    public decimal MarketRate { get; set; }
    public decimal MarketRateTop { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public DateTimeOffset ReceivedDate { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public string Notes { get; set; } = string.Empty;
}

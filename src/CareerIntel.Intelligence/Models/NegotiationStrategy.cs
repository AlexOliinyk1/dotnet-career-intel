namespace CareerIntel.Intelligence.Models;

public sealed class NegotiationStrategy
{
    public string OverallAssessment { get; set; } = string.Empty; // "Below market", "At market", "Above market"
    public decimal SuggestedCounter { get; set; }
    public string CounterJustification { get; set; } = string.Empty;
    public List<string> LeveragePoints { get; set; } = [];
    public decimal BatnaValue { get; set; } // value of your best alternative
    public string NegotiationScript { get; set; } = string.Empty;
    public string RiskAssessment { get; set; } = string.Empty;
    public bool ShouldNegotiate { get; set; }
}

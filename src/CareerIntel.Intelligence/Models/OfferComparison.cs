using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence.Models;

public sealed class OfferComparison
{
    public List<RankedOffer> Rankings { get; set; } = [];
    public string Recommendation { get; set; } = string.Empty;
}

public sealed class RankedOffer
{
    public NegotiationState Offer { get; set; } = null!;
    public int Rank { get; set; }
    public double CompScore { get; set; }
    public double GrowthScore { get; set; }
    public double StackAlignmentScore { get; set; }
    public double OverallScore { get; set; }
    public string Verdict { get; set; } = string.Empty;
}

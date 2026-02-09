namespace CareerIntel.Core.Models;

/// <summary>
/// Intelligence derived from historical negotiation outcomes.
/// </summary>
public sealed class NegotiationInsight
{
    public int TotalNegotiations { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public double SuccessRate { get; set; }

    /// <summary>Average delta between initial offer and final accepted amount.</summary>
    public decimal AverageOfferDelta { get; set; }

    /// <summary>Average percentage increase achieved through negotiation.</summary>
    public double AverageNegotiationLift { get; set; }

    /// <summary>Skills that correlate with higher negotiation success.</summary>
    public List<SkillLeverageCorrelation> SkillLeverageMap { get; set; } = [];

    /// <summary>Counter-offer strategies that worked vs failed.</summary>
    public List<StrategyOutcome> StrategyHistory { get; set; } = [];

    /// <summary>Recommended negotiation approach based on historical data.</summary>
    public string LearnedStrategy { get; set; } = string.Empty;
}

public sealed class SkillLeverageCorrelation
{
    public string SkillName { get; set; } = string.Empty;
    public int TimesUsedAsLeverage { get; set; }
    public int TimesSucceeded { get; set; }
    public double SuccessRate { get; set; }
    public decimal AverageLiftWhenUsed { get; set; }
}

public sealed class StrategyOutcome
{
    public string StrategyType { get; set; } = string.Empty;
    public decimal AskDelta { get; set; }
    public double AskDeltaPercent { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; }
}

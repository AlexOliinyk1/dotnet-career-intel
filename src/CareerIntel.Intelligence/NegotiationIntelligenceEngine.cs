using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

/// <summary>
/// Learns from historical negotiation outcomes to improve counter-offer strategies,
/// identify skill-based leverage correlations, and compute offer vs ask deltas.
/// </summary>
public sealed class NegotiationIntelligenceEngine(ILogger<NegotiationIntelligenceEngine> logger)
{
    private const double AggressiveThreshold = 0.20;
    private const double ModerateThreshold = 0.10;

    /// <summary>
    /// Analyzes all historical negotiations to extract patterns, success rates,
    /// skill leverage correlations, and learned counter-offer strategies.
    /// </summary>
    public NegotiationInsight AnalyzeHistory(IReadOnlyList<NegotiationState> allNegotiations)
    {
        logger.LogInformation("Analyzing {Count} historical negotiations", allNegotiations.Count);

        var accepted = allNegotiations
            .Where(n => n.Status.Equals("Accepted", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var rejected = allNegotiations
            .Where(n => n.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
            .ToList();

        int totalResolved = accepted.Count + rejected.Count;
        double successRate = totalResolved > 0
            ? Math.Round((double)accepted.Count / totalResolved * 100, 1)
            : 0;

        // Offer deltas: for accepted negotiations that had a counter-offer
        var acceptedWithCounter = accepted
            .Where(n => n.CounterOffer.HasValue && n.OfferedSalary > 0)
            .ToList();

        decimal averageOfferDelta = acceptedWithCounter.Count > 0
            ? acceptedWithCounter.Average(n => n.CounterOffer!.Value - n.OfferedSalary)
            : 0;

        double averageNegotiationLift = acceptedWithCounter.Count > 0
            ? Math.Round(acceptedWithCounter.Average(n =>
                (double)((n.CounterOffer!.Value - n.OfferedSalary) / n.OfferedSalary) * 100), 1)
            : 0;

        var skillLeverageMap = BuildSkillLeverageMap(allNegotiations);
        var strategyHistory = BuildStrategyHistory(allNegotiations);
        string learnedStrategy = GenerateLearnedStrategy(strategyHistory, successRate);

        logger.LogInformation(
            "History analysis complete: {Total} total, {Accepted} accepted, {Rejected} rejected, " +
            "{Rate}% success rate, avg lift {Lift}%",
            allNegotiations.Count, accepted.Count, rejected.Count, successRate, averageNegotiationLift);

        return new NegotiationInsight
        {
            TotalNegotiations = allNegotiations.Count,
            AcceptedCount = accepted.Count,
            RejectedCount = rejected.Count,
            SuccessRate = successRate,
            AverageOfferDelta = Math.Round(averageOfferDelta, 2),
            AverageNegotiationLift = averageNegotiationLift,
            SkillLeverageMap = skillLeverageMap,
            StrategyHistory = strategyHistory,
            LearnedStrategy = learnedStrategy
        };
    }

    /// <summary>
    /// Computes skill-to-leverage correlations: which skills, when cited as leverage,
    /// lead to better negotiation outcomes. Optionally cross-references with market
    /// demand data to weight skills that are both rare and effective.
    /// </summary>
    public List<SkillLeverageCorrelation> ComputeSkillLeverage(
        IReadOnlyList<NegotiationState> negotiations,
        IReadOnlyList<JobVacancy> marketData)
    {
        logger.LogInformation(
            "Computing skill leverage from {Negotiations} negotiations and {Market} market data points",
            negotiations.Count, marketData.Count);

        var skillMap = BuildSkillLeverageMap(negotiations);

        // Cross-reference with market demand: skills appearing in fewer vacancies carry more leverage
        if (marketData.Count > 0)
        {
            var allRequired = marketData.SelectMany(v => v.RequiredSkills).ToList();
            int totalVacancies = marketData.Count;

            foreach (var skill in skillMap)
            {
                int appearances = allRequired
                    .Count(r => r.Equals(skill.SkillName, StringComparison.OrdinalIgnoreCase));
                double frequency = (double)appearances / totalVacancies;

                // Rare skills (< 20% frequency) get a boost to their effective lift
                if (frequency > 0 && frequency < 0.20)
                {
                    skill.AverageLiftWhenUsed *= 1.25m; // 25% boost for scarcity
                }
            }
        }

        return skillMap
            .OrderByDescending(s => (double)s.AverageLiftWhenUsed * s.SuccessRate)
            .ToList();
    }

    /// <summary>
    /// Generates a counter-offer strategy recommendation for the current offer
    /// based on historical negotiation intelligence.
    /// </summary>
    public string GenerateCounterStrategy(
        NegotiationState currentOffer,
        NegotiationInsight historicalInsight)
    {
        logger.LogInformation(
            "Generating counter strategy for {Company} offer at {Offered:C0}",
            currentOffer.Company, currentOffer.OfferedSalary);

        if (historicalInsight.TotalNegotiations == 0)
        {
            return "No historical data available. Recommend a moderate counter (12-15% above offer) " +
                   "as a starting point while you build negotiation history.";
        }

        // Analyze which strategy tier works best historically
        var aggressive = historicalInsight.StrategyHistory
            .Where(s => s.StrategyType == "Aggressive")
            .ToList();
        var moderate = historicalInsight.StrategyHistory
            .Where(s => s.StrategyType == "Moderate")
            .ToList();
        var conservative = historicalInsight.StrategyHistory
            .Where(s => s.StrategyType == "Conservative")
            .ToList();

        double aggressiveRate = ComputeAcceptanceRate(aggressive);
        double moderateRate = ComputeAcceptanceRate(moderate);
        double conservativeRate = ComputeAcceptanceRate(conservative);

        var parts = new List<string>();

        // Determine the best strategy based on historical data
        string recommendedTier;
        string recommendedRange;

        if (aggressiveRate >= moderateRate && aggressiveRate >= conservativeRate && aggressive.Count >= 2)
        {
            recommendedTier = "aggressive";
            recommendedRange = "18-25%";
        }
        else if (moderateRate >= conservativeRate && moderate.Count >= 2)
        {
            recommendedTier = "moderate";
            recommendedRange = "12-18%";
        }
        else
        {
            recommendedTier = "conservative";
            recommendedRange = "5-12%";
        }

        parts.Add($"Based on {historicalInsight.TotalNegotiations} past negotiations " +
                  $"({historicalInsight.SuccessRate}% overall success rate):");

        parts.Add($"Recommend a {recommendedTier} counter at {recommendedRange} above the offer.");

        // Add specific tier rates
        if (aggressive.Count > 0)
            parts.Add($"Aggressive counters (>20%): {aggressiveRate:F0}% acceptance rate ({aggressive.Count} attempts).");
        if (moderate.Count > 0)
            parts.Add($"Moderate counters (10-20%): {moderateRate:F0}% acceptance rate ({moderate.Count} attempts).");
        if (conservative.Count > 0)
            parts.Add($"Conservative counters (<10%): {conservativeRate:F0}% acceptance rate ({conservative.Count} attempts).");

        // Compute a specific suggested counter for this offer
        decimal suggestedMultiplier = recommendedTier switch
        {
            "aggressive" => 1.20m,
            "moderate" => 1.15m,
            _ => 1.08m
        };

        decimal suggestedCounter = Math.Round(currentOffer.OfferedSalary * suggestedMultiplier, 0);

        // If market rate is known and the suggested counter is still below market, adjust up
        if (currentOffer.MarketRate > 0 && suggestedCounter < currentOffer.MarketRate)
        {
            suggestedCounter = currentOffer.MarketRate;
            parts.Add($"Adjusted counter up to market rate of {suggestedCounter:C0} since the " +
                      $"calculated counter was below market median.");
        }

        parts.Add($"Suggested counter for {currentOffer.Company}: {suggestedCounter:C0} " +
                  $"({(suggestedCounter - currentOffer.OfferedSalary) / currentOffer.OfferedSalary * 100:F1}% above offer).");

        // Skill-based leverage advice
        var relevantSkills = historicalInsight.SkillLeverageMap
            .Where(s => s.SuccessRate >= 60 && s.TimesUsedAsLeverage >= 2)
            .OrderByDescending(s => s.SuccessRate)
            .Take(3)
            .ToList();

        if (relevantSkills.Count > 0)
        {
            var skillNames = string.Join(", ", relevantSkills.Select(s => s.SkillName));
            parts.Add($"Emphasize these high-leverage skills in your counter: {skillNames}.");
        }

        string strategy = string.Join(" ", parts);

        logger.LogInformation("Counter strategy generated for {Company}: {Tier} approach at {Counter:C0}",
            currentOffer.Company, recommendedTier, suggestedCounter);

        return strategy;
    }

    private static List<SkillLeverageCorrelation> BuildSkillLeverageMap(
        IReadOnlyList<NegotiationState> negotiations)
    {
        // Extract individual skill mentions from leverage strings
        var skillOutcomes = new Dictionary<string, List<(bool Succeeded, decimal Lift)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var negotiation in negotiations)
        {
            bool succeeded = negotiation.Status.Equals("Accepted", StringComparison.OrdinalIgnoreCase);
            decimal lift = negotiation is { CounterOffer: not null, OfferedSalary: > 0 }
                ? negotiation.CounterOffer.Value - negotiation.OfferedSalary
                : 0;

            foreach (var leverageItem in negotiation.Leverage)
            {
                // Extract skill names from leverage strings like "Rare/high-demand skills: Kubernetes, Rust"
                var skills = ParseSkillsFromLeverage(leverageItem);
                foreach (var skill in skills)
                {
                    if (!skillOutcomes.TryGetValue(skill, out var outcomes))
                    {
                        outcomes = [];
                        skillOutcomes[skill] = outcomes;
                    }
                    outcomes.Add((succeeded, lift));
                }
            }
        }

        return skillOutcomes
            .Select(kvp =>
            {
                int total = kvp.Value.Count;
                int succeeded = kvp.Value.Count(o => o.Succeeded);
                decimal avgLift = kvp.Value.Count > 0
                    ? kvp.Value.Average(o => o.Lift)
                    : 0;

                return new SkillLeverageCorrelation
                {
                    SkillName = kvp.Key,
                    TimesUsedAsLeverage = total,
                    TimesSucceeded = succeeded,
                    SuccessRate = total > 0 ? Math.Round((double)succeeded / total * 100, 1) : 0,
                    AverageLiftWhenUsed = Math.Round(avgLift, 2)
                };
            })
            .OrderByDescending(s => s.SuccessRate)
            .ThenByDescending(s => s.AverageLiftWhenUsed)
            .ToList();
    }

    private static List<string> ParseSkillsFromLeverage(string leverageItem)
    {
        // Handle patterns like "Rare/high-demand skills: Kubernetes, Rust, Go"
        const string skillsPrefix = "skills:";
        int prefixIndex = leverageItem.IndexOf(skillsPrefix, StringComparison.OrdinalIgnoreCase);
        if (prefixIndex >= 0)
        {
            string skillsPart = leverageItem[(prefixIndex + skillsPrefix.Length)..];
            return skillsPart
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .ToList();
        }

        // Handle patterns like "5+ years of relevant experience" — not a skill
        if (leverageItem.Contains("years of", StringComparison.OrdinalIgnoreCase) ||
            leverageItem.Contains("competing offer", StringComparison.OrdinalIgnoreCase) ||
            leverageItem.Contains("target salary", StringComparison.OrdinalIgnoreCase) ||
            leverageItem.Contains("below market", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        // Fallback: treat the whole item as a potential skill reference
        return [leverageItem.Trim()];
    }

    private static List<StrategyOutcome> BuildStrategyHistory(
        IReadOnlyList<NegotiationState> negotiations)
    {
        var outcomes = new List<StrategyOutcome>();

        foreach (var negotiation in negotiations)
        {
            // Only classify negotiations that have both an offer and a counter
            if (!negotiation.CounterOffer.HasValue || negotiation.OfferedSalary <= 0)
                continue;

            // Skip unresolved negotiations
            if (negotiation.Status is "Pending" or "Negotiating")
                continue;

            decimal delta = negotiation.CounterOffer.Value - negotiation.OfferedSalary;
            double deltaPercent = (double)(delta / negotiation.OfferedSalary);

            string strategyType = deltaPercent switch
            {
                > AggressiveThreshold => "Aggressive",
                > ModerateThreshold => "Moderate",
                _ => "Conservative"
            };

            outcomes.Add(new StrategyOutcome
            {
                StrategyType = strategyType,
                AskDelta = Math.Round(delta, 2),
                AskDeltaPercent = Math.Round(deltaPercent * 100, 1),
                Outcome = negotiation.Status,
                Company = negotiation.Company,
                Date = negotiation.ReceivedDate
            });
        }

        return outcomes.OrderByDescending(o => o.Date).ToList();
    }

    private static string GenerateLearnedStrategy(
        List<StrategyOutcome> strategyHistory,
        double overallSuccessRate)
    {
        if (strategyHistory.Count == 0)
        {
            return "Insufficient negotiation history to generate a learned strategy. " +
                   "Continue tracking outcomes to build intelligence.";
        }

        var grouped = strategyHistory
            .GroupBy(s => s.StrategyType)
            .Select(g => new
            {
                Type = g.Key,
                Total = g.Count(),
                Accepted = g.Count(s => s.Outcome.Equals("Accepted", StringComparison.OrdinalIgnoreCase)),
                AvgDeltaPercent = g.Average(s => s.AskDeltaPercent)
            })
            .OrderByDescending(g => g.Total > 0 ? (double)g.Accepted / g.Total : 0)
            .ToList();

        var parts = new List<string>();

        foreach (var group in grouped)
        {
            double rate = group.Total > 0 ? Math.Round((double)group.Accepted / group.Total * 100, 0) : 0;
            parts.Add($"{group.Type} counters ({group.AvgDeltaPercent:F0}% avg ask) " +
                      $"have a {rate}% success rate across {group.Total} attempt(s)");
        }

        string summary = string.Join(". ", parts) + ".";

        // Find the best-performing strategy
        var best = grouped.FirstOrDefault();
        if (best is not null && best.Total >= 2)
        {
            double bestRate = (double)best.Accepted / best.Total * 100;
            summary += $" Recommendation: favor {best.Type.ToLowerInvariant()} counters " +
                       $"({bestRate:F0}% success rate) when negotiating.";
        }

        return summary;
    }

    private static double ComputeAcceptanceRate(List<StrategyOutcome> outcomes)
    {
        if (outcomes.Count == 0) return 0;
        int accepted = outcomes.Count(o => o.Outcome.Equals("Accepted", StringComparison.OrdinalIgnoreCase));
        return Math.Round((double)accepted / outcomes.Count * 100, 1);
    }
}

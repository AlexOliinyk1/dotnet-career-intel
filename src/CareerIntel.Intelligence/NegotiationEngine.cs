using CareerIntel.Core.Models;
using CareerIntel.Intelligence.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

public sealed class NegotiationEngine(ILogger<NegotiationEngine> logger)
{
    private const double CounterOfferFloorMultiplier = 1.12; // 12% above if below market
    private const double CounterOfferCeilingMultiplier = 1.18; // 18% above if well below market

    /// <summary>
    /// Analyze an offer and provide a negotiation strategy.
    /// </summary>
    public NegotiationStrategy AnalyzeOffer(
        NegotiationState offer,
        IReadOnlyList<NegotiationState> otherOffers,
        UserProfile profile,
        IReadOnlyList<JobVacancy> marketData)
    {
        logger.LogInformation("Analyzing offer from {Company}: offered {Offered:C0}, market rate {Market:C0}",
            offer.Company, offer.OfferedSalary, offer.MarketRate);

        // 1. Compare offer vs market rate
        string overallAssessment = AssessOfferVsMarket(offer);

        // 2. Identify leverage points
        var leveragePoints = IdentifyLeverage(offer, otherOffers, profile, marketData);

        // 3. Calculate BATNA
        decimal batnaValue = ComputeBatna(otherOffers, profile);

        // 4. Determine if counter-offer is appropriate
        bool shouldNegotiate = ShouldCounter(offer, batnaValue);

        // 5. Suggest counter amount
        decimal suggestedCounter = ComputeCounterOffer(offer, marketData, batnaValue);

        // 6. Generate counter justification
        string counterJustification = BuildCounterJustification(offer, suggestedCounter, leveragePoints, marketData);

        // 7. Generate negotiation script
        string script = GenerateNegotiationScript(offer, suggestedCounter, leveragePoints, overallAssessment);

        // 8. Assess risk
        string riskAssessment = AssessRisk(offer, suggestedCounter, otherOffers);

        logger.LogInformation(
            "Negotiation analysis for {Company}: {Assessment}, shouldNegotiate={ShouldNegotiate}, suggestedCounter={Counter:C0}",
            offer.Company, overallAssessment, shouldNegotiate, suggestedCounter);

        return new NegotiationStrategy
        {
            OverallAssessment = overallAssessment,
            SuggestedCounter = suggestedCounter,
            CounterJustification = counterJustification,
            LeveragePoints = leveragePoints,
            BatnaValue = batnaValue,
            NegotiationScript = script,
            RiskAssessment = riskAssessment,
            ShouldNegotiate = shouldNegotiate
        };
    }

    /// <summary>
    /// Compare multiple active offers and rank them.
    /// </summary>
    public OfferComparison CompareOffers(IReadOnlyList<NegotiationState> offers, UserProfile profile)
    {
        logger.LogInformation("Comparing {Count} active offers", offers.Count);

        if (offers.Count == 0)
        {
            return new OfferComparison
            {
                Rankings = [],
                Recommendation = "No active offers to compare."
            };
        }

        var rankedOffers = new List<RankedOffer>();

        foreach (var offer in offers)
        {
            double compScore = ComputeCompScore(offer, profile);
            double growthScore = ComputeGrowthScore(offer);
            double stackAlignmentScore = ComputeStackAlignmentScore(offer, profile);

            // Overall: weighted combination
            double overallScore = compScore * 0.40 + growthScore * 0.30 + stackAlignmentScore * 0.30;

            rankedOffers.Add(new RankedOffer
            {
                Offer = offer,
                CompScore = compScore,
                GrowthScore = growthScore,
                StackAlignmentScore = stackAlignmentScore,
                OverallScore = overallScore,
                Verdict = GenerateVerdict(overallScore, compScore, growthScore, stackAlignmentScore)
            });
        }

        // Sort by overall score descending and assign ranks
        rankedOffers = rankedOffers.OrderByDescending(r => r.OverallScore).ToList();
        for (int i = 0; i < rankedOffers.Count; i++)
        {
            rankedOffers[i].Rank = i + 1;
        }

        string recommendation = GenerateComparisonRecommendation(rankedOffers, profile);

        logger.LogInformation("Offer comparison complete. Top offer: {Company} with score {Score:F1}",
            rankedOffers[0].Offer.Company, rankedOffers[0].OverallScore);

        return new OfferComparison
        {
            Rankings = rankedOffers,
            Recommendation = recommendation
        };
    }

    private static string AssessOfferVsMarket(NegotiationState offer)
    {
        if (offer.MarketRate <= 0)
            return "At market"; // Unknown market rate, assume fair

        decimal ratio = offer.OfferedSalary / offer.MarketRate;

        return ratio switch
        {
            < 0.90m => "Below market",
            > 1.10m => "Above market",
            _ => "At market"
        };
    }

    private static List<string> IdentifyLeverage(
        NegotiationState offer,
        IReadOnlyList<NegotiationState> otherOffers,
        UserProfile profile,
        IReadOnlyList<JobVacancy> marketData)
    {
        var leverage = new List<string>();

        // Competing offers
        var activeAlternatives = otherOffers
            .Where(o => !o.Company.Equals(offer.Company, StringComparison.OrdinalIgnoreCase)
                        && o.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (activeAlternatives.Count > 0)
        {
            decimal maxAlternative = activeAlternatives.Max(o => o.OfferedSalary);
            if (maxAlternative > offer.OfferedSalary)
            {
                leverage.Add($"Competing offer at {maxAlternative:C0} from another company");
            }
            else
            {
                leverage.Add($"{activeAlternatives.Count} competing offer(s) in hand");
            }
        }

        // Rare skills
        var rareSkills = FindRareSkills(profile, marketData);
        if (rareSkills.Count > 0)
        {
            leverage.Add($"Rare/high-demand skills: {string.Join(", ", rareSkills)}");
        }

        // Below market
        if (offer.MarketRate > 0 && offer.OfferedSalary < offer.MarketRate * 0.95m)
        {
            leverage.Add($"Current offer is {(1 - offer.OfferedSalary / offer.MarketRate) * 100:F0}% below market median");
        }

        // Experience leverage
        double maxExperience = profile.Skills
            .Select(s => s.YearsOfExperience)
            .DefaultIfEmpty(0)
            .Max();

        if (maxExperience >= 5)
        {
            leverage.Add($"{maxExperience:F0}+ years of relevant experience");
        }

        // Target salary leverage
        if (profile.Preferences.TargetSalaryUsd > offer.OfferedSalary)
        {
            leverage.Add($"Your target salary ({profile.Preferences.TargetSalaryUsd:C0}) exceeds the current offer");
        }

        return leverage;
    }

    private static List<string> FindRareSkills(UserProfile profile, IReadOnlyList<JobVacancy> marketData)
    {
        if (marketData.Count == 0)
            return [];

        // Skills that appear in < 20% of vacancies but user has at proficiency >= 4
        var allRequired = marketData.SelectMany(v => v.RequiredSkills).ToList();
        int totalVacancies = marketData.Count;

        return profile.Skills
            .Where(s => s.ProficiencyLevel >= 4)
            .Where(s =>
            {
                int appearances = allRequired.Count(r => r.Equals(s.SkillName, StringComparison.OrdinalIgnoreCase));
                double frequency = (double)appearances / totalVacancies;
                return frequency > 0 && frequency < 0.20;
            })
            .Select(s => s.SkillName)
            .Take(3)
            .ToList();
    }

    private static decimal ComputeBatna(IReadOnlyList<NegotiationState> otherOffers, UserProfile profile)
    {
        // BATNA = best alternative offer, or minimum acceptable salary if no alternatives
        var activeOffers = otherOffers
            .Where(o => o.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (activeOffers.Count > 0)
        {
            return activeOffers.Max(o => o.OfferedSalary);
        }

        // Fall back to user's minimum acceptable salary
        return profile.Preferences.MinSalaryUsd;
    }

    private static bool ShouldCounter(NegotiationState offer, decimal batnaValue)
    {
        // Counter if: offer is below market, or below BATNA, or there's room to negotiate
        if (offer.MarketRate > 0 && offer.OfferedSalary < offer.MarketRate)
            return true;

        if (offer.OfferedSalary < batnaValue)
            return true;

        // Even at-market offers can sometimes be negotiated if there's leverage
        if (offer.Leverage is not null && offer.Leverage.Count > 0)
            return true;

        return false;
    }

    private static decimal ComputeCounterOffer(
        NegotiationState offer,
        IReadOnlyList<JobVacancy> marketData,
        decimal batnaValue)
    {
        decimal marketMedian = offer.MarketRate > 0
            ? offer.MarketRate
            : ComputeMarketMedian(marketData);

        if (marketMedian <= 0)
            marketMedian = offer.OfferedSalary; // Fallback

        // If significantly below market (>10%), counter at 18% above offer
        if (offer.OfferedSalary < marketMedian * 0.90m)
        {
            decimal counter = offer.OfferedSalary * (decimal)CounterOfferCeilingMultiplier;
            return Math.Max(counter, batnaValue);
        }

        // If moderately below market, counter at 12% above offer
        if (offer.OfferedSalary < marketMedian)
        {
            decimal counter = offer.OfferedSalary * (decimal)CounterOfferFloorMultiplier;
            return Math.Max(counter, batnaValue);
        }

        // At or above market: still try for 5-8% bump
        decimal modestCounter = offer.OfferedSalary * 1.05m;
        return Math.Max(modestCounter, batnaValue);
    }

    private static decimal ComputeMarketMedian(IReadOnlyList<JobVacancy> marketData)
    {
        if (marketData.Count == 0)
            return 0m;

        var salaries = marketData
            .Where(v => v.SalaryMin.HasValue && v.SalaryMax.HasValue)
            .Select(v => (v.SalaryMin!.Value + v.SalaryMax!.Value) / 2m)
            .OrderBy(s => s)
            .ToList();

        if (salaries.Count == 0)
            return 0m;

        int mid = salaries.Count / 2;
        return salaries.Count % 2 == 0
            ? (salaries[mid - 1] + salaries[mid]) / 2m
            : salaries[mid];
    }

    private static string BuildCounterJustification(
        NegotiationState offer,
        decimal suggestedCounter,
        List<string> leveragePoints,
        IReadOnlyList<JobVacancy> marketData)
    {
        var parts = new List<string>();

        if (offer.MarketRate > 0 && offer.OfferedSalary < offer.MarketRate)
        {
            parts.Add($"Market data shows the median rate for this role is {offer.MarketRate:C0}, "
                       + $"while the current offer of {offer.OfferedSalary:C0} falls below that benchmark.");
        }

        if (leveragePoints.Count > 0)
        {
            parts.Add($"Key leverage factors support a higher offer: {string.Join("; ", leveragePoints.Take(3))}.");
        }

        if (marketData.Count > 0)
        {
            decimal upperQuartile = marketData
                .Where(v => v.SalaryMax.HasValue)
                .Select(v => v.SalaryMax!.Value)
                .OrderByDescending(s => s)
                .Skip(marketData.Count / 4)
                .FirstOrDefault();

            if (upperQuartile > 0 && suggestedCounter <= upperQuartile)
            {
                parts.Add($"The suggested counter of {suggestedCounter:C0} remains within the upper quartile range ({upperQuartile:C0}) for comparable positions.");
            }
        }

        return parts.Count > 0
            ? string.Join(" ", parts)
            : $"A counter of {suggestedCounter:C0} reflects your qualifications and market positioning.";
    }

    private static string GenerateNegotiationScript(
        NegotiationState offer,
        decimal suggestedCounter,
        List<string> leveragePoints,
        string assessment)
    {
        var lines = new List<string>
        {
            $"\"Thank you for the offer from {offer.Company}. I'm genuinely excited about this opportunity.",
            ""
        };

        if (assessment == "Below market")
        {
            lines.Add("After researching market rates for this role and considering my experience, "
                       + $"I believe a compensation of {suggestedCounter:C0} would better reflect the value I'd bring to the team.");
        }
        else
        {
            lines.Add($"I'd like to discuss the compensation. Based on my research and qualifications, "
                       + $"I'd like to propose {suggestedCounter:C0}.");
        }

        if (leveragePoints.Count > 0)
        {
            lines.Add("");
            lines.Add("Key points to support this:");
            foreach (var point in leveragePoints.Take(3))
            {
                lines.Add($"  - {point}");
            }
        }

        lines.Add("");
        lines.Add("I'm confident we can find a number that works for both of us. "
                   + "I'm very interested in joining the team and contributing to the company's success.\"");

        return string.Join(Environment.NewLine, lines);
    }

    private static string AssessRisk(
        NegotiationState offer,
        decimal suggestedCounter,
        IReadOnlyList<NegotiationState> otherOffers)
    {
        decimal increasePercent = offer.OfferedSalary > 0
            ? (suggestedCounter - offer.OfferedSalary) / offer.OfferedSalary * 100
            : 0;

        bool hasAlternatives = otherOffers.Any(o =>
            o.Status.Equals("Active", StringComparison.OrdinalIgnoreCase));

        if (increasePercent > 25)
        {
            return hasAlternatives
                ? "HIGH: Counter is >25% above offer. Aggressive but you have alternatives if they decline."
                : "VERY HIGH: Counter is >25% above offer with no backup. Consider a more moderate counter.";
        }

        if (increasePercent > 15)
        {
            return hasAlternatives
                ? "MODERATE: Counter is 15-25% above offer. Reasonable with competing offers as leverage."
                : "MODERATE-HIGH: Counter is 15-25% above offer. Be prepared to negotiate down.";
        }

        if (increasePercent > 8)
        {
            return "LOW: Counter is 8-15% above offer. This is a standard negotiation range and unlikely to cause issues.";
        }

        return "VERY LOW: Counter is within 8% of offer. Most employers expect and accept this range.";
    }

    private static double ComputeCompScore(NegotiationState offer, UserProfile profile)
    {
        // Score based on how offer compares to target salary (0-100)
        if (profile.Preferences.TargetSalaryUsd <= 0)
            return 50.0;

        double ratio = (double)(offer.OfferedSalary / profile.Preferences.TargetSalaryUsd);
        return Math.Clamp(ratio * 100.0, 0, 100);
    }

    private static double ComputeGrowthScore(NegotiationState offer)
    {
        // Growth potential heuristic based on offer status and negotiation room
        if (offer.MarketRate <= 0)
            return 50.0;

        // If offered below market, there's more room for growth
        double marketRatio = (double)(offer.OfferedSalary / offer.MarketRate);

        // Companies that offer below market may have higher growth trajectory (startups)
        // Companies at or above market are more established
        return marketRatio switch
        {
            < 0.85 => 70.0, // Likely startup/growth-stage
            < 0.95 => 60.0,
            < 1.05 => 50.0,
            < 1.15 => 45.0,
            _ => 40.0 // Premium pay often means less equity/growth
        };
    }

    private static double ComputeStackAlignmentScore(NegotiationState offer, UserProfile profile)
    {
        // We don't have direct tech stack from NegotiationState, so use leverage as a proxy
        // If offer has leverage items mentioning skills, that indicates alignment
        if (offer.Leverage is null || offer.Leverage.Count == 0)
            return 50.0;

        int skillMentions = 0;
        foreach (var leverageItem in offer.Leverage)
        {
            foreach (var skill in profile.Skills)
            {
                if (leverageItem.Contains(skill.SkillName, StringComparison.OrdinalIgnoreCase))
                    skillMentions++;
            }
        }

        return Math.Clamp(50.0 + skillMentions * 10.0, 0, 100);
    }

    private static string GenerateVerdict(
        double overallScore,
        double compScore,
        double growthScore,
        double stackAlignmentScore)
    {
        if (overallScore >= 80)
            return "Excellent offer - strong across all dimensions";
        if (overallScore >= 65)
            return "Good offer - consider accepting with minor negotiation";
        if (overallScore >= 50)
        {
            var weakDimension = (compScore, growthScore, stackAlignmentScore) switch
            {
                var (c, _, _) when c < 50 => "compensation is below target",
                var (_, g, _) when g < 50 => "growth potential may be limited",
                var (_, _, s) when s < 50 => "tech stack alignment could be better",
                _ => "some trade-offs exist"
            };
            return $"Decent offer but {weakDimension}";
        }
        if (overallScore >= 35)
            return "Below average - significant negotiation needed or consider other options";
        return "Weak offer - likely better to pursue alternatives";
    }

    private static string GenerateComparisonRecommendation(
        List<RankedOffer> rankedOffers,
        UserProfile profile)
    {
        if (rankedOffers.Count == 0)
            return "No offers to compare.";

        var top = rankedOffers[0];
        var parts = new List<string>
        {
            $"Top recommendation: {top.Offer.Company} (score: {top.OverallScore:F1}/100)."
        };

        if (rankedOffers.Count > 1)
        {
            var runner = rankedOffers[1];
            double scoreDiff = top.OverallScore - runner.OverallScore;

            if (scoreDiff < 5)
            {
                parts.Add($"However, {runner.Offer.Company} is very close (score: {runner.OverallScore:F1}). "
                           + "Consider non-quantifiable factors like team culture and commute.");
            }
            else
            {
                parts.Add($"{top.Offer.Company} leads by {scoreDiff:F0} points over {runner.Offer.Company}.");
            }
        }

        // Check if top offer meets target salary
        if (top.Offer.OfferedSalary < profile.Preferences.TargetSalaryUsd)
        {
            parts.Add($"Note: Even the top offer ({top.Offer.OfferedSalary:C0}) is below your target of {profile.Preferences.TargetSalaryUsd:C0}. Negotiate before accepting.");
        }

        return string.Join(" ", parts);
    }
}

using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Matching;

/// <summary>
/// Hard eligibility gate that filters vacancies to B2B/contractor roles
/// workable remotely from Ukraine. This is NOT a scoring weight — ineligible
/// vacancies are excluded before any analysis or matching.
/// </summary>
public static class EligibilityGate
{
    private static readonly HashSet<string> ExclusionaryGeoRestrictions = new(StringComparer.OrdinalIgnoreCase)
    {
        "UK-only", "EU-only", "US-only", "AU-only",
        "UK-based", "EU-based", "US-based", "AU-based",
        "Work-Auth-Required", "No-Visa-Sponsorship", "Security-Clearance-Required"
    };

    /// <summary>
    /// Filters a list of vacancies, returning only those that pass the hard eligibility gate.
    /// </summary>
    public static IReadOnlyList<JobVacancy> Filter(IReadOnlyList<JobVacancy> vacancies)
    {
        var result = new List<JobVacancy>(vacancies.Count);

        foreach (var vacancy in vacancies)
        {
            if (IsEligible(vacancy))
                result.Add(vacancy);
        }

        return result;
    }

    /// <summary>
    /// Returns true if a single vacancy passes all eligibility rules:
    /// 1. Engagement type is B2B/contractor/freelance (or unknown — benefit of doubt)
    /// 2. Not payroll-only employment or Inside IR35
    /// 3. Remote-workable (not on-site or hybrid only)
    /// 4. No region restrictions that exclude Ukraine
    /// </summary>
    public static bool IsEligible(JobVacancy vacancy)
    {
        // Rule 1: Engagement type — exclude explicit employment and Inside IR35
        if (vacancy.EngagementType is EngagementType.Employment or EngagementType.InsideIR35)
            return false;

        // Rule 2: Must allow remote work — on-site and hybrid excluded
        if (vacancy.RemotePolicy is RemotePolicy.OnSite or RemotePolicy.Hybrid)
            return false;

        // Rule 3: No exclusionary geographic restrictions
        if (vacancy.GeoRestrictions.Count > 0 &&
            vacancy.GeoRestrictions.Any(r => ExclusionaryGeoRestrictions.Contains(r)))
            return false;

        return true;
    }

    /// <summary>
    /// Produces a detailed eligibility assessment with per-rule pass/fail and human-readable reasons.
    /// Unlike <see cref="IsEligible"/>, this explains WHY a vacancy passes or fails.
    /// </summary>
    public static EligibilityAssessment Assess(JobVacancy vacancy)
    {
        var rules = new List<EligibilityRule>();

        // Rule 1: Engagement type
        var engagementPassed = vacancy.EngagementType is not
            (EngagementType.Employment or EngagementType.InsideIR35);
        rules.Add(new EligibilityRule(
            "Engagement Type",
            engagementPassed,
            engagementPassed
                ? $"{vacancy.EngagementType} — eligible for B2B/contractor work from Ukraine"
                : $"{vacancy.EngagementType} — payroll employment not available from Ukraine"));

        // Rule 2: Remote policy
        var remotePassed = vacancy.RemotePolicy is not
            (RemotePolicy.OnSite or RemotePolicy.Hybrid);
        rules.Add(new EligibilityRule(
            "Remote Policy",
            remotePassed,
            remotePassed
                ? $"{vacancy.RemotePolicy} — remote work possible"
                : $"{vacancy.RemotePolicy} — requires physical presence"));

        // Rule 3: Geographic restrictions
        var geoRestrictions = vacancy.GeoRestrictions
            .Where(r => ExclusionaryGeoRestrictions.Contains(r))
            .ToList();
        var geoPassed = geoRestrictions.Count == 0;
        rules.Add(new EligibilityRule(
            "Geographic Restrictions",
            geoPassed,
            geoPassed
                ? "No exclusionary geographic restrictions detected"
                : $"Restricted: {string.Join(", ", geoRestrictions)}"));

        var isEligible = rules.All(r => r.Passed);
        var summary = isEligible
            ? "Eligible — B2B/contractor remote work from Ukraine is possible"
            : $"Ineligible — {rules.Count(r => !r.Passed)} rule(s) failed";

        return new EligibilityAssessment(vacancy, isEligible, summary, rules);
    }
}

// ─────────────────────────────────────────────────────────────────────────
//  Eligibility assessment records
// ─────────────────────────────────────────────────────────────────────────

public sealed record EligibilityAssessment(
    JobVacancy Vacancy,
    bool IsEligible,
    string Summary,
    IReadOnlyList<EligibilityRule> Rules);

public sealed record EligibilityRule(
    string RuleName,
    bool Passed,
    string Reason);

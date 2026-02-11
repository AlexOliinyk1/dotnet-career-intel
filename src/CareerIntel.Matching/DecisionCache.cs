using System.Text.Json;
using CareerIntel.Core.Models;

namespace CareerIntel.Matching;

/// <summary>
/// Centralized cache for ApplicationDecision verdicts.
/// SINGLE SOURCE OF TRUTH: All commands must check this cache before allowing actions.
/// </summary>
public static class DecisionCache
{
    private static readonly Dictionary<string, ApplicationDecision> _cache = new();
    private static readonly string _cachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "projects", "decision-cache.json");

    /// <summary>
    /// Get verdict for a vacancy. Returns null if not yet decided.
    /// </summary>
    public static ApplicationDecision? GetDecision(string vacancyId)
    {
        LoadCache();
        return _cache.TryGetValue(vacancyId, out var decision) ? decision : null;
    }

    /// <summary>
    /// Store decision for a vacancy.
    /// </summary>
    public static void SetDecision(string vacancyId, ApplicationDecision decision)
    {
        LoadCache();
        _cache[vacancyId] = decision;
        SaveCache();
    }

    /// <summary>
    /// ENFORCED RULE: Can only apply if verdict is APPLY_NOW.
    /// </summary>
    public static bool CanApply(string vacancyId, out string reason)
    {
        var decision = GetDecision(vacancyId);

        if (decision == null)
        {
            reason = "❌ BLOCKED: Run 'decide' first before applying";
            return false;
        }

        if (decision.Verdict != ApplicationVerdict.ApplyNow)
        {
            reason = decision.Verdict switch
            {
                ApplicationVerdict.LearnThenApply =>
                    $"❌ BLOCKED: Verdict is LEARN_THEN_APPLY ({decision.EstimatedLearningHours}h needed). Learn first!",
                ApplicationVerdict.Skip =>
                    $"❌ BLOCKED: Verdict is SKIP. Don't waste time on this position.",
                _ => "❌ BLOCKED: Unknown verdict"
            };
            return false;
        }

        reason = "✓ ALLOWED: Verdict is APPLY_NOW";
        return true;
    }

    /// <summary>
    /// ENFORCED RULE: Can only learn if verdict is LEARN_THEN_APPLY.
    /// </summary>
    public static bool CanLearn(string vacancyId, out string reason)
    {
        var decision = GetDecision(vacancyId);

        if (decision == null)
        {
            reason = "❌ BLOCKED: Run 'decide' first to get learning plan";
            return false;
        }

        if (decision.Verdict != ApplicationVerdict.LearnThenApply)
        {
            reason = decision.Verdict switch
            {
                ApplicationVerdict.ApplyNow =>
                    $"❌ BLOCKED: You're already ready ({decision.ReadinessScore}%). APPLY NOW, don't over-prepare!",
                ApplicationVerdict.Skip =>
                    $"❌ BLOCKED: Verdict is SKIP. Don't waste learning time on this position.",
                _ => "❌ BLOCKED: Unknown verdict"
            };
            return false;
        }

        reason = $"✓ ALLOWED: {decision.EstimatedLearningHours}h of learning needed";
        return true;
    }

    /// <summary>
    /// Get all decisions for dashboard display.
    /// </summary>
    public static Dictionary<string, ApplicationDecision> GetAllDecisions()
    {
        LoadCache();
        return new Dictionary<string, ApplicationDecision>(_cache);
    }

    /// <summary>
    /// Clear all decisions (for testing or reset).
    /// </summary>
    public static void Clear()
    {
        _cache.Clear();
        SaveCache();
    }

    private static void LoadCache()
    {
        if (!File.Exists(_cachePath))
            return;

        try
        {
            var json = File.ReadAllText(_cachePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, ApplicationDecision>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            _cache.Clear();
            foreach (var kvp in loaded)
            {
                _cache[kvp.Key] = kvp.Value;
            }
        }
        catch
        {
            // Ignore load errors, start fresh
        }
    }

    private static void SaveCache()
    {
        try
        {
            var dir = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_cachePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }
}

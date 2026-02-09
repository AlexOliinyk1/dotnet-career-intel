namespace CareerIntel.Core.Models;

public sealed class ResumeSimulation
{
    public AtsScore AtsScore { get; set; } = new();
    public RecruiterScore RecruiterScore { get; set; } = new();
    public TechLeadScore TechLeadScore { get; set; } = new();
    public double OverallConversionProbability { get; set; }
    public List<string> CriticalIssues { get; set; } = [];
    public List<string> Recommendations { get; set; } = [];
}

public sealed class AtsScore
{
    public double Score { get; set; } // 0-100
    public double KeywordMatchPercent { get; set; }
    public List<string> MissingKeywords { get; set; } = [];
    public List<string> FormatIssues { get; set; } = [];
    public bool PassesFilter { get; set; }
}

public sealed class RecruiterScore
{
    public double Score { get; set; } // 0-100
    public List<SectionHeatmap> Heatmap { get; set; } = [];
    public double ReadTimeSeconds { get; set; }
    public string FirstImpression { get; set; } = string.Empty;
    public List<string> Concerns { get; set; } = [];
}

public sealed class SectionHeatmap
{
    public string Section { get; set; } = string.Empty;
    public string Attention { get; set; } = "Low"; // "High", "Medium", "Low", "Skipped"
    public string Recommendation { get; set; } = string.Empty;
}

public sealed class TechLeadScore
{
    public double Score { get; set; } // 0-100
    public double DepthPercent { get; set; }
    public List<string> ImpressivePoints { get; set; } = [];
    public List<string> RedFlags { get; set; } = [];
    public List<string> QuestionsWouldAsk { get; set; } = [];
}

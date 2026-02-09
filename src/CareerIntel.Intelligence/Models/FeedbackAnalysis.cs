using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence.Models;

public sealed class FeedbackAnalysis
{
    public List<(string Skill, double PriorityBoost)> PriorityAdjustments { get; set; } = [];
    public List<PrepAction> NewPrepTasks { get; set; } = [];
    public List<string> RepeatingWeaknesses { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
}

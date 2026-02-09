namespace CareerIntel.Intelligence.Models;

public sealed class InterviewInsights
{
    public Dictionary<string, double> PassRateByRound { get; set; } = new();
    public List<(string Reason, int Count)> TopRejectionReasons { get; set; } = [];
    public List<(string Area, int Count)> RepeatingWeakAreas { get; set; } = [];
    public double OverallPassRate { get; set; }
    public string Trend { get; set; } = string.Empty; // "Improving", "Stable", "Declining"
    public int TotalInterviews { get; set; }
}

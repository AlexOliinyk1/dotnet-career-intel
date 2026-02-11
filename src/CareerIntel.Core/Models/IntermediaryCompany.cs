namespace CareerIntel.Core.Models;

public sealed class IntermediaryCompany
{
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public string Domain { get; set; } = string.Empty;
    public string Type { get; set; } = "Outsource";
    public string Region { get; set; } = string.Empty;
    public double EstimatedMarkup { get; set; } = 0.35;
    public string? CareersUrl { get; set; }
    public string Notes { get; set; } = string.Empty;
}

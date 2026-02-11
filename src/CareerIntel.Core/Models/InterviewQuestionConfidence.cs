namespace CareerIntel.Core.Models;

/// <summary>
/// Tracks confidence level (0-100%) for specific interview questions.
/// Enables surgical preparation: "Question X: 30% → needs 2h practice"
/// </summary>
public sealed class InterviewQuestionConfidence
{
    public string QuestionId { get; set; } = Guid.NewGuid().ToString();
    public string Question { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // Technical, Behavioral, System Design, etc.
    public int ConfidenceLevel { get; set; } // 0-100%
    public int TimesAsked { get; set; } // How many times you've been asked this
    public int TimesAnsweredWell { get; set; } // How many times you answered well
    public DateTimeOffset LastPracticed { get; set; }
    public int HoursSpentPracticing { get; set; }
    public List<string> RelatedSkills { get; set; } = [];
    public List<string> RelatedConcepts { get; set; } = [];
    public string YourAnswer { get; set; } = string.Empty; // Your prepared answer
    public List<string> ImprovementAreas { get; set; } = [];
    public int EstimatedHoursToMaster { get; set; } // Hours needed to reach 90%+ confidence
}

/// <summary>
/// Tracks question-level performance for an interview.
/// </summary>
public sealed class QuestionPerformance
{
    public string Question { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int ConfidenceBefore { get; set; } // 0-100%
    public int PerformanceDuring { get; set; } // 0-100% (how well you actually did)
    public string WhatWentWrong { get; set; } = string.Empty;
    public List<string> MissingConcepts { get; set; } = []; // Concepts you didn't know
    public List<string> MissingSkills { get; set; } = []; // Skills you lacked
}

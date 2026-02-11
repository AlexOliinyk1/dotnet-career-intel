namespace CareerIntel.Core.Models;

/// <summary>
/// A real interview question scraped from various sources.
/// Used to build company-specific and role-specific question databases.
/// </summary>
public sealed class InterviewQuestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Question { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // "Senior .NET Developer", "Software Engineer", etc.
    public InterviewRound Round { get; set; } // Which interview round
    public QuestionCategory Category { get; set; }
    public DifficultyLevel Difficulty { get; set; }

    // Source tracking
    public string Source { get; set; } = string.Empty; // "Glassdoor", "LeetCode", "Blind", "Reddit"
    public string SourceUrl { get; set; } = string.Empty;
    public DateTimeOffset ScrapedDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset InterviewDate { get; set; } // When the interview happened

    // Question details
    public string ExpectedAnswer { get; set; } = string.Empty; // If available
    public List<string> KeyConcepts { get; set; } = []; // "SOLID", "Dependency Injection", etc.
    public List<string> RequiredSkills { get; set; } = []; // "C#", "System Design", etc.
    public int EstimatedMinutes { get; set; } // Time to answer

    // Community insights
    public int Upvotes { get; set; } // Community relevance score
    public int TimesAsked { get; set; } // How often this question appears
    public string InterviewerTips { get; set; } = string.Empty; // "Focus on scalability", etc.

    // Metadata
    public bool IsVerified { get; set; } // Community verified as accurate
    public List<string> Tags { get; set; } = []; // "tricky", "common", "whiteboard", etc.
}

/// <summary>
/// Interview round where question was asked.
/// </summary>
public enum InterviewRound
{
    Recruiter,
    Phone,
    Technical,
    Coding,
    SystemDesign,
    Behavioral,
    OnSite,
    Final,
    Unknown
}

/// <summary>
/// Category of interview question.
/// </summary>
public enum QuestionCategory
{
    Coding,           // LeetCode-style coding problems
    SystemDesign,     // Architecture and scalability questions
    Behavioral,       // STAR framework questions
    Technical,        // Technical knowledge (async/await, GC, etc.)
    CSharp,          // C#-specific questions
    DotNet,          // .NET framework questions
    Database,        // SQL, optimization, indexing
    Architecture,    // Design patterns, SOLID, DDD
    Troubleshooting, // Debug scenarios
    ProjectExperience // "Tell me about a time..."
}

/// <summary>
/// Difficulty level of question.
/// </summary>
public enum DifficultyLevel
{
    Easy = 1,
    Medium = 2,
    Hard = 3,
    Expert = 4
}

/// <summary>
/// A collection of interview questions for a specific company/role.
/// </summary>
public sealed class InterviewQuestionSet
{
    public string Company { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<InterviewQuestion> Questions { get; set; } = [];
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

    // Statistics
    public int TotalQuestions => Questions.Count;
    public Dictionary<QuestionCategory, int> QuestionsByCategory { get; set; } = new();
    public Dictionary<DifficultyLevel, int> QuestionsByDifficulty { get; set; } = new();
}

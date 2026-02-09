namespace CareerIntel.Core.Models;

/// <summary>
/// A complete interview preparation plan for a specific vacancy,
/// containing inferred questions grouped by skill and round type.
/// </summary>
public sealed class InterviewPrepPlan
{
    public string VacancyId { get; set; } = string.Empty;
    public string VacancyTitle { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public List<SkillQuestionSet> SkillSets { get; set; } = [];
    public List<InferredQuestion> BehavioralQuestions { get; set; } = [];
    public List<InferredQuestion> SystemDesignQuestions { get; set; } = [];
    public List<string> RedFlags { get; set; } = [];
    public string OverallStrategy { get; set; } = string.Empty;
    public int EstimatedPrepHours { get; set; }
    public DateTimeOffset GeneratedDate { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A group of inferred interview questions for a single skill.
/// </summary>
public sealed class SkillQuestionSet
{
    public string SkillName { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public int ExpectedDepth { get; set; } // 1-5: 1=awareness, 2=usage, 3=internals, 4=tradeoffs, 5=expert design
    public string DepthLabel { get; set; } = string.Empty; // human-readable depth description
    public List<InferredQuestion> Questions { get; set; } = [];
    public List<string> RedFlags { get; set; } = []; // things that would signal "no-hire" on this skill
    public string StrongAnswerSummary { get; set; } = string.Empty; // what a strong answer sounds like
}

/// <summary>
/// A single inferred interview question with archetype classification.
/// </summary>
public sealed class InferredQuestion
{
    public string Question { get; set; } = string.Empty;
    public QuestionArchetype Archetype { get; set; }
    public string ArchetypeLabel { get; set; } = string.Empty;
    public int Difficulty { get; set; } // 1-5
    public string WhyAsked { get; set; } = string.Empty;
    public string StrongAnswer { get; set; } = string.Empty;
    public string WeakAnswer { get; set; } = string.Empty;
}

/// <summary>
/// Categories of interview question patterns.
/// </summary>
public enum QuestionArchetype
{
    /// <summary>Why did you choose X over alternatives? (decision reasoning)</summary>
    WhyChoice,

    /// <summary>How does X work under the hood? (internal knowledge)</summary>
    HowItWorks,

    /// <summary>How would you optimize X for performance? (performance engineering)</summary>
    Performance,

    /// <summary>What happens when X fails? How do you handle it? (failure modes)</summary>
    FailureModes,

    /// <summary>Design a system that does X (architecture / system design)</summary>
    SystemDesign,

    /// <summary>Compare X vs Y — when would you use each? (tradeoff analysis)</summary>
    Tradeoffs,

    /// <summary>Walk me through how you'd debug X (diagnostic thinking)</summary>
    Debugging,

    /// <summary>Tell me about a time when... (behavioral / STAR format)</summary>
    Behavioral,

    /// <summary>Implement X / write code that... (live coding)</summary>
    LiveCoding,

    /// <summary>What are the security implications of X? (security awareness)</summary>
    Security
}

/// <summary>
/// Interview-focused learning item with effort classification and impact ranking.
/// </summary>
public sealed class InterviewLearningItem
{
    public string SkillName { get; set; } = string.Empty;
    public LearningEffort Effort { get; set; }
    public string EffortLabel { get; set; } = string.Empty;
    public int EstimatedHours { get; set; }
    public double InterviewImpactScore { get; set; } // 0-100: how much this affects pass probability
    public string FocusArea { get; set; } = string.Empty; // "internals", "tradeoffs", "failure modes", etc.
    public List<string> PrepActions { get; set; } = []; // concrete study actions
    public bool SkipRecommended { get; set; } // true if low-ROI mastery
    public string SkipReason { get; set; } = string.Empty;
}

/// <summary>
/// Effort classification for interview learning gaps.
/// </summary>
public enum LearningEffort
{
    /// <summary>1-4 hours: quick review, refresh existing knowledge</summary>
    Fast,

    /// <summary>5-15 hours: practice exercises, learn new patterns</summary>
    Medium,

    /// <summary>16+ hours: deep study, build projects, fundamentals work</summary>
    Deep
}

/// <summary>
/// Complete interview-focused learning plan ordered by interview impact.
/// </summary>
public sealed class InterviewLearningPlan
{
    public string VacancyId { get; set; } = string.Empty;
    public List<InterviewLearningItem> Items { get; set; } = [];
    public int TotalEstimatedHours { get; set; }
    public int CriticalGapCount { get; set; }
    public int SkippedLowROICount { get; set; }
    public double EstimatedPassProbability { get; set; } // 0-100
    public string Verdict { get; set; } = string.Empty; // "Ready", "Prep needed (X hours)", "Consider skipping"
    public DateTimeOffset GeneratedDate { get; set; } = DateTimeOffset.UtcNow;
}

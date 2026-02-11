using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence;

/// <summary>
/// Tracks and manages per-question interview confidence (0-100%).
/// Enables surgical prep: "Question X: 30% confident → needs 2h practice"
/// </summary>
public sealed class QuestionConfidenceTracker
{
    private readonly Dictionary<string, InterviewQuestionConfidence> _questions = new();

    /// <summary>
    /// Initialize tracker with common interview questions for a role/company.
    /// </summary>
    public void LoadQuestionsForRole(string role, string? company = null)
    {
        var questions = GetCommonQuestionsForRole(role, company);
        foreach (var q in questions)
        {
            _questions[q.QuestionId] = q;
        }
    }

    /// <summary>
    /// Get all questions sorted by confidence (lowest first = highest priority).
    /// </summary>
    public List<InterviewQuestionConfidence> GetQuestionsByPriority()
    {
        return _questions.Values
            .OrderBy(q => q.ConfidenceLevel)
            .ThenByDescending(q => q.TimesAsked)
            .ToList();
    }

    /// <summary>
    /// Get questions below target confidence threshold.
    /// </summary>
    public List<InterviewQuestionConfidence> GetQuestionsNeedingWork(int threshold = 75)
    {
        return _questions.Values
            .Where(q => q.ConfidenceLevel < threshold)
            .OrderBy(q => q.ConfidenceLevel)
            .ToList();
    }

    /// <summary>
    /// Update confidence after practice session.
    /// </summary>
    public void RecordPractice(string questionId, int newConfidence, int hoursSpent)
    {
        if (_questions.TryGetValue(questionId, out var question))
        {
            question.ConfidenceLevel = Math.Clamp(newConfidence, 0, 100);
            question.LastPracticed = DateTimeOffset.UtcNow;
            question.HoursSpentPracticing += hoursSpent;

            // Recalculate hours to master
            question.EstimatedHoursToMaster = Math.Max(0,
                (int)Math.Ceiling((90 - question.ConfidenceLevel) / 10.0) * 2);
        }
    }

    /// <summary>
    /// Record actual interview performance to learn from failures.
    /// </summary>
    public void RecordInterviewPerformance(string questionId, QuestionPerformance performance)
    {
        if (_questions.TryGetValue(questionId, out var question))
        {
            question.TimesAsked++;

            if (performance.PerformanceDuring >= 70)
            {
                question.TimesAnsweredWell++;
                // Boost confidence based on real success
                question.ConfidenceLevel = Math.Min(100, question.ConfidenceLevel + 15);
            }
            else
            {
                // Lower confidence if you struggled
                question.ConfidenceLevel = Math.Max(0, question.ConfidenceLevel - 10);

                // Extract learning from failure
                question.ImprovementAreas.AddRange(performance.MissingConcepts);
                question.RelatedSkills.AddRange(performance.MissingSkills);
            }
        }
        else
        {
            // New question encountered in real interview - add it
            var newQuestion = new InterviewQuestionConfidence
            {
                QuestionId = questionId,
                Question = performance.Question,
                Category = performance.Category,
                ConfidenceLevel = performance.PerformanceDuring,
                TimesAsked = 1,
                TimesAnsweredWell = performance.PerformanceDuring >= 70 ? 1 : 0,
                LastPracticed = DateTimeOffset.UtcNow,
                RelatedSkills = performance.MissingSkills,
                ImprovementAreas = performance.MissingConcepts,
                EstimatedHoursToMaster = (int)Math.Ceiling((90 - performance.PerformanceDuring) / 10.0) * 2
            };

            _questions[questionId] = newQuestion;
        }
    }

    /// <summary>
    /// Calculate overall readiness percentage across all questions.
    /// </summary>
    public int CalculateOverallReadiness()
    {
        if (_questions.Count == 0)
            return 0;

        return (int)_questions.Values.Average(q => q.ConfidenceLevel);
    }

    /// <summary>
    /// Get total hours needed to reach target readiness.
    /// </summary>
    public int GetTotalHoursToReady(int targetConfidence = 75)
    {
        return _questions.Values
            .Where(q => q.ConfidenceLevel < targetConfidence)
            .Sum(q => q.EstimatedHoursToMaster);
    }

    /// <summary>
    /// Get top N priority questions to practice now.
    /// </summary>
    public List<InterviewQuestionConfidence> GetTopPriorities(int count = 5)
    {
        return _questions.Values
            .OrderBy(q => q.ConfidenceLevel)
            .ThenByDescending(q => q.TimesAsked) // Prioritize questions that come up often
            .Take(count)
            .ToList();
    }

    private static List<InterviewQuestionConfidence> GetCommonQuestionsForRole(string role, string? company)
    {
        var questions = new List<InterviewQuestionConfidence>();

        // Technical questions (common for all roles)
        questions.AddRange(new[]
        {
            new InterviewQuestionConfidence
            {
                Question = "Explain SOLID principles with examples",
                Category = "Technical - Architecture",
                ConfidenceLevel = 0,
                RelatedConcepts = ["Single Responsibility", "Open/Closed", "Liskov Substitution", "Interface Segregation", "Dependency Inversion"],
                RelatedSkills = ["Object-Oriented Design", "Software Architecture"],
                EstimatedHoursToMaster = 6
            },
            new InterviewQuestionConfidence
            {
                Question = "Difference between IEnumerable, ICollection, IList, and IQueryable",
                Category = "Technical - .NET",
                ConfidenceLevel = 0,
                RelatedConcepts = ["Deferred Execution", "LINQ", "Collections"],
                RelatedSkills = ["C#", ".NET"],
                EstimatedHoursToMaster = 4
            },
            new InterviewQuestionConfidence
            {
                Question = "Explain async/await and when to use it",
                Category = "Technical - .NET",
                ConfidenceLevel = 0,
                RelatedConcepts = ["Asynchronous Programming", "Task Parallel Library", "Thread Pool"],
                RelatedSkills = ["C#", "Async Programming"],
                EstimatedHoursToMaster = 8
            },
            new InterviewQuestionConfidence
            {
                Question = "How does garbage collection work in .NET?",
                Category = "Technical - .NET",
                ConfidenceLevel = 0,
                RelatedConcepts = ["Generational GC", "Heap", "Stack", "Memory Management"],
                RelatedSkills = [".NET", "Performance Optimization"],
                EstimatedHoursToMaster = 6
            },
            new InterviewQuestionConfidence
            {
                Question = "Design a URL shortener (System Design)",
                Category = "System Design",
                ConfidenceLevel = 0,
                RelatedConcepts = ["Hashing", "Database Design", "Scalability", "Caching"],
                RelatedSkills = ["System Design", "Distributed Systems"],
                EstimatedHoursToMaster = 10
            },
            new InterviewQuestionConfidence
            {
                Question = "Design a rate limiter",
                Category = "System Design",
                ConfidenceLevel = 0,
                RelatedConcepts = ["Token Bucket", "Sliding Window", "Redis", "Distributed Systems"],
                RelatedSkills = ["System Design", "Caching"],
                EstimatedHoursToMaster = 8
            },
            new InterviewQuestionConfidence
            {
                Question = "Explain microservices vs monolith trade-offs",
                Category = "System Design",
                ConfidenceLevel = 0,
                RelatedConcepts = ["Service Boundaries", "Eventual Consistency", "Distributed Transactions", "CAP Theorem"],
                RelatedSkills = ["Microservices", "Architecture"],
                EstimatedHoursToMaster = 6
            }
        });

        // Behavioral questions
        questions.AddRange(new[]
        {
            new InterviewQuestionConfidence
            {
                Question = "Tell me about a time you disagreed with a technical decision",
                Category = "Behavioral",
                ConfidenceLevel = 0,
                RelatedConcepts = ["Conflict Resolution", "Leadership", "Communication"],
                EstimatedHoursToMaster = 2
            },
            new InterviewQuestionConfidence
            {
                Question = "Describe a challenging bug you solved",
                Category = "Behavioral",
                ConfidenceLevel = 0,
                RelatedConcepts = ["Debugging", "Problem Solving", "Root Cause Analysis"],
                EstimatedHoursToMaster = 2
            },
            new InterviewQuestionConfidence
            {
                Question = "Tell me about a time you had to learn a new technology quickly",
                Category = "Behavioral",
                ConfidenceLevel = 0,
                RelatedConcepts = ["Learning Agility", "Adaptability"],
                EstimatedHoursToMaster = 2
            }
        });

        // Company-specific adjustments
        if (company?.Contains("Google", StringComparison.OrdinalIgnoreCase) == true ||
            company?.Contains("Meta", StringComparison.OrdinalIgnoreCase) == true ||
            company?.Contains("Amazon", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Big Tech emphasizes algorithms
            questions.Add(new InterviewQuestionConfidence
            {
                Question = "Implement LRU Cache (LeetCode 146)",
                Category = "Algorithms",
                ConfidenceLevel = 0,
                RelatedConcepts = ["Hash Map", "Doubly Linked List", "O(1) Operations"],
                RelatedSkills = ["Data Structures", "Algorithms"],
                EstimatedHoursToMaster = 8
            });
        }

        return questions;
    }

    public Dictionary<string, InterviewQuestionConfidence> GetAllQuestions() => _questions;

    public void LoadQuestions(IEnumerable<InterviewQuestionConfidence> questions)
    {
        foreach (var q in questions)
        {
            _questions[q.QuestionId] = q;
        }
    }
}

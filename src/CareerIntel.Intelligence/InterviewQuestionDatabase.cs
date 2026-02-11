using System.Text.Json;
using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence;

/// <summary>
/// Database for storing and querying scraped interview questions.
/// Enables company-specific and role-specific interview preparation.
/// </summary>
public sealed class InterviewQuestionDatabase
{
    private readonly string _databasePath;
    private readonly Dictionary<string, InterviewQuestionSet> _questionSets = new();

    public InterviewQuestionDatabase(string databasePath)
    {
        _databasePath = databasePath;
        LoadDatabase();
    }

    /// <summary>
    /// Add questions to the database.
    /// </summary>
    public void AddQuestions(List<InterviewQuestion> questions)
    {
        foreach (var question in questions)
        {
            var key = GetKey(question.Company, question.Role);

            if (!_questionSets.TryGetValue(key, out var questionSet))
            {
                questionSet = new InterviewQuestionSet
                {
                    Company = question.Company,
                    Role = question.Role
                };
                _questionSets[key] = questionSet;
            }

            // Deduplicate by question text
            if (!questionSet.Questions.Any(q => q.Question == question.Question))
            {
                questionSet.Questions.Add(question);
            }

            questionSet.LastUpdated = DateTimeOffset.UtcNow;
        }

        UpdateStatistics();
        SaveDatabase();
    }

    /// <summary>
    /// Get questions for a specific company.
    /// </summary>
    public List<InterviewQuestion> GetCompanyQuestions(string company)
    {
        return _questionSets
            .Where(kv => kv.Value.Company.Equals(company, StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value.Questions)
            .ToList();
    }

    /// <summary>
    /// Get questions for a specific role.
    /// </summary>
    public List<InterviewQuestion> GetRoleQuestions(string role)
    {
        return _questionSets
            .Where(kv => kv.Value.Role.Equals(role, StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value.Questions)
            .ToList();
    }

    /// <summary>
    /// Get questions for a company + role combination.
    /// </summary>
    public List<InterviewQuestion> GetQuestions(string company, string role)
    {
        var key = GetKey(company, role);

        if (_questionSets.TryGetValue(key, out var questionSet))
        {
            return questionSet.Questions;
        }

        return [];
    }

    /// <summary>
    /// Get questions by category.
    /// </summary>
    public List<InterviewQuestion> GetQuestionsByCategory(string company, string role, QuestionCategory category)
    {
        return GetQuestions(company, role)
            .Where(q => q.Category == category)
            .ToList();
    }

    /// <summary>
    /// Get questions by difficulty.
    /// </summary>
    public List<InterviewQuestion> GetQuestionsByDifficulty(string company, string role, DifficultyLevel difficulty)
    {
        return GetQuestions(company, role)
            .Where(q => q.Difficulty == difficulty)
            .ToList();
    }

    /// <summary>
    /// Get top questions by upvotes/popularity.
    /// </summary>
    public List<InterviewQuestion> GetTopQuestions(string company, string role, int count = 10)
    {
        return GetQuestions(company, role)
            .OrderByDescending(q => q.Upvotes)
            .ThenByDescending(q => q.TimesAsked)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Get questions for practice based on user's weak areas.
    /// </summary>
    public List<InterviewQuestion> GetPracticeQuestions(
        string company,
        string role,
        List<string> weakConcepts,
        DifficultyLevel maxDifficulty = DifficultyLevel.Hard,
        int count = 20)
    {
        var questions = GetQuestions(company, role)
            .Where(q => q.Difficulty <= maxDifficulty)
            .Where(q => q.KeyConcepts.Any(c => weakConcepts.Contains(c, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(q => q.Difficulty)
            .ThenByDescending(q => q.TimesAsked)
            .Take(count)
            .ToList();

        // If not enough questions with weak concepts, add general questions
        if (questions.Count < count)
        {
            var additionalQuestions = GetQuestions(company, role)
                .Where(q => q.Difficulty <= maxDifficulty)
                .Where(q => !questions.Contains(q))
                .OrderByDescending(q => q.TimesAsked)
                .Take(count - questions.Count);

            questions.AddRange(additionalQuestions);
        }

        return questions;
    }

    /// <summary>
    /// Generate a study plan with progressive difficulty.
    /// </summary>
    public List<InterviewQuestion> GenerateStudyPlan(
        string company,
        string role,
        int totalQuestions = 50)
    {
        var allQuestions = GetQuestions(company, role);

        // 30% Easy, 50% Medium, 20% Hard
        var easy = allQuestions.Where(q => q.Difficulty == DifficultyLevel.Easy)
            .OrderByDescending(q => q.TimesAsked)
            .Take((int)(totalQuestions * 0.3));

        var medium = allQuestions.Where(q => q.Difficulty == DifficultyLevel.Medium)
            .OrderByDescending(q => q.TimesAsked)
            .Take((int)(totalQuestions * 0.5));

        var hard = allQuestions.Where(q => q.Difficulty == DifficultyLevel.Hard)
            .OrderByDescending(q => q.TimesAsked)
            .Take((int)(totalQuestions * 0.2));

        return easy.Concat(medium).Concat(hard).ToList();
    }

    /// <summary>
    /// Get statistics about the question database.
    /// </summary>
    public QuestionDatabaseStats GetStatistics()
    {
        var allQuestions = _questionSets.SelectMany(kv => kv.Value.Questions).ToList();

        return new QuestionDatabaseStats
        {
            TotalQuestions = allQuestions.Count,
            TotalCompanies = _questionSets.Select(kv => kv.Value.Company).Distinct().Count(),
            TotalRoles = _questionSets.Select(kv => kv.Value.Role).Distinct().Count(),
            QuestionsByCategory = allQuestions
                .GroupBy(q => q.Category)
                .ToDictionary(g => g.Key, g => g.Count()),
            QuestionsByDifficulty = allQuestions
                .GroupBy(q => q.Difficulty)
                .ToDictionary(g => g.Key, g => g.Count()),
            QuestionsBySource = allQuestions
                .GroupBy(q => q.Source)
                .ToDictionary(g => g.Key, g => g.Count()),
            LastUpdated = _questionSets.Values.Any() ? _questionSets.Values.Max(s => s.LastUpdated) : DateTimeOffset.MinValue
        };
    }

    private static string GetKey(string company, string role)
    {
        return $"{company}|{role}".ToLowerInvariant();
    }

    private void UpdateStatistics()
    {
        foreach (var questionSet in _questionSets.Values)
        {
            questionSet.QuestionsByCategory = questionSet.Questions
                .GroupBy(q => q.Category)
                .ToDictionary(g => g.Key, g => g.Count());

            questionSet.QuestionsByDifficulty = questionSet.Questions
                .GroupBy(q => q.Difficulty)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }

    private void LoadDatabase()
    {
        if (!File.Exists(_databasePath))
            return;

        try
        {
            var json = File.ReadAllText(_databasePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, InterviewQuestionSet>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            _questionSets.Clear();
            foreach (var kvp in loaded)
            {
                _questionSets[kvp.Key] = kvp.Value;
            }
        }
        catch
        {
            // Ignore load errors, start fresh
        }
    }

    private void SaveDatabase()
    {
        try
        {
            var dir = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_questionSets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_databasePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }
}

/// <summary>
/// Statistics about the interview question database.
/// </summary>
public sealed class QuestionDatabaseStats
{
    public int TotalQuestions { get; set; }
    public int TotalCompanies { get; set; }
    public int TotalRoles { get; set; }
    public Dictionary<QuestionCategory, int> QuestionsByCategory { get; set; } = new();
    public Dictionary<DifficultyLevel, int> QuestionsByDifficulty { get; set; } = new();
    public Dictionary<string, int> QuestionsBySource { get; set; } = new();
    public DateTimeOffset LastUpdated { get; set; }
}

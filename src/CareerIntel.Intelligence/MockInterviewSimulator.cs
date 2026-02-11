using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence;

/// <summary>
/// Mock interview simulator (Phase 6). Interactive question/answer practice
/// with scoring, hints, and feedback. Uses questions from InterviewTopicBank
/// and interview question databases.
/// </summary>
public sealed class MockInterviewSimulator
{
    private readonly Random _random = new();

    /// <summary>
    /// Generate a mock interview session with questions for the specified topics.
    /// </summary>
    public MockInterviewSession CreateSession(
        MockInterviewConfig config)
    {
        var session = new MockInterviewSession
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            StartedAt = DateTimeOffset.UtcNow,
            Config = config
        };

        var allTopics = InterviewTopicBank.GetAllTopics();
        var targetTopics = config.Topics.Count > 0
            ? allTopics.Where(t => config.Topics.Any(ct =>
                t.Name.Contains(ct, StringComparison.OrdinalIgnoreCase) ||
                t.Id.Contains(ct, StringComparison.OrdinalIgnoreCase)))
                .ToList()
            : allTopics.ToList();

        if (targetTopics.Count == 0)
            targetTopics = allTopics.ToList();

        // Pick questions matching difficulty
        var questions = new List<MockQuestion>();

        foreach (var topic in targetTopics)
        {
            var topicQuestions = topic.Questions
                .Where(q => config.Difficulty == "all" ||
                    q.Difficulty.Equals(config.Difficulty, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (topicQuestions.Count == 0)
                continue;

            // Randomly pick questions from this topic
            var count = Math.Min(config.QuestionsPerTopic, topicQuestions.Count);
            var picked = topicQuestions.OrderBy(_ => _random.Next()).Take(count);

            foreach (var q in picked)
            {
                questions.Add(new MockQuestion
                {
                    Topic = topic.Name,
                    Question = q.Question,
                    ExpectedAnswer = q.ExpectedAnswer,
                    Difficulty = q.Difficulty,
                    Tags = q.Tags.ToList(),
                    Hints = GenerateHints(q.ExpectedAnswer)
                });
            }
        }

        // Limit total questions
        session.Questions = questions
            .OrderBy(_ => _random.Next())
            .Take(config.TotalQuestions)
            .ToList();

        return session;
    }

    /// <summary>
    /// Score a user's answer against the expected answer.
    /// Returns a score 0-100 with feedback.
    /// </summary>
    public AnswerScore ScoreAnswer(MockQuestion question, string userAnswer)
    {
        if (string.IsNullOrWhiteSpace(userAnswer))
        {
            return new AnswerScore
            {
                Score = 0,
                Feedback = "No answer provided.",
                MissedKeyPoints = ExtractKeyPoints(question.ExpectedAnswer)
            };
        }

        var expectedKeyPoints = ExtractKeyPoints(question.ExpectedAnswer);
        var matchedPoints = new List<string>();
        var missedPoints = new List<string>();

        var answerLower = userAnswer.ToLowerInvariant();

        foreach (var point in expectedKeyPoints)
        {
            // Check if the key point concept is mentioned in the answer
            var pointWords = point.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var significantWords = pointWords.Where(w => w.Length > 3).ToList();

            var matchCount = significantWords.Count(w => answerLower.Contains(w));
            var matchRatio = significantWords.Count > 0 ? (double)matchCount / significantWords.Count : 0;

            if (matchRatio >= 0.4)
                matchedPoints.Add(point);
            else
                missedPoints.Add(point);
        }

        var score = expectedKeyPoints.Count > 0
            ? (int)((double)matchedPoints.Count / expectedKeyPoints.Count * 100)
            : 50;

        // Bonus for length/depth
        if (userAnswer.Length > 200 && score > 0)
            score = Math.Min(100, score + 10);

        var feedback = score switch
        {
            >= 80 => "Excellent answer! You covered the key points well.",
            >= 60 => "Good answer, but you missed some important concepts.",
            >= 40 => "Partial answer — significant gaps in coverage.",
            >= 20 => "Weak answer — review this topic thoroughly.",
            _ => "Very incomplete — study this topic before your next interview."
        };

        return new AnswerScore
        {
            Score = score,
            Feedback = feedback,
            MatchedKeyPoints = matchedPoints,
            MissedKeyPoints = missedPoints
        };
    }

    /// <summary>
    /// Score a complete session and return a report.
    /// </summary>
    public MockInterviewReport ScoreSession(MockInterviewSession session)
    {
        var report = new MockInterviewReport
        {
            SessionId = session.Id,
            TotalQuestions = session.Questions.Count,
            AnsweredQuestions = session.Questions.Count(q => q.UserAnswer != null),
            CompletedAt = DateTimeOffset.UtcNow
        };

        var topicScores = new Dictionary<string, List<int>>();

        foreach (var question in session.Questions)
        {
            if (question.UserAnswer == null)
                continue;

            var score = ScoreAnswer(question, question.UserAnswer);
            question.Score = score;

            if (!topicScores.ContainsKey(question.Topic))
                topicScores[question.Topic] = [];

            topicScores[question.Topic].Add(score.Score);
        }

        report.AverageScore = topicScores.Values.SelectMany(s => s).DefaultIfEmpty(0).Average();

        report.TopicScores = topicScores.Select(kv => new TopicScore
        {
            Topic = kv.Key,
            AverageScore = kv.Value.Average(),
            QuestionCount = kv.Value.Count
        })
        .OrderBy(t => t.AverageScore)
        .ToList();

        // Generate verdict
        report.Verdict = report.AverageScore switch
        {
            >= 80 => "READY — You're well-prepared for this interview level.",
            >= 60 => "ALMOST READY — Review weak areas before interviewing.",
            >= 40 => "NEEDS WORK — Dedicate more study time before applying.",
            _ => "NOT READY — Significant gaps. Focus on fundamentals first."
        };

        // Generate improvement areas
        report.ImprovementAreas = report.TopicScores
            .Where(t => t.AverageScore < 60)
            .Select(t => $"{t.Topic}: {t.AverageScore:F0}% — needs more practice")
            .ToList();

        return report;
    }

    private static List<string> GenerateHints(string expectedAnswer)
    {
        var hints = new List<string>();
        var sentences = expectedAnswer.Split(new[] { '.', '!' }, StringSplitOptions.RemoveEmptyEntries);

        if (sentences.Length > 0)
            hints.Add($"Think about: {sentences[0].Trim().Split(' ').Take(5).Aggregate((a, b) => $"{a} {b}")}...");

        if (sentences.Length > 2)
            hints.Add($"Consider also: {sentences[2].Trim().Split(' ').Take(4).Aggregate((a, b) => $"{a} {b}")}...");

        return hints;
    }

    private static List<string> ExtractKeyPoints(string expectedAnswer)
    {
        // Split expected answer into key concept chunks
        var sentences = expectedAnswer.Split(new[] { '.', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 15)
            .Take(8)
            .ToList();

        return sentences;
    }
}

public sealed class MockInterviewConfig
{
    public List<string> Topics { get; set; } = [];
    public string Difficulty { get; set; } = "all";
    public int TotalQuestions { get; set; } = 10;
    public int QuestionsPerTopic { get; set; } = 3;
    public int TimeLimitMinutes { get; set; } = 60;
}

public sealed class MockInterviewSession
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public MockInterviewConfig Config { get; set; } = new();
    public List<MockQuestion> Questions { get; set; } = [];
}

public sealed class MockQuestion
{
    public string Topic { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string ExpectedAnswer { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public List<string> Hints { get; set; } = [];
    public string? UserAnswer { get; set; }
    public AnswerScore? Score { get; set; }
}

public sealed class AnswerScore
{
    public int Score { get; set; }
    public string Feedback { get; set; } = string.Empty;
    public List<string> MatchedKeyPoints { get; set; } = [];
    public List<string> MissedKeyPoints { get; set; } = [];
}

public sealed class MockInterviewReport
{
    public string SessionId { get; set; } = string.Empty;
    public int TotalQuestions { get; set; }
    public int AnsweredQuestions { get; set; }
    public double AverageScore { get; set; }
    public string Verdict { get; set; } = string.Empty;
    public List<TopicScore> TopicScores { get; set; } = [];
    public List<string> ImprovementAreas { get; set; } = [];
    public DateTimeOffset CompletedAt { get; set; }
}

public sealed class TopicScore
{
    public string Topic { get; set; } = string.Empty;
    public double AverageScore { get; set; }
    public int QuestionCount { get; set; }
}

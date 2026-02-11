namespace CareerIntel.Core.Models;

/// <summary>
/// Represents a scraped documentation article from official sources.
/// Used to provide learning resources for skill gaps.
/// </summary>
public sealed class DocumentationArticle
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // "Microsoft Docs", "MDN", "AWS Docs"
    public string Technology { get; set; } = string.Empty; // "C#", "ASP.NET Core", "JavaScript"
    public string Category { get; set; } = string.Empty; // "Tutorial", "API Reference", "Conceptual"
    public DifficultyLevel Difficulty { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty; // Full text content
    public List<string> Topics { get; set; } = []; // "async/await", "dependency injection"
    public List<string> Tags { get; set; } = []; // "beginner", "advanced", "performance"
    public List<string> Prerequisites { get; set; } = []; // Required knowledge
    public int EstimatedMinutes { get; set; } // Reading time
    public DateTimeOffset ScrapedDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUpdated { get; set; } // From source
    public List<string> RelatedArticles { get; set; } = []; // URLs
    public List<CodeSample> CodeSamples { get; set; } = [];
    public string Author { get; set; } = string.Empty;
    public double Relevance { get; set; } // 0-100 relevance score for search
}

/// <summary>
/// Code sample extracted from documentation.
/// </summary>
public sealed class CodeSample
{
    public string Language { get; set; } = string.Empty; // "csharp", "javascript"
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsRunnable { get; set; } // Can be executed
}

/// <summary>
/// Documentation set for a specific topic.
/// </summary>
public sealed class DocumentationSet
{
    public string Topic { get; set; } = string.Empty; // "Async Programming"
    public string Technology { get; set; } = string.Empty; // "C#"
    public List<DocumentationArticle> Articles { get; set; } = [];
    public Dictionary<string, int> TopicFrequency { get; set; } = new(); // How many articles per topic
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Statistics about the documentation database.
/// </summary>
public sealed class DocumentationStats
{
    public int TotalArticles { get; set; }
    public int TotalSources { get; set; }
    public int TotalTechnologies { get; set; }
    public Dictionary<string, int> ArticlesBySource { get; set; } = new();
    public Dictionary<string, int> ArticlesByTechnology { get; set; } = new();
    public Dictionary<DifficultyLevel, int> ArticlesByDifficulty { get; set; } = new();
    public DateTimeOffset LastUpdated { get; set; }
}

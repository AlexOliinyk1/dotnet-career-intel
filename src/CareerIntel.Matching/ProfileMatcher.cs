using System.Text.Json;
using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Matching;

/// <summary>
/// Primary implementation of <see cref="IMatchEngine"/> that loads a user profile
/// from a JSON file and scores job vacancies against it.
/// </summary>
public sealed class ProfileMatcher : IMatchEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _profilePath;
    private readonly ScoringEngine _scoringEngine;
    private readonly RelevanceFilter _filter;
    private readonly ILogger<ProfileMatcher> _logger;

    private UserProfile _profile;

    /// <summary>
    /// Initializes a new <see cref="ProfileMatcher"/> that reads user data from the specified JSON file.
    /// </summary>
    /// <param name="profilePath">Absolute or relative path to the my-profile.json file.</param>
    /// <param name="scoringEngine">Scoring engine with configurable weights.</param>
    /// <param name="filter">Relevance filter for hard-constraint elimination.</param>
    /// <param name="logger">Logger instance.</param>
    public ProfileMatcher(
        string profilePath,
        ScoringEngine scoringEngine,
        RelevanceFilter filter,
        ILogger<ProfileMatcher> logger)
    {
        _profilePath = profilePath ?? throw new ArgumentNullException(nameof(profilePath));
        _scoringEngine = scoringEngine ?? throw new ArgumentNullException(nameof(scoringEngine));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _profile = LoadProfileFromFile(profilePath);
    }

    /// <summary>
    /// Gets the currently loaded user profile.
    /// </summary>
    public UserProfile Profile => _profile;

    /// <inheritdoc />
    public MatchScore ComputeMatch(JobVacancy vacancy)
    {
        ArgumentNullException.ThrowIfNull(vacancy);

        var score = _scoringEngine.Score(vacancy, _profile);

        _logger.LogDebug(
            "Computed match for '{Title}' at {Company}: {Score:F1}/100 ({Action})",
            vacancy.Title, vacancy.Company, score.OverallScore, score.ActionLabel);

        return score;
    }

    /// <inheritdoc />
    public IReadOnlyList<JobVacancy> RankVacancies(
        IReadOnlyList<JobVacancy> vacancies,
        double minimumScore = 0)
    {
        ArgumentNullException.ThrowIfNull(vacancies);

        _logger.LogInformation(
            "Ranking {Count} vacancies with minimum score {MinScore}",
            vacancies.Count, minimumScore);

        var filtered = _filter.Apply(vacancies, _profile);

        _logger.LogInformation(
            "{Passed}/{Total} vacancies passed hard-constraint filters",
            filtered.Count, vacancies.Count);

        var ranked = new List<JobVacancy>(filtered.Count);

        foreach (var vacancy in filtered)
        {
            var score = ComputeMatch(vacancy);
            vacancy.MatchScore = score;

            if (score.OverallScore >= minimumScore)
            {
                ranked.Add(vacancy);
            }
        }

        ranked.Sort((a, b) =>
            b.MatchScore!.OverallScore.CompareTo(a.MatchScore!.OverallScore));

        _logger.LogInformation(
            "Ranked {Count} vacancies above minimum score threshold",
            ranked.Count);

        return ranked.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task ReloadProfileAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reloading profile from {Path}", _profilePath);

        await using var stream = File.OpenRead(_profilePath);
        var profile = await JsonSerializer.DeserializeAsync<UserProfile>(
            stream, JsonOptions, cancellationToken);

        _profile = profile
            ?? throw new InvalidOperationException(
                $"Failed to deserialize profile from '{_profilePath}'.");

        _logger.LogInformation(
            "Profile reloaded: {Name} with {SkillCount} skills",
            _profile.Personal.Name, _profile.Skills.Count);
    }

    private static UserProfile LoadProfileFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"User profile file not found at '{path}'.", path);
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<UserProfile>(json, JsonOptions)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize profile from '{path}'.");
    }
}

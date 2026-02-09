using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Notifications;

/// <summary>
/// Sends notifications via Telegram Bot API. Supports HTML formatting
/// and automatic message chunking for messages exceeding the 4096 character limit.
/// </summary>
public sealed class TelegramNotifier : INotificationService
{
    private const int MaxMessageLength = 4096;
    private const string TelegramApiBase = "https://api.telegram.org/bot";

    private readonly HttpClient _httpClient;
    private readonly string _botToken;
    private readonly string _chatId;
    private readonly ILogger<TelegramNotifier> _logger;

    public string ChannelName => "Telegram";

    public TelegramNotifier(
        HttpClient httpClient,
        string botToken,
        string chatId,
        ILogger<TelegramNotifier> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _botToken = botToken ?? throw new ArgumentNullException(nameof(botToken));
        _chatId = chatId ?? throw new ArgumentNullException(nameof(chatId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task NotifyMatchesAsync(
        IReadOnlyList<JobVacancy> matches,
        CancellationToken cancellationToken = default)
    {
        if (matches.Count == 0)
        {
            _logger.LogInformation("No matches to notify via Telegram");
            return;
        }

        _logger.LogInformation("Sending {Count} match notifications via Telegram", matches.Count);

        var sb = new StringBuilder();
        sb.AppendLine("<b>New Job Matches Found</b>");
        sb.AppendLine();

        foreach (var vacancy in matches)
        {
            var entry = FormatVacancyEntry(vacancy);

            // If adding this entry would exceed the limit, send what we have first
            if (sb.Length + entry.Length > MaxMessageLength - 50)
            {
                await SendMessageAsync(sb.ToString(), cancellationToken);
                sb.Clear();
                sb.AppendLine("<b>Job Matches (continued)</b>");
                sb.AppendLine();
            }

            sb.Append(entry);
        }

        if (sb.Length > 0)
        {
            await SendMessageAsync(sb.ToString(), cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task NotifySnapshotAsync(
        MarketSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending market snapshot via Telegram");

        var sb = new StringBuilder();
        sb.AppendLine("<b>Market Snapshot</b>");
        sb.AppendLine($"Date: {snapshot.Date:yyyy-MM-dd}");
        sb.AppendLine($"Platform: {snapshot.Platform}");
        sb.AppendLine($"Total Vacancies: <b>{snapshot.TotalVacancies}</b>");
        sb.AppendLine();

        // Top skills
        var topSkills = snapshot.SkillFrequency
            .OrderByDescending(kvp => kvp.Value)
            .Take(15);

        sb.AppendLine("<b>Top Skills by Demand:</b>");
        var rank = 1;
        foreach (var (skill, count) in topSkills)
        {
            sb.AppendLine($"  {rank}. {EscapeHtml(skill)} ({count} vacancies)");
            rank++;
        }
        sb.AppendLine();

        // Average salary by seniority
        if (snapshot.AverageSalaryByLevel.Count > 0)
        {
            sb.AppendLine("<b>Average Salary by Seniority:</b>");
            foreach (var (level, salary) in snapshot.AverageSalaryByLevel.OrderBy(kvp => kvp.Key))
            {
                sb.AppendLine($"  {level}: ${salary:N0}");
            }
            sb.AppendLine();
        }

        // Remote policy distribution
        if (snapshot.RemotePolicyDistribution.Count > 0)
        {
            sb.AppendLine("<b>Remote Policy Distribution:</b>");
            foreach (var (policy, count) in snapshot.RemotePolicyDistribution.OrderByDescending(kvp => kvp.Value))
            {
                sb.AppendLine($"  {policy}: {count}");
            }
        }

        var message = sb.ToString();
        var chunks = ChunkMessage(message);

        foreach (var chunk in chunks)
        {
            await SendMessageAsync(chunk, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Testing Telegram bot connection...");

            var url = $"{TelegramApiBase}{_botToken}/getMe";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogInformation("Telegram bot connected successfully: {Response}", content);
                return true;
            }

            _logger.LogWarning("Telegram bot connection failed: HTTP {StatusCode}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test Telegram connection");
            return false;
        }
    }

    private static string FormatVacancyEntry(JobVacancy vacancy)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"<b>{EscapeHtml(vacancy.Title)}</b>");
        sb.AppendLine($"Company: {EscapeHtml(vacancy.Company)}");

        // Salary range
        if (vacancy.SalaryMin.HasValue || vacancy.SalaryMax.HasValue)
        {
            var salaryRange = FormatSalaryRange(vacancy.SalaryMin, vacancy.SalaryMax, vacancy.SalaryCurrency);
            sb.AppendLine($"Salary: {salaryRange}");
        }

        // Match score
        if (vacancy.MatchScore is not null)
        {
            var score = vacancy.MatchScore;
            sb.AppendLine($"Match: <b>{score.OverallScore:F0}/100</b> - {EscapeHtml(score.ActionLabel)}");

            if (score.MatchingSkills.Count > 0)
            {
                var top = string.Join(", ", score.MatchingSkills.Take(5).Select(EscapeHtml));
                sb.AppendLine($"Matching: {top}");
            }

            if (score.MissingSkills.Count > 0)
            {
                var missing = string.Join(", ", score.MissingSkills.Take(3).Select(EscapeHtml));
                sb.AppendLine($"Missing: {missing}");
            }
        }

        if (!string.IsNullOrEmpty(vacancy.Url))
        {
            sb.AppendLine($"<a href=\"{vacancy.Url}\">View Vacancy</a>");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private static string FormatSalaryRange(decimal? min, decimal? max, string currency)
    {
        if (min.HasValue && max.HasValue)
            return $"{currency} {min:N0} - {max:N0}";
        if (max.HasValue)
            return $"Up to {currency} {max:N0}";
        if (min.HasValue)
            return $"From {currency} {min:N0}";
        return "Not specified";
    }

    private static List<string> ChunkMessage(string message)
    {
        if (message.Length <= MaxMessageLength)
            return [message];

        var chunks = new List<string>();
        var lines = message.Split('\n');
        var current = new StringBuilder();

        foreach (var line in lines)
        {
            if (current.Length + line.Length + 1 > MaxMessageLength)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }

            current.AppendLine(line);
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString());
        }

        return chunks;
    }

    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private async Task SendMessageAsync(string text, CancellationToken cancellationToken)
    {
        var url = $"{TelegramApiBase}{_botToken}/sendMessage";

        var payload = new
        {
            chat_id = _chatId,
            text,
            parse_mode = "HTML",
            disable_web_page_preview = true
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Telegram send failed: HTTP {StatusCode}, body: {Body}",
                    response.StatusCode, errorBody);
            }
            else
            {
                _logger.LogDebug("Telegram message sent successfully ({Length} chars)", text.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram message");
            throw;
        }
    }
}

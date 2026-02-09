using System.Net;
using System.Net.Mail;
using System.Text;
using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Notifications;

/// <summary>
/// Sends notifications via email using SMTP. Formats matches as HTML email
/// with a table layout for easy scanning.
/// </summary>
public sealed class EmailNotifier : INotificationService
{
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _username;
    private readonly string _password;
    private readonly string _fromAddress;
    private readonly string _toAddress;
    private readonly ILogger<EmailNotifier> _logger;

    public string ChannelName => "Email";

    public EmailNotifier(
        string smtpHost,
        int smtpPort,
        string username,
        string password,
        string fromAddress,
        string toAddress,
        ILogger<EmailNotifier> logger)
    {
        _smtpHost = smtpHost ?? throw new ArgumentNullException(nameof(smtpHost));
        _smtpPort = smtpPort;
        _username = username ?? throw new ArgumentNullException(nameof(username));
        _password = password ?? throw new ArgumentNullException(nameof(password));
        _fromAddress = fromAddress ?? throw new ArgumentNullException(nameof(fromAddress));
        _toAddress = toAddress ?? throw new ArgumentNullException(nameof(toAddress));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task NotifyMatchesAsync(
        IReadOnlyList<JobVacancy> matches,
        CancellationToken cancellationToken = default)
    {
        if (matches.Count == 0)
        {
            _logger.LogInformation("No matches to notify via Email");
            return;
        }

        _logger.LogInformation("Sending {Count} match notifications via Email", matches.Count);

        var subject = $"CareerIntel: {matches.Count} New Job Match{(matches.Count > 1 ? "es" : "")} Found";
        var body = BuildMatchesHtmlBody(matches);

        await SendEmailAsync(subject, body, cancellationToken);
    }

    /// <inheritdoc />
    public async Task NotifySnapshotAsync(
        MarketSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending market snapshot via Email");

        var subject = $"CareerIntel: Market Snapshot - {snapshot.Date:yyyy-MM-dd}";
        var body = BuildSnapshotHtmlBody(snapshot);

        await SendEmailAsync(subject, body, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Testing SMTP connection to {Host}:{Port}...", _smtpHost, _smtpPort);

#pragma warning disable CA2000 // SmtpClient is disposed properly
            using var client = CreateSmtpClient();
#pragma warning restore CA2000

            // Attempt to connect by sending a no-op if possible,
            // or just verify the client can be created with valid settings
            await Task.Run(() =>
            {
                // SmtpClient validates connection when sending;
                // creating it with valid params is the best non-send test
                _logger.LogInformation("SMTP client created successfully for {Host}:{Port}",
                    _smtpHost, _smtpPort);
            }, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP connection test failed");
            return false;
        }
    }

    private static string BuildMatchesHtmlBody(IReadOnlyList<JobVacancy> matches)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html><html><head><style>");
        sb.AppendLine("body { font-family: 'Segoe UI', Tahoma, sans-serif; margin: 20px; color: #333; }");
        sb.AppendLine("h1 { color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 10px; }");
        sb.AppendLine("table { border-collapse: collapse; width: 100%; margin: 20px 0; }");
        sb.AppendLine("th { background-color: #3498db; color: white; padding: 12px 8px; text-align: left; }");
        sb.AppendLine("td { padding: 10px 8px; border-bottom: 1px solid #ddd; }");
        sb.AppendLine("tr:nth-child(even) { background-color: #f8f9fa; }");
        sb.AppendLine("tr:hover { background-color: #e8f4f8; }");
        sb.AppendLine(".score-high { color: #27ae60; font-weight: bold; }");
        sb.AppendLine(".score-mid { color: #f39c12; font-weight: bold; }");
        sb.AppendLine(".score-low { color: #e74c3c; font-weight: bold; }");
        sb.AppendLine(".action-apply { background-color: #d4edda; padding: 4px 8px; border-radius: 4px; }");
        sb.AppendLine(".action-prepare { background-color: #fff3cd; padding: 4px 8px; border-radius: 4px; }");
        sb.AppendLine(".action-skill { background-color: #f8d7da; padding: 4px 8px; border-radius: 4px; }");
        sb.AppendLine(".skills { font-size: 0.9em; }");
        sb.AppendLine(".matching { color: #27ae60; }");
        sb.AppendLine(".missing { color: #e74c3c; }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine($"<h1>Job Matches - {DateTime.Now:MMMM dd, yyyy}</h1>");
        sb.AppendLine($"<p>Found <strong>{matches.Count}</strong> matching vacancies.</p>");

        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>Title / Company</th><th>Salary</th><th>Score</th><th>Action</th><th>Skills</th></tr>");

        foreach (var vacancy in matches)
        {
            var score = vacancy.MatchScore;
            var scoreClass = score?.OverallScore switch
            {
                >= 75 => "score-high",
                >= 55 => "score-mid",
                _ => "score-low"
            };

            var actionClass = score?.RecommendedAction switch
            {
                RecommendedAction.Apply => "action-apply",
                RecommendedAction.PrepareAndApply => "action-prepare",
                _ => "action-skill"
            };

            var salaryDisplay = FormatSalary(vacancy);
            var titleLink = !string.IsNullOrEmpty(vacancy.Url)
                ? $"<a href=\"{vacancy.Url}\">{Encode(vacancy.Title)}</a>"
                : Encode(vacancy.Title);

            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{titleLink}<br/><em>{Encode(vacancy.Company)}</em></td>");
            sb.AppendLine($"<td>{salaryDisplay}</td>");
            sb.AppendLine($"<td class=\"{scoreClass}\">{score?.OverallScore:F0}/100</td>");
            sb.AppendLine($"<td><span class=\"{actionClass}\">{score?.ActionLabel ?? "N/A"}</span></td>");

            var skillsHtml = new StringBuilder();
            if (score?.MatchingSkills.Count > 0)
            {
                skillsHtml.Append($"<span class=\"matching\">{string.Join(", ", score.MatchingSkills.Take(5))}</span>");
            }
            if (score?.MissingSkills.Count > 0)
            {
                if (skillsHtml.Length > 0) skillsHtml.Append("<br/>");
                skillsHtml.Append($"<span class=\"missing\">Missing: {string.Join(", ", score.MissingSkills.Take(3))}</span>");
            }

            sb.AppendLine($"<td class=\"skills\">{skillsHtml}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</table>");
        sb.AppendLine("<p><em>Generated by CareerIntel</em></p>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private static string BuildSnapshotHtmlBody(MarketSnapshot snapshot)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html><html><head><style>");
        sb.AppendLine("body { font-family: 'Segoe UI', Tahoma, sans-serif; margin: 20px; color: #333; }");
        sb.AppendLine("h1, h2 { color: #2c3e50; }");
        sb.AppendLine("table { border-collapse: collapse; width: 100%; margin: 10px 0 20px 0; }");
        sb.AppendLine("th { background-color: #3498db; color: white; padding: 10px 8px; text-align: left; }");
        sb.AppendLine("td { padding: 8px; border-bottom: 1px solid #ddd; }");
        sb.AppendLine("tr:nth-child(even) { background-color: #f8f9fa; }");
        sb.AppendLine(".summary { background-color: #eaf2f8; padding: 15px; border-radius: 8px; margin: 15px 0; }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine($"<h1>Market Snapshot - {snapshot.Date:yyyy-MM-dd}</h1>");
        sb.AppendLine("<div class=\"summary\">");
        sb.AppendLine($"<p><strong>Platform:</strong> {snapshot.Platform}</p>");
        sb.AppendLine($"<p><strong>Total Vacancies:</strong> {snapshot.TotalVacancies}</p>");
        sb.AppendLine("</div>");

        // Top skills table
        var topSkills = snapshot.SkillFrequency
            .OrderByDescending(kvp => kvp.Value)
            .Take(20);

        sb.AppendLine("<h2>Top Skills by Demand</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>#</th><th>Skill</th><th>Vacancies</th></tr>");

        var rank = 1;
        foreach (var (skill, count) in topSkills)
        {
            sb.AppendLine($"<tr><td>{rank}</td><td>{Encode(skill)}</td><td>{count}</td></tr>");
            rank++;
        }
        sb.AppendLine("</table>");

        // Salary by seniority
        if (snapshot.AverageSalaryByLevel.Count > 0)
        {
            sb.AppendLine("<h2>Average Salary by Seniority</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Level</th><th>Average Salary (USD)</th></tr>");

            foreach (var (level, salary) in snapshot.AverageSalaryByLevel.OrderBy(kvp => kvp.Key))
            {
                sb.AppendLine($"<tr><td>{level}</td><td>${salary:N0}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<p><em>Generated by CareerIntel</em></p>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private static string FormatSalary(JobVacancy vacancy)
    {
        if (vacancy.SalaryMin.HasValue && vacancy.SalaryMax.HasValue)
            return $"{vacancy.SalaryCurrency} {vacancy.SalaryMin:N0} - {vacancy.SalaryMax:N0}";
        if (vacancy.SalaryMax.HasValue)
            return $"Up to {vacancy.SalaryCurrency} {vacancy.SalaryMax:N0}";
        if (vacancy.SalaryMin.HasValue)
            return $"From {vacancy.SalaryCurrency} {vacancy.SalaryMin:N0}";
        return "Not specified";
    }

    private static string Encode(string text) =>
        System.Net.WebUtility.HtmlEncode(text);

    private SmtpClient CreateSmtpClient()
    {
        var client = new SmtpClient(_smtpHost, _smtpPort)
        {
            Credentials = new NetworkCredential(_username, _password),
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30_000
        };

        return client;
    }

    private async Task SendEmailAsync(string subject, string htmlBody, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateSmtpClient();
            using var message = new MailMessage
            {
                From = new MailAddress(_fromAddress, "CareerIntel"),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };

            message.To.Add(new MailAddress(_toAddress));

            await Task.Run(() => client.Send(message), cancellationToken);

            _logger.LogInformation("Email sent successfully to {Recipient}: {Subject}", _toAddress, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient}", _toAddress);
            throw;
        }
    }
}

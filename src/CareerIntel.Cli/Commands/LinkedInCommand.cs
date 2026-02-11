using System.CommandLine;
using System.Text.Json;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Scrapers;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// LinkedIn proposal tracker. Parse your LinkedIn data export to extract and analyze
/// all recruiter proposals you've ever received.
///
/// Usage:
///   career-intel linkedin --import "C:\Downloads\linkedin-export.zip"
///   career-intel linkedin --list
///   career-intel linkedin --list --company "EPAM" --status New
///   career-intel linkedin --stats
///   career-intel linkedin --update 5 --status Interested --notes "Good fit"
///   career-intel linkedin --match --top 10
/// </summary>
public static class LinkedInCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static Command Create()
    {
        var importOption = new Option<string?>(
            "--import",
            description: "Path to LinkedIn data export ZIP or messages.csv");

        var listOption = new Option<bool>(
            "--list",
            description: "Show proposals table");

        var statsOption = new Option<bool>(
            "--stats",
            description: "Show proposal statistics and analytics");

        var updateOption = new Option<int?>(
            "--update",
            description: "Proposal ID to update status/notes");

        var matchOption = new Option<bool>(
            "--match",
            description: "Run proposals through matching engine");

        var statusOption = new Option<string?>(
            "--status",
            description: "Filter by or set status (New/Interested/Replied/Interviewing/Declined/Expired/Converted)");

        var companyOption = new Option<string?>(
            "--company",
            description: "Filter by company name");

        var techOption = new Option<string?>(
            "--tech",
            description: "Filter by tech stack keyword");

        var fromOption = new Option<string?>(
            "--from",
            description: "Filter from date (YYYY-MM-DD)");

        var toOption = new Option<string?>(
            "--to",
            description: "Filter to date (YYYY-MM-DD)");

        var topOption = new Option<int>(
            "--top",
            getDefaultValue: () => 30,
            description: "Number of proposals to show");

        var notesOption = new Option<string?>(
            "--notes",
            description: "Notes to add when updating a proposal");

        var command = new Command("linkedin",
            "Parse and track LinkedIn recruiter proposals from data export")
        {
            importOption, listOption, statsOption, updateOption, matchOption,
            statusOption, companyOption, techOption, fromOption, toOption, topOption, notesOption
        };

        command.SetHandler(async (context) =>
        {
            var import = context.ParseResult.GetValueForOption(importOption);
            var list = context.ParseResult.GetValueForOption(listOption);
            var stats = context.ParseResult.GetValueForOption(statsOption);
            var update = context.ParseResult.GetValueForOption(updateOption);
            var match = context.ParseResult.GetValueForOption(matchOption);
            var status = context.ParseResult.GetValueForOption(statusOption);
            var company = context.ParseResult.GetValueForOption(companyOption);
            var tech = context.ParseResult.GetValueForOption(techOption);
            var from = context.ParseResult.GetValueForOption(fromOption);
            var to = context.ParseResult.GetValueForOption(toOption);
            var top = context.ParseResult.GetValueForOption(topOption);
            var notes = context.ParseResult.GetValueForOption(notesOption);

            if (!string.IsNullOrEmpty(import))
                await ImportAsync(import);
            else if (update.HasValue)
                await UpdateAsync(update.Value, status, notes);
            else if (stats)
                await ShowStatsAsync();
            else if (match)
                await MatchAsync(top);
            else
                await ListAsync(company, status, tech, from, to, top);
        });

        return command;
    }

    private static async Task ImportAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"File not found: {filePath}");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== LinkedIn Proposal Import ===");
        Console.ResetColor();
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"Parsing: {filePath}");
        Console.ResetColor();

        var parser = new LinkedInMessageParser();
        var result = await parser.ParseAsync(filePath);

        Console.WriteLine($"Total messages: {result.TotalMessages}");
        Console.WriteLine($"Total conversations: {result.TotalConversations}");
        Console.WriteLine($"Proposals detected: {result.Proposals.Count}");
        Console.WriteLine();

        // Load existing and merge
        var existing = await LoadProposalsAsync();
        var merged = ProposalAnalyzer.Deduplicate(existing, result.Proposals);
        var newCount = merged.Count - existing.Count;

        await SaveProposalsAsync(merged);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Imported: {newCount} new proposals, {merged.Count} total");
        Console.ResetColor();

        if (result.Proposals.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Latest proposals:");
            Console.ResetColor();

            foreach (var p in result.Proposals.OrderByDescending(p => p.ProposalDate).Take(5))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"  {p.ProposalDate:yyyy-MM-dd}");
                Console.ResetColor();
                Console.Write($"  {Truncate(p.RecruiterName, 20)}");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"  {Truncate(p.Company, 20)}");
                Console.ResetColor();
                Console.WriteLine($"  {Truncate(p.JobTitle, 30)}");
            }
        }
    }

    private static async Task ListAsync(
        string? company, string? status, string? tech, string? from, string? to, int top)
    {
        var proposals = await LoadProposalsAsync();

        if (proposals.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No proposals found. Import your LinkedIn export first:");
            Console.ResetColor();
            Console.WriteLine("  career-intel linkedin --import \"path/to/linkedin-export.zip\"");
            return;
        }

        // Apply filters
        var filtered = proposals.AsEnumerable();

        if (!string.IsNullOrEmpty(company))
            filtered = filtered.Where(p => p.Company.Contains(company, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<ProposalStatus>(status, true, out var statusEnum))
            filtered = filtered.Where(p => p.Status == statusEnum);

        if (!string.IsNullOrEmpty(tech))
            filtered = filtered.Where(p => p.TechStack.Contains(tech, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(from) && DateTimeOffset.TryParse(from, out var fromDate))
            filtered = filtered.Where(p => p.ProposalDate >= fromDate);

        if (!string.IsNullOrEmpty(to) && DateTimeOffset.TryParse(to, out var toDate))
            filtered = filtered.Where(p => p.ProposalDate <= toDate);

        var list = filtered.OrderByDescending(p => p.ProposalDate).Take(top).ToList();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== LinkedIn Proposals ({list.Count}/{proposals.Count}) ===");
        Console.ResetColor();
        Console.WriteLine();

        // Table header
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"{"#",-4} {"Date",-12} {"Company",-22} {"Position",-28} {"Tech",-20} {"Remote",-8} {"Status",-12}");
        Console.WriteLine(new string('-', 110));
        Console.ResetColor();

        foreach (var p in list)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"{p.Id,-4}");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($" {p.ProposalDate:yyyy-MM-dd} ");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($" {Truncate(p.Company, 20),-22}");

            Console.ResetColor();
            Console.Write($"{Truncate(p.JobTitle, 26),-28}");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"{Truncate(p.TechStack, 18),-20}");

            Console.ForegroundColor = p.RemotePolicy == "Remote" ? ConsoleColor.Green :
                p.RemotePolicy == "Hybrid" ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
            Console.Write($"{Truncate(p.RemotePolicy, 6),-8}");

            Console.ForegroundColor = p.Status switch
            {
                ProposalStatus.New => ConsoleColor.White,
                ProposalStatus.Interested or ProposalStatus.Replied => ConsoleColor.Green,
                ProposalStatus.Interviewing => ConsoleColor.Cyan,
                ProposalStatus.Declined or ProposalStatus.Expired => ConsoleColor.Red,
                ProposalStatus.Converted => ConsoleColor.Magenta,
                _ => ConsoleColor.DarkGray
            };
            Console.Write($"{p.Status,-12}");

            Console.ResetColor();

            if (p.RelocationOffered)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(" RELO");
                Console.ResetColor();
            }

            Console.WriteLine();
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"Showing {list.Count} of {proposals.Count} proposals. Use --top to see more.");
        Console.ResetColor();
    }

    private static async Task ShowStatsAsync()
    {
        var proposals = await LoadProposalsAsync();

        if (proposals.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No proposals to analyze. Import first.");
            Console.ResetColor();
            return;
        }

        var analyzer = new ProposalAnalyzer();
        var stats = analyzer.GetStatistics(proposals);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== LinkedIn Proposal Analytics ===");
        Console.ResetColor();
        Console.WriteLine();

        Console.WriteLine($"Total Proposals: {stats.TotalProposals}");
        Console.WriteLine($"Date Range: {stats.DateRange}");
        Console.WriteLine($"Avg Messages/Thread: {stats.AverageMessagesPerThread:F1}");
        Console.WriteLine($"With Salary Info: {stats.WithSalaryHint}");
        Console.WriteLine($"Relocation Offers: {stats.RelocationOffers}");
        Console.WriteLine();

        // Proposals per month
        if (stats.ProposalsPerMonth.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("--- Proposals Per Month ---");
            Console.ResetColor();

            var maxCount = stats.ProposalsPerMonth.Values.Max();
            foreach (var (month, count) in stats.ProposalsPerMonth)
            {
                var barLen = maxCount > 0 ? (int)((double)count / maxCount * 30) : 0;
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"  {month}  ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(new string('#', barLen));
                Console.ResetColor();
                Console.WriteLine($" {count}");
            }
            Console.WriteLine();
        }

        // Top companies
        if (stats.TopCompanies.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("--- Top Companies ---");
            Console.ResetColor();
            foreach (var (company, count) in stats.TopCompanies.Take(10))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"  {count,3}x  ");
                Console.ResetColor();
                Console.WriteLine(company);
            }
            Console.WriteLine();
        }

        // Common titles
        if (stats.CommonJobTitles.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("--- Common Positions ---");
            Console.ResetColor();
            foreach (var (title, count) in stats.CommonJobTitles.Take(10))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"  {count,3}x  ");
                Console.ResetColor();
                Console.WriteLine(title);
            }
            Console.WriteLine();
        }

        // Tech frequency
        if (stats.TechStackFrequency.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("--- Tech Stack Demand ---");
            Console.ResetColor();
            foreach (var (tech, count) in stats.TechStackFrequency.Take(10))
            {
                var pct = stats.TotalProposals > 0 ? (double)count / stats.TotalProposals * 100 : 0;
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"  {pct,5:F0}%  ");
                Console.ResetColor();
                Console.WriteLine(tech);
            }
            Console.WriteLine();
        }

        // Remote breakdown
        if (stats.RemoteBreakdown.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("--- Remote Policy ---");
            Console.ResetColor();
            foreach (var (policy, count) in stats.RemoteBreakdown)
            {
                Console.ForegroundColor = policy == "Remote" ? ConsoleColor.Green :
                    policy == "Hybrid" ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
                Console.WriteLine($"  {policy,-12} {count,3} ({(double)count / stats.TotalProposals * 100:F0}%)");
            }
            Console.ResetColor();
            Console.WriteLine();
        }

        // Top locations
        if (stats.TopLocations.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("--- Top Locations ---");
            Console.ResetColor();
            foreach (var (location, count) in stats.TopLocations.Take(10))
            {
                Console.WriteLine($"  {count,3}x  {location}");
            }
            Console.WriteLine();
        }

        // Status breakdown
        if (stats.StatusBreakdown.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("--- Status ---");
            Console.ResetColor();
            foreach (var (s, count) in stats.StatusBreakdown)
            {
                Console.WriteLine($"  {s,-15} {count}");
            }
        }
    }

    private static async Task UpdateAsync(int proposalId, string? status, string? notes)
    {
        var proposals = await LoadProposalsAsync();
        var proposal = proposals.FirstOrDefault(p => p.Id == proposalId);

        if (proposal == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Proposal #{proposalId} not found.");
            Console.ResetColor();
            return;
        }

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<ProposalStatus>(status, true, out var statusEnum))
            proposal.Status = statusEnum;

        if (!string.IsNullOrEmpty(notes))
            proposal.Notes = notes;

        await SaveProposalsAsync(proposals);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"Updated proposal #{proposalId}: ");
        Console.ResetColor();
        Console.Write($"{proposal.Company} — {proposal.JobTitle}");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($" [{proposal.Status}]");
        Console.ResetColor();
    }

    private static async Task MatchAsync(int top)
    {
        var proposals = await LoadProposalsAsync();

        if (proposals.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No proposals to match. Import first.");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Proposal Matching ===");
        Console.ResetColor();
        Console.WriteLine();

        var converted = 0;
        var skipped = 0;

        foreach (var p in proposals.OrderByDescending(p => p.ProposalDate).Take(top))
        {
            var vacancy = ProposalAnalyzer.ConvertToVacancy(p);
            if (vacancy == null)
            {
                skipped++;
                continue;
            }

            converted++;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"  #{p.Id}  ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"{Truncate(p.Company, 20),-22}");
            Console.ResetColor();
            Console.Write($"{Truncate(p.JobTitle, 25),-27}");

            Console.ForegroundColor = p.RemotePolicy == "Remote" ? ConsoleColor.Green :
                p.RemotePolicy == "Hybrid" ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
            Console.Write($"{p.RemotePolicy,-10}");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"{Truncate(p.TechStack, 25)}");
            Console.ResetColor();
            Console.WriteLine();

            if (!string.IsNullOrEmpty(p.SalaryHint))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"         Salary: {p.SalaryHint}");
                Console.ResetColor();
            }

            if (p.RelocationOffered)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("         Relocation offered");
                Console.ResetColor();
            }

            if (!string.IsNullOrEmpty(p.Location))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"         Location: {p.Location}");
                Console.ResetColor();
            }
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"Converted: {converted}, Skipped (insufficient data): {skipped}");
        Console.ResetColor();
    }

    private static async Task<List<LinkedInProposal>> LoadProposalsAsync()
    {
        var path = Path.Combine(Program.DataDirectory, "linkedin-proposals.json");
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<LinkedInProposal>>(json, JsonOptions) ?? [];
    }

    private static async Task SaveProposalsAsync(List<LinkedInProposal> proposals)
    {
        var path = Path.Combine(Program.DataDirectory, "linkedin-proposals.json");
        var json = JsonSerializer.Serialize(proposals, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    private static string Truncate(string text, int maxLen) =>
        string.IsNullOrEmpty(text) ? "" :
        text.Length <= maxLen ? text : text[..(maxLen - 2)] + "..";
}

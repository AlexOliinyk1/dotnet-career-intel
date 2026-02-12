using System.Text.Json;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from Torre.ai - AI-powered talent matching platform.
/// Supports freelance/contract positions with transparent salary ranges.
/// Known for global remote positions and AI/ML-adjacent roles.
/// </summary>
public sealed class TorreAiScraper(HttpClient httpClient, ILogger<TorreAiScraper> logger)
    : BaseScraper(httpClient, logger)
{
    public override string PlatformName => "Torre.ai";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(2);

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        try
        {
            // Torre.ai has a public API for job search
            for (var page = 0; page < maxPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var offset = page * 20;
                var url = $"https://torre.ai/api/suite/opportunities/search?offset={offset}&size=20";

                await Task.Delay(RequestDelay, cancellationToken);

                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new
                            {
                                and = new[]
                                {
                                    new { skill = new { text = keywords, experience = "potential-to-senior" } }
                                },
                                or = Array.Empty<object>(),
                                not = Array.Empty<object>()
                            }),
                            System.Text.Encoding.UTF8,
                            "application/json")
                    };

                    var response = await httpClient.SendAsync(request, cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        logger.LogWarning("[Torre.ai] HTTP {StatusCode} on page {Page}", response.StatusCode, page);
                        // Fall back to HTML scraping
                        var htmlVacancies = await ScrapeHtmlFallback(keywords, page, cancellationToken);
                        vacancies.AddRange(htmlVacancies);
                        if (htmlVacancies.Count == 0) break;
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var results = root.TryGetProperty("results", out var resultsEl)
                        ? resultsEl
                        : root;

                    if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                    {
                        logger.LogDebug("No more results on page {Page}", page);
                        break;
                    }

                    foreach (var item in results.EnumerateArray())
                    {
                        try
                        {
                            var opp = item.TryGetProperty("opportunity", out var oppEl) ? oppEl : item;

                            var id = opp.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                            var title = opp.TryGetProperty("objective", out var objEl) ? objEl.GetString() ?? "" : "";
                            var orgName = "";
                            if (opp.TryGetProperty("organizations", out var orgs) && orgs.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var org in orgs.EnumerateArray())
                                {
                                    if (org.TryGetProperty("name", out var orgNameEl))
                                    {
                                        orgName = orgNameEl.GetString() ?? "";
                                        break;
                                    }
                                }
                            }

                            if (string.IsNullOrWhiteSpace(title)) continue;

                            // Compensation
                            decimal? salMin = null, salMax = null;
                            var currency = "USD";
                            if (opp.TryGetProperty("compensation", out var comp))
                            {
                                if (comp.TryGetProperty("minAmount", out var minAmt))
                                    salMin = minAmt.TryGetDecimal(out var minV) ? minV : null;
                                if (comp.TryGetProperty("maxAmount", out var maxAmt))
                                    salMax = maxAmt.TryGetDecimal(out var maxV) ? maxV : null;
                                if (comp.TryGetProperty("currency", out var currEl))
                                    currency = currEl.GetString() ?? "USD";
                                if (comp.TryGetProperty("periodicity", out var period) &&
                                    period.GetString()?.Contains("month", StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    // Convert monthly to annual
                                    if (salMin.HasValue) salMin *= 12;
                                    if (salMax.HasValue) salMax *= 12;
                                }
                            }

                            // Remote
                            var isRemote = false;
                            if (opp.TryGetProperty("remote", out var remoteEl))
                                isRemote = remoteEl.GetBoolean();

                            var location = "";
                            if (opp.TryGetProperty("locations", out var locs) && locs.ValueKind == JsonValueKind.Array)
                            {
                                var locParts = new List<string>();
                                foreach (var loc in locs.EnumerateArray())
                                {
                                    if (loc.ValueKind == JsonValueKind.String)
                                        locParts.Add(loc.GetString() ?? "");
                                }
                                location = string.Join(", ", locParts);
                            }

                            // Skills
                            var skills = new List<string>();
                            if (opp.TryGetProperty("skills", out var skillsEl) && skillsEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var skill in skillsEl.EnumerateArray())
                                {
                                    if (skill.TryGetProperty("name", out var skillName))
                                    {
                                        var s = skillName.GetString();
                                        if (!string.IsNullOrWhiteSpace(s))
                                            skills.Add(s);
                                    }
                                    else if (skill.ValueKind == JsonValueKind.String)
                                    {
                                        var s = skill.GetString();
                                        if (!string.IsNullOrWhiteSpace(s))
                                            skills.Add(s);
                                    }
                                }
                            }

                            // Type
                            var type = opp.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "" : "";
                            var fullText = $"{title} {type} {location}";

                            vacancies.Add(new JobVacancy
                            {
                                Id = GenerateId(id),
                                Title = title,
                                Company = orgName,
                                Country = isRemote ? "Remote" : (string.IsNullOrEmpty(location) ? "Unknown" : location),
                                Url = $"https://torre.ai/jobs/{id}",
                                Description = $"[Torre.ai] {type}",
                                RemotePolicy = isRemote
                                    ? Core.Enums.RemotePolicy.FullyRemote
                                    : DetectRemotePolicy(fullText),
                                EngagementType = type.Contains("freelance", StringComparison.OrdinalIgnoreCase)
                                    ? Core.Enums.EngagementType.Freelance
                                    : DetectEngagementType(fullText),
                                SalaryMin = salMin,
                                SalaryMax = salMax,
                                SalaryCurrency = currency,
                                RequiredSkills = skills,
                                PostedDate = DateTimeOffset.UtcNow,
                                SourcePlatform = PlatformName,
                                GeoRestrictions = DetectGeoRestrictions(fullText)
                            });
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, "Failed to parse a Torre.ai listing");
                        }
                    }

                    logger.LogInformation("[Torre.ai] Page {Page}: found jobs", page);
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning(ex, "[Torre.ai] API request failed on page {Page}, trying HTML fallback", page);
                    var htmlVacancies = await ScrapeHtmlFallback(keywords, page, cancellationToken);
                    vacancies.AddRange(htmlVacancies);
                    if (htmlVacancies.Count == 0) break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scrape Torre.ai");
        }

        return vacancies;
    }

    public override Task<JobVacancy?> ScrapeDetailAsync(string url, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<JobVacancy?>(null);
    }

    private async Task<List<JobVacancy>> ScrapeHtmlFallback(string keywords, int page, CancellationToken cancellationToken)
    {
        var vacancies = new List<JobVacancy>();
        var url = $"https://torre.ai/jobs?q={Uri.EscapeDataString(keywords)}&page={page + 1}";

        var doc = await FetchPageAsync(url, cancellationToken);
        if (doc is null) return vacancies;

        var jobCards = SelectNodes(doc, "//div[contains(@class,'opportunity')] | //article[contains(@class,'job')]");
        if (jobCards is null) return vacancies;

        foreach (var card in jobCards)
        {
            try
            {
                var titleNode = card.SelectSingleNode(".//h2 | .//h3 | .//a[contains(@class,'title')]");
                var title = ExtractText(titleNode);
                var jobUrl = ExtractAttribute(titleNode, "href");

                if (string.IsNullOrWhiteSpace(title)) continue;

                var company = ExtractText(card.SelectSingleNode(".//span[contains(@class,'org')] | .//span[contains(@class,'company')]"));
                var salaryText = ExtractText(card.SelectSingleNode(".//span[contains(@class,'compensation')] | .//span[contains(@class,'salary')]"));
                var (salMin, salMax, currency) = ParseSalaryRange(salaryText);

                vacancies.Add(new JobVacancy
                {
                    Id = GenerateId(jobUrl.GetHashCode().ToString("x")),
                    Title = title,
                    Company = company,
                    Country = "Remote",
                    Url = jobUrl.StartsWith("http") ? jobUrl : $"https://torre.ai{jobUrl}",
                    Description = "[Torre.ai]",
                    RemotePolicy = Core.Enums.RemotePolicy.FullyRemote,
                    SalaryMin = salMin,
                    SalaryMax = salMax,
                    SalaryCurrency = currency,
                    PostedDate = DateTimeOffset.UtcNow,
                    SourcePlatform = PlatformName
                });
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to parse a Torre.ai HTML listing");
            }
        }

        return vacancies;
    }
}

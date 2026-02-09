using System.Text.RegularExpressions;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;
using Tesseract;

namespace CareerIntel.Intelligence;

/// <summary>
/// OCR-based engine that scans screenshot images of job listings (LinkedIn, job boards)
/// and extracts <see cref="JobVacancy"/> objects using Tesseract OCR. Each extracted vacancy
/// is automatically assessed for eligibility via <see cref="EligibilityGate"/>.
/// </summary>
public sealed class VacancyImageScanner(ILogger<VacancyImageScanner> logger)
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Constants
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly string TessDataPath =
        Path.Combine(AppContext.BaseDirectory, "tessdata");

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif"
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  Compiled regex patterns
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Regex TitlePattern = new(
        @"(?:Senior|Sr\.?|Lead|Principal|Staff|Mid[- ]?Level|Junior|Jr\.?|Full[- ]?Stack)?\s*(?:\.?NET|C#|Dotnet|ASP\.?NET|Azure)\s*(?:Developer|Engineer|Architect|Consultant|Specialist|Lead|Manager|DevOps|Backend|Full[- ]?Stack)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GeneralTitlePattern = new(
        @"^.{0,60}(Developer|Engineer|Architect|Consultant|Specialist|Tech Lead|Team Lead|Manager|DevOps)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LocationParenPattern = new(
        @"([\w\s]+(?:,\s*[\w\s]+)?)\s*\((\w+)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LocationDotPattern = new(
        @"([\w\s]+)\s*[·•]\s*([\w\s]+)\s*[·•]?\s*(\w+)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SalaryPattern = new(
        @"[\$€£]\s*([\d,]+)\s*[Kk]?\s*[-–]\s*[\$€£]?\s*([\d,]+)\s*[Kk]?\s*/?\s*(yr|hr|hour|mo|month|year)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SalaryKPattern = new(
        @"[\$€£]\s*([\d,]+)\s*[Kk]\s*[-–]\s*[\$€£]?\s*([\d,]+)\s*[Kk]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> SkipLineKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Easy Apply", "Actively recruiting", "Promoted", "Be Seen First",
        "View Details", "1-Click Apply", "connection"
    };

    private static readonly string[] KnownSkills =
    [
        "C#", ".NET", "Azure", "AWS", "Docker", "Kubernetes", "SQL", "React",
        "Angular", "TypeScript", "Entity Framework", "Blazor", "gRPC",
        "RabbitMQ", "Redis", "PostgreSQL", "MongoDB", "Microservices",
        "REST", "GraphQL", "CI/CD", "Terraform", "Linux"
    ];

    // ─────────────────────────────────────────────────────────────────────────
    //  Public methods
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans a single image file, extracts job vacancies from OCR text, and
    /// assesses each vacancy for eligibility.
    /// </summary>
    public ImageScanResult ScanImage(string imagePath)
    {
        logger.LogInformation("Scanning image for job vacancies: {ImagePath}", imagePath);

        var warnings = new List<string>();

        if (!File.Exists(imagePath))
        {
            logger.LogError("Image file does not exist: {ImagePath}", imagePath);
            warnings.Add($"Image file not found: {imagePath}");
            return new ImageScanResult(imagePath, string.Empty, [], [], 0.0, warnings);
        }

        var extension = Path.GetExtension(imagePath);
        if (!SupportedExtensions.Contains(extension))
        {
            logger.LogWarning("Unsupported image format: {Extension}", extension);
            warnings.Add($"Unsupported image format: {extension}");
            return new ImageScanResult(imagePath, string.Empty, [], [], 0.0, warnings);
        }

        var (ocrText, confidence) = ExtractTextFromImage(imagePath, warnings);

        if (string.IsNullOrWhiteSpace(ocrText))
        {
            logger.LogWarning("No text extracted from image: {ImagePath}", imagePath);
            warnings.Add("OCR produced no readable text from the image.");
            return new ImageScanResult(imagePath, ocrText, [], [], confidence, warnings);
        }

        if (confidence < 30.0)
        {
            warnings.Add($"Low OCR confidence ({confidence:F1}%) — extracted text may be unreliable.");
        }

        logger.LogInformation(
            "OCR extracted {Length} characters with {Confidence:F1}% confidence from {ImagePath}",
            ocrText.Length, confidence, imagePath);

        var vacancies = ParseVacanciesFromText(ocrText, imagePath);

        logger.LogInformation(
            "Parsed {Count} vacancy/vacancies from {ImagePath}",
            vacancies.Count, imagePath);

        var assessments = new List<EligibilityAssessment>();
        foreach (var vacancy in vacancies)
        {
            var assessment = EligibilityGate.Assess(vacancy);
            assessments.Add(assessment);

            logger.LogDebug(
                "Vacancy '{Title}' at {Company}: Eligible={Eligible}",
                vacancy.Title, vacancy.Company, assessment.IsEligible);
        }

        return new ImageScanResult(imagePath, ocrText, vacancies, assessments, confidence, warnings);
    }

    /// <summary>
    /// Scans all supported image files in the specified directory, extracts job
    /// vacancies from each, and returns an aggregated summary.
    /// </summary>
    public ImageScanSummary ScanDirectory(string directoryPath, string searchPattern = "*.*")
    {
        logger.LogInformation(
            "Scanning directory for job vacancy images: {DirectoryPath} (pattern: {Pattern})",
            directoryPath, searchPattern);

        if (!Directory.Exists(directoryPath))
        {
            logger.LogError("Directory does not exist: {DirectoryPath}", directoryPath);
            return new ImageScanSummary(0, 0, 0, 0, []);
        }

        var imageFiles = Directory.EnumerateFiles(directoryPath, searchPattern)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogInformation("Found {Count} supported image files in {DirectoryPath}",
            imageFiles.Count, directoryPath);

        if (imageFiles.Count == 0)
        {
            return new ImageScanSummary(0, 0, 0, 0, []);
        }

        var results = new List<ImageScanResult>();

        foreach (var imageFile in imageFiles)
        {
            var result = ScanImage(imageFile);
            results.Add(result);
        }

        int totalVacancies = results.Sum(r => r.Vacancies.Count);
        int eligible = results.Sum(r => r.Assessments.Count(a => a.IsEligible));
        int ineligible = results.Sum(r => r.Assessments.Count(a => !a.IsEligible));

        logger.LogInformation(
            "Directory scan complete: {Images} images, {Vacancies} vacancies ({Eligible} eligible, {Ineligible} ineligible)",
            imageFiles.Count, totalVacancies, eligible, ineligible);

        return new ImageScanSummary(imageFiles.Count, totalVacancies, eligible, ineligible, results);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  OCR extraction
    // ─────────────────────────────────────────────────────────────────────────

    private (string text, double confidence) ExtractTextFromImage(
        string imagePath,
        List<string> warnings)
    {
        if (!Directory.Exists(TessDataPath))
        {
            logger.LogError(
                "Tesseract data folder not found at {TessDataPath}. " +
                "Download eng.traineddata from https://github.com/tesseract-ocr/tessdata and place it in the tessdata folder.",
                TessDataPath);
            warnings.Add(
                $"Tessdata folder missing at '{TessDataPath}'. " +
                "Download eng.traineddata from https://github.com/tesseract-ocr/tessdata and place it in the tessdata/ folder next to the application binary.");
            return (string.Empty, 0.0);
        }

        try
        {
            using var engine = new TesseractEngine(TessDataPath, "eng", EngineMode.Default);
            using var img = Pix.LoadFromFile(imagePath);
            using var page = engine.Process(img);

            var text = page.GetText();
            var confidence = page.GetMeanConfidence() * 100;

            return (text, confidence);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tesseract OCR failed for image: {ImagePath}", imagePath);
            warnings.Add($"OCR engine error: {ex.Message}");
            return (string.Empty, 0.0);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Vacancy parsing
    // ─────────────────────────────────────────────────────────────────────────

    private List<JobVacancy> ParseVacanciesFromText(string ocrText, string sourcePath)
    {
        var vacancies = new List<JobVacancy>();
        var blocks = SplitIntoListingBlocks(ocrText);

        logger.LogDebug("Split OCR text into {Count} candidate block(s)", blocks.Count);

        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var vacancy = TryParseBlock(block, sourcePath, i);

            if (vacancy is not null)
            {
                vacancies.Add(vacancy);
            }
        }

        return vacancies;
    }

    /// <summary>
    /// Splits raw OCR text into candidate listing blocks by looking for
    /// double-newline separators or title-like lines.
    /// </summary>
    private static List<string> SplitIntoListingBlocks(string ocrText)
    {
        // First try splitting by double newlines (common in screenshot layouts)
        var doubleNewlineSplit = Regex.Split(ocrText, @"\n\s*\n")
            .Select(b => b.Trim())
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .ToList();

        if (doubleNewlineSplit.Count > 1)
        {
            return doubleNewlineSplit;
        }

        // Fallback: scan line-by-line and split when a title pattern appears
        var blocks = new List<string>();
        var currentBlock = new List<string>();
        var lines = ocrText.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            bool looksLikeTitle = TitlePattern.IsMatch(trimmed) || GeneralTitlePattern.IsMatch(trimmed);

            if (looksLikeTitle && currentBlock.Count > 0)
            {
                // Start a new block — flush the current one
                blocks.Add(string.Join('\n', currentBlock));
                currentBlock.Clear();
            }

            currentBlock.Add(trimmed);
        }

        if (currentBlock.Count > 0)
        {
            blocks.Add(string.Join('\n', currentBlock));
        }

        // If we still only have one block, return it as-is so at least one
        // vacancy can be parsed from the whole image.
        if (blocks.Count == 0 && !string.IsNullOrWhiteSpace(ocrText))
        {
            blocks.Add(ocrText.Trim());
        }

        return blocks;
    }

    /// <summary>
    /// Attempts to parse a text block into a <see cref="JobVacancy"/>.
    /// Returns null if the block does not contain enough recognizable information.
    /// </summary>
    private JobVacancy? TryParseBlock(string block, string sourcePath, int index)
    {
        var lines = block.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0)
            return null;

        // Try to find a title
        var title = DetectTitle(lines);
        if (string.IsNullOrWhiteSpace(title))
        {
            logger.LogDebug("Block {Index} skipped — no recognizable job title found", index);
            return null;
        }

        var company = DetectCompany(lines, title);
        var (city, country, locationRemoteHint) = DetectLocation(block);
        var (salaryMin, salaryMax, currency) = DetectSalary(block);
        var remotePolicy = DetectRemotePolicy(block, locationRemoteHint);
        var seniority = DetectSeniority(block);
        var engagement = DetectEngagementType(block);
        var skills = DetectSkills(block);

        var vacancy = new JobVacancy
        {
            Id = $"image-{Path.GetFileNameWithoutExtension(sourcePath)}-{index}",
            Title = title,
            Company = company,
            City = city,
            Country = country,
            RemotePolicy = remotePolicy,
            EngagementType = engagement,
            SeniorityLevel = seniority,
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            SalaryCurrency = currency,
            RequiredSkills = skills,
            Description = block,
            SourcePlatform = "image-scan",
            ScrapedDate = DateTimeOffset.UtcNow
        };

        logger.LogDebug(
            "Parsed vacancy from block {Index}: {Vacancy}",
            index, vacancy);

        return vacancy;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Field detection helpers (private static)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Detects the job title from the lines of a listing block.
    /// Returns the first line that matches a known title pattern.
    /// </summary>
    private static string DetectTitle(List<string> lines)
    {
        // First pass: look for a .NET-specific title pattern
        foreach (var line in lines)
        {
            var match = TitlePattern.Match(line);
            if (match.Success)
            {
                // Return the full line as the title (it may contain useful prefix/suffix)
                return line.Length <= 120 ? line : line[..120];
            }
        }

        // Second pass: look for general engineering title patterns
        foreach (var line in lines)
        {
            if (GeneralTitlePattern.IsMatch(line))
            {
                return line.Length <= 120 ? line : line[..120];
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Detects the company name. Typically the line immediately after the title,
    /// excluding lines that look like platform UI elements.
    /// </summary>
    private static string DetectCompany(List<string> lines, string title)
    {
        bool foundTitle = false;

        foreach (var line in lines)
        {
            if (!foundTitle)
            {
                if (line.Equals(title, StringComparison.OrdinalIgnoreCase))
                    foundTitle = true;
                continue;
            }

            // Skip platform UI noise
            if (IsUiNoiseLine(line))
                continue;

            // Skip lines that look like locations (contain parentheses typical of "City (Remote)")
            if (LocationParenPattern.IsMatch(line))
                continue;

            // Skip lines that look like salary
            if (SalaryPattern.IsMatch(line))
                continue;

            // Skip very short lines that are likely tags or labels
            if (line.Length < 3)
                continue;

            // This is likely the company name
            return line.Length <= 100 ? line : line[..100];
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns true if the line contains platform UI noise that should be skipped.
    /// </summary>
    private static bool IsUiNoiseLine(string line)
    {
        foreach (var keyword in SkipLineKeywords)
        {
            if (line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Detects location information from the text block.
    /// Returns (city, country, remoteHintFromLocation).
    /// </summary>
    private static (string city, string country, string? remoteHint) DetectLocation(string text)
    {
        // Pattern 1: "City, State (Remote)" or "Country (Remote)"
        var parenMatch = LocationParenPattern.Match(text);
        if (parenMatch.Success)
        {
            var locationPart = parenMatch.Groups[1].Value.Trim();
            var qualifier = parenMatch.Groups[2].Value.Trim();

            string city = string.Empty;
            string country = string.Empty;

            if (locationPart.Contains(','))
            {
                var parts = locationPart.Split(',', 2);
                city = parts[0].Trim();
                country = parts[1].Trim();
            }
            else
            {
                // Single part — could be city or country
                country = locationPart;
            }

            return (city, country, qualifier);
        }

        // Pattern 2: "Company · Location · Remote" (LinkedIn-style)
        var dotMatch = LocationDotPattern.Match(text);
        if (dotMatch.Success && dotMatch.Groups.Count >= 3)
        {
            var locationPart = dotMatch.Groups[2].Value.Trim();
            var remoteHint = dotMatch.Groups.Count > 3 && dotMatch.Groups[3].Success
                ? dotMatch.Groups[3].Value.Trim()
                : null;

            string city = string.Empty;
            string country = locationPart;

            if (locationPart.Contains(','))
            {
                var parts = locationPart.Split(',', 2);
                city = parts[0].Trim();
                country = parts[1].Trim();
            }

            return (city, country, remoteHint);
        }

        return (string.Empty, string.Empty, null);
    }

    /// <summary>
    /// Detects salary information from the text block.
    /// Normalizes hourly and monthly rates to annual figures.
    /// Returns (minAnnual, maxAnnual, currencyCode).
    /// </summary>
    private static (decimal? min, decimal? max, string currency) DetectSalary(string text)
    {
        // Detect currency symbol first
        string currency = "USD";
        if (text.Contains('\u20ac')) // €
            currency = "EUR";
        else if (text.Contains('\u00a3')) // £
            currency = "GBP";

        // Try the K-notation pattern first (e.g., "$80K - $110K")
        var kMatch = SalaryKPattern.Match(text);
        if (kMatch.Success)
        {
            if (TryParseSalaryValue(kMatch.Groups[1].Value, out decimal kMin) &&
                TryParseSalaryValue(kMatch.Groups[2].Value, out decimal kMax))
            {
                return (kMin * 1_000m, kMax * 1_000m, currency);
            }
        }

        // General salary pattern
        var salaryMatch = SalaryPattern.Match(text);
        if (!salaryMatch.Success)
            return (null, null, currency);

        if (!TryParseSalaryValue(salaryMatch.Groups[1].Value, out decimal rawMin) ||
            !TryParseSalaryValue(salaryMatch.Groups[2].Value, out decimal rawMax))
        {
            return (null, null, currency);
        }

        // Check if values had K suffix in the original match
        var fullMatch = salaryMatch.Value;
        bool hasKSuffix = Regex.IsMatch(fullMatch, @"\d\s*[Kk]", RegexOptions.None);

        if (hasKSuffix)
        {
            rawMin *= 1_000m;
            rawMax *= 1_000m;
        }

        // Determine the period and normalize to annual
        var period = salaryMatch.Groups[3].Success
            ? salaryMatch.Groups[3].Value.ToLowerInvariant()
            : string.Empty;

        (decimal min, decimal max) = period switch
        {
            "hr" or "hour" => (rawMin * 2_080m, rawMax * 2_080m),
            "mo" or "month" => (rawMin * 12m, rawMax * 12m),
            _ => (rawMin, rawMax) // yr, year, or unspecified — assume annual
        };

        return (min, max, currency);
    }

    /// <summary>
    /// Parses a salary value string (possibly containing commas) into a decimal.
    /// </summary>
    private static bool TryParseSalaryValue(string value, out decimal result)
    {
        var cleaned = value.Replace(",", "").Trim();
        return decimal.TryParse(cleaned, out result);
    }

    /// <summary>
    /// Detects the remote work policy from the text block.
    /// The optional <paramref name="locationRemoteHint"/> comes from location parsing.
    /// </summary>
    private static RemotePolicy DetectRemotePolicy(string text, string? locationRemoteHint)
    {
        var combined = locationRemoteHint is not null
            ? $"{text} {locationRemoteHint}"
            : text;

        var lower = combined.ToLowerInvariant();

        if (lower.Contains("fully remote") || lower.Contains("100% remote"))
            return RemotePolicy.FullyRemote;

        if (lower.Contains("remote-friendly") || lower.Contains("remote friendly"))
            return RemotePolicy.RemoteFriendly;

        if (lower.Contains("hybrid"))
            return RemotePolicy.Hybrid;

        if (lower.Contains("on-site") || lower.Contains("on site") || lower.Contains("office"))
            return RemotePolicy.OnSite;

        // Standalone "remote" — check as a word boundary to avoid false positives
        if (Regex.IsMatch(lower, @"\bremote\b"))
            return RemotePolicy.FullyRemote;

        return RemotePolicy.Unknown;
    }

    /// <summary>
    /// Detects the seniority level from the text block.
    /// </summary>
    private static SeniorityLevel DetectSeniority(string text)
    {
        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff"))
            return SeniorityLevel.Principal;

        if (lower.Contains("lead") || lower.Contains("tech lead"))
            return SeniorityLevel.Lead;

        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr "))
            return SeniorityLevel.Senior;

        if (lower.Contains("mid-level") || lower.Contains("mid level") || lower.Contains("middle"))
            return SeniorityLevel.Middle;

        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("jr "))
            return SeniorityLevel.Junior;

        return SeniorityLevel.Unknown;
    }

    /// <summary>
    /// Detects the engagement type from the text block.
    /// </summary>
    private static EngagementType DetectEngagementType(string text)
    {
        var lower = text.ToLowerInvariant();

        // B2B / contractor patterns — check first since "contract" is common
        if (lower.Contains("b2b") || lower.Contains("contractor") ||
            lower.Contains("outside ir35") || lower.Contains("c2c") ||
            lower.Contains("1099") || lower.Contains("contract"))
            return EngagementType.ContractB2B;

        if (lower.Contains("freelance") || lower.Contains("project-based"))
            return EngagementType.Freelance;

        if (lower.Contains("inside ir35") || lower.Contains("paye"))
            return EngagementType.InsideIR35;

        if (lower.Contains("permanent") || lower.Contains("full-time employee") ||
            lower.Contains("fte only"))
            return EngagementType.Employment;

        return EngagementType.Unknown;
    }

    /// <summary>
    /// Extracts recognized technology skills from the text block.
    /// </summary>
    private static List<string> DetectSkills(string text)
    {
        var found = new List<string>();

        foreach (var skill in KnownSkills)
        {
            // Use case-insensitive contains for most skills,
            // but exact-case for short/ambiguous ones like "C#", "SQL", "REST"
            bool detected = skill.Length <= 4
                ? text.Contains(skill, StringComparison.Ordinal)
                : text.Contains(skill, StringComparison.OrdinalIgnoreCase);

            if (detected)
            {
                found.Add(skill);
            }
        }

        return found;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Result records
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Result of scanning a single image for job vacancies.
/// </summary>
public sealed record ImageScanResult(
    string ImagePath,
    string RawOcrText,
    IReadOnlyList<JobVacancy> Vacancies,
    IReadOnlyList<EligibilityAssessment> Assessments,
    double OcrConfidence,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Aggregated summary of scanning multiple images in a directory.
/// </summary>
public sealed record ImageScanSummary(
    int ImagesScanned,
    int TotalVacancies,
    int Eligible,
    int Ineligible,
    IReadOnlyList<ImageScanResult> Results);

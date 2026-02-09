using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using CareerIntel.Matching;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for creating, viewing, editing, and validating the user profile interactively.
/// Usage: career-intel profile {create|show|edit|add-skill|validate}
/// </summary>
public static class ProfileCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string DefaultProfilePath =>
        Path.Combine(Program.DataDirectory, "my-profile.json");

    public static Command Create()
    {
        var command = new Command("profile", "Create, view, and manage your career profile");

        command.AddCommand(CreateCreateCommand());
        command.AddCommand(CreateShowCommand());
        command.AddCommand(CreateEditCommand());
        command.AddCommand(CreateAddSkillCommand());
        command.AddCommand(CreateValidateCommand());

        return command;
    }

    // ──────────────────────────────────────────────────────────────
    //  Subcommand: profile create
    // ──────────────────────────────────────────────────────────────

    private static Command CreateCreateCommand()
    {
        var outputOption = new Option<string?>(
            "--output",
            description: "Output path for the profile JSON. Defaults to data/my-profile.json");

        var cmd = new Command("create", "Interactive wizard to create a new profile")
        {
            outputOption
        };

        cmd.SetHandler(ExecuteCreateAsync, outputOption);
        return cmd;
    }

    private static Task ExecuteCreateAsync(string? output)
    {
        var outputPath = output ?? DefaultProfilePath;

        PrintBanner("PROFILE CREATION WIZARD");
        Console.WriteLine();

        if (File.Exists(outputPath))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Warning: Profile already exists at {outputPath}");
            Console.ResetColor();
            var overwrite = ReadLine("  Overwrite? (y/N)", "N");
            if (!overwrite.Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  Aborted.");
                return Task.CompletedTask;
            }
            Console.WriteLine();
        }

        var profile = new UserProfile();

        // Step 1: Personal Info
        WizardCollectPersonalInfo(profile);

        // Step 2: Skills
        WizardCollectSkills(profile);

        // Step 3: Experience
        WizardCollectExperience(profile);

        // Step 4: Preferences
        WizardCollectPreferences(profile);

        // Save
        return SaveProfileAsync(profile, outputPath, isNew: true);
    }

    // ──────────────────────────────────────────────────────────────
    //  Subcommand: profile show
    // ──────────────────────────────────────────────────────────────

    private static Command CreateShowCommand()
    {
        var pathOption = new Option<string?>(
            "--path",
            description: "Path to profile JSON. Defaults to data/my-profile.json");

        var cmd = new Command("show", "Display your current profile summary")
        {
            pathOption
        };

        cmd.SetHandler(ExecuteShowAsync, pathOption);
        return cmd;
    }

    private static async Task ExecuteShowAsync(string? path)
    {
        var profilePath = path ?? DefaultProfilePath;
        var profile = await LoadProfileAsync(profilePath);
        if (profile is null) return;

        PrintBanner("YOUR CAREER PROFILE");
        Console.WriteLine();

        // Personal Info
        PrintSectionHeader("Personal Info");
        PrintField("Name", profile.Personal.Name);
        PrintField("Title", profile.Personal.Title);
        PrintField("Location", profile.Personal.Location);
        PrintField("Email", profile.Personal.Email);
        PrintField("LinkedIn", profile.Personal.LinkedInUrl);
        if (!string.IsNullOrWhiteSpace(profile.Personal.Summary))
            PrintField("Summary", profile.Personal.Summary);
        if (profile.Personal.TargetRoles.Count > 0)
            PrintField("Target Roles", string.Join(", ", profile.Personal.TargetRoles));
        Console.WriteLine();

        // Skills
        PrintSectionHeader($"Skills ({profile.Skills.Count})");
        if (profile.Skills.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  {"Skill",-22} {"Category",-14} {"Prof",5} {"Years",6}");
            Console.WriteLine($"  {new string('-', 52)}");
            Console.ResetColor();

            foreach (var skill in profile.Skills.OrderByDescending(s => s.ProficiencyLevel))
            {
                var filled = skill.ProficiencyLevel;
                var empty = 5 - filled;
                var bar = new string('\u2588', filled) + new string('\u2591', empty);

                var profColor = filled switch
                {
                    >= 4 => ConsoleColor.Green,
                    >= 3 => ConsoleColor.Yellow,
                    _ => ConsoleColor.Red
                };

                Console.Write("  ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"{skill.SkillName,-22} ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"{skill.Category,-14} ");
                Console.ForegroundColor = profColor;
                Console.Write($"{bar} {skill.ProficiencyLevel}/5 ");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"({skill.YearsOfExperience:F1}y)");
                Console.ResetColor();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  No skills added yet.");
            Console.ResetColor();
        }
        Console.WriteLine();

        // Experience
        PrintSectionHeader($"Experience ({profile.Experiences.Count})");
        if (profile.Experiences.Count > 0)
        {
            foreach (var exp in profile.Experiences)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"  {exp.Role}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" at ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(exp.Company);
                if (!string.IsNullOrWhiteSpace(exp.Duration))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($" ({exp.Duration})");
                }
                Console.WriteLine();
                Console.ResetColor();

                if (exp.TechStack.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"    Tech: {string.Join(", ", exp.TechStack)}");
                    Console.ResetColor();
                }

                foreach (var achievement in exp.Achievements)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"    * {achievement}");
                    Console.ResetColor();
                }

                if (!string.IsNullOrWhiteSpace(exp.Description))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"    {exp.Description}");
                    Console.ResetColor();
                }

                Console.WriteLine();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  No experience added yet.");
            Console.ResetColor();
            Console.WriteLine();
        }

        // Preferences
        PrintSectionHeader("Preferences");
        PrintField("Min Salary", $"${profile.Preferences.MinSalaryUsd:N0} USD");
        PrintField("Target Salary", $"${profile.Preferences.TargetSalaryUsd:N0} USD");
        PrintField("Remote Only", profile.Preferences.RemoteOnly ? "Yes" : "No");
        PrintField("Target Regions", string.Join(", ", profile.Preferences.TargetRegions));
        PrintField("Min Seniority", profile.Preferences.MinSeniority.ToString());
        if (profile.Preferences.ExcludeCompanies.Count > 0)
            PrintField("Excluded", string.Join(", ", profile.Preferences.ExcludeCompanies));
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Profile loaded from: {profilePath}");
        Console.WriteLine("══════════════════════════════════════════════════════════");
        Console.ResetColor();
    }

    // ──────────────────────────────────────────────────────────────
    //  Subcommand: profile edit
    // ──────────────────────────────────────────────────────────────

    private static Command CreateEditCommand()
    {
        var sectionOption = new Option<string>(
            "--section",
            description: "Section to edit: personal, skills, experience, preferences")
        { IsRequired = true };

        var pathOption = new Option<string?>(
            "--path",
            description: "Path to profile JSON. Defaults to data/my-profile.json");

        var cmd = new Command("edit", "Edit a specific section of your profile")
        {
            sectionOption,
            pathOption
        };

        cmd.SetHandler(ExecuteEditAsync, sectionOption, pathOption);
        return cmd;
    }

    private static async Task ExecuteEditAsync(string section, string? path)
    {
        var profilePath = path ?? DefaultProfilePath;
        var profile = await LoadProfileAsync(profilePath);
        if (profile is null) return;

        PrintBanner($"EDIT PROFILE: {section.ToUpperInvariant()}");
        Console.WriteLine();

        switch (section.ToLowerInvariant())
        {
            case "personal":
                WizardCollectPersonalInfo(profile);
                break;
            case "skills":
                WizardCollectSkills(profile);
                break;
            case "experience":
                WizardCollectExperience(profile);
                break;
            case "preferences":
                WizardCollectPreferences(profile);
                break;
            default:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Error: Unknown section '{section}'.");
                Console.WriteLine("  Valid sections: personal, skills, experience, preferences");
                Console.ResetColor();
                return;
        }

        await SaveProfileAsync(profile, profilePath, isNew: false);
    }

    // ──────────────────────────────────────────────────────────────
    //  Subcommand: profile add-skill
    // ──────────────────────────────────────────────────────────────

    private static Command CreateAddSkillCommand()
    {
        var nameOption = new Option<string>(
            "--name",
            description: "Skill name (e.g., C#, Docker, PostgreSQL)")
        { IsRequired = true };

        var categoryOption = new Option<string>(
            "--category",
            getDefaultValue: () => "Unknown",
            description: "Skill category (Language, Framework, Database, Cloud, DevOps, Testing, Architecture, Methodology, Tool, Soft)");

        var levelOption = new Option<int>(
            "--level",
            getDefaultValue: () => 3,
            description: "Proficiency level 1-5 (1=Beginner, 5=Expert)");

        var yearsOption = new Option<double>(
            "--years",
            getDefaultValue: () => 1,
            description: "Years of experience with this skill");

        var pathOption = new Option<string?>(
            "--path",
            description: "Path to profile JSON. Defaults to data/my-profile.json");

        var cmd = new Command("add-skill", "Quick-add a single skill to your profile")
        {
            nameOption,
            categoryOption,
            levelOption,
            yearsOption,
            pathOption
        };

        cmd.SetHandler(ExecuteAddSkillAsync, nameOption, categoryOption, levelOption, yearsOption, pathOption);
        return cmd;
    }

    private static async Task ExecuteAddSkillAsync(string name, string category, int level, double years, string? path)
    {
        var profilePath = path ?? DefaultProfilePath;
        var profile = await LoadProfileAsync(profilePath);
        if (profile is null) return;

        if (level < 1 || level > 5)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Error: Level must be between 1 and 5.");
            Console.ResetColor();
            return;
        }

        if (!Enum.TryParse<SkillCategory>(category, ignoreCase: true, out var parsedCategory))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Error: Unknown category '{category}'.");
            Console.WriteLine("  Valid categories: Language, Framework, Database, Cloud, DevOps, Testing, Architecture, Methodology, Tool, Soft");
            Console.ResetColor();
            return;
        }

        // Check for duplicate
        var existing = profile.Skills.FirstOrDefault(s =>
            s.SkillName.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.Category = parsedCategory;
            existing.ProficiencyLevel = level;
            existing.YearsOfExperience = years;
            existing.LastUsedDate = DateTimeOffset.UtcNow;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Updated existing skill: {name}");
            Console.ResetColor();
        }
        else
        {
            profile.Skills.Add(new SkillProfile
            {
                SkillName = name,
                Category = parsedCategory,
                ProficiencyLevel = level,
                YearsOfExperience = years,
                LastUsedDate = DateTimeOffset.UtcNow
            });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Added skill: {name} ({parsedCategory}, {level}/5, {years}y)");
            Console.ResetColor();
        }

        await SaveProfileAsync(profile, profilePath, isNew: false);
    }

    // ──────────────────────────────────────────────────────────────
    //  Subcommand: profile validate
    // ──────────────────────────────────────────────────────────────

    private static Command CreateValidateCommand()
    {
        var pathOption = new Option<string?>(
            "--path",
            description: "Path to profile JSON. Defaults to data/my-profile.json");

        var cmd = new Command("validate", "Check profile completeness and get recommendations")
        {
            pathOption
        };

        cmd.SetHandler(ExecuteValidateAsync, pathOption);
        return cmd;
    }

    private static async Task ExecuteValidateAsync(string? path)
    {
        var profilePath = path ?? DefaultProfilePath;
        var profile = await LoadProfileAsync(profilePath);
        if (profile is null) return;

        PrintBanner("PROFILE VALIDATION");
        Console.WriteLine();

        var checks = new List<(string Label, bool Passed, string Recommendation)>();

        // Name
        var hasName = !string.IsNullOrWhiteSpace(profile.Personal.Name);
        checks.Add(("Name present", hasName,
            "Add your full name to make your profile complete."));

        // Title
        var hasTitle = !string.IsNullOrWhiteSpace(profile.Personal.Title);
        checks.Add(("Title present", hasTitle,
            "Add a professional title (e.g., Senior .NET Developer)."));

        // Email
        var hasEmail = !string.IsNullOrWhiteSpace(profile.Personal.Email);
        checks.Add(("Email present", hasEmail,
            "Add your email address for application tracking."));

        // Location
        var hasLocation = !string.IsNullOrWhiteSpace(profile.Personal.Location);
        checks.Add(("Location present", hasLocation,
            "Add your location for region-based matching."));

        // Summary
        var hasSummary = !string.IsNullOrWhiteSpace(profile.Personal.Summary);
        checks.Add(("Summary present", hasSummary,
            "Add a professional summary for resume generation."));

        // Target roles
        var hasTargetRoles = profile.Personal.TargetRoles.Count > 0;
        checks.Add(("Target roles defined", hasTargetRoles,
            "Define target roles to improve matching accuracy."));

        // Skills (at least 5)
        var hasEnoughSkills = profile.Skills.Count >= 5;
        checks.Add(($"At least 5 skills ({profile.Skills.Count} found)", hasEnoughSkills,
            "Add more skills. Consider: C#, .NET, ASP.NET Core, SQL, Azure, Docker."));

        // Experience (at least 1)
        var hasExperience = profile.Experiences.Count >= 1;
        checks.Add(($"At least 1 experience ({profile.Experiences.Count} found)", hasExperience,
            "Add your work experience for resume generation and matching."));

        // Salary preferences
        var hasSalary = profile.Preferences.MinSalaryUsd > 0 || profile.Preferences.TargetSalaryUsd > 0;
        checks.Add(("Salary preferences set", hasSalary,
            "Set min/target salary for salary-based vacancy filtering."));

        // Target regions
        var hasRegions = profile.Preferences.TargetRegions.Count > 0;
        checks.Add(("Target regions defined", hasRegions,
            "Define target regions for location-based matching."));

        // Print results
        var passedCount = 0;
        foreach (var (label, passed, recommendation) in checks)
        {
            if (passed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  [PASS] {label}");
                Console.ResetColor();
                passedCount++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"  [FAIL] {label}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" - {recommendation}");
                Console.ResetColor();
            }
        }

        // Completeness percentage
        var percentage = (int)((double)passedCount / checks.Count * 100);
        Console.WriteLine();

        var percentColor = percentage switch
        {
            >= 80 => ConsoleColor.Green,
            >= 50 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };

        Console.ForegroundColor = percentColor;
        Console.WriteLine($"  Profile completeness: {percentage}% ({passedCount}/{checks.Count})");
        Console.ResetColor();

        if (percentage == 100)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Your profile is fully complete! Ready for matching.");
            Console.ResetColor();
        }
        else if (percentage >= 70)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Your profile is mostly complete. Address the items above for best results.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Your profile needs attention. Run 'profile edit --section <section>' to fill in missing data.");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("══════════════════════════════════════════════════════════");
        Console.ResetColor();
    }

    // ──────────────────────────────────────────────────────────────
    //  Wizard: Personal Info
    // ──────────────────────────────────────────────────────────────

    private static void WizardCollectPersonalInfo(UserProfile profile)
    {
        PrintStepHeader("Step 1/4", "Personal Info");

        profile.Personal.Name = ReadLine(
            "  Full name", NullIfEmpty(profile.Personal.Name));
        profile.Personal.Location = ReadLine(
            "  Location", NullIfEmpty(profile.Personal.Location) ?? "Ukraine");
        profile.Personal.Title = ReadLine(
            "  Professional title", NullIfEmpty(profile.Personal.Title) ?? "Senior .NET Developer");
        profile.Personal.Summary = ReadLine(
            "  Professional summary (one-liner)", NullIfEmpty(profile.Personal.Summary));

        var rolesDefault = profile.Personal.TargetRoles.Count > 0
            ? string.Join(", ", profile.Personal.TargetRoles)
            : "Senior .NET Developer, Backend Engineer, Software Architect";
        var rolesInput = ReadLine("  Target roles (comma-separated)", rolesDefault);
        profile.Personal.TargetRoles = ParseCommaSeparated(rolesInput);

        profile.Personal.LinkedInUrl = ReadLine(
            "  LinkedIn URL", NullIfEmpty(profile.Personal.LinkedInUrl));
        profile.Personal.Email = ReadLine(
            "  Email", NullIfEmpty(profile.Personal.Email));

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  Personal info saved.");
        Console.ResetColor();
        Console.WriteLine();
    }

    // ──────────────────────────────────────────────────────────────
    //  Wizard: Skills
    // ──────────────────────────────────────────────────────────────

    private static void WizardCollectSkills(UserProfile profile)
    {
        PrintStepHeader("Step 2/4", "Skills");

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Common .NET skills: C#, .NET, ASP.NET Core, Entity Framework,");
        Console.WriteLine("  SQL Server, Azure, Docker, Kubernetes, RabbitMQ, Redis,");
        Console.WriteLine("  PostgreSQL, Git, CI/CD, REST APIs, gRPC, Blazor, MAUI");
        Console.ResetColor();
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Enter skills one at a time. Press Enter on an empty name to finish.");
        Console.ResetColor();
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Categories: 1=Language, 2=Framework, 3=Database, 4=Cloud,");
        Console.WriteLine("              5=DevOps, 6=Testing, 7=Architecture, 8=Methodology,");
        Console.WriteLine("              9=Tool, 10=Soft");
        Console.ResetColor();
        Console.WriteLine();

        var skillCount = 0;
        while (true)
        {
            var skillName = ReadLine($"  Skill name (#{skillCount + 1})", null);
            if (string.IsNullOrWhiteSpace(skillName))
                break;

            var categoryInput = ReadLine("    Category (1-10)", "1");
            var category = ParseSkillCategory(categoryInput);

            var levelInput = ReadLine("    Proficiency (1=Beginner, 2=Basic, 3=Intermediate, 4=Advanced, 5=Expert)", "3");
            if (!int.TryParse(levelInput, out var level) || level < 1 || level > 5)
                level = 3;

            var yearsInput = ReadLine("    Years of experience", "1");
            if (!double.TryParse(yearsInput, out var years) || years < 0)
                years = 1;

            // Check for existing skill and update
            var existing = profile.Skills.FirstOrDefault(s =>
                s.SkillName.Equals(skillName, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.Category = category;
                existing.ProficiencyLevel = level;
                existing.YearsOfExperience = years;
                existing.LastUsedDate = DateTimeOffset.UtcNow;

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"    Updated: {skillName}");
                Console.ResetColor();
            }
            else
            {
                profile.Skills.Add(new SkillProfile
                {
                    SkillName = skillName,
                    Category = category,
                    ProficiencyLevel = level,
                    YearsOfExperience = years,
                    LastUsedDate = DateTimeOffset.UtcNow
                });

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"    Added: {skillName} ({category}, {level}/5, {years}y)");
                Console.ResetColor();
            }

            skillCount++;
            Console.WriteLine();
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Skills section complete. Total skills: {profile.Skills.Count}");
        Console.ResetColor();
        Console.WriteLine();
    }

    // ──────────────────────────────────────────────────────────────
    //  Wizard: Experience
    // ──────────────────────────────────────────────────────────────

    private static void WizardCollectExperience(UserProfile profile)
    {
        PrintStepHeader("Step 3/4", "Work Experience");

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Enter work experiences one at a time. Press Enter on an empty company to finish.");
        Console.ResetColor();
        Console.WriteLine();

        var expCount = 0;
        while (true)
        {
            var company = ReadLine($"  Company (#{expCount + 1})", null);
            if (string.IsNullOrWhiteSpace(company))
                break;

            var role = ReadLine("    Role/Title", "Software Developer");
            var duration = ReadLine("    Duration (e.g., 2020-2024)", null);

            var techStackInput = ReadLine("    Tech stack (comma-separated)", null);
            var techStack = ParseCommaSeparated(techStackInput);

            var achievement = ReadLine("    Key achievement (one line)", null);
            var achievements = string.IsNullOrWhiteSpace(achievement)
                ? new List<string>()
                : [achievement];

            // Parse start/end dates from duration if possible
            DateTimeOffset? startDate = null;
            DateTimeOffset? endDate = null;
            if (!string.IsNullOrWhiteSpace(duration))
            {
                var parts = duration.Split('-', StringSplitOptions.TrimEntries);
                if (parts.Length >= 1 && int.TryParse(parts[0].Trim(), out var startYear))
                    startDate = new DateTimeOffset(startYear, 1, 1, 0, 0, 0, TimeSpan.Zero);
                if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var endYear))
                    endDate = new DateTimeOffset(endYear, 1, 1, 0, 0, 0, TimeSpan.Zero);
            }

            profile.Experiences.Add(new Experience
            {
                Company = company,
                Role = role,
                Duration = duration ?? string.Empty,
                StartDate = startDate,
                EndDate = endDate,
                TechStack = techStack,
                Achievements = achievements
            });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"    Added: {role} at {company}");
            Console.ResetColor();

            expCount++;
            Console.WriteLine();
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Experience section complete. Total entries: {profile.Experiences.Count}");
        Console.ResetColor();
        Console.WriteLine();
    }

    // ──────────────────────────────────────────────────────────────
    //  Wizard: Preferences
    // ──────────────────────────────────────────────────────────────

    private static void WizardCollectPreferences(UserProfile profile)
    {
        PrintStepHeader("Step 4/4", "Preferences");

        var minSalaryInput = ReadLine("  Minimum salary (USD)",
            profile.Preferences.MinSalaryUsd > 0
                ? profile.Preferences.MinSalaryUsd.ToString("F0")
                : "60000");
        if (decimal.TryParse(minSalaryInput, out var minSalary))
            profile.Preferences.MinSalaryUsd = minSalary;

        var targetSalaryInput = ReadLine("  Target salary (USD)",
            profile.Preferences.TargetSalaryUsd > 0
                ? profile.Preferences.TargetSalaryUsd.ToString("F0")
                : "90000");
        if (decimal.TryParse(targetSalaryInput, out var targetSalary))
            profile.Preferences.TargetSalaryUsd = targetSalary;

        var remoteInput = ReadLine("  Remote only? (Y/n)",
            profile.Preferences.RemoteOnly ? "Y" : "n");
        profile.Preferences.RemoteOnly =
            !remoteInput.Equals("n", StringComparison.OrdinalIgnoreCase);

        var regionsDefault = profile.Preferences.TargetRegions.Count > 0
            ? string.Join(", ", profile.Preferences.TargetRegions)
            : "Ukraine, EU, US";
        var regionsInput = ReadLine("  Target regions (comma-separated)", regionsDefault);
        profile.Preferences.TargetRegions = ParseCommaSeparated(regionsInput);

        var seniorityDefault = profile.Preferences.MinSeniority != SeniorityLevel.Unknown
            ? profile.Preferences.MinSeniority.ToString()
            : "Senior";
        var seniorityInput = ReadLine(
            "  Min seniority (Intern, Junior, Middle, Senior, Lead, Architect, Principal)",
            seniorityDefault);
        if (Enum.TryParse<SeniorityLevel>(seniorityInput, ignoreCase: true, out var seniority))
            profile.Preferences.MinSeniority = seniority;

        var excludeDefault = profile.Preferences.ExcludeCompanies.Count > 0
            ? string.Join(", ", profile.Preferences.ExcludeCompanies)
            : null;
        var excludeInput = ReadLine("  Companies to exclude (comma-separated)", excludeDefault);
        profile.Preferences.ExcludeCompanies = ParseCommaSeparated(excludeInput);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  Preferences saved.");
        Console.ResetColor();
        Console.WriteLine();
    }

    // ──────────────────────────────────────────────────────────────
    //  Helpers: File I/O
    // ──────────────────────────────────────────────────────────────

    private static async Task<UserProfile?> LoadProfileAsync(string profilePath)
    {
        if (!File.Exists(profilePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Error: Profile not found at {profilePath}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Run 'profile create' to create a new profile.");
            Console.ResetColor();
            return null;
        }

        var json = await File.ReadAllTextAsync(profilePath);
        return JsonSerializer.Deserialize<UserProfile>(json, ReadOptions) ?? new UserProfile();
    }

    private static async Task SaveProfileAsync(UserProfile profile, string outputPath, bool isNew)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(isNew
            ? $"  Profile created at: {outputPath}"
            : $"  Profile updated at: {outputPath}");
        Console.ResetColor();
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Next steps:");
        Console.WriteLine("    career-intel profile show       View your profile");
        Console.WriteLine("    career-intel profile validate   Check completeness");
        Console.WriteLine("    career-intel scan               Scan for vacancies");
        Console.WriteLine("    career-intel match              Match against your profile");
        Console.ResetColor();
    }

    // ──────────────────────────────────────────────────────────────
    //  Helpers: Console I/O
    // ──────────────────────────────────────────────────────────────

    private static string ReadLine(string prompt, string? defaultValue)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(prompt);

        if (defaultValue is not null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($" [{defaultValue}]");
        }

        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(": ");
        Console.ResetColor();

        var input = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(input) ? (defaultValue ?? string.Empty) : input;
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static List<string> ParseCommaSeparated(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        return input
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static SkillCategory ParseSkillCategory(string input)
    {
        if (int.TryParse(input, out var num) && num >= 0 && num <= 10)
            return (SkillCategory)num;

        if (Enum.TryParse<SkillCategory>(input, ignoreCase: true, out var parsed))
            return parsed;

        return SkillCategory.Unknown;
    }

    // ──────────────────────────────────────────────────────────────
    //  Helpers: Display
    // ──────────────────────────────────────────────────────────────

    private static void PrintBanner(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("══════════════════════════════════════════════════════════");
        Console.WriteLine($"  {title,-54}");
        Console.WriteLine("══════════════════════════════════════════════════════════");
        Console.ResetColor();
    }

    private static void PrintStepHeader(string step, string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  --- {step}: {title} ---");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintSectionHeader(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  {title}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {new string('-', title.Length)}");
        Console.ResetColor();
    }

    private static void PrintField(string label, string value)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"  {label}: ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(string.IsNullOrWhiteSpace(value) ? "(not set)" : value);
        Console.ResetColor();
    }
}

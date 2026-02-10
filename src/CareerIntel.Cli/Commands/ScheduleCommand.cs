using System.CommandLine;
using System.Text.Json;
using CareerIntel.Core.Models;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for managing interview schedules and preparation time blocking.
/// Helps organize interview calendar, set reminders, and allocate prep time.
/// Usage: career-intel schedule [--list] [--add] [--prep]
/// </summary>
public static class ScheduleCommand
{
    public static Command Create()
    {
        var listOption = new Option<bool>(
            "--list",
            description: "List all upcoming interviews");

        var addOption = new Option<bool>(
            "--add",
            description: "Add a new interview to the schedule");

        var prepOption = new Option<bool>(
            "--prep",
            description: "Show preparation time blocks for upcoming interviews");

        var exportOption = new Option<bool>(
            "--export",
            description: "Export schedule to .ics calendar format");

        var command = new Command("schedule", "Manage interview schedules and preparation time")
        {
            listOption,
            addOption,
            prepOption,
            exportOption
        };

        command.SetHandler(ExecuteAsync, listOption, addOption, prepOption, exportOption);

        return command;
    }

    private static async Task ExecuteAsync(bool list, bool add, bool prep, bool export)
    {
        var schedulePath = Path.Combine(Program.DataDirectory, "interview-schedule.json");

        // Initialize if doesn't exist
        if (!File.Exists(schedulePath))
        {
            var emptySchedule = new InterviewSchedule { Interviews = [] };
            var json = JsonSerializer.Serialize(emptySchedule, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(schedulePath, json);
        }

        if (add)
        {
            await AddInterview(schedulePath);
        }
        else if (prep)
        {
            await ShowPrepSchedule(schedulePath);
        }
        else if (export)
        {
            await ExportToCalendar(schedulePath);
        }
        else
        {
            await ListInterviews(schedulePath);
        }
    }

    private static async Task AddInterview(string schedulePath)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n═══ Add Interview to Schedule ═══\n");
        Console.ResetColor();

        // Load applications to suggest
        var applicationsPath = Path.Combine(Program.DataDirectory, "applications.json");
        if (File.Exists(applicationsPath))
        {
            var appsJson = await File.ReadAllTextAsync(applicationsPath);
            var applications = JsonSerializer.Deserialize<List<JobApplication>>(appsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];

            var interviewApps = applications
                .Where(a => a.Status == ApplicationStatus.Interview)
                .OrderByDescending(a => a.ResponseDate ?? a.CreatedDate)
                .Take(5)
                .ToList();

            if (interviewApps.Any())
            {
                Console.WriteLine("Recent applications in interview stage:\n");
                var index = 1;
                foreach (var app in interviewApps)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  {index}. {app.VacancyTitle} at {app.Company}");
                    Console.ResetColor();
                    index++;
                }
                Console.WriteLine();
            }
        }

        Console.Write("Company: ");
        var company = Console.ReadLine() ?? "";

        Console.Write("Position: ");
        var position = Console.ReadLine() ?? "";

        Console.Write("Interview Type (Phone/Technical/Behavioral/System Design/Final): ");
        var interviewType = Console.ReadLine() ?? "Technical";

        Console.Write("Date (yyyy-MM-dd): ");
        var dateInput = Console.ReadLine() ?? "";
        if (!DateTime.TryParse(dateInput, out var date))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid date format.");
            Console.ResetColor();
            return;
        }

        Console.Write("Time (HH:mm): ");
        var timeInput = Console.ReadLine() ?? "10:00";
        if (!TimeSpan.TryParse(timeInput, out var time))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid time format.");
            Console.ResetColor();
            return;
        }

        var dateTime = date.Date + time;

        Console.Write("Duration (minutes, default 60): ");
        var durationInput = Console.ReadLine();
        var duration = int.TryParse(durationInput, out var d) ? d : 60;

        Console.Write("Interviewer name (optional): ");
        var interviewer = Console.ReadLine() ?? "";

        Console.Write("Meeting link (optional): ");
        var meetingLink = Console.ReadLine() ?? "";

        Console.Write("Notes (optional): ");
        var notes = Console.ReadLine() ?? "";

        // Load and update schedule
        var json = await File.ReadAllTextAsync(schedulePath);
        var schedule = JsonSerializer.Deserialize<InterviewSchedule>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new InterviewSchedule { Interviews = [] };

        var interview = new Interview
        {
            Id = Guid.NewGuid().ToString(),
            Company = company,
            Position = position,
            InterviewType = interviewType,
            ScheduledDate = dateTime,
            DurationMinutes = duration,
            Interviewer = interviewer,
            MeetingLink = meetingLink,
            Notes = notes,
            PrepTimeNeeded = CalculatePrepTime(interviewType),
            Status = "Scheduled"
        };

        schedule.Interviews.Add(interview);

        var updatedJson = JsonSerializer.Serialize(schedule, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(schedulePath, updatedJson);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n✓ Interview added to schedule!");
        Console.ResetColor();

        Console.WriteLine($"\nRecommended prep time: {interview.PrepTimeNeeded} hours");
        Console.WriteLine($"Start preparation by: {dateTime.AddHours(-interview.PrepTimeNeeded):yyyy-MM-dd HH:mm}");
    }

    private static async Task ListInterviews(string schedulePath)
    {
        var json = await File.ReadAllTextAsync(schedulePath);
        var schedule = JsonSerializer.Deserialize<InterviewSchedule>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new InterviewSchedule { Interviews = [] };

        var upcoming = schedule.Interviews
            .Where(i => i.ScheduledDate >= DateTime.Now && i.Status != "Completed" && i.Status != "Cancelled")
            .OrderBy(i => i.ScheduledDate)
            .ToList();

        var past = schedule.Interviews
            .Where(i => i.ScheduledDate < DateTime.Now || i.Status == "Completed")
            .OrderByDescending(i => i.ScheduledDate)
            .Take(5)
            .ToList();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n═══ Interview Schedule ═══\n");
        Console.ResetColor();

        if (upcoming.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("UPCOMING INTERVIEWS:\n");
            Console.ResetColor();

            foreach (var interview in upcoming)
            {
                PrintInterview(interview, true);
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("No upcoming interviews scheduled.");
            Console.ResetColor();
        }

        if (past.Any())
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("PAST INTERVIEWS (last 5):\n");
            Console.ResetColor();

            foreach (var interview in past)
            {
                PrintInterview(interview, false);
            }
        }

        Console.WriteLine("\nCommands:");
        Console.WriteLine("  career-intel schedule --add      Add new interview");
        Console.WriteLine("  career-intel schedule --prep     View preparation schedule");
        Console.WriteLine("  career-intel schedule --export   Export to calendar");
    }

    private static async Task ShowPrepSchedule(string schedulePath)
    {
        var json = await File.ReadAllTextAsync(schedulePath);
        var schedule = JsonSerializer.Deserialize<InterviewSchedule>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new InterviewSchedule { Interviews = [] };

        var upcoming = schedule.Interviews
            .Where(i => i.ScheduledDate >= DateTime.Now && i.Status == "Scheduled")
            .OrderBy(i => i.ScheduledDate)
            .ToList();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n═══ Interview Preparation Schedule ═══\n");
        Console.ResetColor();

        if (!upcoming.Any())
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("No upcoming interviews to prepare for.");
            Console.ResetColor();
            return;
        }

        var now = DateTime.Now;

        foreach (var interview in upcoming)
        {
            var daysUntil = (interview.ScheduledDate - now).TotalDays;
            var prepDeadline = interview.ScheduledDate.AddHours(-interview.PrepTimeNeeded);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{interview.Company} - {interview.Position}");
            Console.ResetColor();

            Console.WriteLine($"Interview: {interview.ScheduledDate:ddd, MMM dd yyyy HH:mm} ({interview.InterviewType})");
            Console.WriteLine($"Time until interview: {FormatTimeUntil(daysUntil)}");

            Console.ForegroundColor = daysUntil < 1 ? ConsoleColor.Red :
                                      daysUntil < 3 ? ConsoleColor.Yellow :
                                      ConsoleColor.Green;
            Console.WriteLine($"Preparation needed: {interview.PrepTimeNeeded} hours");
            Console.WriteLine($"Start prep by: {prepDeadline:ddd, MMM dd HH:mm}");
            Console.ResetColor();

            // Suggested prep tasks
            var prepTasks = GetPrepTasks(interview.InterviewType);
            Console.WriteLine("\nPrep checklist:");
            foreach (var task in prepTasks)
            {
                Console.WriteLine($"  □ {task}");
            }

            if (!string.IsNullOrEmpty(interview.MeetingLink))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\nMeeting link: {interview.MeetingLink}");
                Console.ResetColor();
            }

            Console.WriteLine();
        }

        // Time blocking suggestion
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("═══ Suggested Time Blocks ═══\n");
        Console.ResetColor();

        foreach (var interview in upcoming.Take(3))
        {
            var prepStart = interview.ScheduledDate.AddHours(-interview.PrepTimeNeeded);
            var blocksNeeded = (int)Math.Ceiling(interview.PrepTimeNeeded / 2.0); // 2-hour blocks

            Console.WriteLine($"{interview.Company} - {interview.InterviewType}:");

            for (int i = 0; i < blocksNeeded; i++)
            {
                var blockStart = prepStart.AddHours(i * 2);
                var blockEnd = blockStart.AddHours(2);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  Block {i + 1}: {blockStart:ddd HH:mm} - {blockEnd:HH:mm}");
                Console.ResetColor();
            }

            Console.WriteLine();
        }
    }

    private static async Task ExportToCalendar(string schedulePath)
    {
        var json = await File.ReadAllTextAsync(schedulePath);
        var schedule = JsonSerializer.Deserialize<InterviewSchedule>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new InterviewSchedule { Interviews = [] };

        var upcoming = schedule.Interviews
            .Where(i => i.ScheduledDate >= DateTime.Now && i.Status == "Scheduled")
            .OrderBy(i => i.ScheduledDate)
            .ToList();

        if (!upcoming.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No upcoming interviews to export.");
            Console.ResetColor();
            return;
        }

        var icsContent = GenerateICS(upcoming);

        var outputPath = Path.Combine(Program.DataDirectory, "interviews.ics");
        await File.WriteAllTextAsync(outputPath, icsContent);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✓ Calendar exported to: {outputPath}");
        Console.ResetColor();
        Console.WriteLine("\nImport this file into:");
        Console.WriteLine("  • Google Calendar");
        Console.WriteLine("  • Outlook");
        Console.WriteLine("  • Apple Calendar");
        Console.WriteLine("  • Any other iCal-compatible calendar app");
    }

    private static void PrintInterview(Interview interview, bool isUpcoming)
    {
        var color = isUpcoming ? ConsoleColor.White : ConsoleColor.DarkGray;

        Console.ForegroundColor = color;
        Console.WriteLine($"[{interview.ScheduledDate:ddd, MMM dd HH:mm}] {interview.Company} - {interview.Position}");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Type: {interview.InterviewType} | Duration: {interview.DurationMinutes} min | Status: {interview.Status}");

        if (!string.IsNullOrEmpty(interview.Interviewer))
            Console.WriteLine($"  Interviewer: {interview.Interviewer}");

        if (!string.IsNullOrEmpty(interview.MeetingLink))
            Console.WriteLine($"  Link: {interview.MeetingLink}");

        if (isUpcoming)
        {
            var timeUntil = interview.ScheduledDate - DateTime.Now;
            if (timeUntil.TotalHours < 24)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ⚠ Interview in {timeUntil.TotalHours:F1} hours!");
            }
            else if (timeUntil.TotalDays < 3)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  ⚠ Interview in {timeUntil.TotalDays:F1} days");
            }
        }

        Console.ResetColor();
        Console.WriteLine();
    }

    private static int CalculatePrepTime(string interviewType)
    {
        return interviewType.ToLowerInvariant() switch
        {
            "phone" or "recruiter" => 1,
            "behavioral" => 3,
            "technical" => 6,
            "system design" => 8,
            "final" or "onsite" => 10,
            _ => 4
        };
    }

    private static List<string> GetPrepTasks(string interviewType)
    {
        return interviewType.ToLowerInvariant() switch
        {
            "phone" or "recruiter" => new List<string>
            {
                "Review company background and recent news",
                "Prepare elevator pitch (2-minute intro)",
                "List 3 questions to ask recruiter",
                "Clarify role expectations and timeline"
            },
            "behavioral" => new List<string>
            {
                "Prepare 5-7 STAR stories covering different competencies",
                "Review company values and match to your experiences",
                "Practice common questions (leadership, conflict, failure)",
                "Prepare thoughtful questions about team culture"
            },
            "technical" => new List<string>
            {
                "Review data structures & algorithms",
                "Practice 10-15 LeetCode problems (medium difficulty)",
                "Review core language concepts and syntax",
                "Prepare questions about tech stack",
                "Test your dev environment if live coding"
            },
            "system design" => new List<string>
            {
                "Review system design fundamentals (scalability, CAP theorem)",
                "Practice designing 2-3 common systems (URL shortener, chat app)",
                "Prepare clarifying questions framework",
                "Review company's tech stack and scale",
                "Practice whiteboarding or diagramming"
            },
            "final" or "onsite" => new List<string>
            {
                "Review all previous interview feedback",
                "Prepare questions for leadership/hiring manager",
                "Research team structure and projects",
                "Prepare salary negotiation talking points",
                "Plan travel and logistics",
                "Get good rest the night before"
            },
            _ => new List<string>
            {
                "Research the company",
                "Review job description",
                "Prepare questions",
                "Practice common interview questions"
            }
        };
    }

    private static string FormatTimeUntil(double days)
    {
        if (days < 1)
            return $"{days * 24:F1} hours";
        else if (days < 7)
            return $"{days:F1} days";
        else
            return $"{days / 7:F1} weeks";
    }

    private static string GenerateICS(List<Interview> interviews)
    {
        var ics = new System.Text.StringBuilder();
        ics.AppendLine("BEGIN:VCALENDAR");
        ics.AppendLine("VERSION:2.0");
        ics.AppendLine("PRODID:-//CareerIntel//Interview Scheduler//EN");
        ics.AppendLine("CALSCALE:GREGORIAN");
        ics.AppendLine("METHOD:PUBLISH");

        foreach (var interview in interviews)
        {
            var start = interview.ScheduledDate;
            var end = start.AddMinutes(interview.DurationMinutes);

            ics.AppendLine("BEGIN:VEVENT");
            ics.AppendLine($"UID:{interview.Id}@careerintel.local");
            ics.AppendLine($"DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}");
            ics.AppendLine($"DTSTART:{start:yyyyMMddTHHmmss}");
            ics.AppendLine($"DTEND:{end:yyyyMMddTHHmmss}");
            ics.AppendLine($"SUMMARY:{interview.InterviewType} Interview - {interview.Company}");
            ics.AppendLine($"DESCRIPTION:{interview.Position} at {interview.Company}\\nType: {interview.InterviewType}");

            if (!string.IsNullOrEmpty(interview.Interviewer))
                ics.AppendLine($"DESCRIPTION:Interviewer: {interview.Interviewer}");

            if (!string.IsNullOrEmpty(interview.MeetingLink))
                ics.AppendLine($"LOCATION:{interview.MeetingLink}");

            ics.AppendLine("STATUS:CONFIRMED");

            // Add reminder 1 hour before
            ics.AppendLine("BEGIN:VALARM");
            ics.AppendLine("TRIGGER:-PT1H");
            ics.AppendLine("ACTION:DISPLAY");
            ics.AppendLine($"DESCRIPTION:Interview in 1 hour: {interview.Company}");
            ics.AppendLine("END:VALARM");

            ics.AppendLine("END:VEVENT");
        }

        ics.AppendLine("END:VCALENDAR");

        return ics.ToString();
    }

    private sealed class InterviewSchedule
    {
        public List<Interview> Interviews { get; set; } = [];
    }

    private sealed class Interview
    {
        public string Id { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string InterviewType { get; set; } = string.Empty;
        public DateTime ScheduledDate { get; set; }
        public int DurationMinutes { get; set; }
        public string Interviewer { get; set; } = string.Empty;
        public string MeetingLink { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public int PrepTimeNeeded { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}

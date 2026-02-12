using System.Text;
using CareerIntel.Core.Models;

namespace CareerIntel.Web.Services;

/// <summary>
/// Generates CSV content from domain model collections for download.
/// Escapes fields per RFC 4180 (quoted fields containing commas, quotes, newlines).
/// </summary>
public static class CsvExportService
{
    /// <summary>
    /// Exports job vacancies to CSV format.
    /// </summary>
    public static string ExportVacancies(IEnumerable<JobVacancy> vacancies)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Title,Company,Country,City,Remote Policy,Seniority,Engagement,Salary Min,Salary Max,Currency,Required Skills,Platform,Posted Date,URL");

        foreach (var v in vacancies)
        {
            sb.Append(Escape(v.Title)).Append(',');
            sb.Append(Escape(v.Company)).Append(',');
            sb.Append(Escape(v.Country)).Append(',');
            sb.Append(Escape(v.City)).Append(',');
            sb.Append(Escape(v.RemotePolicy.ToString())).Append(',');
            sb.Append(Escape(v.SeniorityLevel.ToString())).Append(',');
            sb.Append(Escape(v.EngagementType.ToString())).Append(',');
            sb.Append(v.SalaryMin?.ToString("F0") ?? "").Append(',');
            sb.Append(v.SalaryMax?.ToString("F0") ?? "").Append(',');
            sb.Append(Escape(v.SalaryCurrency ?? "USD")).Append(',');
            sb.Append(Escape(string.Join("; ", v.RequiredSkills ?? []))).Append(',');
            sb.Append(Escape(v.SourcePlatform)).Append(',');
            sb.Append(v.PostedDate.ToString("yyyy-MM-dd")).Append(',');
            sb.AppendLine(Escape(v.Url));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Exports job applications to CSV format.
    /// </summary>
    public static string ExportApplications(IEnumerable<JobApplication> applications)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Company,Vacancy Title,Status,Applied Date,Notes,Vacancy URL");

        foreach (var a in applications)
        {
            sb.Append(Escape(a.Company)).Append(',');
            sb.Append(Escape(a.VacancyTitle)).Append(',');
            sb.Append(Escape(a.Status.ToString())).Append(',');
            sb.Append(a.AppliedDate?.ToString("yyyy-MM-dd") ?? "").Append(',');
            sb.Append(Escape(a.Notes ?? "")).Append(',');
            sb.AppendLine(Escape(a.VacancyUrl ?? ""));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Exports salary data to CSV format.
    /// </summary>
    public static string ExportSalaryData(IEnumerable<JobVacancy> vacancies)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Title,Company,Seniority,Remote Policy,Salary Min,Salary Max,Currency,Hourly,Platform,URL");

        foreach (var v in vacancies.Where(v => v.SalaryMin.HasValue || v.SalaryMax.HasValue))
        {
            sb.Append(Escape(v.Title)).Append(',');
            sb.Append(Escape(v.Company)).Append(',');
            sb.Append(Escape(v.SeniorityLevel.ToString())).Append(',');
            sb.Append(Escape(v.RemotePolicy.ToString())).Append(',');
            sb.Append(v.SalaryMin?.ToString("F0") ?? "").Append(',');
            sb.Append(v.SalaryMax?.ToString("F0") ?? "").Append(',');
            sb.Append(Escape(v.SalaryCurrency ?? "USD")).Append(',');
            sb.Append(v.IsHourlyRate ? "Yes" : "No").Append(',');
            sb.Append(Escape(v.SourcePlatform)).Append(',');
            sb.AppendLine(Escape(v.Url));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Exports interview feedback to CSV format.
    /// </summary>
    public static string ExportInterviewFeedback(IEnumerable<InterviewFeedback> feedbacks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Company,Date,Round,Outcome,Difficulty,Strong Areas,Weak Areas,Feedback");

        foreach (var f in feedbacks)
        {
            sb.Append(Escape(f.Company)).Append(',');
            sb.Append(f.InterviewDate.ToString("yyyy-MM-dd")).Append(',');
            sb.Append(Escape(f.Round)).Append(',');
            sb.Append(Escape(f.Outcome)).Append(',');
            sb.Append(f.DifficultyRating).Append(',');
            sb.Append(Escape(string.Join("; ", f.StrongAreas ?? []))).Append(',');
            sb.Append(Escape(string.Join("; ", f.WeakAreas ?? []))).Append(',');
            sb.AppendLine(Escape(f.Feedback ?? ""));
        }

        return sb.ToString();
    }

    /// <summary>
    /// RFC 4180 CSV field escaping: wraps field in quotes if it contains
    /// commas, quotes, or newlines. Internal quotes are doubled.
    /// </summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }
}

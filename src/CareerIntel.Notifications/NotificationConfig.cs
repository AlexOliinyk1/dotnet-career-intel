namespace CareerIntel.Notifications;

public sealed class NotificationConfig
{
    public TelegramConfig? Telegram { get; set; }
    public EmailConfig? Email { get; set; }
    public double MinScoreToNotify { get; set; } = 60;
    public bool NotifyOnNewHighPayingRoles { get; set; } = true;
}

public sealed class TelegramConfig
{
    public string BotToken { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class EmailConfig
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

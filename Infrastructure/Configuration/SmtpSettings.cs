namespace Infrastructure.Configuration;

public class SmtpSettings
{
    public const string SectionName = "Smtp";
    
    public string Host { get; set; } = default!;
    public int Port { get; set; }
    public string FromEmail { get; set; } = default!;
    public string FromName { get; set; } = default!;
    public string Username { get; set; } = default!;
    public string Password { get; set; } = default!;
}

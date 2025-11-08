using System.Net;
using System.Net.Mail;
using System.Text;
using Application.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly string _username;
    private readonly string _password;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        string smtpHost,
        int smtpPort,
        string fromEmail,
        string fromName,
        string username,
        string password,
        ILogger<SmtpEmailSender> logger)
    {
        _smtpHost = smtpHost;
        _smtpPort = smtpPort;
        _fromEmail = fromEmail;
        _fromName = fromName;
        _username = username;
        _password = password;
        _logger = logger;
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        try
        {
            _logger.LogInformation("Attempting to send email to {Email} via {Host}:{Port}", email, _smtpHost, _smtpPort);
            
            using var client = new SmtpClient(_smtpHost, _smtpPort)
            {
                Credentials = new NetworkCredential(_username, _password),
                EnableSsl = true,
                Timeout = 30000,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(_fromEmail, _fromName),
                Subject = subject,
                Body = htmlMessage,
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8,
                HeadersEncoding = Encoding.UTF8
            };
            mailMessage.To.Add(email);

            _logger.LogDebug("Sending email with subject: {Subject}", subject);
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            await client.SendMailAsync(mailMessage, cts.Token);
            
            _logger.LogInformation("Email sent successfully to {Email}", email);
        }
        catch (SmtpException smtpEx)
        {
            _logger.LogError(smtpEx, "SMTP Error sending email to {Email}. StatusCode: {StatusCode}", 
                email, smtpEx.StatusCode);
            throw new Exception($"SMTP Error: {smtpEx.Message} (Status: {smtpEx.StatusCode})", smtpEx);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Email sending to {Email} timed out after 35 seconds", email);
            throw new Exception("Email sending timed out. Please check SMTP server connection.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}. Error type: {ErrorType}", 
                email, ex.GetType().Name);
            throw new Exception($"Failed to send email: {ex.Message}", ex);
        }
    }

    public async Task SendTemplateEmailAsync(string email, string templateId, object templateData)
    {
        _logger.LogInformation("Sending template email. Template: {TemplateId}, Recipient: {Email}", 
            templateId, email);
        
        string htmlContent;
        string subject;

        switch (templateId)
        {
            case "confirm-email":
                var confirmationLink = GetPropertyValue<string>(templateData, "confirmationLink");
                htmlContent = EmailTemplates.GetConfirmEmailTemplate(confirmationLink);
                subject = "Confirm Your Email - Sard Novels";
                break;

            case "reset-password":
                var resetPasswordLink = GetPropertyValue<string>(templateData, "resetPasswordLink");
                htmlContent = EmailTemplates.GetResetPasswordTemplate(resetPasswordLink);
                subject = "Reset Your Password - Sard Novels";
                break;

            default:
                _logger.LogError("Unknown template ID: {TemplateId}", templateId);
                throw new ArgumentException($"Unknown template ID: {templateId}");
        }

        await SendEmailAsync(email, subject, htmlContent);
    }

    private T GetPropertyValue<T>(object obj, string propertyName)
    {
        var property = obj.GetType().GetProperty(propertyName);
        if (property == null)
        {
            throw new ArgumentException($"Property '{propertyName}' not found in template data");
        }

        var value = property.GetValue(obj);
        if (value is T typedValue)
        {
            return typedValue;
        }

        throw new ArgumentException($"Property '{propertyName}' is not of type {typeof(T).Name}");
    }
}

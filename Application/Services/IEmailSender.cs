namespace Application.Services;

public interface IEmailSender
{
    Task SendEmailAsync(string email, string subject, string htmlMessage);
    Task SendTemplateEmailAsync(string email, string templateId, object templateData);
}

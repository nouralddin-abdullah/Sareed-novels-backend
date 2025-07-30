

using System.Net.Mail;
using Application.Services;
using SendGrid.Helpers.Mail;
using SendGrid;

namespace Infrastructure.Services;

public class SendGridEmailSender(string apiKey, string fromEmail, string fromName) : IEmailSender
{
    private readonly string _apiKey = apiKey;
    private readonly string _fromEmail = fromEmail;
    private readonly string _fromName = fromName;

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var client = new SendGridClient(_apiKey);
        var from = new EmailAddress(_fromEmail, _fromName);
        var to = new EmailAddress(email);
        var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent: null, htmlContent: htmlMessage);
        await client.SendEmailAsync(msg);
    }

    public async Task SendTemplateEmailAsync(string email, string templateId, object templateData)
    {
        var client = new SendGridClient(_apiKey);
        var from = new EmailAddress(_fromEmail, _fromName);
        var to = new EmailAddress(email);

        var msg = new SendGridMessage
        {
            From = from,
            TemplateId = templateId
        };
        msg.AddTo(to);
        msg.SetTemplateData(templateData);

        var response = await client.SendEmailAsync(msg);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Body.ReadAsStringAsync();
            // Log or throw for diagnostics
            throw new Exception($"SendGrid failed: {response.StatusCode} - {body}");
        }
    }
}

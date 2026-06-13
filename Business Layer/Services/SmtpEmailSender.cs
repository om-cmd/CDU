using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using IEmailSender = Analysis_Web.Services.IEmailSender;

namespace Business_Layer.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(bool Success, string Message)> SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        var host = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
        var port = int.TryParse(_configuration["Email:SmtpPort"], out var parsedPort) ? parsedPort : 587;
        var fromEmail = _configuration["Email:FromEmail"] ?? "parladrayamajhi89@gmail.com";
        var fromName = _configuration["Email:FromName"] ?? "Crime Analysis";
        var username = _configuration["Email:Username"] ?? fromEmail;
        var password = _configuration["Email:AppPassword"];

        if (string.IsNullOrWhiteSpace(password))
        {
            var message = "Email app password is not configured. Add Email:AppPassword in appsettings.Development.json or user secrets.";
            _logger.LogWarning(message);
            return (false, message);
        }

        using var smtp = new SmtpClient(host, port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(username, password)
        };

        using var mail = new MailMessage
        {
            From = new MailAddress(fromEmail, fromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        mail.To.Add(toEmail);

        try
        {
            await smtp.SendMailAsync(mail);
            return (true, "Email sent.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            return (false, ex.Message);
        }
    }
}

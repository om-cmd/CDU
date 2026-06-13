namespace Analysis_Web.Services;

public interface IEmailSender
{
    Task<(bool Success, string Message)> SendEmailAsync(string toEmail, string subject, string htmlBody);
}

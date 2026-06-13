using System.Security.Cryptography;
using System.Text;
using Analysis_Web.Services;
using Core_Layer.HelperMethod;
using Core_Layer.ViewModels;
using Domain_Layer.DbModels;
using Microsoft.EntityFrameworkCore;

namespace Business_Layer.Services;

public class CommunicationService : ICommunicationService
{
    private const int OtpMinutes = 3;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailSender _emailSender;

    public CommunicationService(IUnitOfWork unitOfWork, IEmailSender emailSender)
    {
        _unitOfWork = unitOfWork;
        _emailSender = emailSender;
    }

    public async Task<(bool Success, string Message)> SendPasswordResetOtpAsync(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await _unitOfWork.Users.FirstOrDefaultAsync(x => x.Email.ToLower() == normalized && !x.Deleted);
        if (user == null)
            return (false, "No user found with that email address.");

        var code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        var otp = new PasswordResetOtp
        {
            UserAccountId = user.UserAccountId,
            Email = user.Email,
            CodeHash = HashCode(code),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(OtpMinutes),
            Purpose = "PasswordReset"
        };

        _unitOfWork._db.PasswordResetOtps.Add(otp);
        await _unitOfWork.SaveChangesAsync();

        var body = BuildTemplate(
            "Password Reset OTP",
            $"Your one-time password reset code is <strong style=\"font-size:22px;letter-spacing:4px;\">{code}</strong>.<br/>This code expires in {OtpMinutes} minutes.");
        var sent = await _emailSender.SendEmailAsync(user.Email, "Crime Analysis Password Reset OTP", body);
        return sent.Success
            ? (true, $"OTP sent to {MaskEmail(user.Email)}. It expires in {OtpMinutes} minutes.")
            : (false, sent.Message);
    }

    public async Task<(bool Success, string Message)> VerifyPasswordResetOtpAsync(string email, string code)
    {
        var otp = await GetValidOtp(email, code);
        return otp == null
            ? (false, "OTP is invalid or expired. Please request a new code.")
            : (true, "OTP verified. You can reset your password now.");
    }

    public async Task<(bool Success, string Message)> ResetPasswordAsync(ResetPasswordDto dto)
    {
        var otp = await GetValidOtp(dto.Email, dto.OtpCode);
        if (otp == null)
            return (false, "OTP is invalid or expired. Please request a new code.");

        var user = await _unitOfWork.Users.FirstOrDefaultAsync(x => x.UserAccountId == otp.UserAccountId);
        if (user == null)
            return (false, "User account was not found.");

        user.Password = StaticMethods.HashPassword(dto.NewPassword).Item2;
        user.UpdatedAt = DateTime.UtcNow;
        otp.UsedAtUtc = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        return (true, "Password reset successfully. Please sign in with your new password.");
    }

    public async Task<CommunicationIndexDto> GetCommunicationIndexAsync(int currentUserId, string currentUserType)
    {
        var users = await _unitOfWork.Users
            .Where(x => !x.Deleted && x.IsActive)
            .OrderBy(x => x.FullName)
            .Select(x => new UserSelectDto
            {
                UserAccountId = x.UserAccountId,
                FullName = x.FullName,
                Email = x.Email,
                UserType = x.UserType.ToString()
            })
            .ToListAsync();

        var emails = await _unitOfWork._db.EmailMessages
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(50)
            .Select(x => new SentEmailListItemDto
            {
                EmailMessageId = x.EmailMessageId,
                Title = x.Title,
                Subject = x.Subject,
                Body = x.Body,
                CreatedAtUtc = x.CreatedAtUtc,
                CreatedByEmail = x.CreatedByEmail,
                RecipientCount = x.Recipients.Count
            })
            .ToListAsync();

        var notifications = await _unitOfWork._db.UserNotifications
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(50)
            .Select(x => new SentNotificationListItemDto
            {
                UserNotificationId = x.UserNotificationId,
                Title = x.Title,
                Message = x.Message,
                CreatedAtUtc = x.CreatedAtUtc,
                CreatedByEmail = x.CreatedByEmail,
                RecipientCount = x.Recipients.Count
            })
            .ToListAsync();

        return new CommunicationIndexDto
        {
            Users = users,
            Emails = emails,
            Notifications = notifications,
            CanManage = IsSuperAdmin(currentUserType)
        };
    }

    public async Task<(bool Success, string Message)> SendEmailAsync(EmailComposeDto dto, int senderUserId, string senderEmail)
    {
        var recipients = await ResolveRecipients(dto.SendToAll, dto.SelectedUserIds);
        if (recipients.Count == 0)
            return (false, "Select at least one active user or choose Send to all.");

        var message = new EmailMessage
        {
            Title = dto.Title,
            Subject = dto.Subject,
            Body = dto.Body,
            CompanyName = "Crime Analysis",
            SendToAll = dto.SendToAll,
            CreatedByUserId = senderUserId,
            CreatedByEmail = senderEmail,
            CreatedAtUtc = DateTime.UtcNow,
            Recipients = recipients.Select(x => new EmailMessageRecipient
            {
                UserAccountId = x.UserAccountId,
                Email = x.Email
            }).ToList()
        };

        _unitOfWork._db.EmailMessages.Add(message);
        await _unitOfWork.SaveChangesAsync();

        foreach (var recipient in message.Recipients)
        {
            var html = BuildTemplate(dto.Title, dto.Body);
            var sent = await _emailSender.SendEmailAsync(recipient.Email, dto.Subject, html);
            recipient.Sent = sent.Success;
            recipient.ErrorMessage = sent.Success ? null : sent.Message[..Math.Min(500, sent.Message.Length)];
            recipient.SentAtUtc = sent.Success ? DateTime.UtcNow : null;
        }

        await _unitOfWork.SaveChangesAsync();
        return (true, $"Email created for {recipients.Count} recipient(s).");
    }

    public async Task<(bool Success, string Message)> SendNotificationAsync(NotificationComposeDto dto, int senderUserId, string senderEmail)
    {
        var recipients = await ResolveRecipients(dto.SendToAll, dto.SelectedUserIds);
        if (recipients.Count == 0)
            return (false, "Select at least one active user or choose Send to all.");

        var notification = new UserNotification
        {
            Title = dto.Title,
            Message = dto.Message,
            CompanyName = "Crime Analysis",
            SendToAll = dto.SendToAll,
            CreatedByUserId = senderUserId,
            CreatedByEmail = senderEmail,
            CreatedAtUtc = DateTime.UtcNow,
            Recipients = recipients.Select(x => new UserNotificationRecipient
            {
                UserAccountId = x.UserAccountId
            }).ToList()
        };

        _unitOfWork._db.UserNotifications.Add(notification);
        await _unitOfWork.SaveChangesAsync();
        return (true, $"Notification sent to {recipients.Count} user(s).");
    }

    public async Task<(bool Success, string Message)> DeleteEmailAsync(long id)
    {
        var item = await _unitOfWork._db.EmailMessages.FindAsync(id);
        if (item == null) return (false, "Email record not found.");
        item.IsDeleted = true;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
        return (true, "Email record deleted.");
    }

    public async Task<(bool Success, string Message)> DeleteNotificationAsync(long id)
    {
        var item = await _unitOfWork._db.UserNotifications.FindAsync(id);
        if (item == null) return (false, "Notification not found.");
        item.IsDeleted = true;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
        return (true, "Notification deleted.");
    }

    public async Task<(bool Success, string Message)> UpdateEmailAsync(EmailComposeDto dto)
    {
        if (!dto.EmailMessageId.HasValue) return (false, "Email id is required.");
        var item = await _unitOfWork._db.EmailMessages.FindAsync(dto.EmailMessageId.Value);
        if (item == null || item.IsDeleted) return (false, "Email record not found.");
        item.Title = dto.Title;
        item.Subject = dto.Subject;
        item.Body = dto.Body;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
        return (true, "Email record updated.");
    }

    public async Task<(bool Success, string Message)> UpdateNotificationAsync(NotificationComposeDto dto)
    {
        if (!dto.UserNotificationId.HasValue) return (false, "Notification id is required.");
        var item = await _unitOfWork._db.UserNotifications.FindAsync(dto.UserNotificationId.Value);
        if (item == null || item.IsDeleted) return (false, "Notification not found.");
        item.Title = dto.Title;
        item.Message = dto.Message;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
        return (true, "Notification updated.");
    }

    public async Task<int> GetUnreadNotificationCountAsync(int userId)
    {
        return await _unitOfWork._db.UserNotificationRecipients
            .CountAsync(x => x.UserAccountId == userId && x.ReadAtUtc == null && x.Notification != null && !x.Notification.IsDeleted);
    }

    private async Task<PasswordResetOtp?> GetValidOtp(string email, string code)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var hash = HashCode(code.Trim());
        return await _unitOfWork._db.PasswordResetOtps
            .Where(x => x.Email.ToLower() == normalized
                        && x.CodeHash == hash
                        && x.UsedAtUtc == null
                        && x.ExpiresAtUtc >= DateTime.UtcNow)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync();
    }

    private async Task<List<ApplicationUser>> ResolveRecipients(bool sendToAll, List<int> selectedIds)
    {
        var query = _unitOfWork.Users.Where(x => !x.Deleted && x.IsActive);
        if (!sendToAll)
            query = query.Where(x => selectedIds.Contains(x.UserAccountId));
        return await query.OrderBy(x => x.Email).ToListAsync();
    }

    private static string BuildTemplate(string title, string body)
    {
        return $$"""
<!doctype html>
<html>
<body style="margin:0;background:#f3f6fb;font-family:Segoe UI,Arial,sans-serif;color:#1f2937;">
  <table width="100%" cellpadding="0" cellspacing="0" style="background:#f3f6fb;padding:28px 0;">
    <tr><td align="center">
      <table width="640" cellpadding="0" cellspacing="0" style="background:#ffffff;border:1px solid #e5eaf2;border-radius:10px;overflow:hidden;">
        <tr>
          <td style="background:#111827;color:#ffffff;padding:18px 24px;">
            <div style="font-size:12px;text-transform:uppercase;letter-spacing:1px;color:#93c5fd;">Crime Analysis</div>
            <h1 style="margin:6px 0 0;font-size:22px;">{{WebUtilityHtmlEncode(title)}}</h1>
          </td>
        </tr>
        <tr>
          <td style="padding:24px;font-size:15px;line-height:1.65;">
            {{body}}
          </td>
        </tr>
        <tr>
          <td style="padding:14px 24px;background:#f8fafc;color:#64748b;font-size:12px;">
            Sent by Crime Analysis on {{DateTime.Now:yyyy-MM-dd HH:mm}}.
          </td>
        </tr>
      </table>
    </td></tr>
  </table>
</body>
</html>
""";
    }

    private static string HashCode(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }

    private static string MaskEmail(string email)
    {
        var parts = email.Split('@');
        if (parts.Length != 2 || parts[0].Length <= 2) return email;
        return $"{parts[0][0]}***{parts[0][^1]}@{parts[1]}";
    }

    private static bool IsSuperAdmin(string userType)
        => string.Equals(userType, "SuperAdmin", StringComparison.OrdinalIgnoreCase);

    private static string WebUtilityHtmlEncode(string value)
        => System.Net.WebUtility.HtmlEncode(value);
}

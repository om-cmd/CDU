using Core_Layer.ViewModels;

namespace Analysis_Web.Services;

public interface ICommunicationService
{
    Task<(bool Success, string Message)> SendPasswordResetOtpAsync(string email);
    Task<(bool Success, string Message)> VerifyPasswordResetOtpAsync(string email, string code);
    Task<(bool Success, string Message)> ResetPasswordAsync(ResetPasswordDto dto);
    Task<CommunicationIndexDto> GetCommunicationIndexAsync(int currentUserId, string currentUserType);
    Task<(bool Success, string Message)> SendEmailAsync(EmailComposeDto dto, int senderUserId, string senderEmail);
    Task<(bool Success, string Message)> SendNotificationAsync(NotificationComposeDto dto, int senderUserId, string senderEmail);
    Task<(bool Success, string Message)> UpdateEmailAsync(EmailComposeDto dto);
    Task<(bool Success, string Message)> UpdateNotificationAsync(NotificationComposeDto dto);
    Task<(bool Success, string Message)> DeleteEmailAsync(long id);
    Task<(bool Success, string Message)> DeleteNotificationAsync(long id);
    Task<int> GetUnreadNotificationCountAsync(int userId);
    Task<List<NotificationMenuItemDto>> GetRecentNotificationsAsync(int userId, int take = 6);
    Task CreateAuditNotificationAsync(
        string title,
        string message,
        int actorUserId,
        string actorEmail,
        IReadOnlyCollection<int>? recipientUserIds = null,
        string? actionUrl = null);
}

using System.ComponentModel.DataAnnotations;

namespace Core_Layer.ViewModels;

public class ForgotPasswordRequestDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class VerifyOtpDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(6, MinimumLength = 6)]
    public string OtpCode { get; set; } = string.Empty;
}

public class ResetPasswordDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(6, MinimumLength = 6)]
    public string OtpCode { get; set; } = string.Empty;

    [Required, MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;

    [Required, Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class UserSelectDto
{
    public int UserAccountId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string UserType { get; set; } = string.Empty;
}

public class EmailComposeDto
{
    public long? EmailMessageId { get; set; }

    [Required, MaxLength(180)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    public bool SendToAll { get; set; }

    public List<int> SelectedUserIds { get; set; } = new();
}

public class NotificationComposeDto
{
    public long? UserNotificationId { get; set; }

    [Required, MaxLength(180)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Message { get; set; } = string.Empty;

    public bool SendToAll { get; set; }

    public List<int> SelectedUserIds { get; set; } = new();
}

public class CommunicationIndexDto
{
    public List<UserSelectDto> Users { get; set; } = new();
    public List<SentEmailListItemDto> Emails { get; set; } = new();
    public List<SentNotificationListItemDto> Notifications { get; set; } = new();
    public bool CanManage { get; set; }
}

public class SentEmailListItemDto
{
    public long EmailMessageId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int RecipientCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedByEmail { get; set; } = string.Empty;
}

public class SentNotificationListItemDto
{
    public long UserNotificationId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int RecipientCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedByEmail { get; set; } = string.Empty;
}

public class NotificationMenuItemDto
{
    public long NotificationId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
    public string SentBy { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
    public bool IsRead { get; set; }
}

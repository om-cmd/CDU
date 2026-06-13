using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain_Layer.DbModels;

public class UserNotificationRecipient
{
    [Key]
    public long UserNotificationRecipientId { get; set; }

    public long UserNotificationId { get; set; }

    public int UserAccountId { get; set; }

    public DateTime? ReadAtUtc { get; set; }

    [ForeignKey(nameof(UserNotificationId))]
    public UserNotification? Notification { get; set; }

    [ForeignKey(nameof(UserAccountId))]
    public ApplicationUser? User { get; set; }
}

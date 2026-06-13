using System.ComponentModel.DataAnnotations;

namespace Domain_Layer.DbModels;

public class UserNotification
{
    [Key]
    public long UserNotificationId { get; set; }

    [Required, MaxLength(180)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Message { get; set; } = string.Empty;

    [MaxLength(120)]
    public string CompanyName { get; set; } = "Crime Analysis";

    public bool SendToAll { get; set; }

    public int CreatedByUserId { get; set; }

    [MaxLength(255)]
    public string CreatedByEmail { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public bool IsDeleted { get; set; }

    public ICollection<UserNotificationRecipient> Recipients { get; set; } = new List<UserNotificationRecipient>();
}

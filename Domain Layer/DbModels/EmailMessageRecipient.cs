using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain_Layer.DbModels;

public class EmailMessageRecipient
{
    [Key]
    public long EmailMessageRecipientId { get; set; }

    public long EmailMessageId { get; set; }

    public int UserAccountId { get; set; }

    [Required, MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    public bool Sent { get; set; }

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    public DateTime? SentAtUtc { get; set; }

    [ForeignKey(nameof(EmailMessageId))]
    public EmailMessage? EmailMessage { get; set; }

    [ForeignKey(nameof(UserAccountId))]
    public ApplicationUser? User { get; set; }
}

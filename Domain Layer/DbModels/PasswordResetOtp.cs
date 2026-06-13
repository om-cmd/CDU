using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain_Layer.DbModels;

public class PasswordResetOtp
{
    [Key]
    public long PasswordResetOtpId { get; set; }

    public int UserAccountId { get; set; }

    [Required, MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string CodeHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UsedAtUtc { get; set; }

    [MaxLength(80)]
    public string? Purpose { get; set; } = "PasswordReset";

    [ForeignKey(nameof(UserAccountId))]
    public ApplicationUser? User { get; set; }
}

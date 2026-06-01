namespace Domain_Layer.DbModels;

public class UserRole
{
    public long Id { get; set; }
    public int UserAccountId { get; set; }
    public long RoleId { get; set; }
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; }

    public virtual ApplicationUser Users { get; set; }
    public virtual Role Roles { get; set; }
}
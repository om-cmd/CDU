namespace Domain_Layer.DbModels;

public class UserRole
{
    public long Id { get; set; }
    public long UserAccountId { get; set; }
    public int RoleId { get; set; }
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; }

    public virtual ApplicationUser Users { get; set; }
    public virtual Role Roles { get; set; }
}
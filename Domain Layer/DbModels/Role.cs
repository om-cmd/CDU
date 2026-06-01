namespace Domain_Layer.DbModels;

public class Role
{
    public long RoleId { get; set; }
    public string RoleName { get; set; }
    public string RoleDescription { get; set; }
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime ModifiedOn { get; set; }
    public string Status{ get; set; }
    public string ModifiedBy { get; set; }
    
    public virtual ICollection<RolePermission> RolePermissions { get; set; }
    public virtual ICollection<UserRole> UserRoles { get; set; }
}
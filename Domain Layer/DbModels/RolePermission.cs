namespace Domain_Layer.DbModels;

public class RolePermission
{
    public int Id { get; set; }
    public int PermissionId { get; set; }
    public long RoleId { get; set; }
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; }

    public virtual Role Roles { get; set; }
    public virtual Permission Permissions { get; set; }
}

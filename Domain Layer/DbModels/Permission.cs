namespace Domain_Layer.DbModels;

public class Permission
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }
    public string MenuName { get; set; }
    public string Controller { get; set; }
    public string ActionName { get; set; }
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; }
}
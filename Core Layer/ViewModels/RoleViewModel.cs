using System.ComponentModel.DataAnnotations;
using Domain_Layer.DbModels;

namespace Core_Layer.ViewModels;

public class RoleViewModel
{
    public long RoleId { get; set; }
    public string RoleName { get; set; }
    public string RoleDescription { get; set; }
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime ModifiedOn { get; set; }
    public string Status{ get; set; }
    public string ModifiedBy { get; set; }
}
public class AddRoleViewModel
{
    [Required(ErrorMessage = "Role name is required")]
    [MinLength(3, ErrorMessage = "Role name must be at least 3 characters long")]
    public string RoleName { get; set; }
    [Required(ErrorMessage = "Role description is required")]
    [MinLength(3, ErrorMessage = "Role description must be at least 3 characters long")]
    public string RoleDescription { get; set; }
}
public class AddRolePermissionViewModel
{
    public long RoleId{get;set;}
    public string RoleName{get;set;}
    public string RoleDescription{get;set;}
    public List<RolePermissionViewModel> RolePermissions{get;set;}
}
public class ChangePermission
{
    public long RoleId { get; set; }
    public int PermissionId { get; set; }
    public bool Selected { get; set; }
}
public class RolePermissionViewModel
{
    public int Id { get; set; }
    public int PermissionId { get; set; }
    public string Slug { get; set; }
    public string ControllerName { get; set; }
    public string ActionName { get; set; }
    public int RoleId { get; set; }
    public DateTime CreatedDate { get; set; }
    public bool IsChecked { get; set; }
    public string CreatedBy { get; set; }
}
public class AddRolePermission
{
    public int RoleId{get;set;}
    public int PermissionId{get;set;}
    public bool IsChecked{get;set;}
}
using Core_Layer.ViewModels;

namespace Analysis_Web.Services;

public interface IRoleService
{
    Task<(string Message,string Token,bool Success)> AddRole(AddRoleViewModel model);
    Task<(string Message,string Token,bool Success,List<RolePermissionViewModel> Permission)> GetRolePermission(long roleId);
    Task<(string Message, string Token, bool Success,List<RoleViewModel> role)> GetAllRoles();
    Task<(string Message, string Token, bool Success)> UpdateRolePermission(ChangePermission model);
}
using Analysis_Web.Services;
using Core_Layer.ViewModels;
using Domain_Layer.DbModels;
using Microsoft.EntityFrameworkCore;

namespace Business_Layer.Services;

public class RoleService : IRoleService
{
    public readonly IUnitOfWork _unitOfWork;
    public RoleService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }
    public async Task<(string Message, string Token, bool Success)> AddRole(AddRoleViewModel model)
    {
        try
        {
            if (_unitOfWork._db.Roles.Any(x => x.RoleName == model.RoleName))
            {
                return ("Role Already Exists", "1", false);
            }

            _unitOfWork._db.Roles.Add(new Role()
            {
                RoleName =  model.RoleName,
                RoleDescription = model.RoleDescription,
                 CreatedBy = _unitOfWork.CurrentUser.UserName,
                 CreatedOn = DateTime.UtcNow,
                 Status = "Active",
                 ModifiedBy = _unitOfWork.CurrentUser.UserName,
                 ModifiedOn = DateTime.UtcNow,
            });
            _unitOfWork._db.SaveChanges();
            
            return ("Role Added", "00", true);
        }
        catch (Exception e)
        {
            return ("Unable to add role", "1", false);
        }
    }

public async Task<(string Message, string Token, bool Success, List<RolePermissionViewModel> Permission)> GetRolePermission(long roleId)
{
    try
    {
        if (roleId == 0)
        {
            return ("Unable to get permission list", "1", false, new List<RolePermissionViewModel>());
        }

        var currentUserId = _unitOfWork.CurrentUser.UserAccountId;

        var userRole = await _unitOfWork._db.UserRoles
            .FirstOrDefaultAsync(x => x.UserAccountId == currentUserId);

        if (userRole == null)
        {
            return ("Unable to get permission list", "1", false, new List<RolePermissionViewModel>());
        }

        var userPermissions = await _unitOfWork._db.RolePermissions
            .Include(x => x.Roles)
            .Include(x => x.Permissions)
            .Where(x => x.RoleId == userRole.RoleId)
            .ToListAsync();

        var selectedRolePermissions = await _unitOfWork._db.RolePermissions
            .Include(x => x.Roles)
            .Include(x => x.Permissions)
            .Where(x => x.RoleId == roleId)
            .ToListAsync();

        var list = userPermissions
            .Where(x => x.Permissions != null)
            .Select(x => x.Permissions)
            .Select(x => new RolePermissionViewModel
            {
                CreatedBy = x.CreatedBy,
                ActionName = x.ActionName,
                ControllerName = x.Controller,
                CreatedDate = x.CreatedDate,
                IsChecked = false,
                PermissionId = x.Id,
                Slug = x.Slug
            })
            .ToList();

        foreach (var item in selectedRolePermissions.Where(x => x.Permissions != null).Select(x => x.Permissions))
        {
            var existingPermission = list.FirstOrDefault(x => x.PermissionId == item.Id);

            if (existingPermission != null)
            {
                existingPermission.IsChecked = true;
            }
            else
            {
                list.Add(new RolePermissionViewModel
                {
                    CreatedBy = item.CreatedBy,
                    ActionName = item.ActionName,
                    ControllerName = item.Controller,
                    CreatedDate = item.CreatedDate,
                    IsChecked = true,
                    PermissionId = item.Id,
                    Slug = item.Slug
                });
            }
        }

        return ("List Fetched Successfully!!!", "00", true, list);
    }
    catch (Exception)
    {
        return ("Unable to get permission list", "1", false, new List<RolePermissionViewModel>());
    }
}

    public async Task<(string Message, string Token, bool Success,List<RoleViewModel> role)> GetAllRoles()
    {
        try
        {
            var roles = _unitOfWork._db.Roles.ToList();
            var list = new List<RoleViewModel>();
            if (roles.Any())
            {
                list = roles.Select(x => new RoleViewModel()
                {
CreatedBy =  x.CreatedBy,
RoleId =  x.RoleId,
RoleName = x.RoleName,
RoleDescription = x.RoleDescription,
CreatedOn =  x.CreatedOn,
Status =   x.Status,
                }).ToList();
            }
            return ("Role List Fetched Succesfully", "00", true,list);

        }
        catch (Exception e)
        {
            return ("Unable to get role list", "1", false,null);

        }
    }

    public async Task<(string Message, string Token, bool Success)> UpdateRolePermission(ChangePermission model)
    {
        try
        {
            if (!_unitOfWork._db.Roles.Any(x => x.RoleId == model.RoleId))
            {
                return ("Unable to get role", "1", false);
            }

            if (!_unitOfWork._db.Permissions.Any(x => x.Id == model.PermissionId))
            {
                return ("Unable to get permission", "1", false);
            }

            var existingPermission = _unitOfWork._db.RolePermissions
                .FirstOrDefault(x =>
                    x.RoleId == model.RoleId &&
                    x.PermissionId == model.PermissionId);

            switch (model.Selected)
            {
                case true when existingPermission != null:
                    return ("Permission is already assigned", "1", false);

                case true:
                    _unitOfWork._db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = model.RoleId,
                        PermissionId = model.PermissionId,
                        CreatedBy = _unitOfWork.CurrentUser?.UserName,
                        CreatedDate = DateTime.UtcNow
                    });

                    await _unitOfWork._db.SaveChangesAsync();
                    return ("Permission Added Successfully", "00", true);

                case false when existingPermission == null:
                    return ("This role doesn't have this permission", "1", false);

                case false:
                    _unitOfWork._db.RolePermissions.Remove(existingPermission);
                    await _unitOfWork._db.SaveChangesAsync();
                    return ("Permission Removed Successfully", "00", true);
            }
        }
        catch (Exception ex)
        {
            return ("Unable to get role list", "1", false);
        }
    }
}
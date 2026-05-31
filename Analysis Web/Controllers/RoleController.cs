using Analysis_Web.Services;
using Business_Layer.Middleware;
using Core_Layer.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Analysis_Web.Controllers;
[Authorize]
public class RoleController : Controller
{
    private readonly IRoleService _roleService;

    public RoleController(IRoleService roleService)
    {
        _roleService = roleService;
    }
    [HttpGet]
    [AuthorizePermission("RoleList")]
    public async Task<IActionResult> Index()
    {
        var response =await _roleService.GetAllRoles();
        return View(response.role);
    }

    [HttpGet]
    [AuthorizePermission("AddRole")]
    public IActionResult AddRole()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> AddRole(AddRoleViewModel model)
    {
        if (ModelState.IsValid)
        {
            var response =await _roleService.AddRole(model);
            if(response.Success)
            {
                return RedirectToAction("Index");
            }
            else
            {
                return View(model);
            }
            
        }
        else
        {
            ViewBag.Error ="PLEASE FILL THE FORM PROPErLY!!!";
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult>ModifyPermission(long roleId)
    {
        var response = await _roleService.GetRolePermission(roleId);
        ViewBag.RoleId = roleId;
        return View(response.Permission);
    }
    [HttpPost]
    public async Task<IActionResult> ChangePermission([FromBody] ChangePermission model)
    {
        var result = await _roleService.UpdateRolePermission(model);

        return Json(new
        {
            success = result.Success,
            message = result.Message
        });
    }
}
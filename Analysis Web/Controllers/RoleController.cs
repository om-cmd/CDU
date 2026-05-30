using Business_Layer.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Analysis_Web.Controllers;
[Authorize]
public class RoleController : Controller
{
    [HttpGet]
    [AuthorizePermission("RoleList")]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    [AuthorizePermission("AddRole")]
    public IActionResult AddRole()
    {
        return View();
    }
}
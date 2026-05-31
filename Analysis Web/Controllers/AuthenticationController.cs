using Analysis_Web.Services;
using Domain_Layer.Database;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Core_Layer.HelperMethod;
using Core_Layer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace YourApp.Controllers
{
    public class AuthenticationController : Controller
    {
        private readonly AnalysisDbContext _context;
        private readonly IAuthenticationService _authenticationService;

        public AuthenticationController(AnalysisDbContext context, IAuthenticationService authenticationService)
        {
            _context = context;
            _authenticationService = authenticationService;
        }

        [HttpGet]
        public IActionResult Login(string returnUrl)
        {
            if (User.Identity.IsAuthenticated && User.Identity != null)
            {
                return RedirectToAction("Index", "Home");
            }
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginDto dto, string returnUrl)
        {
                var response = await _authenticationService.Login(dto);
                if (response.Success == true)
                {
                    return RedirectToAction("Index", "Home");
                }
                else
                {
                    ViewData["ReturnUrl"] = returnUrl;
                    ViewBag.Error = response.Message;
                    return View(dto);
                }
            
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterDto model)
        {
            if (ModelState.IsValid)
            {
                var response = await _authenticationService.RegisterUserAsync(model);
                if (response.Success)
                {
                    return RedirectToAction("Index", "Home");
                }
                else
                {
                    return View(model);
                }
            }
            else
            {
                return View(model);
            }
        }

        [HttpGet]
        public  IActionResult LogOut()
        {
            _authenticationService.Logout();
            return RedirectToAction("Login", "Authentication");
        }
    }
}
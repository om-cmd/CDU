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
        private readonly ICommunicationService _communicationService;

        public AuthenticationController(
            AnalysisDbContext context,
            IAuthenticationService authenticationService,
            ICommunicationService communicationService)
        {
            _context = context;
            _authenticationService = authenticationService;
            _communicationService = communicationService;
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

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View(new ForgotPasswordRequestDto());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordRequestDto dto)
        {
            if (!ModelState.IsValid) return View(dto);
            var result = await _communicationService.SendPasswordResetOtpAsync(dto.Email);
            if (!result.Success)
            {
                ViewBag.Error = result.Message;
                return View(dto);
            }

            TempData["Info"] = result.Message;
            return RedirectToAction("ResetPassword", new { email = dto.Email });
        }

        [HttpGet]
        public IActionResult ResetPassword(string email)
        {
            return View(new ResetPasswordDto { Email = email ?? string.Empty });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
        {
            if (!ModelState.IsValid) return View(dto);
            var result = await _communicationService.ResetPasswordAsync(dto);
            if (!result.Success)
            {
                ViewBag.Error = result.Message;
                return View(dto);
            }

            TempData["Success"] = result.Message;
            return RedirectToAction("Login");
        }
    }
}

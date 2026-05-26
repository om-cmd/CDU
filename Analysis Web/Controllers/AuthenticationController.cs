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

        public AuthenticationController(AnalysisDbContext context)
        {
            _context = context;
        }


        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginDto model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
                return View(model);

            var user = await _context.ApplicationUsers
                .FirstOrDefaultAsync(u =>
                    u.Email == model.Email ||
                    u.UserName == model.UserName);

            if (user == null ||
                !HashPassword.Verify(model.Password, user.Password))
            {
                ModelState.AddModelError("", "Invalid login attempt.");
                return View(model);
            }

            if (!user.IsActive)
            {
                ModelState.AddModelError("", "Your account is inactive.");
                return View(model);
            }

            HttpContext.Session.SetInt32("UserId", user.UserAccountId);
            HttpContext.Session.SetString("UserType", user.UserType.ToString());
            HttpContext.Session.SetString("FullName", user.FullName);

            user.LastLoginAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(returnUrl)
                && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
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
            if (!ModelState.IsValid)
                return View(model);

            var result = await CreateUser(model, UserType.User);

            if (!result.Success)
            {
                ModelState.AddModelError("", result.Message);
                return View(model);
            }

            TempData["Success"] = "Account created successfully.";

            return RedirectToAction(nameof(Login));
        }


        [HttpGet]
        public IActionResult AdminRegister()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminRegister(RegisterDto model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var result = await CreateUser(model, model.UserType);

            if (!result.Success)
            {
                ModelState.AddModelError("", result.Message);
                return View(model);
            }

            TempData["Success"] = "Internal user created successfully.";

            return RedirectToAction("Index", "Home");
        }


        private async Task<(bool Success, string Message)> CreateUser(
            RegisterDto model,
            UserType userType)
        {
            bool exists = await _context.ApplicationUsers.AnyAsync(u =>
                u.Email == model.Email ||
                u.UserName == model.UserName);

            if (exists)
            {
                return (false, "Email or Username already exists.");
            }

            var user = new ApplicationUser
            {
                FullName = model.FullName,
                UserName = model.UserName,
                Email = model.Email,

                Password = HashPassword.Hash(model.Password),

                Contact = model.Contact,
                Department = model.Department,
                DateOfBirth = model.DateOfBirth,
                Address = model.Address,
                City = model.City,
                State = model.State,
                Country = model.Country,
                PostalCode = model.PostalCode,
                Gender = model.Gender,
                ImageUrl = model.ImageUrl,

                UserType = userType,

                IsActive = true,
                IsValidated = false,
                PhoneValidated = false,
                EmailConfirmedStatus = false,

                CreatedAt = DateTime.UtcNow
            };

            _context.ApplicationUsers.Add(user);

            await _context.SaveChangesAsync();

            return (true, "");
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();

            TempData["Info"] = "Logged out successfully.";

            return RedirectToAction(nameof(Login));
        }
    }
}
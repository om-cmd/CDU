using Analysis_Web.Services;
using Domain_Layer.Database;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Core_Layer.HelperMethod;
using Core_Layer.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace YourApp.Controllers
{
    public class AuthenticationController : Controller
    {
        private readonly AnalysisDbContext _context;
        private readonly IAuthenticationService _authenticationService;
        private readonly ICommunicationService _communicationService;
        private readonly IEmailSender _emailSender;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<AuthenticationController> _logger;

        public AuthenticationController(
            AnalysisDbContext context,
            IAuthenticationService authenticationService,
            ICommunicationService communicationService,
            IEmailSender emailSender,
            IWebHostEnvironment environment,
            ILogger<AuthenticationController> logger)
        {
            _context = context;
            _authenticationService = authenticationService;
            _communicationService = communicationService;
            _emailSender = emailSender;
            _environment = environment;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login(string returnUrl)
        {
            if (User.Identity?.IsAuthenticated == true)
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
            ValidateRegistrationFiles(model);
            if (model.DateOfBirth.HasValue && model.DateOfBirth.Value.Date >= DateTime.UtcNow.Date)
                ModelState.AddModelError(nameof(model.DateOfBirth), "Date of birth must be in the past.");

            if (!ModelState.IsValid)
                return View(model);

            string? evidenceDirectory = null;
            var registrationSaved = false;
            try
            {
                var saved = await SaveRegistrationEvidenceAsync(model);
                evidenceDirectory = saved.DirectoryPath;
                var response = await _authenticationService.RegisterUserAsync(model, saved.Evidence);
                if (!response.Success || !response.UserId.HasValue)
                {
                    DeleteEvidenceDirectory(evidenceDirectory);
                    ModelState.AddModelError(string.Empty, response.Message);
                    return View(model);
                }

                registrationSaved = true;
                var userId = response.UserId.Value;
                try
                {
                    var administrators = await _context.ApplicationUsers
                        .AsNoTracking()
                        .Where(x => !x.Deleted
                                    && x.IsActive
                                    && x.ApprovalStatus == AccountApprovalStatus.Approved
                                    && (x.UserType == UserType.Admin || x.UserType == UserType.SuperAdmin))
                        .Select(x => new { x.UserAccountId, x.Email, x.FullName })
                        .ToListAsync();

                    if (administrators.Count > 0)
                    {
                        var reviewPath = Url.Action(
                            "Details",
                            "RegistrationApproval",
                            new { id = userId }) ?? $"/RegistrationApproval/Details/{userId}";
                        try
                        {
                            await _communicationService.CreateAuditNotificationAsync(
                                "New user registration requires verification",
                                $"{model.FullName} ({model.Email}) submitted a registration request with identity evidence.",
                                0,
                                "registration@system",
                                administrators.Select(x => x.UserAccountId).ToArray(),
                                reviewPath);
                        }
                        catch (Exception notificationException)
                        {
                            _logger.LogWarning(
                                notificationException,
                                "Registration {UserId} was saved, but the administrator notification could not be created.",
                                userId);
                        }

                        var reviewUrl = Url.Action(
                            "Details",
                            "RegistrationApproval",
                            new { id = userId },
                            Request.Scheme) ?? reviewPath;

                        foreach (var administrator in administrators)
                        {
                            var body = $"""
                                <h2>New registration request</h2>
                                <p>Hello {WebUtility.HtmlEncode(administrator.FullName)},</p>
                                <p><strong>{WebUtility.HtmlEncode(model.FullName)}</strong>
                                ({WebUtility.HtmlEncode(model.Email)}) has requested an account.</p>
                                <p>The account remains disabled until an Admin or SuperAdmin verifies the profile photo,
                                identity document, and registration details.</p>
                                <p><a href="{WebUtility.HtmlEncode(reviewUrl)}">Review this registration</a></p>
                                """;
                            try
                            {
                                var sent = await _emailSender.SendEmailAsync(
                                    administrator.Email,
                                    "New Crime Analysis registration awaiting approval",
                                    body);
                                if (sent.Success)
                                    continue;

                                _logger.LogWarning(
                                    "Registration {UserId} was saved, but the alert email to {Email} failed: {Reason}",
                                    userId,
                                    administrator.Email,
                                    sent.Message);
                            }
                            catch (Exception emailException)
                            {
                                _logger.LogWarning(
                                    emailException,
                                    "Registration {UserId} was saved, but the alert email to {Email} failed.",
                                    userId,
                                    administrator.Email);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Registration {UserId} is pending, but no active approved Admin or SuperAdmin account was found.",
                            userId);
                    }
                }
                catch (Exception notificationException)
                {
                    _logger.LogWarning(
                        notificationException,
                        "Registration {UserId} was saved, but administrator alerts could not be completed.",
                        userId);
                }

                TempData["Info"] = response.Message;
                return RedirectToAction(nameof(Login));
            }
            catch (Exception ex)
            {
                if (!registrationSaved && evidenceDirectory != null)
                    DeleteEvidenceDirectory(evidenceDirectory);
                _logger.LogError(ex, "Unable to submit registration for {Email}", model.Email);
                ModelState.AddModelError(string.Empty, "Unable to submit the registration request. Please try again.");
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

        private void ValidateRegistrationFiles(RegisterDto model)
        {
            ValidateFile(
                model.ProfilePhoto,
                nameof(model.ProfilePhoto),
                5 * 1024 * 1024,
                new[] { ".jpg", ".jpeg", ".png" },
                allowPdf: false);
            ValidateFile(
                model.IdentityDocument,
                nameof(model.IdentityDocument),
                10 * 1024 * 1024,
                new[] { ".jpg", ".jpeg", ".png", ".pdf" },
                allowPdf: true);
        }

        private void ValidateFile(
            IFormFile? file,
            string fieldName,
            long maximumBytes,
            IReadOnlyCollection<string> allowedExtensions,
            bool allowPdf)
        {
            if (file == null || file.Length == 0)
                return;

            if (file.Length > maximumBytes)
            {
                ModelState.AddModelError(fieldName, $"The file must be smaller than {maximumBytes / 1024 / 1024} MB.");
                return;
            }

            var extension = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension) || !HasAllowedSignature(file, extension, allowPdf))
                ModelState.AddModelError(fieldName, "The file type is not accepted or its contents do not match its extension.");
        }

        private async Task<(RegistrationEvidenceDto Evidence, string DirectoryPath)> SaveRegistrationEvidenceAsync(RegisterDto model)
        {
            var registrationKey = Guid.NewGuid().ToString("N");
            var root = Path.Combine(_environment.ContentRootPath, "App_Data", "RegistrationEvidence");
            var directory = Path.Combine(root, registrationKey);
            Directory.CreateDirectory(directory);

            var photoExtension = Path.GetExtension(Path.GetFileName(model.ProfilePhoto!.FileName)).ToLowerInvariant();
            var identityExtension = Path.GetExtension(Path.GetFileName(model.IdentityDocument!.FileName)).ToLowerInvariant();
            var photoRelative = Path.Combine(registrationKey, $"profile{photoExtension}");
            var identityRelative = Path.Combine(registrationKey, $"identity{identityExtension}");
            try
            {
                await SaveFileAsync(model.ProfilePhoto, Path.Combine(root, photoRelative));
                await SaveFileAsync(model.IdentityDocument, Path.Combine(root, identityRelative));

                return (
                    new RegistrationEvidenceDto
                    {
                        ProfilePhotoPath = photoRelative,
                        IdentityDocumentPath = identityRelative,
                        IdentityDocumentOriginalName = Path.GetFileName(model.IdentityDocument.FileName),
                        IdentityDocumentContentType = model.IdentityDocument.ContentType ?? "application/octet-stream"
                    },
                    directory);
            }
            catch
            {
                DeleteEvidenceDirectory(directory);
                throw;
            }
        }

        private static async Task SaveFileAsync(IFormFile file, string destination)
        {
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(output);
        }

        private static bool HasAllowedSignature(IFormFile file, string extension, bool allowPdf)
        {
            Span<byte> header = stackalloc byte[8];
            using var stream = file.OpenReadStream();
            var read = stream.Read(header);
            var isJpeg = read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            var isPng = read >= 8
                && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
            var isPdf = allowPdf
                && read >= 4
                && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46;

            return extension switch
            {
                ".jpg" or ".jpeg" => isJpeg,
                ".png" => isPng,
                ".pdf" => isPdf,
                _ => false
            };
        }

        private void DeleteEvidenceDirectory(string directory)
        {
            try
            {
                var root = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "App_Data", "RegistrationEvidence"));
                var target = Path.GetFullPath(directory);
                if (target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(target))
                    Directory.Delete(target, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to clean up failed registration evidence at {Directory}", directory);
            }
        }
    }
}

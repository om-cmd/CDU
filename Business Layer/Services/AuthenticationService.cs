using System.Security.Claims;
using Core_Layer.HelperMethod;
using Core_Layer.ViewModels;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using IAuthenticationService = Analysis_Web.Services.IAuthenticationService;

namespace Business_Layer.Services;

public class AuthenticationService : IAuthenticationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(IUnitOfWork unitOfWork, ILogger<AuthenticationService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<(string Message, bool Success, string Status, int? UserId)> RegisterUserAsync(
        RegisterDto model,
        RegistrationEvidenceDto evidence)
    {
        try
        {
            var validate = await ValidateUser(model);
            if (!validate.Success)
            {
                return (validate.Message, false, validate.Status, null);
            }

            var normalizedEmail = model.Email.Trim().ToLowerInvariant();
            var user = new ApplicationUser
            {
                FullName = model.FullName.Trim(),
                UserName = normalizedEmail,
                Email = normalizedEmail,
                Password = StaticMethods.HashPassword(model.Password).Item2,
                Contact = model.Contact?.Trim(),
                Department = model.Department?.Trim(),
                DateOfBirth = model.DateOfBirth,
                Address = model.Address?.Trim(),
                City = model.City?.Trim(),
                State = model.State?.Trim(),
                Country = model.Country?.Trim(),
                PostalCode = model.PostalCode?.Trim(),
                Gender = model.Gender,
                UserType = UserType.User,
                IsActive = false,
                IsValidated = false,
                PhoneValidated = false,
                EmailConfirmedStatus = false,
                ApprovalStatus = AccountApprovalStatus.Pending,
                ApprovalRequestedAtUtc = DateTime.UtcNow,
                RegistrationPhotoPath = evidence.ProfilePhotoPath,
                IdentityDocumentPath = evidence.IdentityDocumentPath,
                IdentityDocumentOriginalName = evidence.IdentityDocumentOriginalName,
                IdentityDocumentContentType = evidence.IdentityDocumentContentType,
                CreatedAt = DateTime.UtcNow
            };

            await using var transaction = await _unitOfWork._db.Database.BeginTransactionAsync();
            try
            {
                _unitOfWork.Users.Add(user);
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
                return (
                    "Registration submitted. An administrator must verify and approve the account before you can sign in.",
                    true,
                    "PENDING",
                    user.UserAccountId);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed for {Email}", model.Email);
            return ("Unable to submit the registration request. Please try again.", false, "1", null);
        }
    }

    public async Task<(bool Success, string Message, string Status)> ValidateUser(RegisterDto user)
    {
        try
        {
            var normalizedEmail = user.Email.Trim().ToLowerInvariant();
            if (await _unitOfWork.Users.AnyAsync(x => !x.Deleted && x.Email.ToLower() == normalizedEmail))
                return (false, "An account or pending registration already exists for this email address.", "DUPLICATE_EMAIL");

            var normalizedContact = user.Contact?.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedContact)
                && await _unitOfWork.Users.AnyAsync(x => !x.Deleted && x.Contact == normalizedContact))
                return (false, "An account or pending registration already exists for this phone number.", "DUPLICATE_CONTACT");

            return (true, "Registration details are available.", "00");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration validation failed for {Email}", user.Email);
            return (false, ex.Message, "1");
        }
    }

    public async Task<(string Message, bool Success, string Status, LoginResponseDto)> Login(LoginDto model)
    {
        try
        {
            if (string.IsNullOrEmpty(model.Password))
                return ("PASSWORD IS REQUIRED!!", false, "1",null);
            if (string.IsNullOrEmpty(model.Email))
                return ("EMAIL IS REQUIRED!!", false, "1",null);
            var login = model.Email.Trim().ToLowerInvariant();
            var user = await _unitOfWork.Users.FirstOrDefaultAsync(
                x => !x.Deleted && (x.Email.ToLower() == login || x.UserName.ToLower() == login));
            if (user == null)
                return ("No account was found. Please register first.", false, "404", null);

            if (!StaticMethods.VerifyPassword(model.Password, user.Password))
            {
                return ("Email or password is incorrect.", false, "1", null);
            }

            if (user.ApprovalStatus == AccountApprovalStatus.Pending)
                return ("Your registration is awaiting administrator verification. You will receive an email after it is reviewed.", false, "PENDING", null);

            if (user.ApprovalStatus == AccountApprovalStatus.Rejected)
                return ("Your registration was not approved. Please contact an administrator if you need more information.", false, "REJECTED", null);

            if (!user.IsActive)
                return ("This account is not active. Please contact an administrator.", false, "INACTIVE", null);

             var tokens = new LoginResponseDto()
             {
                 EmailAddress = user.Email,
                 UserName = user.UserName,
                 Roles = null,
                 UserId = user.UserAccountId,
                 UserType = user.UserType.ToString(),

             };
             var token = StaticMethods.GenTokenkey(tokens);
             var auth = new AuthenticationProperties
             {
                 AllowRefresh = true,
                 ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2),
                 IssuedUtc = DateTimeOffset.UtcNow,
                 IsPersistent = true
             };

             var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);

             identity.AddClaim(new Claim(ClaimTypes.Name, user.FullName ?? ""));
             identity.AddClaim(new Claim(ClaimTypes.Actor, user.Email ?? ""));
             identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.UserAccountId.ToString()));
             identity.AddClaim(new Claim("Token", token.data.Token));
             identity.AddClaim(new Claim("RToken", token.data.RefreshToken));
             identity.AddClaim(new Claim(ClaimTypes.Role, user.UserType.ToString()));

             await _unitOfWork.HttpContextAccessor.HttpContext!.SignInAsync(
                 CookieAuthenticationDefaults.AuthenticationScheme,
                 new ClaimsPrincipal(identity),
                 auth);
             user.LastLoginAt = DateTime.UtcNow;
             await _unitOfWork.SaveChangesAsync();
             LoginResponseDto dto = new LoginResponseDto()
             {
                 EmailAddress = user.Email,
                 UserName = user.UserName,
                 Roles = null,
                 UserId = user.UserAccountId,
                 UserType = user.UserType.ToString(),
             };
             return ("Login Success", true, "00", dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for {Email}", model.Email);
            return ("Unable to sign in right now. Please try again.", false, "1", null);
        }
    }

    public async void Logout()
    {
        await _unitOfWork.HttpContextAccessor.HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
        _unitOfWork.HttpContextAccessor.HttpContext.Session.Clear();
    }
}

using System.Net;
using System.Security.Claims;
using Analysis_Web.Services;
using Core_Layer.ViewModels;
using Domain_Layer.Database;
using Domain_Layer.DbModels;
using Domain_Layer.DbModels.Enum;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Analysis_Web.Controllers;

[Authorize(Roles = "SuperAdmin,Admin")]
public class RegistrationApprovalController : Controller
{
    private readonly AnalysisDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<RegistrationApprovalController> _logger;

    public RegistrationApprovalController(
        AnalysisDbContext db,
        IEmailSender emailSender,
        IWebHostEnvironment environment,
        ILogger<RegistrationApprovalController> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _environment = environment;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var requests = await _db.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.Deleted
                        && x.UserType == UserType.User
                        && x.ApprovalRequestedAtUtc != null)
            .OrderBy(x => x.ApprovalStatus == AccountApprovalStatus.Pending ? 0 : 1)
            .ThenByDescending(x => x.ApprovalRequestedAtUtc)
            .Select(x => new RegistrationApprovalListItemDto
            {
                UserAccountId = x.UserAccountId,
                FullName = x.FullName,
                Email = x.Email,
                Contact = x.Contact ?? string.Empty,
                Country = x.Country ?? string.Empty,
                ApprovalStatus = x.ApprovalStatus,
                RequestedAtUtc = x.ApprovalRequestedAtUtc ?? x.CreatedAt
            })
            .ToListAsync();

        return View(requests);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var request = await _db.ApplicationUsers
            .AsNoTracking()
            .Where(x => x.UserAccountId == id
                        && !x.Deleted
                        && x.UserType == UserType.User
                        && x.ApprovalRequestedAtUtc != null)
            .Select(x => new RegistrationApprovalDetailsDto
            {
                UserAccountId = x.UserAccountId,
                FullName = x.FullName,
                Email = x.Email,
                Contact = x.Contact ?? string.Empty,
                Department = x.Department,
                DateOfBirth = x.DateOfBirth,
                Address = x.Address ?? string.Empty,
                City = x.City ?? string.Empty,
                State = x.State ?? string.Empty,
                Country = x.Country ?? string.Empty,
                PostalCode = x.PostalCode ?? string.Empty,
                Gender = x.Gender,
                ApprovalStatus = x.ApprovalStatus,
                RequestedAtUtc = x.ApprovalRequestedAtUtc ?? x.CreatedAt,
                ReviewedAtUtc = x.ReviewedAtUtc,
                ReviewNotes = x.ReviewNotes,
                HasProfilePhoto = x.RegistrationPhotoPath != null,
                HasIdentityDocument = x.IdentityDocumentPath != null,
                IdentityDocumentName = x.IdentityDocumentOriginalName ?? "Identity document"
            })
            .FirstOrDefaultAsync();

        return request == null ? NotFound() : View(request);
    }

    [HttpGet]
    public async Task<IActionResult> Evidence(int id, string kind)
    {
        var user = await _db.ApplicationUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserAccountId == id
                                      && !x.Deleted
                                      && x.UserType == UserType.User
                                      && x.ApprovalRequestedAtUtc != null);
        if (user == null)
            return NotFound();

        var isPhoto = string.Equals(kind, "photo", StringComparison.OrdinalIgnoreCase);
        var isIdentity = string.Equals(kind, "identity", StringComparison.OrdinalIgnoreCase);
        if (!isPhoto && !isIdentity)
            return BadRequest();

        var relativePath = isPhoto ? user.RegistrationPhotoPath : user.IdentityDocumentPath;
        var path = ResolveEvidencePath(relativePath);
        if (path == null)
            return NotFound();

        var contentType = isPhoto
            ? ContentTypeFromExtension(path)
            : NormalizedIdentityContentType(user.IdentityDocumentContentType, path);
        Response.Headers.CacheControl = "private, no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, contentType, enableRangeProcessing: true);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? reviewNotes)
    {
        var user = await GetPendingRequestAsync(id);
        if (user == null)
        {
            TempData["Error"] = "This registration is no longer pending or could not be found.";
            return RedirectToAction(nameof(Index));
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        user.ApprovalStatus = AccountApprovalStatus.Approved;
        user.IsActive = true;
        user.IsValidated = true;
        user.EmailConfirmedStatus = true;
        user.ReviewedAtUtc = DateTime.UtcNow;
        user.ReviewedByUserId = CurrentUserId();
        user.ReviewNotes = NormalizeNotes(reviewNotes);
        user.UpdatedAt = DateTime.UtcNow;
        await EnsureUserRoleAsync(user.UserAccountId);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var sent = await SendDecisionEmailAsync(user, approved: true);
        TempData[sent.Success ? "Success" : "Warning"] = sent.Success
            ? $"{user.FullName}'s account was approved and can now sign in."
            : $"{user.FullName}'s account was approved, but the decision email could not be sent.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reviewNotes)
    {
        var user = await GetPendingRequestAsync(id);
        if (user == null)
        {
            TempData["Error"] = "This registration is no longer pending or could not be found.";
            return RedirectToAction(nameof(Index));
        }

        user.ApprovalStatus = AccountApprovalStatus.Rejected;
        user.IsActive = false;
        user.IsValidated = false;
        user.ReviewedAtUtc = DateTime.UtcNow;
        user.ReviewedByUserId = CurrentUserId();
        user.ReviewNotes = NormalizeNotes(reviewNotes);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var sent = await SendDecisionEmailAsync(user, approved: false);
        TempData[sent.Success ? "Success" : "Warning"] = sent.Success
            ? $"{user.FullName}'s registration was rejected."
            : $"{user.FullName}'s registration was rejected, but the decision email could not be sent.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private Task<ApplicationUser?> GetPendingRequestAsync(int id)
        => _db.ApplicationUsers.FirstOrDefaultAsync(
            x => x.UserAccountId == id
                 && !x.Deleted
                 && x.UserType == UserType.User
                 && x.ApprovalRequestedAtUtc != null
                 && x.ApprovalStatus == AccountApprovalStatus.Pending);

    private async Task EnsureUserRoleAsync(int userId)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(x => x.RoleName.ToLower() == "user");
        if (role == null)
        {
            role = new Role
            {
                RoleName = "User",
                RoleDescription = "Normal approved user",
                CreatedBy = CurrentEmail(),
                CreatedOn = DateTime.UtcNow,
                ModifiedBy = CurrentEmail(),
                ModifiedOn = DateTime.UtcNow,
                Status = "Active"
            };
            _db.Roles.Add(role);
            await _db.SaveChangesAsync();
        }

        if (!await _db.UserRoles.AnyAsync(x => x.UserAccountId == userId && x.RoleId == role.RoleId))
        {
            _db.UserRoles.Add(new UserRole
            {
                UserAccountId = userId,
                RoleId = role.RoleId,
                CreatedBy = CurrentEmail(),
                CreatedDate = DateTime.UtcNow
            });
        }
    }

    private async Task<(bool Success, string Message)> SendDecisionEmailAsync(ApplicationUser user, bool approved)
    {
        var status = approved ? "approved" : "not approved";
        var extra = approved
            ? "<p>You can now sign in using the email address and password supplied during registration.</p>"
            : "<p>If you believe this decision is incorrect, please contact an administrator.</p>";
        var notes = string.IsNullOrWhiteSpace(user.ReviewNotes)
            ? string.Empty
            : $"<p><strong>Reviewer notes:</strong> {WebUtility.HtmlEncode(user.ReviewNotes)}</p>";
        var html = $"""
            <h2>Registration {status}</h2>
            <p>Hello {WebUtility.HtmlEncode(user.FullName)},</p>
            <p>Your Crime Analysis registration has been <strong>{status}</strong>.</p>
            {extra}
            {notes}
            """;

        try
        {
            var result = await _emailSender.SendEmailAsync(
                user.Email,
                approved ? "Your Crime Analysis account has been approved" : "Crime Analysis registration decision",
                html);
            if (!result.Success)
                _logger.LogWarning(
                    "Registration decision for user {UserId} was saved, but email failed: {Reason}",
                    user.UserAccountId,
                    result.Message);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Registration decision for user {UserId} was saved, but email failed: {Reason}",
                user.UserAccountId,
                ex.Message);
            return (false, "The decision was saved, but the email could not be sent.");
        }
    }

    private string? ResolveEvidencePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return null;

        var root = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "App_Data", "RegistrationEvidence"));
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               && System.IO.File.Exists(candidate)
            ? candidate
            : null;
    }

    private static string ContentTypeFromExtension(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream"
        };

    private static string NormalizedIdentityContentType(string? storedContentType, string path)
        => storedContentType?.ToLowerInvariant() switch
        {
            "image/jpeg" => "image/jpeg",
            "image/png" => "image/png",
            "application/pdf" => "application/pdf",
            _ => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                ? "application/pdf"
                : ContentTypeFromExtension(path)
        };

    private static string? NormalizeNotes(string? notes)
    {
        var trimmed = notes?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed[..Math.Min(trimmed.Length, 1000)];
    }

    private int CurrentUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private string CurrentEmail()
        => User.FindFirstValue(ClaimTypes.Actor) ?? "administrator";
}

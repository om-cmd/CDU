using System.Security.Claims;
using Analysis_Web.Services;
using Core_Layer.ViewModels;
using Domain_Layer.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Analysis_Web.Controllers;

[Authorize]
public class CommunicationController : Controller
{
    private readonly ICommunicationService _communicationService;
    private readonly AnalysisDbContext _db;

    public CommunicationController(ICommunicationService communicationService, AnalysisDbContext db)
    {
        _communicationService = communicationService;
        _db = db;
    }

    [Authorize(Roles = "SuperAdmin,Admin")]
    [HttpGet]
    public async Task<IActionResult> GlobalEmail()
    {
        var model = await _communicationService.GetCommunicationIndexAsync(CurrentUserId(), CurrentUserType());
        return View(model);
    }

    [Authorize(Roles = "SuperAdmin,Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendEmail(EmailComposeDto dto)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please complete the email title, subject, and body.";
            return RedirectToAction(nameof(GlobalEmail));
        }

        var result = dto.EmailMessageId.HasValue && IsSuperAdmin()
            ? await _communicationService.UpdateEmailAsync(dto)
            : await _communicationService.SendEmailAsync(dto, CurrentUserId(), CurrentEmail());

        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(GlobalEmail));
    }

    [Authorize(Roles = "SuperAdmin,Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendNotification(NotificationComposeDto dto)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please complete the notification title and message.";
            return RedirectToAction(nameof(GlobalEmail));
        }

        var result = dto.UserNotificationId.HasValue && IsSuperAdmin()
            ? await _communicationService.UpdateNotificationAsync(dto)
            : await _communicationService.SendNotificationAsync(dto, CurrentUserId(), CurrentEmail());

        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(GlobalEmail));
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEmail(long id)
    {
        var result = await _communicationService.DeleteEmailAsync(id);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(GlobalEmail));
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNotification(long id)
    {
        var result = await _communicationService.DeleteNotificationAsync(id);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(GlobalEmail));
    }

    [HttpGet]
    public async Task<IActionResult> Notifications()
    {
        var userId = CurrentUserId();
        var notifications = await _db.UserNotificationRecipients
            .Where(x => x.UserAccountId == userId && x.Notification != null && !x.Notification.IsDeleted)
            .OrderByDescending(x => x.Notification!.CreatedAtUtc)
            .Select(x => new NotificationInboxItem
            {
                RecipientId = x.UserNotificationRecipientId,
                NotificationId = x.UserNotificationId,
                Title = x.Notification!.Title,
                Message = x.Notification.Message,
                SentBy = x.Notification.CreatedByEmail,
                SentAtUtc = x.Notification.CreatedAtUtc,
                ReadAtUtc = x.ReadAtUtc
            })
            .ToListAsync();

        return View(notifications);
    }

    [HttpGet]
    public async Task<IActionResult> NotificationDetails(long id)
    {
        var userId = CurrentUserId();
        var item = await _db.UserNotificationRecipients
            .Include(x => x.Notification)
            .FirstOrDefaultAsync(x => x.UserNotificationId == id && x.UserAccountId == userId);

        if (item?.Notification == null || item.Notification.IsDeleted)
            return NotFound();

        if (item.ReadAtUtc == null)
        {
            item.ReadAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return View(item);
    }

    [HttpGet]
    public async Task<IActionResult> NotificationCount()
    {
        return Json(new { count = await _communicationService.GetUnreadNotificationCountAsync(CurrentUserId()) });
    }

    private int CurrentUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private string CurrentEmail()
        => User.FindFirstValue(ClaimTypes.Actor) ?? "";

    private string CurrentUserType()
        => User.FindFirstValue(ClaimTypes.Role) ?? "";

    private bool IsSuperAdmin()
        => string.Equals(CurrentUserType(), "SuperAdmin", StringComparison.OrdinalIgnoreCase);
}

public class NotificationInboxItem
{
    public long RecipientId { get; set; }
    public long NotificationId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string SentBy { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}

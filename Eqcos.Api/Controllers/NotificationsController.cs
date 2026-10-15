using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Data;
using Eqcos.Api.Models;

namespace Eqcos.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = "Student")]   // на весь контроллер сразу
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // мои непрочитанные уведомления
    [HttpGet]
    public IActionResult GetMine()
    {
        return Ok(_db.Notifications
            .Where(n => n.UserId == CurrentUserId && !n.IsRead)
            .OrderBy(n => n.CreatedAt)
            .Select(n => new { n.Id, n.Text, CanChange = n.BookingId != null })
            .ToList());
    }

    // "Ок" — просто скрыть уведомление
    [HttpPost("{id}/ok")]
    public IActionResult Confirm(int id)
    {
        var n = _db.Notifications.Find(id);
        if (n == null || n.UserId != CurrentUserId)
            return NotFound("Не найдено");

        n.IsRead = true;
        _db.SaveChanges();
        return Ok("");
    }

    // "Выбрать другое время" — отменить перенесённую запись и скрыть уведомление
    [HttpPost("{id}/other")]
    public IActionResult Other(int id)
    {
        var n = _db.Notifications.Find(id);
        if (n == null || n.UserId != CurrentUserId)
            return NotFound("Не найдено");

        if (n.BookingId != null)
        {
            var b = _db.Bookings.Find(n.BookingId);
                if (b != null && b.StudentId == CurrentUserId && b.Status == BookingStatus.Active)
                    b.Status = BookingStatus.Cancelled;   // отменил сам студент (перенесённую — без штрафа, шаг 24)
        }

        n.IsRead = true;
        _db.SaveChanges();
        return Ok("Запись отменена. Выберите новое время ниже.");
    }
}
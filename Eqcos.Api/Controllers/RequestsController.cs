using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Data;
using Eqcos.Api.Models;

namespace Eqcos.Api.Controllers;

public record RejectRequest(string Reason);

// заявки на подтверждение учётных записей
[ApiController]
[Route("api/requests")]
[Authorize(Roles = "Admin")]
public class RequestsController : ControllerBase
{
    private readonly AppDbContext _db;

    public RequestsController(AppDbContext db)
    {
        _db = db;
    }

    // все студенты, которые ждут проверки (сначала самые ранние)
    [HttpGet]
    public IActionResult GetPending()
    {
        return Ok(_db.Users
            .Where(u => u.Role == "Student" && u.Status == "Pending")
            .OrderBy(u => u.Id)
            .Select(u => new { u.Id, u.Login, u.FullName, u.Group })
            .ToList());
    }

    // подтвердить
    [HttpPost("{id}/approve")]
    public IActionResult Approve(int id)
    {
        var u = _db.Users.Find(id);
        if (u == null || u.Role != "Student")
            return NotFound("Студент не найден");

        u.Status = "Approved";
        u.RejectReason = null;
        Notify(u.Id, "Учебная часть подтвердила вашу учётную запись. Теперь можно записываться.");
        _db.SaveChanges();
        return Ok($"Учётная запись подтверждена: {u.FullName}");
    }

    // отклонить с причиной
    [HttpPost("{id}/reject")]
    public IActionResult Reject(int id, RejectRequest r)
    {
        var reason = r.Reason.Trim();
        if (reason == "")
            return BadRequest("Укажите причину отказа");

        var u = _db.Users.Find(id);
        if (u == null || u.Role != "Student")
            return NotFound("Студент не найден");

        u.Status = "Rejected";
        u.RejectReason = reason;
        Notify(u.Id, $"Учётная запись не подтверждена: {reason}. Исправьте данные в профиле.");
        _db.SaveChanges();
        return Ok($"Учётная запись отклонена: {u.FullName}");
    }

    private void Notify(int userId, string text)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Text = text,
            CreatedAt = DateTime.UtcNow   // только UTC (помнишь ошибку шага 15)
        });
    }
}
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Data;
using Eqcos.Api.Models;

namespace Eqcos.Api.Controllers;

// FullName и Group — то, что админ видел на экране, когда нажимал кнопку
public record ApproveRequest(string FullName, string Group);
public record RejectRequest(string Reason, string FullName, string Group);

// заявки: новые регистрации и изменения данных
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

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // заявка на изменение = есть новое ФИО или группа "в стороне"
    private static bool HasChange(User u) => u.PendingFullName != null || u.PendingGroup != null;

    // админ видел ровно то, что сейчас лежит в заявке? (студент мог успеть поменять данные)
    private static bool SameAsSeen(User u, string fullName, string group) =>
        (u.PendingFullName ?? u.FullName) == fullName && (u.PendingGroup ?? u.Group) == group;

    private const string Changed = "Студент только что изменил данные. Обновите список и проверьте заново.";

    // все заявки (сначала самые ранние)
    [HttpGet]
    public IActionResult GetPending()
    {
        var users = _db.Users
            .Where(u => u.Role == "Student" &&
                        (u.Status == "Pending" || u.PendingFullName != null || u.PendingGroup != null))
            .OrderBy(u => u.Id)
            .ToList();

        return Ok(users.Select(u => new
        {
            u.Id,
            u.Login,
            Kind = HasChange(u) ? "Изменение данных" : "Новая регистрация",
            u.FullName,                       // текущие данные
            u.Group,
            NewFullName = u.PendingFullName,  // новые (только у изменения)
            NewGroup = u.PendingGroup
        }));
    }

    // подтвердить
    [HttpPost("{id}/approve")]
    public IActionResult Approve(int id, ApproveRequest r)
    {
        var u = _db.Users.Find(id);
        if (u == null || u.Role != "Student")
            return NotFound("Студент не найден");
        if (!SameAsSeen(u, r.FullName, r.Group))
            return Conflict(Changed);

        if (HasChange(u))
        {
            // применяем новые данные
            u.FullName = u.PendingFullName ?? u.FullName;
            u.Group = u.PendingGroup ?? u.Group;
            u.PendingFullName = null;
            u.PendingGroup = null;

            // и в будущих записях, чтобы админ видел актуальные данные
            foreach (var b in _db.Bookings.Where(b => b.StudentId == u.Id && b.Date >= Today))
            {
                b.StudentName = u.FullName;
                b.Group = u.Group;
            }

            Notify(u.Id, $"Учебная часть подтвердила изменение данных: {u.FullName}, группа {u.Group}.");
            _db.SaveChanges();
            return Ok($"Изменение данных подтверждено: {u.FullName}");
        }

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
        if (!SameAsSeen(u, r.FullName, r.Group))
            return Conflict(Changed);

        if (HasChange(u))
        {
            // новые данные выбрасываем, старые остаются, учётная запись остаётся подтверждённой
            u.PendingFullName = null;
            u.PendingGroup = null;
            Notify(u.Id, $"Изменение данных отклонено: {reason}. Остались прежние данные.");
            _db.SaveChanges();
            return Ok($"Изменение данных отклонено: {u.FullName}");
        }

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
            CreatedAt = DateTime.UtcNow   // только UTC
        });
    }
}
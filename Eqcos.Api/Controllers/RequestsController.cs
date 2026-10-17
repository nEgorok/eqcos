using Eqcos.Api.Data;
using Eqcos.Api.Models;
using Eqcos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Eqcos.Api.Controllers;

// FullName и Group — то, что админ видел на экране, когда нажимал кнопку
public record ApproveRequest(string FullName, string Group);
public record RejectRequest(string Reason, string FullName, string Group);
public record MoveRequest(int OfficeId);   // передать заявку в другой корпус

// заявки: новые регистрации и изменения данных (в том числе перевод в другой корпус)
[ApiController]
[Route("api/requests")]
[Authorize(Roles = "Secretary")]   // заявки проверяет секретарь
public class RequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly College _college;

    public RequestsController(AppDbContext db, College college)
    {
        _db = db;
        _college = college;   // сверка со списком техникума
    }

    // корпус админа из пропуска: он видит заявки только своего корпуса
    private int? CurrentOfficeId =>
        int.TryParse(User.FindFirst("office")?.Value, out var id) ? id : null;

    // заявка на изменение = есть новые данные "в стороне"
    private static bool HasChange(User u) =>
        u.PendingFullName != null || u.PendingGroup != null || u.PendingOfficeId != null;

    // чья это заявка: регистрацию проверяет выбранный корпус, перевод — НОВЫЙ корпус
    private static int? TargetOffice(User u) => HasChange(u) ? (u.PendingOfficeId ?? u.OfficeId) : u.OfficeId;

    // студент, чья заявка относится к корпусу этого админа (или null)
    private User? FindStudent(int id)
    {
        var u = _db.Users.Find(id);
        return u != null && u.Role == "Student" && TargetOffice(u) == CurrentOfficeId ? u : null;
    }

    // админ видел ровно то, что сейчас лежит в заявке? (студент мог успеть поменять данные)
    private static bool SameAsSeen(User u, string fullName, string group) =>
        (u.PendingFullName ?? u.FullName) == fullName && (u.PendingGroup ?? u.Group) == group;

    private const string Changed = "Студент только что изменил данные. Обновите список и проверьте заново.";

    private string? OfficeName(int? id) => id == null ? null : _db.Offices.Find(id)?.Name;

    // все заявки своего корпуса (сначала самые ранние)
    [HttpGet]
    public IActionResult GetPending()
    {
        var cur = CurrentOfficeId;
        var users = _db.Users
            .Where(u => u.Role == "Student" &&
                        ((u.Status == "Pending" && u.OfficeId == cur) ||
                         ((u.PendingFullName != null || u.PendingGroup != null || u.PendingOfficeId != null)
                          && (u.PendingOfficeId ?? u.OfficeId) == cur)))
            .OrderBy(u => u.Id)
            .ToList();

        return Ok(users.Select(u => new
        {
            u.Id,
            u.Login,
            Kind = !HasChange(u) ? "Новая регистрация"
                 : u.PendingOfficeId != null && u.PendingOfficeId != u.OfficeId ? "Перевод"
                 : "Изменение данных",
            Reason = HasChange(u) ? u.PendingReason : null,
            u.FullName,                       // текущие данные
            u.Group,
            Office = OfficeName(u.OfficeId),
            NewFullName = u.PendingFullName,  // новые (только у изменения)
            NewGroup = u.PendingGroup,
            NewOffice = OfficeName(u.PendingOfficeId),
            TargetOfficeId = TargetOffice(u), // корпус, в котором сейчас лежит заявка
            // сверка со списком техникума по тем данным, что сейчас в заявке (null — список не подключён)
            College = _college.Enabled
                ? _college.Check(u.Login, u.PendingFullName ?? u.FullName, u.PendingGroup ?? u.Group, TargetOffice(u))
                : null
        }));
    }

    // подключён ли список техникума и включена ли автоматика: сайт решает, что показывать
    [HttpGet("college")]
    public IActionResult CollegeMode() => Ok(new { _college.Enabled, _college.Auto, _college.Sync });

    // поиск в списке техникума: номер или фамилия (как в «Вызове»)
    [HttpGet("college/search")]
    public IActionResult CollegeSearch(string? q)
    {
        if (!_college.Enabled)
            return BadRequest("Список техникума не подключён");
        if (string.IsNullOrWhiteSpace(q))
            return Ok(Array.Empty<object>());

        return Ok(_college.Search(q.Trim()).Select(s => new
        {
            s.Login,
            s.FullName,
            s.Group,
            Office = OfficeName(s.OfficeId)
        }));
    }

    // подтвердить
    [HttpPost("{id}/approve")]
    public IActionResult Approve(int id, ApproveRequest r)
    {
        var u = FindStudent(id);
        if (u == null)
            return NotFound("Заявка не найдена. Обновите список");
        if (!SameAsSeen(u, r.FullName, r.Group))
            return Conflict(Changed);

        if (HasChange(u))
        {
            var oldOffice = u.OfficeId;

            // применяем новые данные
            u.FullName = u.PendingFullName ?? u.FullName;
            u.Group = u.PendingGroup ?? u.Group;
            u.OfficeId = u.PendingOfficeId ?? u.OfficeId;
            u.PendingFullName = null;
            u.PendingGroup = null;
            u.PendingOfficeId = null;
            u.PendingReason = null;

            var future = _db.Bookings
                .Where(b => b.StudentId == u.Id && b.Date >= Clock.Today && b.Status == BookingStatus.Active)
                .ToList();
            foreach (var b in future)
            {
                // перевели в другой корпус — своя запись в старый корпус отменяется (вызов остаётся)
                if (b.OfficeId != u.OfficeId && !IsSummon(b))
                {
                    b.Status = BookingStatus.SystemCancelled;
                    b.Note = "Перевод в другой корпус";
                    Mover.Notify(_db, u.Id, null,
                        $"Ваша запись на {b.Date:dd.MM} {b.Time:HH:mm} отменена: вы переведены в другой корпус.");
                    continue;
                }
                // в остальных записях — актуальные данные, чтобы админ видел их в очереди
                b.StudentName = u.FullName;
                b.Group = u.Group;
            }

            var text = oldOffice != u.OfficeId
                ? $"Учебная часть подтвердила перевод: {OfficeName(u.OfficeId)}, {u.FullName}, группа {u.Group}."
                : $"Учебная часть подтвердила изменение данных: {u.FullName}, группа {u.Group}.";
            Mover.Notify(_db, u.Id, null, text);
            _db.SaveChanges();
            return Ok($"Подтверждено: {u.FullName}");
        }

        u.Status = "Approved";
        u.RejectReason = null;
        Mover.Notify(_db, u.Id, null, "Учебная часть подтвердила вашу учётную запись. Теперь можно записываться.");
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

        var u = FindStudent(id);
        if (u == null)
            return NotFound("Заявка не найдена. Обновите список");
        if (!SameAsSeen(u, r.FullName, r.Group))
            return Conflict(Changed);

        if (HasChange(u))
        {
            // новые данные выбрасываем, старые остаются, учётная запись остаётся подтверждённой
            u.PendingFullName = null;
            u.PendingGroup = null;
            u.PendingOfficeId = null;
            u.PendingReason = null;
            Mover.Notify(_db, u.Id, null, $"Изменение данных отклонено: {reason}. Остались прежние данные.");
            _db.SaveChanges();
            return Ok($"Изменение данных отклонено: {u.FullName}");
        }

        // новая регистрация: учётку стираем, номер освобождается.
        // Причину 30 дней покажем при входе с верным паролем.
        _db.RejectedAccounts.Add(new RejectedAccount
        {
            Login = u.Login,
            PasswordHash = u.PasswordHash,
            Reason = reason,
            RejectedAt = DateTime.UtcNow
        });
        _db.Notifications.RemoveRange(_db.Notifications.Where(n => n.UserId == u.Id));
        _db.Users.Remove(u);
        _db.SaveChanges();
        return Ok($"Регистрация отклонена, учётная запись удалена: {u.FullName}");
    }

    // передать заявку в другой корпус (студент ошибся корпусом)
    [HttpPost("{id}/office")]
    public IActionResult Move(int id, MoveRequest r)
    {
        var u = FindStudent(id);
        if (u == null)
            return NotFound("Заявка не найдена. Обновите список");

        var office = _db.Offices.Find(r.OfficeId);
        if (office == null)
            return BadRequest("Корпус не найден");
        if (r.OfficeId == CurrentOfficeId)
            return BadRequest("Заявка уже в вашем корпусе");

        if (HasChange(u))
            u.PendingOfficeId = r.OfficeId;   // изменение данных: новый корпус — тоже "в стороне"
        else
            u.OfficeId = r.OfficeId;          // новая регистрация: просто меняем корпус

        Mover.Notify(_db, u.Id, null, $"Учебная часть передала вашу заявку в другой корпус: {office.Name}.");
        _db.SaveChanges();
        return Ok($"Заявка передана ({office.Name}): {u.FullName}");
    }

    // вызов ли это (цель "только админ")
    private bool IsSummon(Booking b) => _db.Purposes.Find(b.PurposeId)?.AdminOnly == true;
}
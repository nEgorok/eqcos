using System.Security.Claims;
using Eqcos.Api.Data;
using Eqcos.Api.Models;
using Eqcos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;

namespace Eqcos.Api.Controllers;

// админ правит данные студента сам (без заявки): ФИО, группа, корпус
public record StudentEdit(string? LastName, string? FirstName, string? MiddleName, string? GroupDigits, string? GroupCode,
                          int OfficeId);
public record StudentDelete(string? Reason);

// вкладка «Студенты»: список, карточка, правка, удаление
[ApiController]
[Route("api/students")]
[Authorize(Roles = "Admin")]
public class StudentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Mailer _mailer;

    public StudentsController(AppDbContext db, Mailer mailer)
    {
        _db = db;
        _mailer = mailer;
    }

    // корпус и логин админа из пропуска
    private int? CurrentOfficeId =>
        int.TryParse(User.FindFirst("office")?.Value, out var id) ? id : null;
    private string AdminLogin => User.FindFirstValue(ClaimTypes.Name) ?? "?";

    // студент своего корпуса (или null)
    private User? FindStudent(int id)
    {
        var u = _db.Users.Find(id);
        return u != null && u.Role == "Student" && u.OfficeId == CurrentOfficeId ? u : null;
    }

    // СПИСОК: студенты своего корпуса, поиск по номеру, ФИО или группе
    [HttpGet]
    public IActionResult GetList(string? q)
    {
        var query = _db.Users.Where(u => u.Role == "Student" && u.OfficeId == CurrentOfficeId);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLower();
            query = query.Where(u => u.Login.StartsWith(s) || u.FullName.ToLower().Contains(s) || u.Group.ToLower().Contains(s));
        }

        var list = query
            .OrderBy(u => u.Group).ThenBy(u => u.FullName)
            .Take(300)
            .Select(u => new { u.Id, u.Login, u.FullName, u.Group, u.Status, u.Level, u.Blocked })
            .ToList();
        return Ok(list);
    }

    // КАРТОЧКА: данные, итоги, история записей в этом корпусе, журнал
    [HttpGet("{id}")]
    public IActionResult GetCard(int id)
    {
        var u = FindStudent(id);
        if (u == null)
            return NotFound("Студент не найден");

        Conduct.Refresh(_db, u);
        _db.SaveChanges();

        var purposes = _db.Purposes.ToDictionary(p => p.Id);
        var bookings = _db.Bookings
            .Where(b => b.StudentId == u.Id && b.OfficeId == CurrentOfficeId)
            .OrderByDescending(b => b.Date).ThenByDescending(b => b.Time)
            .Take(200)
            .ToList();

        var history = bookings.Select(b => new
        {
            b.Date,
            b.Time,
            Purpose = purposes.GetValueOrDefault(b.PurposeId)?.Name ?? "—",
            IsSummon = purposes.GetValueOrDefault(b.PurposeId)?.AdminOnly == true,
            b.Status,
            b.Note,
            b.Moved,
            b.FirstMark
        });

        var log = _db.AdminLogs
            .Where(l => l.StudentId == u.Id)
            .OrderByDescending(l => l.At)
            .Take(50)
            .ToList()
            .Select(l => new { At = Clock.ToMsk(l.At), l.Admin, l.Text });

        return Ok(new
        {
            u.Id,
            u.Login,
            u.FullName,
            u.Group,
            u.OfficeId,
            Office = _db.Offices.Find(u.OfficeId)?.Name,
            u.Status,
            Email = EmailCodes.Mask(u.Email),   // скрыто; null — почта не привязана
            Phone = Phones.Mask(u.Phone),       // скрыто; null — телефон не привязан
            CreatedAt = u.CreatedAt == null ? (DateTime?)null : Clock.ToMsk(u.CreatedAt.Value),
            HasRequest = u.PendingFullName != null || u.PendingGroup != null || u.PendingOfficeId != null,
            // итоги по своим записям (вызовы не считаем)
            Came = bookings.Count(b => b.Status == BookingStatus.Came && purposes.GetValueOrDefault(b.PurposeId)?.AdminOnly != true),
            NoShow = bookings.Count(b => b.Status == BookingStatus.NoShow && purposes.GetValueOrDefault(b.PurposeId)?.AdminOnly != true),
            Cancelled = bookings.Count(b => b.Status == BookingStatus.Cancelled),
            History = history,
            Log = log,
            Conduct = ConductCard(u)
        });
    }

    // порядочность для карточки: уровень, наказание и три шкалы
    private object ConductCard(User u)
    {
        var weeks = Conduct.StrikeWeeks(u);
        var strikes = Conduct.Strikes(_db, u).Select(b => new
        {
            Text = $"{b.Date:dd.MM} в {b.Time:HH:mm}" + (b.Status == BookingStatus.Cancelled ? " (поздняя отмена)" : ""),
            Burns = Clock.ToMsk(b.StrikeAt!.Value.AddDays(7 * weeks))   // когда сгорит
        });

        var events = _db.ConductEvents
            .Where(e => e.UserId == u.Id && e.Kind != ConductKind.Request)
            .OrderByDescending(e => e.At)
            .Take(30)
            .ToList()
            .Select(e => new { At = Clock.ToMsk(e.At), e.Text });

        return new
        {
            u.Level,
            Name = Conduct.Names[u.Level],
            Punishment = Conduct.Punishment(u),
            NextDrop = Conduct.NextDrop(u),
            u.Lifted,
            CanLift = u.PunishUntil != null || Conduct.IsLifetime(u),
            Strikes = strikes,
            StrikeLimit = Conduct.StrikeLimit(u),
            StrikeWeeks = weeks,
            ActionsHour = Conduct.ActionsHour(_db, u),
            SpamLimit = Conduct.SpamLimit,
            SpamWarnings = Conduct.SpamCount(_db, u),
            SpamWarningLimit = Conduct.SpamWarnings,
            Requests = Conduct.RequestsHour(_db, u),
            DataLimit = Conduct.DataLimit,
            DataWarnings = Conduct.DataSpamCount(_db, u),
            DataBlockAt = Conduct.DataBlockAt,
            DataUntil = u.DataUntil == null ? (DateTime?)null : Clock.ToMsk(u.DataUntil.Value),
            u.Blocked,
            Events = events
        };
    }

    // СНЯТЬ БАН (или ограничение «1 запись»)
    [HttpPost("{id}/lift")]
    public IActionResult Lift(int id)
    {
        var u = FindStudent(id);
        if (u == null)
            return NotFound("Студент не найден");

        Conduct.Refresh(_db, u);
        if (u.PunishUntil == null && !Conduct.IsLifetime(u))
            return BadRequest("Сейчас у студента нет бана");

        var was = Conduct.Punishment(u);
        Conduct.Lift(_db, u);
        Log(u.Id, $"Снят бан ({was})");
        _db.SaveChanges();
        return Ok("Бан снят");
    }

    // РАЗБЛОКИРОВАТЬ учётку (заблокирована за подозрительно частые заявки на изменение данных)
    [HttpPost("{id}/unblock")]
    public IActionResult Unblock(int id)
    {
        var u = FindStudent(id);
        if (u == null)
            return NotFound("Студент не найден");
        if (!u.Blocked)
            return BadRequest("Учётная запись не заблокирована");

        Conduct.Unblock(_db, u);
        Log(u.Id, "Учётная запись разблокирована");
        _db.SaveChanges();
        return Ok("Учётная запись разблокирована");
    }

    // СБРОСИТЬ ПАРОЛЬ: студент без почты забыл пароль. Новый пароль из 8 цифр показываем админу один раз
    [HttpPost("{id}/password")]
    public async Task<IActionResult> ResetPassword(int id)
    {
        var u = FindStudent(id);
        if (u == null)
            return NotFound("Студент не найден");

        var password = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString();   // 8 цифр
        u.PasswordHash = new PasswordHasher<User>().HashPassword(u, password);
        u.TokenVersion++;   // старые входы студента закрываются
        u.TempPassword = password;   // студент увидит его в профиле, пока не сменит
        Log(u.Id, "Пароль сброшен учебной частью");
        _db.SaveChanges();

        if (u.Email != null)
            await _mailer.Send(u.Email, "Пароль сброшен", "Учебная часть сбросила пароль от вашей учётной записи в электронной очереди.");

        return Ok(password);   // сайт покажет его плашкой в карточке
    }

    // ПОНИЗИТЬ УРОВЕНЬ на ступень
    [HttpPost("{id}/lower")]
    public IActionResult Lower(int id)
    {
        var u = FindStudent(id);
        if (u == null)
            return NotFound("Студент не найден");

        Conduct.Refresh(_db, u);
        if (u.Level == 0)
            return BadRequest("Уровень и так зелёный");

        var was = u.Level;
        Conduct.Lower(_db, u);
        Log(u.Id, $"Уровень понижен: {was} → {u.Level}");
        _db.SaveChanges();
        return Ok($"Уровень понижен до {u.Level}");
    }

    // ПРАВКА данных админом: сразу, без заявки, пишется в журнал
    [HttpPut("{id}")]
    public IActionResult Edit(int id, StudentEdit r)
    {
        var u = FindStudent(id);
        if (u == null)
            return NotFound("Студент не найден");

        var last = StudentData.Fix(r.LastName);
        var first = StudentData.Fix(r.FirstName);
        var middle = StudentData.Fix(r.MiddleName);
        var digits = (r.GroupDigits ?? "").Trim();
        var code = (r.GroupCode ?? "").Trim();

        var error = StudentData.CheckName(last, first, middle) ?? StudentData.CheckGroup(_db, digits, code);
        if (error != null)
            return BadRequest(error);

        var office = _db.Offices.Find(r.OfficeId);
        if (office == null)
            return BadRequest("Выберите корпус");

        var fullName = StudentData.JoinName(last, first, middle);
        var group = digits + code;
        if (fullName == u.FullName && group == u.Group && r.OfficeId == u.OfficeId)
            return Ok("Данные не изменились");

        // что поменялось — для журнала
        var changes = new List<string>();
        if (fullName != u.FullName) changes.Add($"ФИО: {u.FullName} → {fullName}");
        if (group != u.Group) changes.Add($"группа: {u.Group} → {group}");
        if (r.OfficeId != u.OfficeId) changes.Add($"корпус: {_db.Offices.Find(u.OfficeId)?.Name} → {office.Name}");

        u.FullName = fullName;
        u.Group = group;
        u.OfficeId = r.OfficeId;
        // заявка студента на изменение больше не нужна: админ уже решил
        u.PendingFullName = null;
        u.PendingGroup = null;
        u.PendingOfficeId = null;
        u.PendingReason = null;

        foreach (var b in _db.Bookings.Where(b => b.StudentId == u.Id && b.Date >= Clock.Today && b.Status == BookingStatus.Active).ToList())
        {
            // перевели в другой корпус — своя запись в старый корпус отменяется (вызов остаётся)
            if (b.OfficeId != u.OfficeId && _db.Purposes.Find(b.PurposeId)?.AdminOnly != true)
            {
                b.Status = BookingStatus.SystemCancelled;
                b.Note = "Перевод в другой корпус";
                Mover.Notify(_db, u.Id, null,
                    $"Ваша запись на {b.Date:dd.MM} {b.Time:HH:mm} отменена: вы переведены в другой корпус.");
                continue;
            }
            b.StudentName = u.FullName;
            b.Group = u.Group;
        }

        var text = "Учебная часть изменила ваши данные: " + string.Join(", ", changes) + ".";
        Mover.Notify(_db, u.Id, null, text);
        Log(u.Id, "Изменены данные: " + string.Join("; ", changes));
        _db.SaveChanges();
        return Ok(r.OfficeId == CurrentOfficeId
            ? "Данные изменены"
            : $"Данные изменены, студент переведён: {office.Name}");
    }

    // УДАЛИТЬ учётку (отчислен и т.п.). Записи остаются в истории, но без ФИО
    [HttpPost("{id}/delete")]
    public IActionResult Delete(int id, StudentDelete r)
    {
        var reason = (r.Reason ?? "").Trim();
        if (reason == "")
            return BadRequest("Укажите причину удаления");

        var u = FindStudent(id);
        if (u == null)
            return NotFound("Студент не найден");

        foreach (var b in _db.Bookings.Where(b => b.StudentId == u.Id).ToList())
        {
            // будущие записи освобождаем
            if (b.Status == BookingStatus.Active && Clock.NotEnded(b.Date, b.Time, b.Minutes))
            {
                b.Status = BookingStatus.SystemCancelled;
                b.Note = "Учётная запись удалена";
            }
            b.StudentName = "Удалённый студент";   // группа и цель остаются для статистики
        }

        _db.Notifications.RemoveRange(_db.Notifications.Where(n => n.UserId == u.Id));
        _db.ConductEvents.RemoveRange(_db.ConductEvents.Where(e => e.UserId == u.Id));
        Log(u.Id, $"Удалена учётная запись {u.Login} ({u.FullName}, {u.Group}). Причина: {reason}");
        _db.Users.Remove(u);
        _db.SaveChanges();
        return Ok($"Учётная запись удалена: {u.FullName}");
    }

    private void Log(int studentId, string text)
    {
        _db.AdminLogs.Add(new AdminLog
        {
            OfficeId = CurrentOfficeId,
            Admin = AdminLogin,
            StudentId = studentId,
            Text = text,
            At = DateTime.UtcNow
        });
    }
}
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Data;
using Eqcos.Api.Models;
using Eqcos.Api.Services;

namespace Eqcos.Api.Controllers;

// админ присылает: отметку ("Came" или "NoShow")
public record MarkRequest(string Status);

// админ присылает: причину удаления (можно пустую)
public record DeleteRequest(string? Reason);

// подсказка в поле «Номер студенческого»
public record StudentHint(string Login, string FullName, string Group);

// одна строка очереди или истории — всё, что нужно показать, и какие кнопки можно нажать
public record AdminBooking(
    int Id, DateOnly Date, TimeOnly Time, TimeOnly End,
    string Login, string StudentName, string Group, string Purpose, bool IsSummon,
    string Status, bool Moved, string? Note,
    string? FirstMark, DateTime? CorrectedAt,
    bool CanCame, bool CanNoShow, bool CanDelete);

// панель учебной части: очередь, история, отметки, удаление
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private const int NoShowDays = 3;   // «Не пришёл» и исправление отметки — в течение 3 рабочих дней

    private readonly AppDbContext _db;

    public AdminController(AppDbContext db)
    {
        _db = db;
    }

    private int? CurrentOfficeId =>
        int.TryParse(User.FindFirst("office")?.Value, out var id) ? id : null;

    // ОЧЕРЕДЬ: действующие записи своего корпуса на сегодня
    [HttpGet("queue")]
    public IActionResult GetQueue()
    {
        var office = _db.Offices.Find(CurrentOfficeId);
        if (office == null)
            return BadRequest("У учётной записи не указан корпус");

        var list = _db.Bookings
            .Where(b => b.OfficeId == office.Id && b.Date == Clock.Today && b.Status == BookingStatus.Active)
            .OrderBy(b => b.Time).ThenBy(b => b.Id)
            .ToList();

        return Ok(new
        {
            Office = office.Name,
            Today = Clock.Today,
            Now = Clock.Time,
            Items = ToRows(list)
        });
    }

    // ИСТОРИЯ: все записи своего корпуса за период (новые сверху)
    [HttpGet("history")]
    public IActionResult GetHistory(DateOnly from, DateOnly to, bool showCancelled = false, string? search = null)
    {
        if (from > to)
            return BadRequest("Дата «с» позже даты «по»");

        var query = _db.Bookings.Where(b => b.OfficeId == CurrentOfficeId && b.Date >= from && b.Date <= to);

        // отмены студентов по умолчанию скрыты, чтобы спамер не забил историю
        if (!showCancelled)
            query = query.Where(b => b.Status != BookingStatus.Cancelled);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(b => b.StudentName.ToLower().Contains(s) || b.Group.ToLower().Contains(s));
        }

        var list = query
            .OrderByDescending(b => b.Date).ThenByDescending(b => b.Time)
            .Take(500)   // больше за раз не показываем
            .ToList();

        return Ok(ToRows(list));
    }

    // ЗАПИСИ НА ДЕНЬ: для вкладок «Вызов» и «Закрытие» — видно, кого заденет
    [HttpGet("day")]
    public IActionResult GetDay(DateOnly date)
    {
        var list = _db.Bookings
            .Where(b => b.OfficeId == CurrentOfficeId && b.Date == date && b.Status == BookingStatus.Active)
            .OrderBy(b => b.Time).ThenBy(b => b.Id)
            .ToList();
        return Ok(ToRows(list));
    }

    // НУЖНО ПЕРЕНЕСТИ: вызовы, которые отменила система (закрыли время), и их ещё не назначили заново
    [HttpGet("resummon")]
    public IActionResult GetResummon()
    {
        var adminOnly = _db.Purposes.Where(p => p.AdminOnly).Select(p => p.Id).ToList();
        var list = _db.Bookings
            .Where(b => b.OfficeId == CurrentOfficeId && b.Status == BookingStatus.SystemCancelled
                     && !b.Resolved && adminOnly.Contains(b.PurposeId))
            .OrderBy(b => b.Date).ThenBy(b => b.Time)
            .ToList();
        return Ok(ToRows(list));
    }

    // «Не нужно»: убрать отменённый вызов из списка «Нужно перенести»
    [HttpPost("bookings/{id}/resolve")]
    public IActionResult Resolve(int id)
    {
        var b = _db.Bookings.Find(id);
        if (b == null || b.OfficeId != CurrentOfficeId || b.Status != BookingStatus.SystemCancelled || !IsSummon(b))
            return NotFound("Вызов не найден");

        b.Resolved = true;
        _db.SaveChanges();
        return Ok($"{b.StudentName}: вызов убран из списка");
    }

    // ПОДСКАЗКИ СТУДЕНТОВ: по началу номера или части ФИО, не больше 10
    [HttpGet("students")]
    public IActionResult GetStudents(string? q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(new List<StudentHint>());   // по одной букве не ищем — слишком много

        var s = q.Trim().ToLower();
        var list = _db.Users
            .Where(u => u.Role == "Student" && u.Status == "Approved"
                     && (u.Login.StartsWith(s) || u.FullName.ToLower().Contains(s)))
            .OrderBy(u => u.FullName)
            .Take(10)
            .Select(u => new StudentHint(u.Login, u.FullName, u.Group))
            .ToList();
        return Ok(list);
    }

    // ОТМЕТКА: «Пришёл» или «Не пришёл». Исправить ошибочную отметку можно один раз.
    [HttpPost("bookings/{id}/mark")]
    public IActionResult Mark(int id, MarkRequest r)
    {
        var b = _db.Bookings.Find(id);
        if (b == null || b.OfficeId != CurrentOfficeId)
            return NotFound("Запись не найдена");

        if (r.Status == BookingStatus.Came)
        {
            if (!CanCame(b))
                return BadRequest(b.FirstMark != null ? "Отметку уже исправляли, второй раз нельзя"
                    : b.Status == BookingStatus.NoShow ? $"Исправить отметку можно только в течение {NoShowDays} рабочих дней"
                    : "Отметить «Пришёл» можно только в день записи или позже");
        }
        else if (r.Status == BookingStatus.NoShow)
        {
            if (!CanNoShow(b))
                return BadRequest(b.FirstMark != null ? "Отметку уже исправляли, второй раз нельзя"
                    : Clock.NotEnded(b.Date, b.Time, b.Minutes) ? "«Не пришёл» можно отметить только после окончания записи"
                    : $"«Не пришёл» можно отметить только в течение {NoShowDays} рабочих дней");
        }
        else
        {
            return BadRequest("Неизвестная отметка");
        }

        // запись уже была отмечена — значит, это исправление: запоминаем, что стояло раньше
        if (b.Status == BookingStatus.Came || b.Status == BookingStatus.NoShow)
        {
            b.FirstMark = b.Status;
            b.CorrectedAt = DateTime.UtcNow;
        }
        b.Status = r.Status;

        _db.SaveChanges();
        return Ok($"{b.StudentName}, {b.Date:dd.MM} {b.Time:HH:mm}: {(b.Status == BookingStatus.Came ? "пришёл" : "не пришёл")}");
    }

    // УДАЛЕНИЕ записи с причиной + уведомление студенту
    [HttpPost("bookings/{id}/delete")]
    public IActionResult Delete(int id, DeleteRequest r)
    {
        var b = _db.Bookings.Find(id);
        if (b == null || b.OfficeId != CurrentOfficeId)
            return NotFound("Запись не найдена");

        if (!CanDelete(b))
            return BadRequest("Удалить можно только действующую запись, которая ещё не закончилась");

        var reason = string.IsNullOrWhiteSpace(r.Reason) ? null : r.Reason.Trim();
        b.Status = BookingStatus.AdminDeleted;
        b.Note = reason;

        var what = IsSummon(b) ? "вызов" : "вашу запись";
        _db.Notifications.Add(new Notification
        {
            UserId = b.StudentId,
            Text = $"Учебная часть отменила {what} на {b.Date:dd.MM} {b.Time:HH:mm}" +
                   (reason == null ? "." : $". Причина: {reason}."),
            CreatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();
        return Ok($"Запись {b.StudentName} на {b.Date:dd.MM} в {b.Time:HH:mm} удалена");
    }

    // ---------- ПРАВИЛА КНОПОК ----------
    // Одни и те же правила и для показа кнопок, и для проверки на сервере.

    // исправить уже поставленную отметку: только один раз и в течение 3 рабочих дней
    private static bool CanCorrect(Booking b) =>
        b.FirstMark == null && Clock.WorkdaysBetween(b.Date, Clock.Today) <= NoShowDays;

    // «Пришёл»: действующая запись сегодня или раньше; или исправить ошибочное «Не пришёл»
    private static bool CanCame(Booking b) =>
        (b.Status == BookingStatus.Active && b.Date <= Clock.Today)
        || (b.Status == BookingStatus.NoShow && CanCorrect(b));

    // «Не пришёл»: приём уже закончился, и прошло не больше 3 рабочих дней; или исправить ошибочное «Пришёл»
    private static bool CanNoShow(Booking b) =>
        !Clock.NotEnded(b.Date, b.Time, b.Minutes)
        && ((b.Status == BookingStatus.Active && Clock.WorkdaysBetween(b.Date, Clock.Today) <= NoShowDays)
            || (b.Status == BookingStatus.Came && CanCorrect(b)));

    // удалить: действующая и ещё не закончилась
    private static bool CanDelete(Booking b) =>
        b.Status == BookingStatus.Active && Clock.NotEnded(b.Date, b.Time, b.Minutes);

    // ---------- ПОМОЩНИКИ ----------

    private bool IsSummon(Booking b) => _db.Purposes.Find(b.PurposeId)?.AdminOnly == true;

    // записи → строки для сайта (номер студенческого и цель подставляем по Id)
    private List<AdminBooking> ToRows(List<Booking> list)
    {
        var ids = list.Select(b => b.StudentId).Distinct().ToList();
        var logins = _db.Users.Where(u => ids.Contains(u.Id)).ToDictionary(u => u.Id, u => u.Login);
        var purposes = _db.Purposes.ToDictionary(p => p.Id);

        return list.Select(b =>
        {
            var purpose = purposes.GetValueOrDefault(b.PurposeId);
            return new AdminBooking(
                b.Id, b.Date, b.Time, b.Time.AddMinutes(b.Minutes),
                logins.GetValueOrDefault(b.StudentId, "—"),   // аккаунт могли удалить
                b.StudentName, b.Group,
                purpose?.Name ?? "—", purpose?.AdminOnly == true,
                b.Status, b.Moved, b.Note,
                b.FirstMark, b.CorrectedAt == null ? null : Clock.ToMsk(b.CorrectedAt.Value),
                CanCame(b), CanNoShow(b), CanDelete(b));
        }).ToList();
    }
}
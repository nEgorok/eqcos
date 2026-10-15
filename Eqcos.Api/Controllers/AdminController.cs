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

// одна строка очереди или истории — всё, что нужно показать, и какие кнопки можно нажать
public record AdminBooking(
    int Id, DateOnly Date, TimeOnly Time, TimeOnly End,
    string Login, string StudentName, string Group, string Purpose, bool IsSummon,
    string Status, bool Moved, string? Note,
    bool CanCame, bool CanNoShow, bool CanDelete);

// панель учебной части: живая очередь, история, отметки, удаление
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private const int NoShowDays = 3;   // «Не пришёл» можно поставить в течение 3 рабочих дней

    private readonly AppDbContext _db;

    public AdminController(AppDbContext db)
    {
        _db = db;
    }

    private int? CurrentOfficeId =>
        int.TryParse(User.FindFirst("office")?.Value, out var id) ? id : null;

    // ЖИВАЯ ОЧЕРЕДЬ: действующие записи своего корпуса на сегодня
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

    // ОТМЕТКА: «Пришёл» или «Не пришёл» (и исправление ошибочной отметки)
    [HttpPost("bookings/{id}/mark")]
    public IActionResult Mark(int id, MarkRequest r)
    {
        var b = _db.Bookings.Find(id);
        if (b == null || b.OfficeId != CurrentOfficeId)
            return NotFound("Запись не найдена");

        if (r.Status == BookingStatus.Came)
        {
            if (!CanCame(b))
                return BadRequest("Отметить «Пришёл» можно только в день записи или позже");
            b.Status = BookingStatus.Came;
        }
        else if (r.Status == BookingStatus.NoShow)
        {
            if (!CanNoShow(b))
                return BadRequest(Clock.NotEnded(b.Date, b.Time, b.Minutes)
                    ? "«Не пришёл» можно отметить только после окончания записи"
                    : $"«Не пришёл» можно отметить только в течение {NoShowDays} рабочих дней");
            b.Status = BookingStatus.NoShow;
        }
        else
        {
            return BadRequest("Неизвестная отметка");
        }

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

    // «Пришёл»: действующая запись сегодня или раньше; или исправить ошибочное «Не пришёл»
    private static bool CanCame(Booking b) =>
        (b.Status == BookingStatus.Active && b.Date <= Clock.Today) || b.Status == BookingStatus.NoShow;

    // «Не пришёл»: приём уже закончился и прошло не больше 3 рабочих дней
    private static bool CanNoShow(Booking b) =>
        (b.Status == BookingStatus.Active || b.Status == BookingStatus.Came)
        && !Clock.NotEnded(b.Date, b.Time, b.Minutes)
        && Clock.WorkdaysBetween(b.Date, Clock.Today) <= NoShowDays;

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
                CanCame(b), CanNoShow(b), CanDelete(b));
        }).ToList();
    }
}
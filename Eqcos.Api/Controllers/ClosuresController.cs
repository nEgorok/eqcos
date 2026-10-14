using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Models;
using Eqcos.Api.Data;
using static Eqcos.Api.Services.Schedule;

namespace Eqcos.Api.Controllers;

// админ присылает: день, с, по, причина (корпус — из пропуска)
public record CloseRequest(DateOnly Date, TimeOnly Start, TimeOnly End, string Reason);

[ApiController]
[Route("api/closures")]
public class ClosuresController : ControllerBase
{
    private const int DaysAhead = 14;   // сколько дней вперёд искать место для переноса

    private readonly AppDbContext _db;

    public ClosuresController(AppDbContext db)
    {
        _db = db;
    }

    private int? CurrentOfficeId =>
        int.TryParse(User.FindFirst("office")?.Value, out var id) ? id : null;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // АДМИН: закрытия СВОЕГО корпуса на день
    [Authorize(Roles = "Admin")]
    [HttpGet("mine")]
    public IActionResult GetMine(DateOnly date)
    {
        return Ok(_db.Closures
            .Where(c => c.OfficeId == CurrentOfficeId && c.Date == date)
            .OrderBy(c => c.Start)
            .ToList());
    }

    // АДМИН: закрыть время (+ автоперенос записей)
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public IActionResult Close(CloseRequest r)
    {
        var office = _db.Offices.Find(CurrentOfficeId);
        if (office == null)
            return BadRequest("У учётной записи не указан корпус");

        if (r.Date < Today)
            return BadRequest("Нельзя закрыть прошедший день");

        if (r.Start >= r.End)
            return BadRequest("Время «с» должно быть раньше, чем «по»");

        var reason = string.IsNullOrWhiteSpace(r.Reason) ? "Приёма нет" : r.Reason.Trim();

        _db.Closures.Add(new Closure
        {
            OfficeId = office.Id,
            Date = r.Date,
            Start = r.Start,
            End = r.End,
            Reason = reason
        });
        _db.SaveChanges();

        var (moved, summons) = MoveDisplaced(office, r.Date, r.Start, r.End);

        var text = $"Закрыто {r.Date:dd.MM} с {r.Start:HH:mm} до {r.End:HH:mm}: {reason}";
        if (moved > 0)
            text += $". Перенесено записей: {moved}, студенты увидят уведомление.";
        if (summons > 0)
            text += $" Внимание: в это время есть вызовы ({summons}) — перенесите их сами.";

        return Ok(text);
    }

    // АДМИН: открыть время обратно
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public IActionResult Open(int id)
    {
        var c = _db.Closures.Find(id);
        if (c == null || c.OfficeId != CurrentOfficeId)
            return NotFound("Не найдено");

        _db.Closures.Remove(c);
        _db.SaveChanges();
        return Ok($"Время {c.Start:HH:mm}–{c.End:HH:mm} снова открыто");
    }

    // ---------- ПЕРЕНОС ----------

    // переносит записи студентов из закрытого времени; возвращает (сколько перенесли, сколько вызовов внутри)
    private (int Moved, int Summons) MoveDisplaced(Office office, DateOnly date, TimeOnly start, TimeOnly end)
    {
        var purposes = _db.Purposes.ToList();
        var minutes = purposes.ToDictionary(p => p.Id, p => p.Minutes);
        var adminOnly = purposes.Where(p => p.AdminOnly).Select(p => p.Id).ToList();

        // все записи, которые задевают закрытое время, по порядку очереди
        var inside = _db.Bookings
            .Where(b => b.OfficeId == office.Id && b.Date == date)
            .ToList()
            .Where(b => b.Time < end && start < b.Time.AddMinutes(minutes.GetValueOrDefault(b.PurposeId, Step)))
            .OrderBy(b => b.Time).ThenBy(b => b.Id)
            .ToList();

        var summons = inside.Count(b => adminOnly.Contains(b.PurposeId));
        var students = inside.Where(b => !adminOnly.Contains(b.PurposeId)).ToList();

        var moved = 0;
        foreach (var b in students)
        {
            var oldDate = b.Date;
            var oldTime = b.Time;
            var len = minutes.GetValueOrDefault(b.PurposeId, Step);

            // убираем запись "в сторону", чтобы она не мешала сама себе при поиске
            b.Date = DateOnly.MinValue;
            _db.SaveChanges();

            var place = FindPlace(office, date, end, len);

            if (place != null)
            {
                b.Date = place.Value.Date;
                b.Time = place.Value.Time;
                moved++;
                Notify(b.StudentId, b.Id,
                    $"Ваша запись перенесена с {oldDate:dd.MM} {oldTime:HH:mm} на {b.Date:dd.MM} {b.Time:HH:mm} " +
                    "(учебная часть в это время не принимает).");
            }
            else
            {
                _db.Bookings.Remove(b);
                Notify(b.StudentId, null,
                    $"Ваша запись на {oldDate:dd.MM} {oldTime:HH:mm} отменена: учебная часть не принимает, " +
                    "свободного времени для переноса нет. Запишитесь заново.");
            }
            _db.SaveChanges();
        }

        return (moved, summons);
    }

    // ближайшее свободное место длиной len: сначала в этот день после "after", потом в следующие дни
    private (DateOnly Date, TimeOnly Time)? FindPlace(Office office, DateOnly date, TimeOnly after, int len)
    {
        for (var d = date; d <= date.AddDays(DaysAhead); d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Sunday)
                continue;   // в воскресенье не работают

            var busy = Busy(_db, office.Id, d);

            foreach (var t in GetSlots(office))
            {
                if (d == date && t < after)
                    continue;   // в тот же день — только после закрытия

                if (d == Today && t <= TimeOnly.FromDateTime(DateTime.Now))
                    continue;   // прошедшее время сегодня не предлагаем

                if (Fits(office, t, len) && !Overlaps(busy, t, t.AddMinutes(len)))
                    return (d, t);
            }
        }
        return null;   // за 2 недели места нет
    }

    // создать уведомление студенту
    private void Notify(int userId, int? bookingId, string text)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            BookingId = bookingId,
            Text = text,
            CreatedAt = DateTime.UtcNow   // PostgreSQL хранит время с поясом только в UTC
        });
    }
}
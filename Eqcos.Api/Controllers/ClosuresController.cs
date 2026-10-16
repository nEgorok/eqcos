using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Models;
using Eqcos.Api.Data;
using Eqcos.Api.Services;
using static Eqcos.Api.Services.Schedule;

namespace Eqcos.Api.Controllers;

// админ присылает: день, с, по, причина (корпус — из пропуска)
public record CloseRequest(DateOnly Date, TimeOnly Start, TimeOnly End, string Reason);

[ApiController]
[Route("api/closures")]
public class ClosuresController : ControllerBase
{
    private readonly AppDbContext _db;

    public ClosuresController(AppDbContext db)
    {
        _db = db;
    }

    private int? CurrentOfficeId =>
        int.TryParse(User.FindFirst("office")?.Value, out var id) ? id : null;

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

        if (r.Date < Clock.Today)
            return BadRequest("Нельзя закрыть прошедший день");

        if (r.Start >= r.End)
            return BadRequest("Время «с» должно быть раньше, чем «по»");

        if (!Mover.OnGrid(r.Start) || !Mover.OnGrid(r.End))
            return BadRequest("Время должно быть кратно 5 минутам, например 10:05 или 10:10");

        var reason = string.IsNullOrWhiteSpace(r.Reason) ? "Приёма нет" : r.Reason.Trim();

        // всё закрытие — одна транзакция: или всё перенеслось, или ничего не поменялось
        using var tx = _db.Database.BeginTransaction();
        LockDay(_db, office.Id, r.Date);

        _db.Closures.Add(new Closure
        {
            OfficeId = office.Id,
            Date = r.Date,
            Start = r.Start,
            End = r.End,
            Reason = reason
        });
        _db.SaveChanges();

        var (moved, cancelled, summons) = MoveDisplaced(office, r.Date, r.Start, r.End);
        tx.Commit();

        var text = $"Закрыто {r.Date:dd.MM} с {r.Start:HH:mm} до {r.End:HH:mm}: {reason}";
        if (moved > 0)
            text += $". Перенесено записей: {moved}";
        if (cancelled > 0)
            text += $". Отменено (некуда перенести): {cancelled}";
        if (summons.Count > 0)
            text += $". Отменены вызовы (они в списке «Нужно перенести» на вкладке «Вызов»): {string.Join(", ", summons)}";
        if (moved + cancelled + summons.Count > 0)
            text += ". Студенты увидят уведомление.";

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

    // убирает записи из закрытого времени.
    // Записи студентов переносятся в порядке очереди (Mover), вызовы отменяются — они попадут
    // в список «Нужно перенести» на вкладке «Вызов».
    // Возвращает: сколько перенесли, сколько отменили, кого из вызванных надо вызвать заново.
    private (int Moved, int Cancelled, List<string> Summons) MoveDisplaced(Office office, DateOnly date, TimeOnly start, TimeOnly end)
    {
        var adminOnly = _db.Purposes.Where(p => p.AdminOnly).Select(p => p.Id).ToList();

        // действующие записи, которые задевают закрытое время
        var inside = _db.Bookings
            .Where(b => b.OfficeId == office.Id && b.Date == date && b.Status == BookingStatus.Active)
            .ToList()
            .Where(b => b.Time < end && start < b.Time.AddMinutes(b.Minutes))
            .OrderBy(b => b.Time).ThenBy(b => b.Id)
            .ToList();

        // 1) вызовы — отменить
        var summons = new List<string>();
        foreach (var b in inside.Where(b => adminOnly.Contains(b.PurposeId)))
        {
            b.Status = BookingStatus.SystemCancelled;
            b.Note = "Закрыто время";
            summons.Add($"{b.StudentName} ({b.Time:HH:mm})");
            Mover.Notify(_db, b.StudentId, null,
                $"Вызов на {b.Date:dd.MM} {b.Time:HH:mm} отменён: учебная часть в это время не принимает. " +
                "Учебная часть назначит новое время.");
        }
        _db.SaveChanges();

        // 2) записи студентов — перенести на ближайшее свободное время
        var students = inside.Where(b => !adminOnly.Contains(b.PurposeId)).ToList();
        var (moved, cancelled) = Mover.MoveStudents(_db, office, date, end, students,
            "учебная часть в это время не принимает", "Закрыто время, переносить некуда");

        return (moved, cancelled, summons);
    }
}
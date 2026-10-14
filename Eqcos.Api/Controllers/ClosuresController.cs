using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Models;
using Eqcos.Api.Data;

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

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // АДМИН: закрытия СВОЕГО корпуса на день
    // пример: GET api/closures/mine?date=2026-10-22
    [Authorize(Roles = "Admin")]
    [HttpGet("mine")]
    public IActionResult GetMine(DateOnly date)
    {
        return Ok(_db.Closures
            .Where(c => c.OfficeId == CurrentOfficeId && c.Date == date)
            .OrderBy(c => c.Start)
            .ToList());
    }

    // АДМИН: закрыть время
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public IActionResult Close(CloseRequest r)
    {
        if (CurrentOfficeId == null)
            return BadRequest("У учётной записи не указан корпус");

        if (r.Date < Today)
            return BadRequest("Нельзя закрыть прошедший день");

        if (r.Start >= r.End)
            return BadRequest("Время «с» должно быть раньше, чем «по»");

        var reason = string.IsNullOrWhiteSpace(r.Reason) ? "Приёма нет" : r.Reason.Trim();

        _db.Closures.Add(new Closure
        {
            OfficeId = CurrentOfficeId.Value,
            Date = r.Date,
            Start = r.Start,
            End = r.End,
            Reason = reason
        });
        _db.SaveChanges();

        // кто уже записан на это время?
        var inside = _db.Bookings.Count(b => b.OfficeId == CurrentOfficeId && b.Date == r.Date
                                          && b.Time >= r.Start && b.Time < r.End);

        var text = $"Закрыто {r.Date:dd.MM} с {r.Start:HH:mm} до {r.End:HH:mm}: {reason}";
        if (inside > 0)
            text += $". Внимание: в это время уже есть записи: {inside}. Удалите их или предупредите студентов.";

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
}
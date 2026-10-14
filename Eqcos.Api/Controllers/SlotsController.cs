using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Models;
using Eqcos.Api.Data;
using static Eqcos.Api.Services.Schedule;   // Step, GetSlots, Fits, Overlaps — общие с ClosuresController

namespace Eqcos.Api.Controllers;

// студент присылает: корпус, день, время, цель
public record BookRequest(int OfficeId, DateOnly Date, TimeOnly Time, int PurposeId);

// админ присылает: номер студенческого, день, время (корпус берём из его пропуска)
public record SummonRequest(string StudentLogin, DateOnly Date, TimeOnly Time);

// одна запись студента для главной страницы
public record MyBooking(int Id, string Text, bool CanCancel);

[ApiController]
[Route("api")]
public class SlotsController : ControllerBase
{
    private readonly AppDbContext _db;

    public SlotsController(AppDbContext db)
    {
        _db = db;
    }

    // Id пользователя из пропуска
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // корпус админа из пропуска (у студента — null)
    private int? CurrentOfficeId =>
        int.TryParse(User.FindFirstValue("office"), out var id) ? id : null;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ---------- ВСЕ ----------

    // список корпусов
    [HttpGet("offices")]
    public IActionResult GetOffices()
    {
        return Ok(_db.Offices.OrderBy(o => o.Id).ToList());
    }

    // цели, которые может выбрать студент (без "только админ")
    [HttpGet("purposes")]
    public IActionResult GetPurposes()
    {
        return Ok(_db.Purposes.Where(p => !p.AdminOnly).OrderBy(p => p.Id).ToList());
    }

    // свободные 5-минутные клетки в корпусе на день
    [HttpGet("slots/free")]
    public IActionResult GetFree(int officeId, DateOnly date)
    {
        var office = _db.Offices.Find(officeId);
        if (office == null)
            return NotFound("Корпус не найден");

        if (date < Today)
            return Ok(new List<string>());

        var busy = Busy(officeId, date);   // занятые отрезки этого дня

        var free = GetSlots(office).Where(t => !Overlaps(busy, t, t.AddMinutes(Step)));

        if (date == Today)
            free = free.Where(t => t > TimeOnly.FromDateTime(DateTime.Now));

        return Ok(free.Select(t => t.ToString("HH:mm")));
    }

    // ---------- СТУДЕНТ ----------

    // записаться
    [Authorize(Roles = "Student")]
    [HttpPost("slots/book")]
    public IActionResult Book(BookRequest r)
    {
        var office = _db.Offices.Find(r.OfficeId);
        if (office == null)
            return BadRequest("Корпус не найден");

        var purpose = _db.Purposes.Find(r.PurposeId);
        if (purpose == null || purpose.AdminOnly)
            return BadRequest("Выберите цель визита");

        if (r.Date < Today)
            return BadRequest("Нельзя записаться на прошедший день");

        if (!Fits(office, r.Time, purpose.Minutes))
            return BadRequest("Такого времени нет");

        // одна СВОЯ запись (вызовы не считаются)
        var adminOnly = AdminOnlyIds();
        var my = _db.Bookings
            .Where(b => b.StudentId == CurrentUserId && b.Date >= Today)
            .ToList()
            .FirstOrDefault(b => !adminOnly.Contains(b.PurposeId));
        if (my != null)
            return BadRequest($"Вы уже записаны на {my.Date:dd.MM} в {my.Time:HH:mm}");

        if (Overlaps(Busy(r.OfficeId, r.Date), r.Time, r.Time.AddMinutes(purpose.Minutes)))
            return BadRequest("Время занято");

        var user = _db.Users.Find(CurrentUserId)!;
        _db.Bookings.Add(new Booking
        {
            OfficeId = r.OfficeId,
            Date = r.Date,
            Time = r.Time,
            PurposeId = r.PurposeId,
            StudentId = user.Id,
            StudentName = user.FullName,
            Group = user.Group
        });
        _db.SaveChanges();
        return Ok($"Вы записаны: {office.Name}, {r.Date:dd.MM} в {r.Time:HH:mm} ({purpose.Name})");
    }

    // мои записи (своя + вызовы), начиная с сегодня
    [Authorize(Roles = "Student")]
    [HttpGet("slots/my")]
    public IActionResult GetMy()
    {
        var adminOnly = AdminOnlyIds();

        var list = _db.Bookings
            .Where(b => b.StudentId == CurrentUserId && b.Date >= Today)
            .OrderBy(b => b.Date).ThenBy(b => b.Time)
            .ToList();

        var result = list.Select(b => new MyBooking(
            b.Id,
            $"{OfficeName(b.OfficeId)}, {b.Date:dd.MM} в {b.Time:HH:mm} ({PurposeName(b.PurposeId)})",
            !adminOnly.Contains(b.PurposeId)));   // вызов отменить нельзя

        return Ok(result);
    }

    // отменить СВОЮ запись по Id
    [Authorize(Roles = "Student")]
    [HttpDelete("slots/my/{id}")]
    public IActionResult CancelMy(int id)
    {
        var b = _db.Bookings.Find(id);
        if (b == null || b.StudentId != CurrentUserId)
            return NotFound("Запись не найдена");

        if (AdminOnlyIds().Contains(b.PurposeId))
            return BadRequest("Вызов может отменить только учебная часть");

        _db.Bookings.Remove(b);
        _db.SaveChanges();
        return Ok($"Запись на {b.Date:dd.MM} в {b.Time:HH:mm} отменена");
    }

    // ---------- АДМИН ----------

    // записи СВОЕГО корпуса на день
    [Authorize(Roles = "Admin")]
    [HttpGet("slots/bookings")]
    public IActionResult GetBookings(DateOnly date)
    {
        var list = _db.Bookings
            .Where(b => b.OfficeId == CurrentOfficeId && b.Date == date)
            .OrderBy(b => b.Time)
            .ToList();

        return Ok(list.Select(b => new
        {
            b.Id,
            b.Time,
            b.StudentName,
            b.Group,
            Purpose = PurposeName(b.PurposeId)
        }));
    }

    // поставить студенту вызов на профбеседу
    [Authorize(Roles = "Admin")]
    [HttpPost("slots/summon")]
    public IActionResult Summon(SummonRequest r)
    {
        var office = _db.Offices.Find(CurrentOfficeId);
        if (office == null)
            return BadRequest("У учётной записи не указан корпус");

        var student = _db.Users.FirstOrDefault(u => u.Login == r.StudentLogin && u.Role == "Student");
        if (student == null)
            return BadRequest("Студент с таким номером не найден");

        var purpose = _db.Purposes.First(p => p.AdminOnly);   // профбеседа

        if (r.Date < Today)
            return BadRequest("Нельзя назначить на прошедший день");

        if (!Fits(office, r.Time, purpose.Minutes))
            return BadRequest($"{purpose.Minutes} минут с {r.Time:HH:mm} не помещаются в рабочее время");

        if (Overlaps(Busy(office.Id, r.Date), r.Time, r.Time.AddMinutes(purpose.Minutes)))
            return BadRequest("Это время пересекается с другой записью");

        _db.Bookings.Add(new Booking
        {
            OfficeId = office.Id,
            Date = r.Date,
            Time = r.Time,
            PurposeId = purpose.Id,
            StudentId = student.Id,
            StudentName = student.FullName,
            Group = student.Group
        });
        _db.SaveChanges();
        return Ok($"Вызов назначен: {student.FullName}, {r.Date:dd.MM} в {r.Time:HH:mm}");
    }

    // удалить запись своего корпуса (в том числе вызов)
    [Authorize(Roles = "Admin")]
    [HttpDelete("slots/bookings/{id}")]
    public IActionResult DeleteBooking(int id)
    {
        var b = _db.Bookings.Find(id);
        if (b == null || b.OfficeId != CurrentOfficeId)
            return NotFound("Запись не найдена");

        _db.Bookings.Remove(b);
        _db.SaveChanges();
        return Ok($"Запись {b.StudentName} на {b.Date:dd.MM} в {b.Time:HH:mm} удалена");
    }

    // ---------- ПОМОЩНИКИ ----------

    private string PurposeName(int id) => _db.Purposes.Find(id)?.Name ?? "—";
    private string OfficeName(int id) => _db.Offices.Find(id)?.Name ?? "—";

    // Id целей "только админ" (сейчас это одна профбеседа)
    private List<int> AdminOnlyIds() =>
        _db.Purposes.Where(p => p.AdminOnly).Select(p => p.Id).ToList();

    // занятые отрезки этого корпуса на день (сама логика — в Services/Schedule.cs)
    private List<(TimeOnly Start, TimeOnly End)> Busy(int officeId, DateOnly date) => Eqcos.Api.Services.Schedule.Busy(_db, officeId, date);
}

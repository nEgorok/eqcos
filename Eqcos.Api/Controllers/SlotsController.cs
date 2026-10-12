using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Models;
using Eqcos.Api.Data;

namespace Eqcos.Api.Controllers;

// студент присылает: корпус, день, время
public record BookRequest(int OfficeId, DateOnly Date, TimeOnly Time);

[ApiController]
[Route("api")]
public class SlotsController : ControllerBase
{
    private const int Step = 5;   // шаг сетки в минутах

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

    // ВСЕ: список корпусов
    [HttpGet("offices")]
    public IActionResult GetOffices()
    {
        return Ok(_db.Offices.OrderBy(o => o.Id).ToList());
    }

    // ВСЕ: свободное время в корпусе на день
    // пример: GET api/slots/free?officeId=1&date=2026-10-13
    [HttpGet("slots/free")]
    public IActionResult GetFree(int officeId, DateOnly date)
    {
        var office = _db.Offices.Find(officeId);
        if (office == null)
            return NotFound("Корпус не найден");

        if (date < Today)
            return Ok(new List<string>());   // прошедший день — ничего нет

        var taken = _db.Bookings
            .Where(b => b.OfficeId == officeId && b.Date == date)
            .Select(b => b.Time)
            .ToList();

        var free = GetSlots(office).Where(t => !taken.Contains(t));

        // сегодня не показываем время, которое уже прошло
        if (date == Today)
            free = free.Where(t => t > TimeOnly.FromDateTime(DateTime.Now));

        return Ok(free.Select(t => t.ToString("HH:mm")));
    }

    // СТУДЕНТ: записаться
    [Authorize(Roles = "Student")]
    [HttpPost("slots/book")]
    public IActionResult Book(BookRequest r)
    {
        var office = _db.Offices.Find(r.OfficeId);
        if (office == null)
            return BadRequest("Корпус не найден");

        if (r.Date < Today)
            return BadRequest("Нельзя записаться на прошедший день");

        if (!GetSlots(office).Contains(r.Time))
            return BadRequest("Такого времени нет");

        // одна активная запись на студента (сегодня или позже)
        var my = _db.Bookings.FirstOrDefault(b => b.StudentId == CurrentUserId && b.Date >= Today);
        if (my != null)
            return BadRequest($"Вы уже записаны на {my.Date:dd.MM} в {my.Time:HH:mm}");

        if (_db.Bookings.Any(b => b.OfficeId == r.OfficeId && b.Date == r.Date && b.Time == r.Time))
            return BadRequest("Время занято");

        var user = _db.Users.Find(CurrentUserId)!;
        _db.Bookings.Add(new Booking
        {
            OfficeId = r.OfficeId,
            Date = r.Date,
            Time = r.Time,
            StudentId = user.Id,
            StudentName = user.FullName,
            Group = user.Group
        });
        _db.SaveChanges();
        return Ok($"Вы записаны: {office.Name}, {r.Date:dd.MM} в {r.Time:HH:mm}");
    }

    // СТУДЕНТ: моя запись
    [Authorize(Roles = "Student")]
    [HttpGet("slots/my")]
    public IActionResult GetMy()
    {
        var my = _db.Bookings.FirstOrDefault(b => b.StudentId == CurrentUserId && b.Date >= Today);
        if (my == null)
            return Ok("У вас нет записи");

        var office = _db.Offices.Find(my.OfficeId);
        return Ok($"Вы записаны: {office?.Name}, {my.Date:dd.MM} в {my.Time:HH:mm}");
    }

    // СТУДЕНТ: отменить свою запись
    [Authorize(Roles = "Student")]
    [HttpDelete("slots/my")]
    public IActionResult CancelMy()
    {
        var my = _db.Bookings.FirstOrDefault(b => b.StudentId == CurrentUserId && b.Date >= Today);
        if (my == null)
            return NotFound("У вас нет записи");

        _db.Bookings.Remove(my);
        _db.SaveChanges();
        return Ok($"Запись на {my.Date:dd.MM} в {my.Time:HH:mm} отменена");
    }

    // АДМИН: записи СВОЕГО корпуса на день
    // пример: GET api/slots/bookings?date=2026-10-13
    [Authorize(Roles = "Admin")]
    [HttpGet("slots/bookings")]
    public IActionResult GetBookings(DateOnly date)
    {
        return Ok(_db.Bookings
            .Where(b => b.OfficeId == CurrentOfficeId && b.Date == date)
            .OrderBy(b => b.Time)
            .ToList());
    }

    // АДМИН: удалить запись своего корпуса
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

    // все слоты корпуса с шагом Step, без перерыва
    private static List<TimeOnly> GetSlots(Office office)
    {
        var slots = new List<TimeOnly>();
        var t = office.Open;
        while (t < office.Close)
        {
            if (t < office.BreakStart || t >= office.BreakEnd)
                slots.Add(t);
            t = t.AddMinutes(Step);
        }
        return slots;
    }
}
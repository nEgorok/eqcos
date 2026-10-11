using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Models;
using Eqcos.Api.Data;

namespace Eqcos.Api.Controllers;

public record BookRequest(TimeOnly Time);   // студент присылает только время

[ApiController]
[Route("api/slots")]
public class SlotsController : ControllerBase
{
    private readonly AppDbContext _db;

    public SlotsController(AppDbContext db)
    {
        _db = db;
    }

    // Id пользователя из пропуска
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ГОСТЬ и все: свободное время
    [HttpGet("free")]
    public IActionResult GetFree()
    {
        var taken = _db.Bookings.Select(b => b.Time).ToList();
        var free = GetSlots(15).Where(t => !taken.Contains(t));
        return Ok(free.Select(t => t.ToString("HH:mm")));
    }

    // СТУДЕНТ: записаться
    [Authorize(Roles = "Student")]
    [HttpPost("book")]
    public IActionResult Book(BookRequest request)
    {
        if (!GetSlots(15).Contains(request.Time))
            return BadRequest("Такого времени нет");

        var my = _db.Bookings.FirstOrDefault(b => b.StudentId == CurrentUserId);
        if (my != null)
            return BadRequest($"Вы уже записаны на {my.Time:HH:mm}");

        if (_db.Bookings.Any(b => b.Time == request.Time))
            return BadRequest("Время занято");

        var user = _db.Users.Find(CurrentUserId)!;   // ФИО и группу берём из базы
        _db.Bookings.Add(new Booking
        {
            Time = request.Time,
            StudentId = user.Id,
            StudentName = user.FullName,
            Group = user.Group
        });
        _db.SaveChanges();
        return Ok($"Вы записаны на {request.Time:HH:mm}");
    }

    // СТУДЕНТ: моя запись
    [Authorize(Roles = "Student")]
    [HttpGet("my")]
    public IActionResult GetMy()
    {
        var my = _db.Bookings.FirstOrDefault(b => b.StudentId == CurrentUserId);
        if (my == null)
            return Ok("У вас нет записи");
        return Ok($"Вы записаны на {my.Time:HH:mm}");
    }

    // СТУДЕНТ: отменить свою запись
    [Authorize(Roles = "Student")]
    [HttpDelete("my")]
    public IActionResult CancelMy()
    {
        var my = _db.Bookings.FirstOrDefault(b => b.StudentId == CurrentUserId);
        if (my == null)
            return NotFound("У вас нет записи");

        _db.Bookings.Remove(my);
        _db.SaveChanges();
        return Ok($"Запись на {my.Time:HH:mm} отменена");
    }

    // АДМИН: все записи с именами
    [Authorize(Roles = "Admin")]
    [HttpGet("bookings")]
    public IActionResult GetBookings()
    {
        return Ok(_db.Bookings.OrderBy(b => b.Time).ToList());
    }

    // АДМИН: удалить любую запись по её Id
    [Authorize(Roles = "Admin")]
    [HttpDelete("bookings/{id}")]
    public IActionResult DeleteBooking(int id)
    {
        var b = _db.Bookings.Find(id);
        if (b == null)
            return NotFound("Запись не найдена");

        _db.Bookings.Remove(b);
        _db.SaveChanges();
        return Ok($"Запись {b.StudentName} на {b.Time:HH:mm} удалена");
    }

    private List<TimeOnly> GetSlots(int slotMinutes)
    {
        var slots = new List<TimeOnly>();
        var t = new TimeOnly(10, 0);
        while (t < new TimeOnly(16, 0))
        {
            if (t.Hour != 13) slots.Add(t);
            t = t.AddMinutes(slotMinutes);
        }
        return slots;
    }
}
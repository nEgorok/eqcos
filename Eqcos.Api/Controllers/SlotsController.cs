using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Models;
using Eqcos.Api.Data;

namespace Eqcos.Api.Controllers;

[ApiController]
[Route("api/slots")]
public class SlotsController : ControllerBase
{
    // база данных, её передаёт сам ASP.NET (мы зарегистрировали её в Program.cs)
    private readonly AppDbContext _db;

    public SlotsController(AppDbContext db)
    {
        _db = db;
    }

    // все слоты
    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(GetSlots(15).Select(t => t.ToString("HH:mm")));
    }

    // свободные слоты
    [HttpGet("free")]
    public IActionResult GetFree()
    {
        var taken = _db.Bookings.Select(b => b.Time).ToList();
        var free = GetSlots(15).Where(t => !taken.Contains(t));
        return Ok(free.Select(t => t.ToString("HH:mm")));
    }

    // записаться
    [HttpPost("book")]
    public IActionResult Book(Booking request)
    {
        if (!GetSlots(15).Contains(request.Time))
            return BadRequest("Такого времени нет");

        var my = _db.Bookings.FirstOrDefault(b => b.StudentId == request.StudentId);
        if (my != null)
            return BadRequest($"Вы уже записаны на {my.Time:HH:mm}");

        if (_db.Bookings.Any(b => b.Time == request.Time))
            return BadRequest("Время занято");

        request.Id = 0;              // Id выдаст сама база
        _db.Bookings.Add(request);
        _db.SaveChanges();           // записали в PostgreSQL
        return Ok($"Вы записаны на {request.Time:HH:mm}");
    }

    // все записи
    [HttpGet("bookings")]
    public IActionResult GetBookings()
    {
        return Ok(_db.Bookings.OrderBy(b => b.Time).ToList());
    }

    // отменить запись
    [HttpDelete("book/{studentId}")]
    public IActionResult Cancel(int studentId)
    {
        var my = _db.Bookings.FirstOrDefault(b => b.StudentId == studentId);
        if (my == null)
            return NotFound("У вас нет записи");

        _db.Bookings.Remove(my);
        _db.SaveChanges();
        return Ok($"Запись на {my.Time:HH:mm} отменена");
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
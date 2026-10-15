using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Models;
using Eqcos.Api.Data;
using Eqcos.Api.Services;
using static Eqcos.Api.Services.Schedule;   // Step, GetSlots, Fits, Overlaps, Busy, LockDay — общие с ClosuresController

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

    // ---------- ВСЕ ----------

    // календарь: какой сегодня день по Москве и до какого дня можно записаться
    [HttpGet("calendar")]
    public IActionResult GetCalendar()
    {
        return Ok(new { Today = Clock.Today, MaxDate = Clock.MaxDate });
    }

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

        // прошедший день, выходной или дальше двух недель — свободного времени нет
        if (date < Clock.Today || date > Clock.MaxDate || !Clock.IsWorkday(date))
            return Ok(new List<string>());

        var busy = Busy(_db, officeId, date);   // занятые отрезки этого дня

        var free = GetSlots(office).Where(t => !Overlaps(busy, t, t.AddMinutes(Step)));

        if (date == Clock.Today)
            free = free.Where(t => t > Clock.Time);   // прошедшее время сегодня не показываем

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

        var dateError = CheckDate(r.Date, r.Time);
        if (dateError != null)
            return BadRequest(dateError);

        if (r.Date > Clock.MaxDate)
            return BadRequest($"Записаться можно не дальше чем на {Clock.DaysAhead} дней вперёд (до {Clock.MaxDate:dd.MM})");

        if (!Fits(office, r.Time, purpose.Minutes))
            return BadRequest("Такого времени нет");

        var user = _db.Users.Find(CurrentUserId)!;
        if (user.Status != "Approved")
            return BadRequest("Записаться можно после того, как учебная часть подтвердит вашу учётную запись");

        // дальше — строго по очереди: сначала этот студент, потом этот корпус в этот день
        using var tx = _db.Database.BeginTransaction();
        LockStudent(_db, user.Id);
        LockDay(_db, r.OfficeId, r.Date);

        // одна СВОЯ действующая запись (вызовы не считаются, прошедшая сегодня — тоже)
        var my = MyActive(user.Id).FirstOrDefault(b => !IsSummon(b));
        if (my != null)
            return BadRequest($"Вы уже записаны на {my.Date:dd.MM} в {my.Time:HH:mm}");

        if (Overlaps(Busy(_db, r.OfficeId, r.Date), r.Time, r.Time.AddMinutes(purpose.Minutes)))
            return BadRequest("Это время только что заняли, выберите другое");

        _db.Bookings.Add(new Booking
        {
            OfficeId = r.OfficeId,
            Date = r.Date,
            Time = r.Time,
            PurposeId = r.PurposeId,
            Minutes = purpose.Minutes,
            StudentId = user.Id,
            StudentName = user.FullName,
            Group = user.Group
        });
        _db.SaveChanges();
        tx.Commit();   // всё прошло — фиксируем и отпускаем очередь
        return Ok($"Вы записаны: {office.Name}, {r.Date:dd.MM} в {r.Time:HH:mm} ({purpose.Name})");
    }

    // мои записи (своя + вызовы), которые ещё не закончились
    [Authorize(Roles = "Student")]
    [HttpGet("slots/my")]
    public IActionResult GetMy()
    {
        var result = MyActive(CurrentUserId).Select(b => new MyBooking(
            b.Id,
            $"{OfficeName(b.OfficeId)}, {b.Date:dd.MM} в {b.Time:HH:mm} ({PurposeName(b.PurposeId)})",
            !IsSummon(b)));   // вызов отменить нельзя

        return Ok(result);
    }

    // отменить СВОЮ запись по Id
    [Authorize(Roles = "Student")]
    [HttpDelete("slots/my/{id}")]
    public IActionResult CancelMy(int id)
    {
        var b = _db.Bookings.Find(id);
        if (b == null || b.StudentId != CurrentUserId || b.Status != BookingStatus.Active)
            return NotFound("Запись не найдена");

        if (IsSummon(b))
            return BadRequest("Вызов может отменить только учебная часть");

        if (!Clock.NotEnded(b.Date, b.Time, b.Minutes))
            return BadRequest("Эта запись уже прошла");

        b.Status = BookingStatus.Cancelled;   // не удаляем, а помечаем — останется в истории
        _db.SaveChanges();
        return Ok($"Запись на {b.Date:dd.MM} в {b.Time:HH:mm} отменена");
    }

    // ---------- АДМИН ----------

    // действующие записи СВОЕГО корпуса на день
    [Authorize(Roles = "Admin")]
    [HttpGet("slots/bookings")]
    public IActionResult GetBookings(DateOnly date)
    {
        var list = _db.Bookings
            .Where(b => b.OfficeId == CurrentOfficeId && b.Date == date && b.Status == BookingStatus.Active)
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

        var dateError = CheckDate(r.Date, r.Time);
        if (dateError != null)
            return BadRequest(dateError);

        if (!Fits(office, r.Time, purpose.Minutes))
            return BadRequest($"{purpose.Minutes} минут с {r.Time:HH:mm} не помещаются в рабочее время");

        using var tx = _db.Database.BeginTransaction();
        LockDay(_db, office.Id, r.Date);

        // перенос записей студентов под вызов сделаем в шаге 22, пока — отказ
        if (Overlaps(Busy(_db, office.Id, r.Date), r.Time, r.Time.AddMinutes(purpose.Minutes)))
            return BadRequest("Это время пересекается с другой записью");

        _db.Bookings.Add(new Booking
        {
            OfficeId = office.Id,
            Date = r.Date,
            Time = r.Time,
            PurposeId = purpose.Id,
            Minutes = purpose.Minutes,
            StudentId = student.Id,
            StudentName = student.FullName,
            Group = student.Group
        });
        _db.SaveChanges();
        tx.Commit();
        return Ok($"Вызов назначен: {student.FullName}, {r.Date:dd.MM} в {r.Time:HH:mm}");
    }

    // удалить запись своего корпуса (в том числе вызов)
    [Authorize(Roles = "Admin")]
    [HttpDelete("slots/bookings/{id}")]
    public IActionResult DeleteBooking(int id)
    {
        var b = _db.Bookings.Find(id);
        if (b == null || b.OfficeId != CurrentOfficeId || b.Status != BookingStatus.Active)
            return NotFound("Запись не найдена");

        b.Status = BookingStatus.AdminDeleted;
        _db.SaveChanges();
        return Ok($"Запись {b.StudentName} на {b.Date:dd.MM} в {b.Time:HH:mm} удалена");
    }

    // ---------- ПОМОЩНИКИ ----------

    private string PurposeName(int id) => _db.Purposes.Find(id)?.Name ?? "—";
    private string OfficeName(int id) => _db.Offices.Find(id)?.Name ?? "—";

    // вызов ли это (цель "только админ")
    private bool IsSummon(Booking b) => _db.Purposes.Find(b.PurposeId)?.AdminOnly == true;

    // общие проверки дня и времени для записи и вызова; null — всё в порядке
    private static string? CheckDate(DateOnly date, TimeOnly time)
    {
        if (date < Clock.Today)
            return "Нельзя записаться на прошедший день";
        if (!Clock.IsWorkday(date))
            return "В субботу и воскресенье учебная часть не работает";
        if (date == Clock.Today && time <= Clock.Time)
            return "Это время уже прошло";
        return null;
    }

    // действующие записи студента, которые ещё не закончились, по порядку
    private List<Booking> MyActive(int userId)
    {
        return _db.Bookings
            .Where(b => b.StudentId == userId && b.Status == BookingStatus.Active && b.Date >= Clock.Today)
            .OrderBy(b => b.Date).ThenBy(b => b.Time)
            .ToList()
            .Where(b => Clock.NotEnded(b.Date, b.Time, b.Minutes))   // сегодняшняя прошедшая не мешает
            .ToList();
    }
}
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Data;
using Eqcos.Api.Models;

namespace Eqcos.Api.Controllers;

public record ProfileUpdate(string FullName, string Group);
public record DeleteAccountRequest(string Password);

[ApiController]
[Route("api/profile")]
[Authorize]   // любой вошедший
public class ProfileController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<User> _hasher = new();

    public ProfileController(AppDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // мои данные
    [HttpGet]
    public IActionResult Get()
    {
        var u = _db.Users.Find(CurrentUserId);
        if (u == null)
            return NotFound("Пользователь не найден");

        var office = u.OfficeId == null ? null : _db.Offices.Find(u.OfficeId)?.Name;

        return Ok(new { u.Login, u.FullName, u.Group, u.Role, Office = office, u.Status, u.RejectReason });
    }

    // СТУДЕНТ: изменить ФИО и группу
    [Authorize(Roles = "Student")]
    [HttpPut]
    public IActionResult Update(ProfileUpdate r)
    {
        var fullName = r.FullName.Trim();
        var group = r.Group.Trim();
        if (fullName == "" || group == "")
            return BadRequest("ФИО и группа не могут быть пустыми");

        var u = _db.Users.Find(CurrentUserId)!;

        // данные поменялись — снова на проверку
        var changed = u.FullName != fullName || u.Group != group;
        if (changed)
        {
            u.Status = "Pending";
            u.RejectReason = null;
        }

        u.FullName = fullName;
        u.Group = group;

        // в будущих записях тоже обновить, чтобы админ видел новые данные
        foreach (var b in _db.Bookings.Where(b => b.StudentId == u.Id && b.Date >= Today))
        {
            b.StudentName = fullName;
            b.Group = group;
        }

        _db.SaveChanges();
        return Ok(changed ? "Данные сохранены и отправлены на проверку в учебную часть" : "Данные сохранены");
    }

    // СТУДЕНТ: удалить аккаунт (нужен пароль)
    [Authorize(Roles = "Student")]
    [HttpPost("delete")]
    public IActionResult Delete(DeleteAccountRequest r)
    {
        var u = _db.Users.Find(CurrentUserId)!;

        var check = _hasher.VerifyHashedPassword(u, u.PasswordHash, r.Password);
        if (check == PasswordVerificationResult.Failed)
            return BadRequest("Неверный пароль");

        // будущие записи освобождаем, уведомления удаляем
        _db.Bookings.RemoveRange(_db.Bookings.Where(b => b.StudentId == u.Id && b.Date >= Today));
        _db.Notifications.RemoveRange(_db.Notifications.Where(n => n.UserId == u.Id));
        _db.Users.Remove(u);
        _db.SaveChanges();

        return Ok("Аккаунт удалён");
    }
}
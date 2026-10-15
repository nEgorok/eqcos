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

        return Ok(new
        {
            u.Login,
            u.FullName,
            u.Group,
            u.Role,
            Office = office,
            u.Status,
            u.RejectReason,
            u.PendingFullName,
            u.PendingGroup
        });
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

        if (u.Status == "Approved")
        {
            // вернул как было — заявку на изменение снимаем
            if (u.FullName == fullName && u.Group == group)
            {
                u.PendingFullName = null;
                u.PendingGroup = null;
                _db.SaveChanges();
                return Ok("Данные не изменились");
            }

            // подтверждённый: старые данные продолжают действовать, новые — на проверку
            u.PendingFullName = fullName;
            u.PendingGroup = group;
            _db.SaveChanges();
            return Ok("Изменения отправлены на проверку в учебную часть. Пока действуют прежние данные.");
        }

        // ещё не подтверждён или отклонён: правим сразу и (снова) отправляем на проверку.
        // Даже если данные те же — отклонённый может отправить их повторно (вдруг отклонили по ошибке).
        u.FullName = fullName;
        u.Group = group;
        u.Status = "Pending";
        u.RejectReason = null;
        _db.SaveChanges();
        return Ok("Данные сохранены и отправлены на проверку в учебную часть");
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
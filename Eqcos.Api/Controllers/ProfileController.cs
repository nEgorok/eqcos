using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Data;
using Eqcos.Api.Services;

namespace Eqcos.Api.Controllers;

// новые данные студента: ФИО тремя полями, группа двумя частями ("204" + "Лг")
public record ProfileUpdate(string? LastName, string? FirstName, string? MiddleName, string? GroupDigits, string? GroupCode);

[ApiController]
[Route("api/profile")]
[Authorize]   // любой вошедший
public class ProfileController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProfileController(AppDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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
            u.OfficeId,        // номер корпуса: на главной студенту показываем только его
            Office = office,   // название корпуса
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
        var last = StudentData.Fix(r.LastName);
        var first = StudentData.Fix(r.FirstName);
        var middle = StudentData.Fix(r.MiddleName);
        var digits = (r.GroupDigits ?? "").Trim();
        var code = (r.GroupCode ?? "").Trim();

        var error = StudentData.CheckName(last, first, middle) ?? StudentData.CheckGroup(_db, digits, code);
        if (error != null)
            return BadRequest(error);

        var fullName = StudentData.JoinName(last, first, middle);
        var group = digits + code;

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

    // удаление аккаунта самим студентом убрано: удаляет только учебная часть
}
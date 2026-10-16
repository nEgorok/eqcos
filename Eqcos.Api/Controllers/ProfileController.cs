using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Eqcos.Api.Data;
using Eqcos.Api.Services;

namespace Eqcos.Api.Controllers;

// новые данные студента: ФИО тремя полями, группа двумя частями ("204" + "Лг"), корпус и причина изменения
public record ProfileUpdate(string? LastName, string? FirstName, string? MiddleName, string? GroupDigits, string? GroupCode,
                            int OfficeId, string? Reason);

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
        var pendingOffice = u.PendingOfficeId == null ? null : _db.Offices.Find(u.PendingOfficeId)?.Name;

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
            u.PendingGroup,
            u.PendingOfficeId,
            PendingOffice = pendingOffice,
            u.PendingReason
        });
    }

    // СТУДЕНТ: изменить ФИО, группу и корпус
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

        var office = _db.Offices.Find(r.OfficeId);
        if (office == null)
            return BadRequest("Выберите корпус");

        var fullName = StudentData.JoinName(last, first, middle);
        var group = digits + code;
        var reason = (r.Reason ?? "").Trim();

        var u = _db.Users.Find(CurrentUserId)!;

        if (u.Status == "Approved")
        {
            // вернул как было — заявку на изменение снимаем
            if (u.FullName == fullName && u.Group == group && u.OfficeId == r.OfficeId)
            {
                u.PendingFullName = null;
                u.PendingGroup = null;
                u.PendingOfficeId = null;
                u.PendingReason = null;
                _db.SaveChanges();
                return Ok("Данные не изменились");
            }

            if (reason == "")
                return BadRequest("Укажите причину изменения");
            if (reason.Length > 100)
                return BadRequest("Причина слишком длинная");

            // подтверждённый: старые данные продолжают действовать, новые — на проверку.
            // Если меняется корпус, заявку увидит админ НОВОГО корпуса.
            u.PendingFullName = fullName;
            u.PendingGroup = group;
            u.PendingOfficeId = r.OfficeId;
            u.PendingReason = reason;
            _db.SaveChanges();
            return Ok(u.OfficeId == r.OfficeId
                ? "Изменения отправлены на проверку в учебную часть. Пока действуют прежние данные."
                : $"Заявка на перевод отправлена в учебную часть ({office.Name}). Пока действуют прежние данные.");
        }

        // ещё не подтверждён: правим сразу (и корпус тоже — заявка уйдёт админу нового корпуса)
        u.FullName = fullName;
        u.Group = group;
        u.OfficeId = r.OfficeId;
        u.Status = "Pending";
        u.RejectReason = null;
        _db.SaveChanges();
        return Ok("Данные сохранены и отправлены на проверку в учебную часть");
    }

    // удаление аккаунта самим студентом убрано: удаляет только учебная часть
}
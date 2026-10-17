using System.Security.Claims;
using Eqcos.Api.Data;
using Eqcos.Api.Models;
using Eqcos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Eqcos.Api.Controllers;

public record PhoneSendRequest(string? Phone, string? Password);   // пароль нужен только при СМЕНЕ номера

// телефон: привязка с кодом по SMS (работает, только если SMS включены в настройках)
[ApiController]
[Route("api/phone")]
public class PhoneController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SmsSender _sms;
    private readonly PasswordHasher<User> _hasher = new();

    public PhoneController(AppDbContext db, SmsSender sms)
    {
        _db = db;
        _sms = sms;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // включены ли SMS: сайт по этому решает, показывать ли телефон (без входа тоже — для «Забыли пароль?»)
    [HttpGet("enabled")]
    public bool IsEnabled() => _sms.Enabled;

    // ПРИВЯЗАТЬ / СМЕНИТЬ: шаг 1 — код по SMS на новый номер
    [Authorize]
    [HttpPost("send")]
    public async Task<IActionResult> Send(PhoneSendRequest r)
    {
        if (!_sms.Enabled)
            return BadRequest("SMS отключены");

        var u = _db.Users.Find(CurrentUserId)!;
        var phone = Phones.Normalize(r.Phone);

        if (phone == null)
            return BadRequest("Введите номер телефона, например +7 999 123-45-67");
        if (phone == u.Phone)
            return BadRequest("Этот номер уже привязан");

        // смена номера — только с текущим паролем
        if (u.Phone != null &&
            _hasher.VerifyHashedPassword(u, u.PasswordHash, r.Password ?? "") == PasswordVerificationResult.Failed)
            return BadRequest("Неверный пароль");

        if (_db.Users.Any(x => x.Phone == phone && x.Id != u.Id))
            return BadRequest("Этот номер уже привязан к другой учётной записи");

        var limit = EmailCodes.Limit(_db, u.Id);
        if (limit != null)
            return BadRequest(limit.Replace("писем", "кодов"));

        var code = EmailCodes.New(_db, u.Id, EmailCodes.Phone, phone);
        if (!await _sms.Send(phone, $"Код: {code}. Электронная очередь КГАМТ"))
            return BadRequest("Не удалось отправить SMS. Проверьте номер или попробуйте позже");

        _db.SaveChanges();
        return Ok($"Код отправлен на {Phones.Mask(phone)}. Он действует {EmailCodes.Minutes} минут");
    }

    // ПРИВЯЗАТЬ / СМЕНИТЬ: шаг 2 — ввести код из SMS
    [Authorize]
    [HttpPost("confirm")]
    public IActionResult Confirm(EmailConfirmRequest r)
    {
        if (!_sms.Enabled)
            return BadRequest("SMS отключены");

        var u = _db.Users.Find(CurrentUserId)!;

        var error = EmailCodes.Check(_db, u.Id, EmailCodes.Phone, r.Code, out var row);
        if (error != null)
        {
            _db.SaveChanges();   // сохранить счётчик попыток
            return BadRequest(error);
        }

        // пока вводили код, номер мог занять кто-то другой
        if (_db.Users.Any(x => x.Phone == row!.Email && x.Id != u.Id))
        {
            _db.SaveChanges();
            return BadRequest("Этот номер уже привязан к другой учётной записи");
        }

        var old = u.Phone;
        u.Phone = row!.Email;   // в коде номер лежит в поле Email (поле общее: «куда отправили»)
        _db.SaveChanges();
        return Ok(old == null ? "Телефон привязан" : "Телефон изменён");
    }
}
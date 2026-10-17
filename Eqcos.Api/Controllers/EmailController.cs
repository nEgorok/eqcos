using System.Security.Claims;
using Eqcos.Api.Data;
using Eqcos.Api.Models;
using Eqcos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Eqcos.Api.Controllers;

public record EmailSendRequest(string? Email, string? Password);   // пароль нужен только при СМЕНЕ почты
public record EmailConfirmRequest(string? Code);
public record ResetSendRequest(string? Login, bool Staff = false);
public record ResetConfirmRequest(string? Login, string? Code, string? NewPassword, bool Staff = false);

// почта: привязка с кодом, смена, сброс пароля по коду
[ApiController]
[Route("api/email")]
public class EmailController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Mailer _mailer;
    private readonly PasswordHasher<User> _hasher = new();

    public EmailController(AppDbContext db, Mailer mailer)
    {
        _db = db;
        _mailer = mailer;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ПРИВЯЗАТЬ / СМЕНИТЬ: шаг 1 — отправить код на новый адрес
    [Authorize]
    [HttpPost("send")]
    public async Task<IActionResult> Send(EmailSendRequest r)
    {
        var u = _db.Users.Find(CurrentUserId)!;
        var email = (r.Email ?? "").Trim().ToLower();

        if (!EmailCodes.IsEmail(email))
            return BadRequest("Введите адрес почты, например ivanov@mail.ru");
        if (email == u.Email)
            return BadRequest("Эта почта уже привязана");

        // смена почты — только с текущим паролем (чтобы чужой за открытым компьютером не перехватил учётку)
        if (u.Email != null &&
            _hasher.VerifyHashedPassword(u, u.PasswordHash, r.Password ?? "") == PasswordVerificationResult.Failed)
            return BadRequest("Неверный пароль");

        if (_db.Users.Any(x => x.Email == email && x.Id != u.Id))
            return BadRequest("Эта почта уже привязана к другой учётной записи");

        var limit = EmailCodes.Limit(_db, u.Id);
        if (limit != null)
            return BadRequest(limit);

        var code = EmailCodes.New(_db, u.Id, EmailCodes.Bind, email);
        if (!await _mailer.Send(email, $"Код подтверждения: {code}",
                $"Ваш код для привязки почты к электронной очереди: {code}\nКод действует {EmailCodes.Minutes} минут."))
            return BadRequest("Не удалось отправить письмо. Проверьте адрес или попробуйте позже");

        _db.SaveChanges();
        return Ok($"Код отправлен на {EmailCodes.Mask(email)}. Он действует {EmailCodes.Minutes} минут");
    }

    // ПРИВЯЗАТЬ / СМЕНИТЬ: шаг 2 — ввести код из письма
    [Authorize]
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(EmailConfirmRequest r)
    {
        var u = _db.Users.Find(CurrentUserId)!;

        var error = EmailCodes.Check(_db, u.Id, EmailCodes.Bind, r.Code, out var row);
        if (error != null)
        {
            _db.SaveChanges();   // сохранить счётчик попыток
            return BadRequest(error);
        }

        // пока вводили код, адрес мог занять кто-то другой
        if (_db.Users.Any(x => x.Email == row!.Email && x.Id != u.Id))
        {
            _db.SaveChanges();
            return BadRequest("Эта почта уже привязана к другой учётной записи");
        }

        var old = u.Email;
        u.Email = row!.Email;
        _db.SaveChanges();

        // на старый адрес — предупреждение, что почту сменили
        if (old != null)
            await _mailer.Send(old, "Почта изменена",
                $"К вашей учётной записи в электронной очереди привязана другая почта: {EmailCodes.Mask(u.Email)}.");

        return Ok(old == null ? "Почта привязана" : "Почта изменена");
    }

    // СБРОС ПАРОЛЯ: шаг 1 — код на привязанную почту (без входа)
    [HttpPost("reset/send")]
    public async Task<IActionResult> ResetSend(ResetSendRequest r)
    {
        var u = FindForReset(r.Login, r.Staff);
        if (u == null)
            return BadRequest(r.Staff ? "Учётная запись не найдена" : "Такой номер студенческого не зарегистрирован");
        if (u.Email == null)
            return BadRequest("К этой учётной записи не привязана почта. Сбросить пароль может учебная часть");

        var limit = EmailCodes.Limit(_db, u.Id);
        if (limit != null)
            return BadRequest(limit);

        var code = EmailCodes.New(_db, u.Id, EmailCodes.Reset, u.Email);
        if (!await _mailer.Send(u.Email, $"Код для сброса пароля: {code}",
                $"Ваш код для сброса пароля в электронной очереди: {code}\nКод действует {EmailCodes.Minutes} минут."))
            return BadRequest("Не удалось отправить письмо. Попробуйте позже");

        _db.SaveChanges();
        return Ok($"Код отправлен на {EmailCodes.Mask(u.Email)}. Он действует {EmailCodes.Minutes} минут");
    }

    // СБРОС ПАРОЛЯ: шаг 2 — код + новый пароль
    [HttpPost("reset/confirm")]
    public async Task<IActionResult> ResetConfirm(ResetConfirmRequest r)
    {
        var u = FindForReset(r.Login, r.Staff);
        if (u == null)
            return BadRequest("Учётная запись не найдена");

        var password = r.NewPassword ?? "";
        if (password.Length < 6)
            return BadRequest("Новый пароль должен быть не короче 6 символов");

        var error = EmailCodes.Check(_db, u.Id, EmailCodes.Reset, r.Code, out _);
        if (error != null)
        {
            _db.SaveChanges();
            return BadRequest(error);
        }

        u.PasswordHash = _hasher.HashPassword(u, password);
        u.TokenVersion++;   // все старые входы (на других устройствах) закрываются
        _db.SaveChanges();

        await _mailer.Send(u.Email!, "Пароль изменён", "Пароль от вашей учётной записи в электронной очереди сброшен по коду из письма.");
        return Ok("Пароль изменён. Войдите с новым паролем");
    }

    // студент ищется по номеру студенческого в окне студентов, админ — по логину в окне сотрудников
    private User? FindForReset(string? login, bool staff)
    {
        var l = (login ?? "").Trim();
        var role = staff ? "Admin" : "Student";
        return _db.Users.FirstOrDefault(u => u.Login == l && u.Role == role);
    }
}
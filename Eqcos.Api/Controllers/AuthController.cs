using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Eqcos.Api.Data;
using Eqcos.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Eqcos.Api.Services;

namespace Eqcos.Api.Controllers;

// что присылает студент при регистрации и входе
// группа приходит двумя частями: GroupDigits = "204", GroupCode = "Лг".
// string? — поле может прийти пустым; тогда сервер ответит понятной фразой, а не своей стандартной ошибкой
public record RegisterRequest(string? Login, string? Password, string? LastName, string? FirstName, string? MiddleName,
                              string? GroupDigits, string? GroupCode, int OfficeId);
public record LoginRequest(string Login, string Password, bool Staff = false);   // Staff = вход через окно сотрудников
public record ChangePasswordRequest(string OldPassword, string NewPassword);
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly PasswordHasher<User> _hasher = new();

    private readonly IStudentVerifier _verifier;

    public AuthController(AppDbContext db, IConfiguration config, IStudentVerifier verifier)
    {
        _db = db;
        _config = config;
        _verifier = verifier;   // сервер сам подставит ManualVerifier (см. Program.cs)
    }

    // регистрация студента
    [HttpPost("register")]
    public IActionResult Register(RegisterRequest r)
    {
        // номер студенческого — только цифры (так студент не займёт логин вроде admin_kpk)
        var login = (r.Login ?? "").Trim();
        if (login == "" || !login.All(char.IsDigit))
            return BadRequest("Номер студенческого должен состоять только из цифр");
        if ((r.Password ?? "").Length < 6)
            return BadRequest("Пароль должен быть не короче 6 символов");

        // ФИО: исправляем регистр сами, потом проверяем
        var last = StudentData.Fix(r.LastName);
        var first = StudentData.Fix(r.FirstName);
        var middle = StudentData.Fix(r.MiddleName);
        var digits = (r.GroupDigits ?? "").Trim();
        var code = (r.GroupCode ?? "").Trim();

        // ?? — если первая проверка прошла (null), берём ошибку второй
        var error = StudentData.CheckName(last, first, middle) ?? StudentData.CheckGroup(_db, digits, code);
        if (error != null)
            return BadRequest(error);

        if (!_db.Offices.Any(o => o.Id == r.OfficeId))
            return BadRequest("Выберите корпус");

        if (_db.Users.Any(u => u.Login == login))
            return BadRequest("Такой номер студенческого уже зарегистрирован");

        var user = new User
        {
            Login = login,
            FullName = StudentData.JoinName(last, first, middle),
            Group = digits + code,     // "204" + "Лг" = "204Лг"
            OfficeId = r.OfficeId,     // свой корпус: записываться можно только сюда
            Role = "Student",
            CreatedAt = DateTime.UtcNow   // через 2 недели без проверки заявка удалится сама
        };
        user.PasswordHash = _hasher.HashPassword(user, r.Password!); // пароль -> отпечаток

        user.Status = _verifier.Check(user);   // сразу подтвердить или ждать админа

        _db.Users.Add(user);
        // зарегистрировался заново — старая причина отклонения больше не нужна
        _db.RejectedAccounts.RemoveRange(_db.RejectedAccounts.Where(x => x.Login == login));
        _db.SaveChanges();
        return Ok(user.Status == "Approved"
            ? "Регистрация прошла успешно"
            : "Регистрация прошла успешно. Учебная часть проверит ваши данные, после этого можно будет записаться.");
    }

    // вход: проверяем пароль и выдаём пропуск
    [HttpPost("login")]
    public IActionResult Login(LoginRequest r)
    {
        var user = _db.Users.FirstOrDefault(u => u.Login == r.Login);
        if (user == null)
            return Unauthorized(RejectedText(r) ?? "Неверный логин или пароль");

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, r.Password);
        if (result == PasswordVerificationResult.Failed)
            return Unauthorized("Неверный логин или пароль");
        // студент — только через студенческое окно, админ — только через окно сотрудников
        var isAdmin = user.Role == "Admin";
        if (isAdmin != r.Staff)
            return Unauthorized("Неверный логин или пароль");
        return Ok(new { token = CreateToken(user), role = user.Role, fullName = user.FullName });
    }
    // учётку отклонили и стёрли: при верном пароле объясняем почему (30 дней).
    // С неверным паролем — обычное «Неверный логин или пароль», чтобы чужой не узнал причину.
    private string? RejectedText(LoginRequest r)
    {
        if (r.Staff)
            return null;

        var since = DateTime.UtcNow.AddDays(-Cleanup.RejectedDays);
        var rej = _db.RejectedAccounts
            .Where(x => x.Login == r.Login && x.RejectedAt > since)
            .OrderByDescending(x => x.RejectedAt)
            .FirstOrDefault();
        if (rej == null)
            return null;

        // проверяем пароль по сохранённому отпечатку (сам User тут не нужен, подойдёт пустой)
        if (_hasher.VerifyHashedPassword(new User(), rej.PasswordHash, r.Password) == PasswordVerificationResult.Failed)
            return null;

        return $"Регистрация отклонена учебной частью: {rej.Reason}. " +
               "Номер студенческого свободен — зарегистрируйтесь заново с верными данными.";
    }

    // смена пароля: для любого вошедшего (студент или админ)
    [Authorize]
    [HttpPost("password")]
    public IActionResult ChangePassword(ChangePasswordRequest r)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = _db.Users.Find(id);
        if (user == null)
            return NotFound("Пользователь не найден");

        // старый пароль должен совпасть
        var check = _hasher.VerifyHashedPassword(user, user.PasswordHash, r.OldPassword);
        if (check == PasswordVerificationResult.Failed)
            return BadRequest("Старый пароль введён неверно");

        if (r.NewPassword.Length < 6)
            return BadRequest("Новый пароль должен быть не короче 6 символов");

        if (r.NewPassword == r.OldPassword)
            return BadRequest("Новый пароль совпадает со старым");

        user.PasswordHash = _hasher.HashPassword(user, r.NewPassword);   // новый отпечаток
        _db.SaveChanges();
        return Ok("Пароль изменён");
    }
    // собираем пропуск: кто это и какая роль
    private string CreateToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Login),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("office", user.OfficeId?.ToString() ?? "")   // корпус админа
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),   // пропуск живёт 8 часов
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
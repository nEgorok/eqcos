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
public record RegisterRequest(string Login, string Password, string FullName, string Group);
public record LoginRequest(string Login, string Password);
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
        if (_db.Users.Any(u => u.Login == r.Login))
            return BadRequest("Такой номер студенческого уже зарегистрирован");

        var user = new User
        {
            Login = r.Login,
            FullName = r.FullName,
            Group = r.Group,
            Role = "Student"
        };
        user.PasswordHash = _hasher.HashPassword(user, r.Password); // пароль -> отпечаток

        user.Status = _verifier.Check(user);   // сразу подтвердить или ждать админа

        _db.Users.Add(user);
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
            return Unauthorized("Неверный логин или пароль");

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, r.Password);
        if (result == PasswordVerificationResult.Failed)
            return Unauthorized("Неверный логин или пароль");

        return Ok(new { token = CreateToken(user), role = user.Role, fullName = user.FullName });
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
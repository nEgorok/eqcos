using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Eqcos.Api.Data;
using Eqcos.Api.Models;

namespace Eqcos.Api.Controllers;

// что присылает студент при регистрации и входе
public record RegisterRequest(string Login, string Password, string FullName, string Group);
public record LoginRequest(string Login, string Password);

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly PasswordHasher<User> _hasher = new();

    public AuthController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
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

        _db.Users.Add(user);
        _db.SaveChanges();
        return Ok("Регистрация прошла успешно");
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

    // собираем пропуск: кто это и какая роль
    private string CreateToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Login),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),   // пропуск живёт 8 часов
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Eqcos.Api.Data;
using Eqcos.Api.Models;

namespace Eqcos.Api.Services;

// правила кодов из письма: 6 цифр, 15 минут, 5 попыток, не больше 3 писем в час
public static class EmailCodes
{
    public const int Minutes = 15;     // сколько живёт код
    public const int MaxAttempts = 5;  // неверных попыток на один код
    public const int PerHour = 5;      // писем с кодом в час на одну учётку

    public const string Bind = "bind";    // привязать или сменить почту
    public const string Reset = "reset";  // сбросить пароль

    // "ivanov@mail.ru" → "i***@mail.ru" (чтобы чужой не узнал адрес целиком)
    public static string? Mask(string? email)
    {
        if (email == null)
            return null;
        var at = email.IndexOf('@');
        return at <= 0 ? "***" : email[0] + "***" + email[at..];
    }

    // похоже ли на адрес почты: есть @, после неё есть точка, без пробелов
    public static bool IsEmail(string s)
    {
        if (s.Length > 100 || s.Contains(' '))
            return false;
        var at = s.IndexOf('@');
        if (at <= 0 || !s[(at + 1)..].Contains('.') || s.EndsWith('.'))
            return false;
        return MailAddress.TryCreate(s, out _);
    }

    // можно ли отправить ещё одно письмо; null — можно, иначе текст «когда можно»
    public static string? Limit(AppDbContext db, int userId)
    {
        var hourAgo = DateTime.UtcNow.AddHours(-1);
        var recent = db.EmailCodes
            .Where(c => c.UserId == userId && c.CreatedAt > hourAgo)
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.CreatedAt)
            .ToList();
        if (recent.Count < PerHour)
            return null;
        // самое старое письмо в окне выпадет из часа — тогда можно снова
        return $"Слишком много писем. Отправить код снова можно в {Clock.ToMsk(recent[0].AddHours(1)):HH:mm}";
    }

    // новый код: прошлые коды той же цели больше не действуют. Вернёт сам код (его отправят в письме)
    public static string New(AppDbContext db, int userId, string purpose, string email)
    {
        // заодно убираем совсем старые коды (старше суток) всех пользователей
        var dayAgo = DateTime.UtcNow.AddDays(-1);
        db.EmailCodes.RemoveRange(db.EmailCodes.Where(c => c.CreatedAt < dayAgo));

        foreach (var old in db.EmailCodes.Where(c => c.UserId == userId && c.Purpose == purpose && !c.Used).ToList())
            old.Used = true;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");   // 000000–999999
        db.EmailCodes.Add(new EmailCode
        {
            UserId = userId,
            Purpose = purpose,
            Email = email,
            CodeHash = Hash(code),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(Minutes)
        });
        return code;
    }

    // проверить введённый код. null — верно (row = найденный код), иначе текст ошибки.
    // После вызова нужен SaveChanges: счётчик попыток и отметка Used должны сохраниться
    public static string? Check(AppDbContext db, int userId, string purpose, string? code, out EmailCode? row)
    {
        row = db.EmailCodes
            .Where(c => c.UserId == userId && c.Purpose == purpose && !c.Used)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault();

        if (row == null)
            return "Сначала запросите код";
        if (row.ExpiresAt < DateTime.UtcNow)
            return "Код устарел. Запросите новый";
        if (row.Attempts >= MaxAttempts)
            return "Слишком много неверных попыток. Запросите новый код";

        if (Hash((code ?? "").Trim()) != row.CodeHash)
        {
            row.Attempts++;
            var left = MaxAttempts - row.Attempts;
            return left > 0 ? $"Неверный код. Осталось попыток: {left}" : "Неверный код. Запросите новый";
        }

        row.Used = true;   // второй раз тот же код не сработает
        return null;
    }

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
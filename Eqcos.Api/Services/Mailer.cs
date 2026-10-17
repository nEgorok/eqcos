using System.Net;
using System.Net.Mail;

namespace Eqcos.Api.Services;

// отправка писем через SMTP Яндекса.
// Настройки — в разделе "Mail" (адрес и пароль приложения кладём в секреты, НЕ в репозиторий).
// Пока почта не настроена, письмо не уходит, а печатается в окно сервера: так можно проверять без ящика.
public class Mailer
{
    private readonly IConfiguration _config;
    private readonly ILogger<Mailer> _log;

    public Mailer(IConfiguration config, ILogger<Mailer> log)
    {
        _config = config;
        _log = log;
    }

    // true — письмо ушло (или напечатано), false — не получилось
    public async Task<bool> Send(string to, string subject, string body)
    {
        var user = _config["Mail:User"];
        if (string.IsNullOrEmpty(user))
        {
            _log.LogWarning("ПИСЬМО (почта не настроена) для {To}: {Subject}\n{Body}", to, subject, body);
            return true;
        }

        try
        {
            using var smtp = new SmtpClient(_config["Mail:Host"] ?? "smtp.yandex.ru", int.Parse(_config["Mail:Port"] ?? "587"))
            {
                EnableSsl = true,   // шифрованное соединение (STARTTLS)
                Credentials = new NetworkCredential(user, _config["Mail:Password"])
            };
            using var message = new MailMessage(new MailAddress(user, "Электронная очередь КГАМТ"), new MailAddress(to))
            {
                Subject = subject,
                Body = body + "\n\nЕсли это были не вы, обратитесь в учебную часть.\nЭто письмо отправлено автоматически, отвечать на него не нужно."
            };
            await smtp.SendMailAsync(message);
            return true;
        }
        catch (Exception e)
        {
            _log.LogError(e, "Не удалось отправить письмо на {To}", to);
            return false;
        }
    }
}
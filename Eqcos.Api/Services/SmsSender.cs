namespace Eqcos.Api.Services;

// отправка SMS через SMS.ru (платно, около 3–5 ₽ за SMS).
// Выключатель — "Sms:Enabled" в настройках. Пока он false, телефона на сайте нигде не видно.
// Ключ "Sms:ApiId" кладём в секреты сервера, НЕ в репозиторий.
// Если SMS включены, а ключа нет — SMS не уходит, а печатается в окно сервера (так можно проверять бесплатно).
public class SmsSender
{
    private static readonly HttpClient Http = new();
    private readonly IConfiguration _config;
    private readonly ILogger<SmsSender> _log;

    public SmsSender(IConfiguration config, ILogger<SmsSender> log)
    {
        _config = config;
        _log = log;
    }

    // включены ли SMS (выключатель в настройках)
    public bool Enabled => _config.GetValue<bool>("Sms:Enabled");

    // phone — 11 цифр, например 79991234567. true — ушло (или напечатано), false — не получилось
    public async Task<bool> Send(string phone, string text)
    {
        var key = _config["Sms:ApiId"];
        if (string.IsNullOrEmpty(key))
        {
            _log.LogWarning("SMS (сервис не подключён) для {Phone}: {Text}", phone, text);
            return true;
        }

        try
        {
            var url = $"https://sms.ru/sms/send?api_id={Uri.EscapeDataString(key)}&to={phone}" +
                      $"&msg={Uri.EscapeDataString(text)}&json=1";
            var answer = await Http.GetStringAsync(url);
            if (answer.Contains("\"status\": \"OK\"") || answer.Contains("\"status\":\"OK\""))
                return true;
            _log.LogError("SMS.ru не отправил SMS на {Phone}: {Answer}", phone, answer);
            return false;
        }
        catch (Exception e)
        {
            _log.LogError(e, "Не удалось отправить SMS на {Phone}", phone);
            return false;
        }
    }
}
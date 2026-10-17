namespace Eqcos.Api.Services;

// номер телефона: приводим к одному виду и показываем скрыто
public static class Phones
{
    // "8 (999) 123-45-67", "+7 999 1234567", "9991234567" → "79991234567"; не похоже на номер — null
    public static string? Normalize(string? s)
    {
        var digits = new string((s ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && (digits[0] == '8' || digits[0] == '7'))
            return "7" + digits[1..];
        if (digits.Length == 10 && digits[0] == '9')
            return "7" + digits;
        return null;
    }

    // "79991234567" → "+7 999 123-45-67"
    public static string? Format(string? phone) =>
        phone == null ? null : $"+7 {phone[1..4]} {phone[4..7]}-{phone[7..9]}-{phone[9..]}";

    // "79991234567" → "+7 *** ***-45-67"
    public static string? Mask(string? phone) =>
        phone == null ? null : $"+7 *** ***-{phone[7..9]}-{phone[9..]}";
}
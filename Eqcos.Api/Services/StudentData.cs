using System.Text.RegularExpressions;
using Eqcos.Api.Data;

namespace Eqcos.Api.Services;

// проверка ФИО и группы — общая для регистрации и для изменения данных в профиле
public static class StudentData
{
    // одна часть ФИО: русские буквы, первая заглавная, дефис только между частями ("Петров-Водкин")
    private static readonly Regex NamePart = new(@"^[А-ЯЁ][а-яё]*(-[А-ЯЁ][а-яё]*)*$");

    // группа: 3 цифры, первая — курс от 1 до 4
    private static readonly Regex GroupDigits = new(@"^[1-4][0-9]{2}$");

    // " иВАНОВ-петров " -> "Иванов-Петров": первая буква каждой части заглавная, остальные строчные
    public static string Fix(string? s)
    {
        var parts = (s ?? "").Trim().Split('-');
        return string.Join("-", parts.Select(p =>
            p.Length == 0 ? "" : char.ToUpper(p[0]) + p.Substring(1).ToLower()));
    }

    // проверить ФИО (уже после Fix); null — всё в порядке, иначе текст ошибки
    public static string? CheckName(string last, string first, string middle)
    {
        if (last == "" || first == "")
            return "Укажите фамилию и имя";
        if (!NamePart.IsMatch(last))
            return "Фамилия: только русские буквы и дефис";
        if (!NamePart.IsMatch(first))
            return "Имя: только русские буквы и дефис";
        if (middle != "" && !NamePart.IsMatch(middle))
            return "Отчество: только русские буквы и дефис";
        if (last.Length > 40 || first.Length > 40 || middle.Length > 40)
            return "Слишком длинное ФИО";
        return null;
    }

    // "Иванов", "Иван", "" -> "Иванов Иван"   (отчество необязательное)
    public static string JoinName(string last, string first, string middle) =>
        middle == "" ? $"{last} {first}" : $"{last} {first} {middle}";

    // проверить группу; null — всё в порядке
    public static string? CheckGroup(AppDbContext db, string digits, string code)
    {
        if (!GroupDigits.IsMatch(digits))
            return "Номер группы начинается с курса: 1, 2, 3 или 4. Например, 204";
        if (!db.GroupCodes.Any(c => c.Code == code))
            return "Выберите буквы группы из списка";
        return null;
    }
}
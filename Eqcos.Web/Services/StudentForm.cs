namespace Eqcos.Web.Services;

// поля студента: регистрация и «Изменить» в профиле.
// Это класс (а не record), поэтому компонент StudentFields меняет поля прямо в нём,
// и страница сразу видит, что ввели.
public class StudentForm
{
    public string LastName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string MiddleName { get; set; } = "";   // отчество, необязательно
    public string GroupDigits { get; set; } = "";  // "204"
    public string GroupCode { get; set; } = "";    // "Лг"
    public int OfficeId { get; set; }              // корпус (выбирается только при регистрации)

    // " иВАНОВ-петров " -> "Иванов-Петров" (то же самое делает сервер)
    public static string Fix(string s)
    {
        var parts = s.Trim().Split('-');
        return string.Join("-", parts.Select(p =>
            p.Length == 0 ? "" : char.ToUpper(p[0]) + p.Substring(1).ToLower()));
    }

    // заполнить из того, что хранится на сервере: "Иванов Иван Иванович" и "204Лг"
    public void Fill(string fullName, string group)
    {
        var words = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        LastName = words.Length > 0 ? words[0] : "";
        FirstName = words.Length > 1 ? words[1] : "";
        MiddleName = words.Length > 2 ? string.Join(" ", words.Skip(2)) : "";

        // первые 3 символа — цифры, остальное — буквы. Старая группа "как попало" — поля пустые
        if (group.Length > 3 && group.Take(3).All(char.IsDigit))
        {
            GroupDigits = group.Substring(0, 3);
            GroupCode = group.Substring(3);
        }
        else
        {
            GroupDigits = "";
            GroupCode = "";
        }
    }

    // что отправить на сервер (имена полей совпадают с ProfileUpdate / RegisterRequest)
    public object ToJson(string? login = null, string? password = null) => new
    {
        login,
        password,
        lastName = LastName,
        firstName = FirstName,
        middleName = MiddleName,
        groupDigits = GroupDigits,
        groupCode = GroupCode,
        officeId = OfficeId
    };

    // быстрая проверка до отправки; null — можно отправлять
    public string? Check(bool needOffice)
    {
        if (LastName == "" || FirstName == "")
            return "Укажите фамилию и имя";
        if (GroupDigits.Length != 3 || !GroupDigits.All(char.IsDigit))
            return "Номер группы — 3 цифры, например 204";
        if (GroupDigits[0] < '1' || GroupDigits[0] > '4')
            return "Номер группы начинается с курса: 1, 2, 3 или 4. Например, 204";
        if (GroupCode == "")
            return "Выберите буквы группы";
        if (needOffice && OfficeId == 0)
            return "Выберите корпус";
        return null;
    }
}
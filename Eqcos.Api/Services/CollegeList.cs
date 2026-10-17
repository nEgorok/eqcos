using System.Text.Json;
using Eqcos.Api.Data;
using Eqcos.Api.Models;

namespace Eqcos.Api.Services;

// студент из списка техникума
public record CollegeStudent(string Login, string LastName, string FirstName, string MiddleName, string Group, int OfficeId)
{
    public string FullName => MiddleName == "" ? $"{LastName} {FirstName}" : $"{LastName} {FirstName} {MiddleName}";
}

// откуда берём список студентов техникума
public interface ICollegeList
{
    CollegeStudent? Find(string login);       // по номеру студенческого
    List<CollegeStudent> Search(string q);    // номер или фамилия, для поиска у админа
}

// ПОКА: тестовый список из файла Data/college-fake.json (читается один раз при запуске сервера).
// Когда техникум даст доступ к своей базе: пишем класс CollegeDbList : ICollegeList
// и меняем ОДНУ строку в Program.cs. Остальной код не трогаем.
public class FakeCollegeList : ICollegeList
{
    private readonly List<CollegeStudent> _all;

    public FakeCollegeList(IWebHostEnvironment env)
    {
        var path = Path.Combine(env.ContentRootPath, "Data", "college-fake.json");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _all = JsonSerializer.Deserialize<List<CollegeStudent>>(File.ReadAllText(path), options) ?? new();
    }

    public CollegeStudent? Find(string login) => _all.FirstOrDefault(s => s.Login == login);

    public List<CollegeStudent> Search(string q)
    {
        q = College.Norm(q);
        return _all
            .Where(s => s.Login.StartsWith(q) || College.Norm(s.FullName).Contains(q))
            .OrderBy(s => s.LastName)
            .Take(10)
            .ToList();
    }
}

// итог сверки. Short — студенту (без чужих данных), Details — админу (что именно в списке)
public record CollegeCheck(bool Ok, string Short, string Details);

// сверка со списком техникума (выключатель "College" в appsettings.json)
public class College
{
    private readonly ICollegeList _list;
    private readonly AppDbContext _db;

    public bool Enabled { get; }   // список подключён: админ видит сверку и поиск
    public bool Auto { get; }      // новые регистрации подтверждаются и отклоняются сами
    public bool Sync { get; }      // ФИО, группа и корпус берутся из списка: студент и админ сами их не меняют

    public College(ICollegeList list, AppDbContext db, IConfiguration config)
    {
        _list = list;
        _db = db;
        Enabled = config.GetValue<bool>("College:Enabled");
        Auto = Enabled && config.GetValue<bool>("College:Auto");
        Sync = Enabled && config.GetValue<bool>("College:Sync");
    }

    public List<CollegeStudent> Search(string q) => _list.Search(q);

    // корпус студента по списку (null — номера в списке нет)
    public int? OfficeOf(string login) => _list.Find(login)?.OfficeId;

    // сначала номер студенческого; нашли — сравниваем ФИО, группу, корпус
    public CollegeCheck Check(string login, string fullName, string group, int? officeId)
    {
        var s = _list.Find(login);
        if (s == null)
            return new(false, "номер студенческого не найден в списке техникума", "Номера нет в списке техникума");
        if (Norm(s.FullName) != Norm(fullName))
            return new(false, "ФИО не совпадает со списком техникума", $"ФИО не совпадает. В списке: {s.FullName}");
        if (Norm(s.Group) != Norm(group))
            return new(false, "группа не совпадает со списком техникума", $"Группа не совпадает. В списке: {s.Group}");
        if (s.OfficeId != officeId)
            return new(false, "корпус не совпадает со списком техникума",
                       $"Корпус не совпадает. В списке: {_db.Offices.Find(s.OfficeId)?.Name}");
        return new(true, "совпадает", "Совпадает со списком техникума");
    }

    // СИНХРОНИЗАЦИЯ: подтянуть ФИО, группу и корпус из списка. true — что-то поменялось (тогда нужен SaveChanges).
    // Вызывается при входе, при открытии профиля и раз в час в уборке.
    public bool Pull(User u)
    {
        if (!Sync || u.Role != "Student")
            return false;
        var s = _list.Find(u.Login);
        if (s == null)
            return false;   // студента нет в списке — не трогаем (отчисленных решим, когда будет настоящая база)
        if (u.FullName == s.FullName && u.Group == s.Group && u.OfficeId == s.OfficeId)
            return false;   // всё уже совпадает

        u.FullName = s.FullName;
        u.Group = s.Group;
        u.OfficeId = s.OfficeId;
        // заявка на изменение больше не нужна: данные теперь из списка
        u.PendingFullName = null;
        u.PendingGroup = null;
        u.PendingOfficeId = null;
        u.PendingReason = null;

        var future = _db.Bookings
            .Where(b => b.StudentId == u.Id && b.Date >= Clock.Today && b.Status == BookingStatus.Active)
            .ToList();
        foreach (var b in future)
        {
            // корпус сменился — своя запись в старый корпус отменяется (вызов остаётся), как при переводе
            if (b.OfficeId != u.OfficeId && _db.Purposes.Find(b.PurposeId)?.AdminOnly != true)
            {
                b.Status = BookingStatus.SystemCancelled;
                b.Note = "Перевод в другой корпус";
                Mover.Notify(_db, u.Id, null,
                    $"Ваша запись на {b.Date:dd.MM} {b.Time:HH:mm} отменена: вы переведены в другой корпус.");
                continue;
            }
            b.StudentName = u.FullName;
            b.Group = u.Group;
        }

        Mover.Notify(_db, u.Id, null,
            $"Данные обновлены по списку техникума: {u.FullName}, группа {u.Group}, {_db.Offices.Find(u.OfficeId)?.Name}.");
        return true;
    }

    // " Фёдорова  ОЛЬГА " -> "федорова ольга": без лишних пробелов, регистра и разницы е/ё
    public static string Norm(string s) =>
        string.Join(" ", s.ToLower().Replace('ё', 'е').Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
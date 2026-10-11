using System.Text.Json;
// ===== Б1. ДАННЫЕ ===== 
var bookings = LoadBookings();
// ===== Б2. МЕНЮ =====
while (true)
{
    Console.WriteLine("1. Показать свободные слоты");
    Console.WriteLine("2. Записаться на слот");
    Console.WriteLine("3. Показать все записи");
    Console.WriteLine("4. Удалить запись");
    Console.WriteLine("5. Выход");
    Console.Write("Выберите действие: ");
    string choice = Console.ReadLine();
    switch (choice)
    {
        case "1":
            ShowFreeSlots();
            break;
        case "2":
            BookSlot();
            break;
        case "3":
            ShowAllBookings();
            break;
        case "4":
            RemoveBooking();
            break;
        case "5":
            return;
        default:
            Console.WriteLine("Ошибка: неверный выбор");
            break;
    }
    SaveBookings();
}
// ===== Б3. ПОКАЗ СЛОТОВ =====
void ShowFreeSlots()
{
    var free = GetFreeSlots();

    Console.WriteLine("Свободное время:");
    for (int i = 0; i < free.Count; i++)
        Console.WriteLine($"{i + 1}. {free[i]:HH:mm}");
}
// ===== Б4. ЗАПИСЬ =====
void BookSlot()
{
    // 1. Сначала ID
    Console.Write("Введите ваш ID: ");
    if (!int.TryParse(Console.ReadLine(), out int studentId))
    {
        Console.WriteLine("Ошибка: неверный ID");
        return;
    }

    // 2. Проверка: нет ли уже записи с таким ID
    var existing = bookings.FirstOrDefault(x => x.StudentId == studentId);
    if (existing != null)
    {
        Console.WriteLine($"Ошибка: вы уже записаны на {existing.Time:HH:mm}");
        return;
    }

    // 3. Показ свободных слотов
    var free = GetFreeSlots();
    Console.WriteLine("Свободное время:");
    for (int i = 0; i < free.Count; i++)
        Console.WriteLine($"{i + 1}. {free[i]:HH:mm}");

    // 4. Выбор слота
    Console.Write("Выберите номер слота: ");
    if (!int.TryParse(Console.ReadLine(), out int n) || n < 1 || n > free.Count)
    {
        Console.WriteLine("Ошибка: нет такого номера");
        return;
    }
    TimeOnly inputTime = free[n - 1];

    // 5. Имя и группа
    Console.Write("Введите ваше имя: ");
    string studentName = Console.ReadLine() ?? "";
    Console.Write("Введите вашу группу: ");
    string group = Console.ReadLine() ?? "";

    // 6. Запись
    bookings.Add(new Booking { Time = inputTime, StudentName = studentName, Group = group, StudentId = studentId });
    Console.WriteLine($"Вы записаны на {inputTime:HH:mm}!");
}
// ===== Б5. ВСЕ ЗАПИСИ =====
void ShowAllBookings()
{
    Console.WriteLine("Все записи:");
    foreach (var b in bookings.OrderBy(x => x.Time))
        Console.WriteLine($"{b.Time:HH:mm} — {b.StudentName} ({b.Group})");
}

// ===== Б6. ОТМЕНА =====
void RemoveBooking()

{
    if (!bookings.Any())
    {
        Console.WriteLine("Нет записей для удаления.");
        return;
    }
    Console.Write("Введите ваш ID: ");
    if (!int.TryParse(Console.ReadLine(), out int id))
    {
        Console.WriteLine("Ошибка: неверный ID");
        return;
    }

    var my = bookings.Where(x => x.StudentId == id).OrderBy(x => x.Time).ToList();
    if (my.Count == 0)
    {
        Console.WriteLine("У вас нет записей.");
        return;
    }
    
    Console.WriteLine("Выберите запись для удаления:");
    for (int i = 0; i < my.Count; i++)
        Console.WriteLine($"{i + 1}. {my[i].Time:HH:mm} — {my[i].StudentName} ({my[i].Group})");

    Console.Write("Номер записи: ");
    if (!int.TryParse(Console.ReadLine(), out int n) || n < 1 || n > my.Count)
    {
        Console.WriteLine("Ошибка: неверный номер");
        return;
    }

    var toRemove = my[n - 1];
    bookings.Remove(toRemove);
    Console.WriteLine($"Запись на {toRemove.Time:HH:mm} удалена.");
}
// ===== Б7. СВОБОДНЫЕ СЛОТЫ =====
List<TimeOnly> GetFreeSlots()
{
    var free = new List<TimeOnly>();
    foreach (var s in GetSlots(15))
    {
        if (bookings.FirstOrDefault(x => x.Time == s) == null)
            free.Add(s);
    }
    return free;
}
// ===== Б8. ВСЕ СЛОТЫ ДНЯ =====
List<TimeOnly> GetSlots(int slotMinutes)
{
    var slots = new List<TimeOnly>();
    var t = new TimeOnly(10,00);
    while (t < new TimeOnly(16,00))
    {
        if (!(t >= new TimeOnly(13,00) && t < new TimeOnly(14,00)))
            slots.Add(t);
        t = t.AddMinutes(slotMinutes);
    }
    return slots;
}
// ===== Б9. ФАЙЛ =====
void SaveBookings()
{
    string json = JsonSerializer.Serialize(bookings);
    File.WriteAllText("bookings.json", json);
}

List<Booking> LoadBookings()
{
    if (!File.Exists("bookings.json"))
        return new List<Booking>();

    string json = File.ReadAllText("bookings.json");
    return JsonSerializer.Deserialize<List<Booking>>(json) ?? new List<Booking>();
}
// ===== КЛАСС ЗАПИСИ =====
class Booking
{
    public TimeOnly Time { get; set; }
    public string StudentName { get; set; } = "";
    public string Group { get; set; } = "";
    public int StudentId { get; set; }
}
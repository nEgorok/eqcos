using Eqcos.Api.Data;
using Eqcos.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Eqcos.Api.Services;

// общие правила расписания: какие клетки есть, что занято, помещается ли приём
public static class Schedule
{
    public const int Step = 5;   // шаг сетки в минутах

    // все клетки корпуса с шагом Step, без перерыва
    public static List<TimeOnly> GetSlots(Office office)
    {
        var slots = new List<TimeOnly>();
        var t = office.Open;
        while (t < office.Close)
        {
            if (t < office.BreakStart || t >= office.BreakEnd)
                slots.Add(t);
            t = t.AddMinutes(Step);
        }
        return slots;
    }

    // помещается ли приём длиной minutes целиком в рабочее время (без перерыва)
    public static bool Fits(Office office, TimeOnly start, int minutes)
    {
        var slots = GetSlots(office);
        for (var t = start; t < start.AddMinutes(minutes); t = t.AddMinutes(Step))
        {
            if (!slots.Contains(t))
                return false;
        }
        return true;
    }

    // пересекается ли start..end хоть с одним занятым отрезком
    public static bool Overlaps(List<(TimeOnly Start, TimeOnly End)> busy, TimeOnly start, TimeOnly end)
    {
        return busy.Any(x => start < x.End && x.Start < end);
    }

    // занятые отрезки в корпусе на день: ДЕЙСТВУЮЩИЕ записи + закрытое время.
    // except — записи, которые не считать (те, что сейчас переносим)
    public static List<(TimeOnly Start, TimeOnly End)> Busy(AppDbContext db, int officeId, DateOnly date,
        ICollection<int>? except = null)
    {
        var bookings = db.Bookings
            .Where(b => b.OfficeId == officeId && b.Date == date && b.Status == BookingStatus.Active)
            .ToList()
            .Where(b => except == null || !except.Contains(b.Id))
            .Select(b => (b.Time, b.Time.AddMinutes(b.Minutes)));

        var closures = db.Closures
            .Where(c => c.OfficeId == officeId && c.Date == date)
            .ToList()
            .Select(c => (c.Start, c.End));

        return bookings.Concat(closures).ToList();
    }

    // ---------- ОЧЕРЕДЬ ----------
    // Блокировка PostgreSQL до конца транзакции. Второй запрос с теми же числами
    // ждёт, пока первый не закончит. Так два человека не займут одно время одновременно.
    // Работает только внутри BeginTransaction().

    // запись в корпус на этот день — строго по одному
    public static void LockDay(AppDbContext db, int officeId, DateOnly date) =>
        db.Database.ExecuteSqlRaw("SELECT pg_advisory_xact_lock({0}, {1})", officeId, date.DayNumber);

    // действия одного студента — строго по одному (двойной клик не даст двух записей)
    public static void LockStudent(AppDbContext db, int userId) =>
        db.Database.ExecuteSqlRaw("SELECT pg_advisory_xact_lock({0}, {1})", 0, userId);
}
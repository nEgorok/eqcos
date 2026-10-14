using Eqcos.Api.Data;
using Eqcos.Api.Models;

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

    // занятые отрезки в корпусе на день: записи + закрытое время
    public static List<(TimeOnly Start, TimeOnly End)> Busy(AppDbContext db, int officeId, DateOnly date)
    {
        var minutes = db.Purposes.ToDictionary(p => p.Id, p => p.Minutes);

        var bookings = db.Bookings
            .Where(b => b.OfficeId == officeId && b.Date == date)
            .ToList()
            .Select(b => (b.Time, b.Time.AddMinutes(minutes.GetValueOrDefault(b.PurposeId, Step))));

        var closures = db.Closures
            .Where(c => c.OfficeId == officeId && c.Date == date)
            .ToList()
            .Select(c => (c.Start, c.End));

        return bookings.Concat(closures).ToList();
    }
}
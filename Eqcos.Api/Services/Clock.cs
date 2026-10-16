namespace Eqcos.Api.Services;

// единые часы и календарь учебной части.
// Время всегда московское, даже если сервер стоит в другом поясе (на VPS часы идут по UTC).
public static class Clock
{
    public const int DaysAhead = 14;   // на сколько дней вперёд можно записаться

    private static readonly TimeZoneInfo Msk = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

    // сейчас по Москве
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Msk);
    // перевести время из UTC (так оно лежит в базе) в московское
    public static DateTime ToMsk(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Msk);

    public static DateOnly Today => DateOnly.FromDateTime(Now);
    public static TimeOnly Time => TimeOnly.FromDateTime(Now);

    // последний день, на который можно записаться
    public static DateOnly MaxDate => Today.AddDays(DaysAhead);

    // рабочий ли день: понедельник–пятница
    // (каникулы и закрытые дни добавим в шаге 27)
    public static bool IsWorkday(DateOnly d) =>
        d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday;

    // приём ещё не закончился: день впереди, или сегодня и конец приёма позже, чем сейчас
    public static bool NotEnded(DateOnly date, TimeOnly start, int minutes) =>
        date > Today || (date == Today && start.AddMinutes(minutes) > Time);
    // сколько рабочих дней прошло ПОСЛЕ дня from до дня to включительно.
    // Запись в пятницу, сегодня среда: пн, вт, ср = 3
    public static int WorkdaysBetween(DateOnly from, DateOnly to)
    {
        var count = 0;
        for (var d = from.AddDays(1); d <= to; d = d.AddDays(1))
        {
            if (IsWorkday(d))
                count++;
        }
        return count;
    }

}
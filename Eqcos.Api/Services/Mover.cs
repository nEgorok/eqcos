using Eqcos.Api.Data;
using Eqcos.Api.Models;
using static Eqcos.Api.Services.Schedule;

namespace Eqcos.Api.Services;

// перенос записей студентов, когда их время занимают: закрыли время или поставили вызов.
// Общий для ClosuresController и SlotsController, чтобы правила переноса были одни.
// Вызывать только внутри транзакции, после LockDay на этот день.
public static class Mover
{
    // why — что написать студенту в скобках, например "учебная часть в это время не принимает"
    // noteIfCancelled — что записать в Note, если переносить некуда
    // Возвращает: сколько перенесли и сколько отменили.
    public static (int Moved, int Cancelled) MoveStudents(AppDbContext db, Office office, DateOnly date,
        TimeOnly after, List<Booking> students, string? why, string noteIfCancelled)
    {
        // по порядку очереди: кто раньше записан на более раннее время, тот раньше и получает новое место
        students = students.OrderBy(b => b.Time).ThenBy(b => b.Id).ToList();
        var waiting = students.Select(b => b.Id).ToHashSet();   // ещё не перенесённые: при поиске их не считаем
        var reason = why == null ? "" : $" ({why})";
        int moved = 0, cancelled = 0;
        foreach (var b in students)
        {
            var oldDate = b.Date;
            var oldTime = b.Time;

            var place = FindPlace(db, office, date, after, b.Minutes, waiting);
            waiting.Remove(b.Id);

            if (place != null)
            {
                b.Date = place.Value.Date;
                b.Time = place.Value.Time;
                b.Moved = true;   // перенесла система
                moved++;
                Notify(db, b.StudentId, b.Id,
                    $"Ваша запись перенесена с {oldDate:dd.MM} {oldTime:HH:mm} на {b.Date:dd.MM} {b.Time:HH:mm}{reason}.");
            }
            else
            {
                b.Status = BookingStatus.SystemCancelled;
                b.Note = noteIfCancelled;
                cancelled++;
                Notify(db, b.StudentId, null,
                    $"Ваша запись на {oldDate:dd.MM} {oldTime:HH:mm} отменена{reason}, " +
                    "свободного времени для переноса нет. Запишитесь заново.");
            }
            db.SaveChanges();   // внутри транзакции: следующий поиск уже видит это место занятым
        }

        return (moved, cancelled);
    }

    // ближайшее свободное место длиной len: сначала в этот день после "after", потом в следующие рабочие дни
    public static (DateOnly Date, TimeOnly Time)? FindPlace(AppDbContext db, Office office, DateOnly date,
        TimeOnly after, int len, ICollection<int> except)
    {
        for (var d = date; d <= date.AddDays(Clock.DaysAhead); d = d.AddDays(1))
        {
            if (!Clock.IsWorkday(d))
                continue;   // суббота и воскресенье

            if (d != date)
                LockDay(db, office.Id, d);   // пока ищем место в этот день, туда никто не запишется

            var busy = Busy(db, office.Id, d, except);

            foreach (var t in GetSlots(office))
            {
                if (d == date && t < after)
                    continue;   // в тот же день — только после занятого отрезка

                if (d == Clock.Today && t <= Clock.Time)
                    continue;   // прошедшее время сегодня не предлагаем

                if (Fits(office, t, len) && !Overlaps(busy, t, t.AddMinutes(len)))
                    return (d, t);
            }
        }
        return null;   // за 2 недели места нет
    }

    // создать уведомление студенту
    public static void Notify(AppDbContext db, int userId, int? bookingId, string text)
    {
        db.Notifications.Add(new Notification
        {
            UserId = userId,
            BookingId = bookingId,   // есть Id — у студента будет кнопка «Выбрать другое время»
            Text = text,
            CreatedAt = DateTime.UtcNow   // PostgreSQL хранит время с поясом только в UTC
        });
    }

    // время на сетке: минуты кратны 5, без секунд (закрытие и вызов «не по сетке» ломают расписание)
    public static bool OnGrid(TimeOnly t) => t.Minute % Step == 0 && t.Second == 0;
}
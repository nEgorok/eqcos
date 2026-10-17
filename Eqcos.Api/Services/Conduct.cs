using Eqcos.Api.Data;
using Eqcos.Api.Models;

namespace Eqcos.Api.Services;

// ПОРЯДОЧНОСТЬ студента: уровень 0–6 и три шкалы (неявки, спам записью/отменой, спам сменой данных).
// Полные правила — в bany.md. Шкалы и слово «порядочность» видит только учебная часть.
// Студент видит то же самое как «Доступ к записи»: шкалу из 7 ступеней с названиями из AccessNames
// (никаких «уровней» и «рейтингов»), предупреждения и «запись ограничена до ...».
// Ничего не запускается по таймеру: уровень пересчитывается, когда студент или админ что-то делает
// (и раз в час в Cleanup), поэтому выключенный сервер ничего не пропустит.
// Методы НЕ вызывают SaveChanges в конце — сохраняет тот, кто их вызвал (кроме мест, где сказано).
public static class Conduct
{
    public const int SpamLimit = 10;      // записей и отмен за SpamMinutes; дальше запись закрыта на SpamMinutes
    public const int SpamMinutes = 60;    // окно и пауза для спама: 1 час
    public const int SpamWarnings = 3;    // предупреждений за спам за 4 недели → уровень +1
    public const int DataLimit = 3;       // заявок на изменение данных за час; 4-я — предупреждение
    public const int DataDayAt = 3;       // 3-е предупреждение за смену данных — пауза на сутки
    public const int DataBlockAt = 6;     // 6-е — учётка заблокирована (снимает только админ)
    public const int LateMinutes = 30;    // отмена меньше чем за 30 минут до записи = неявка
    public const int GraceMinutes = 5;    // ...но если записался не больше 5 минут назад — отмена без наказания

    // по номеру уровня: сколько недель наказание и сколько недель чистого срока, чтобы упасть на ступень
    private static readonly int[] PunishWeeks = { 0, 1, 1, 4, 12, 52, 0 };   // 6 — пожизненно
    private static readonly int[] CleanWeeks = { 0, 1, 2, 4, 8, 12, 16 };    // 6 — после снятия админом
    // названия ступеней для студента: чем хуже ведёт себя, тем строже доступ
    public static readonly string[] AccessNames =
        { "без ограничений", "под наблюдением", "ограничен", "ограничен строго", "ограничен очень строго",
          "закрыт надолго", "закрыт" };

    // названия цветов для учебной части
    public static readonly string[] Names =
        { "зелёный", "салатовый", "жёлтый", "оранжевый", "красный", "тёмно-красный", "красно-бурый" };

    // ---------- ПЕРЕСЧЁТ ----------

    // закончилось наказание? прошёл чистый срок? — применить. Вызывать перед любой проверкой.
    public static void Refresh(AppDbContext db, User u)
    {
        var now = DateTime.UtcNow;

        if (u.PunishUntil != null && now >= u.PunishUntil)
        {
            Mover.Notify(db, u.Id, null, "Ограничение записи закончилось: снова можно записываться.");
            u.PunishFrom = null;
            u.PunishUntil = null;
        }

        if (u.DataUntil != null && now >= u.DataUntil)
            u.DataUntil = null;   // пауза на смену данных прошла

        // чистый срок прошёл — уровень на ступень ниже. Цикл: если студент долго не заходил,
        // за раз может упасть несколько ступеней (каждая следующая — от конца предыдущей)
        while (u.Level > 0 && u.PunishUntil == null && !IsLifetime(u) && u.CleanFrom != null)
        {
            var next = u.CleanFrom.Value.AddDays(7 * CleanWeeks[u.Level]);
            if (now < next)
                break;

            u.Level--;
            u.Lifted = false;
            u.CleanFrom = next;
            Event(db, u, ConductKind.Level, $"Уровень понизился до {u.Level} (чистый срок прошёл)");
            Mover.Notify(db, u.Id, null, $"Доступ к записи улучшился: {AccessNames[u.Level]}.");
        }
    }

    // ---------- НЕЯВКИ ----------

    // неявки, которые сейчас считаются: не сгорели (4 недели, после снятия пожизненного — 8)
    // и ещё не «потрачены» на подъём уровня
    public static List<Booking> Strikes(AppDbContext db, User u)
    {
        var since = DateTime.UtcNow.AddDays(-7 * StrikeWeeks(u));
        if (u.StrikesFrom != null && u.StrikesFrom > since)
            since = u.StrikesFrom.Value;

        return db.Bookings
            .Where(b => b.StudentId == u.Id && b.StrikeAt != null && b.StrikeAt > since)
            .OrderBy(b => b.StrikeAt)
            .ToList();
    }

    public static int StrikeWeeks(User u) => u.Lifted ? 8 : 4;

    // на уровне 0 уровень поднимает 3-я неявка, дальше — 2-я
    public static int StrikeLimit(User u) => u.Level == 0 ? 3 : 2;

    // засчитать неявку по записи b (админ отметил «Не пришёл» или студент отменил поздно).
    // what — что написать студенту, например «Неявка 16.10 в 10:15». Сохраняет сам.
    public static void AddStrike(AppDbContext db, User u, Booking b, string what)
    {
        Refresh(db, u);
        b.StrikeAt = DateTime.UtcNow;
        db.SaveChanges();   // чтобы Strikes() уже видела эту неявку

        var count = Strikes(db, u).Count;
        var limit = StrikeLimit(u);
        if (count >= limit)
        {
            u.StrikesFrom = DateTime.UtcNow;   // счётчик неявок с нуля
            LevelUp(db, u, $"неявок подряд: {count}");
        }
        else
        {
            Dirty(u);
            Event(db, u, ConductKind.Strike, $"{what} (предупреждение {count} из {limit - 1})");
            // студенту — постоянная жёлтая плашка из ActiveWarnings, без уведомления
        }
        db.SaveChanges();
    }

    // отметку «Не пришёл» исправили на «Пришёл» — неявку убрать (уровень, если уже поднялся, админ понижает сам)
    public static void RemoveStrike(AppDbContext db, Booking b)
    {
        if (b.StrikeAt == null)
            return;
        b.StrikeAt = null;
        Mover.Notify(db, b.StudentId, null, $"Отметка исправлена: неявка {b.Date:dd.MM} в {b.Time:HH:mm} снята.");
    }

    // отмена поздняя (меньше 30 минут до начала)? Свежая запись (до 5 минут) и перенесённая системой — не в счёт
    public static bool IsLateCancel(Booking b)
    {
        if (b.Moved)
            return false;
        if (b.CreatedAt != null && DateTime.UtcNow - b.CreatedAt.Value < TimeSpan.FromMinutes(GraceMinutes))
            return false;
        return b.Date.ToDateTime(b.Time) - Clock.Now < TimeSpan.FromMinutes(LateMinutes);
    }

    // ---------- ЗАПИСЬ ----------

    // почему сейчас нельзя записаться; null — можно. Ничего не меняет, только проверяет
    // (так можно показывать плашку на главной, не выдавая предупреждений)
    public static string? BookBlock(AppDbContext db, User u)
    {
        if (IsLifetime(u))
            return "Запись закрыта. Снять ограничение может только учебная часть";

        if (u.PunishUntil != null && u.Level >= 2)
            return $"Запись закрыта до {Msk(u.PunishUntil):dd.MM.yyyy HH:mm}";

        if (u.PunishUntil != null && u.Level == 1 && BookedSince(db, u, u.PunishFrom!.Value) > 0)
            return $"До {Msk(u.PunishUntil):dd.MM HH:mm} можно записаться только один раз, вы уже записывались";

        // предупреждение за спам было меньше часа назад — пауза до конца часа
        var since = DateTime.UtcNow.AddMinutes(-SpamMinutes);
        var spam = db.ConductEvents
            .Where(e => e.UserId == u.Id && e.Kind == ConductKind.Spam && e.At >= since)
            .OrderByDescending(e => e.At)
            .FirstOrDefault();
        if (spam != null)
            return $"Вы слишком часто записывались и отменяли. Записаться снова можно в {Clock.ToMsk(spam.At.AddMinutes(SpamMinutes)):HH:mm}";

        return null;
    }

    // проверка перед записью: то же, что BookBlock, плюс счётчик спама (может выдать предупреждение)
    public static string? CanBook(AppDbContext db, User u)
    {
        Refresh(db, u);
        var block = BookBlock(db, u);
        if (block != null)
            return block;

        if (ActionsHour(db, u) >= SpamLimit)
        {
            SpamWarning(db, u);
            return BookBlock(db, u) ?? "Записаться снова можно через час";
        }
        return null;
    }

    // сколько раз за последний час записывался и отменял сам.
    // Считаем только записи, которые он потом сам отменил (запись + отмена = 2 действия):
    // кто реально ходит в учебную часть много раз за день, сюда не попадает
    public static int ActionsHour(AppDbContext db, User u)
    {
        var since = DateTime.UtcNow.AddMinutes(-SpamMinutes);
        var booked = db.Bookings.Count(b => b.StudentId == u.Id && b.CreatedAt >= since && b.Status == BookingStatus.Cancelled);
        var cancelled = db.Bookings.Count(b => b.StudentId == u.Id && b.CancelledAt >= since);
        return booked + cancelled;
    }

    // сколько своих записей сделал с момента from (отменённые системой и админом не считаем — он не виноват)
    private static int BookedSince(AppDbContext db, User u, DateTime from)
    {
        var summons = db.Purposes.Where(p => p.AdminOnly).Select(p => p.Id).ToList();
        return db.Bookings.Count(b => b.StudentId == u.Id && b.CreatedAt >= from && !summons.Contains(b.PurposeId)
            && b.Status != BookingStatus.SystemCancelled && b.Status != BookingStatus.AdminDeleted);
    }

    private static void SpamWarning(AppDbContext db, User u)
    {
        Event(db, u, ConductKind.Spam, $"{SpamLimit} записей и отмен за час");
        db.SaveChanges();

        var count = SpamCount(db, u);
        if (count >= SpamWarnings)
        {
            u.SpamFrom = DateTime.UtcNow;
            LevelUp(db, u, "слишком частые записи и отмены");
        }
        else
        {
            Dirty(u);   // студенту — красная плашка с временем (BookBlock) и жёлтая из ActiveWarnings
        }
    }

    // предупреждения за спам записью за последние 4 недели (после последнего подъёма уровня)
    public static int SpamCount(AppDbContext db, User u)
    {
        var since = DateTime.UtcNow.AddDays(-28);
        if (u.SpamFrom != null && u.SpamFrom > since)
            since = u.SpamFrom.Value;
        return db.ConductEvents.Count(e => e.UserId == u.Id && e.Kind == ConductKind.Spam && e.At > since);
    }

    // ---------- СМЕНА ДАННЫХ ----------

    // можно ли подать заявку на изменение данных; null — можно (может выдать предупреждение).
    // Спам сменой данных отдельно от уровня: 2 предупреждения (пауза час) → сутки → ещё 2 предупреждения → блок учётки
    public static string? CanRequestData(AppDbContext db, User u)
    {
        Refresh(db, u);

        if (u.DataUntil != null)
            return $"Подать заявку на изменение данных можно будет с {Msk(u.DataUntil):dd.MM 'в' HH:mm}";

        if (RequestsHour(db, u) >= DataLimit)
            return DataWarning(db, u);

        return null;
    }

    // заявку приняли в обработку — запомнить (отклонённые тоже считаются)
    public static void DataRequested(AppDbContext db, User u) =>
        Event(db, u, ConductKind.Request, "Заявка на изменение данных");

    // заявок за последний час
    public static int RequestsHour(AppDbContext db, User u)
    {
        var since = DateTime.UtcNow.AddMinutes(-60);
        return db.ConductEvents.Count(e => e.UserId == u.Id && e.Kind == ConductKind.Request && e.At >= since);
    }

    // предупреждений за смену данных за 4 недели (после последней разблокировки)
    public static int DataSpamCount(AppDbContext db, User u)
    {
        var since = DateTime.UtcNow.AddDays(-28);
        if (u.DataFrom != null && u.DataFrom > since)
            since = u.DataFrom.Value;
        return db.ConductEvents.Count(e => e.UserId == u.Id && e.Kind == ConductKind.DataSpam && e.At > since);
    }

    // выдать предупреждение за спам сменой данных; возвращает текст отказа
    private static string DataWarning(AppDbContext db, User u)
    {
        Event(db, u, ConductKind.DataSpam, $"Больше {DataLimit} заявок на изменение данных за час");
        db.SaveChanges();
        Dirty(u);

        var n = DataSpamCount(db, u);
        var now = DateTime.UtcNow;

        if (n >= DataBlockAt)
        {
            u.Blocked = true;
            u.DataFrom = now;   // после разблокировки счёт с нуля
            Event(db, u, ConductKind.Level, "Учётная запись заблокирована: подозрительно частые заявки на изменение данных");
            return "Учётная запись заблокирована из-за подозрительных действий. Обратитесь в учебную часть.";
        }

        if (n == DataDayAt)
        {
            u.DataUntil = now.AddDays(1);
            return $"Смена данных закрыта до {Msk(u.DataUntil):dd.MM 'в' HH:mm}";
        }

        u.DataUntil = now.AddHours(1);
        return $"Слишком много заявок за час. Подать новую можно с {Msk(u.DataUntil):HH:mm}";
    }

    // разблокировать учётку (админ)
    public static void Unblock(AppDbContext db, User u)
    {
        u.Blocked = false;
        u.DataUntil = null;
        u.DataFrom = DateTime.UtcNow;
        Event(db, u, ConductKind.Level, "Учебная часть разблокировала учётную запись");
        Mover.Notify(db, u.Id, null, "Учебная часть разблокировала вашу учётную запись.");
    }

    // ---------- АДМИН ----------

    // снять бан (или ограничение). С уровня 6 — «снят пожизненный»: строже следят дальше
    public static void Lift(AppDbContext db, User u)
    {
        if (u.Level == 6)
            u.Lifted = true;
        u.PunishFrom = null;
        u.PunishUntil = null;
        u.CleanFrom = DateTime.UtcNow;
        Event(db, u, ConductKind.Level, "Учебная часть сняла бан");
        Mover.Notify(db, u.Id, null, "Учебная часть сняла ограничение записи: снова можно записываться.");
    }

    // понизить уровень на ступень (и снять наказание текущего уровня)
    public static void Lower(AppDbContext db, User u)
    {
        if (u.Level == 0)
            return;
        var hadPunishment = u.PunishUntil != null || IsLifetime(u);
        u.Level--;
        u.Lifted = false;
        u.PunishFrom = null;
        u.PunishUntil = null;
        u.CleanFrom = DateTime.UtcNow;
        Event(db, u, ConductKind.Level, $"Учебная часть понизила уровень до {u.Level}");
        Mover.Notify(db, u.Id, null, hadPunishment
            ? $"Учебная часть сняла ограничение записи. Доступ к записи: {AccessNames[u.Level]}."
            : $"Доступ к записи улучшился: {AccessNames[u.Level]}.");
    }

    // ---------- ДЛЯ ПОКАЗА ----------

    // действующие предупреждения для студента: постоянная жёлтая плашка, скрыть нельзя.
    // Пропадает сама, когда предупреждение сгорит (4 недели) или когда вместо него наступило ограничение.
    // Без счётчиков «1 из 3» и без сроков: срок ограничения показывает красная плашка
    public static List<string> ActiveWarnings(AppDbContext db, User u)
    {
        var list = new List<string>();
        if (Strikes(db, u).Count > 0)
            list.Add("У вас есть неявки. Если не можете прийти, отменяйте запись заранее, иначе доступ к записи будет ограничен.");
        if (SpamCount(db, u) > 0)
            list.Add("Вы слишком часто записывались и отменяли записи. Если это продолжится, доступ к записи будет ограничен.");
        if (DataSpamCount(db, u) > 0)
            list.Add("Вы слишком часто отправляли заявки на изменение данных. Если это продолжится, учётная запись будет заблокирована.");
        return list;
    }

    // смена данных сейчас закрыта (пауза за спам); null — открыта
    public static string? DataBlock(User u) =>
        u.DataUntil == null ? null : $"Смена данных закрыта до {Msk(u.DataUntil):dd.MM 'в' HH:mm}";

    public static bool IsLifetime(User u) => u.Level == 6 && !u.Lifted;

    // наказание сейчас словами; null — наказания нет
    public static string? Punishment(User u)
    {
        if (IsLifetime(u))
            return "запись закрыта, снять ограничение может только учебная часть";
        if (u.PunishUntil == null)
            return null;
        return u.Level == 1
            ? $"до {Msk(u.PunishUntil):dd.MM HH:mm} можно записаться только один раз"
            : $"запись закрыта до {Msk(u.PunishUntil):dd.MM.yyyy HH:mm}";
    }

    // когда уровень станет лучше, если не будет нарушений (по Москве); null — уже 0 или пожизненный
    public static DateTime? NextDrop(User u)
    {
        if (u.Level == 0 || IsLifetime(u))
            return null;
        var from = u.CleanFrom ?? u.PunishUntil;   // CleanFrom никогда не раньше конца наказания
        return from == null ? null : Clock.ToMsk(from.Value.AddDays(7 * CleanWeeks[u.Level]));
    }

    // ---------- ВНУТРЕННЕЕ ----------

    // поднять уровень и назначить наказание
    private static void LevelUp(AppDbContext db, User u, string why)
    {
        var now = DateTime.UtcNow;
        if (u.Level < 6)
            u.Level++;
        u.Lifted = false;
        u.PunishFrom = now;
        u.PunishUntil = u.Level == 6 ? null : now.AddDays(7 * PunishWeeks[u.Level]);
        u.CleanFrom = u.PunishUntil;   // чистый срок считается с конца наказания

        Event(db, u, ConductKind.Level, $"Уровень повышен до {u.Level}: {why}");
        Mover.Notify(db, u.Id, null,
            $"Доступ к записи ухудшился: {AccessNames[u.Level]}. Сейчас {Punishment(u)}. Причина: {why}. " +
            "Чем больше нарушений, тем строже и дольше ограничения.");
    }

    // предупреждение или неявка — чистый срок начинается заново (но не раньше конца наказания)
    private static void Dirty(User u)
    {
        var now = DateTime.UtcNow;
        u.CleanFrom = u.PunishUntil != null && u.PunishUntil > now ? u.PunishUntil : now;
    }

    private static void Event(AppDbContext db, User u, string kind, string text)
    {
        db.ConductEvents.Add(new ConductEvent { UserId = u.Id, Kind = kind, Text = text, At = DateTime.UtcNow });
    }

    private static DateTime Msk(DateTime? utc) => Clock.ToMsk(utc!.Value);
}
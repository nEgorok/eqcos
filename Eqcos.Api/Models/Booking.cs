namespace Eqcos.Api.Models;

public class Booking
{
    public int Id { get; set; }
    public TimeOnly Time { get; set; }
    public string StudentName { get; set; } = "";
    public string Group { get; set; } = "";
    public int StudentId { get; set; }
    public int OfficeId { get; set; }   // в какой корпус
    public DateOnly Date { get; set; }  // на какой день
    public int PurposeId { get; set; }  // цель визита
    public int Minutes { get; set; }    // сколько длится приём (запоминаем при записи)
    public string Status { get; set; } = BookingStatus.Active;   // что с записью сейчас
    public bool Moved { get; set; }     // true — запись переносила система (неявка по ней не наказывается)
    public string? Note { get; set; }   // причина: почему удалил админ или отменила система
    public string? FirstMark { get; set; }       // отметка до исправления (Came или NoShow); null — не исправляли
    public DateTime? CorrectedAt { get; set; }   // когда исправили отметку (UTC)
    public bool Resolved { get; set; }   // вызов, отменённый системой, уже разобран: назначен заново или «не нужно»
}

// возможные состояния записи. Запись больше не удаляется из базы — у неё меняется статус,
// так остаётся история (кто отменил, кто не пришёл).
public static class BookingStatus
{
    public const string Active = "Active";                    // действует
    public const string Cancelled = "Cancelled";              // отменил сам студент
    public const string SystemCancelled = "SystemCancelled";  // отменила система (закрыли время, удалили аккаунт)
    public const string AdminDeleted = "AdminDeleted";        // удалил админ
    public const string Came = "Came";                        // пришёл (кнопка в шаге 22)
    public const string NoShow = "NoShow";                    // не пришёл (кнопка в шаге 22)
}
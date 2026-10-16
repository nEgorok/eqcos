namespace Eqcos.Web.Services;

// строка записи для панели учебной части (так её присылает сервер)
public record AdminBookingDto(
    int Id, DateOnly Date, TimeOnly Time, TimeOnly End,
    string Login, string StudentName, string Group, string Purpose, bool IsSummon,
    string Status, bool Moved, string? Note,
    string? FirstMark, DateTime? CorrectedAt,
    bool CanCame, bool CanNoShow, bool CanDelete);

// статус записи по-русски
public static class BookingText
{
    public static string Status(AdminBookingDto b)
    {
        var text = b.Status == "Active"
            ? (b.CanNoShow ? "Не отмечена" : "Ожидается")
            : Word(b.Status);

        if (b.Note != null)
            text += $" ({b.Note})";
        if (b.Moved)
            text += ", перенесена";
        // отметку исправляли — пишем когда; что стояло сначала, History покажет мелко справа
        if (b.FirstMark != null)
            text += $", исправлено {b.CorrectedAt:dd.MM HH:mm}";
        return text;
    }

    // одно слово для статуса
    public static string Word(string status) => status switch
    {
        "Cancelled" => "Отменил студент",
        "SystemCancelled" => "Отменена системой",
        "AdminDeleted" => "Удалена учебной частью",
        "Came" => "Пришёл",
        "NoShow" => "Не пришёл",
        _ => status
    };
}
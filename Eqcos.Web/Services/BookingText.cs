namespace Eqcos.Web.Services;

// строка записи для панели учебной части (так её присылает сервер)
public record AdminBookingDto(
    int Id, DateOnly Date, TimeOnly Time, TimeOnly End,
    string Login, string StudentName, string Group, string Purpose, bool IsSummon,
    string Status, bool Moved, string? Note,
    bool CanCame, bool CanNoShow, bool CanDelete);

// статус записи по-русски
public static class BookingText
{
    public static string Status(AdminBookingDto b)
    {
        var text = b.Status switch
        {
            "Active" => b.CanNoShow ? "Не отмечена" : "Ожидается",
            "Cancelled" => "Отменил студент",
            "SystemCancelled" => "Отменена системой",
            "AdminDeleted" => "Удалена учебной частью",
            "Came" => "Пришёл",
            "NoShow" => "Не пришёл",
            _ => b.Status
        };
        if (b.Note != null)
            text += $" ({b.Note})";
        if (b.Moved)
            text += ", перенесена";
        return text;
    }
}
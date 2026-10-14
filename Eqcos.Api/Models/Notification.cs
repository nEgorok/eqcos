namespace Eqcos.Api.Models;

// уведомление студенту
public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }          // кому
    public string Text { get; set; } = "";   // что написать
    public DateTime CreatedAt { get; set; }  // когда создано
    public bool IsRead { get; set; }         // уже нажал "Ок"
    public int? BookingId { get; set; }      // к какой записи относится (если есть)
}
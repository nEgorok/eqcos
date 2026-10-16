namespace Eqcos.Api.Models;

// журнал действий учебной части над студентами: правка данных, удаление (потом — баны)
public class AdminLog
{
    public int Id { get; set; }
    public int? OfficeId { get; set; }          // корпус админа
    public string Admin { get; set; } = "";     // логин админ-аккаунта, например admin_lab
    public int? StudentId { get; set; }         // о ком запись (учётку могли удалить — номер останется)
    public string Text { get; set; } = "";      // что сделали
    public DateTime At { get; set; }            // когда (UTC)
}
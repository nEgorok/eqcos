namespace Eqcos.Api.Models;

public class User
{
    public int Id { get; set; }
    public string Login { get; set; } = "";        // у студента = номер студенческого
    public string PasswordHash { get; set; } = ""; // пароль храним НЕ открыто, а в зашифрованном виде
    public string Role { get; set; } = "Student";  // "Student" или "Admin"
    public string FullName { get; set; } = "";     // "Фамилия Имя Отчество" (собирается из трёх полей)
    public string Group { get; set; } = "";        // "204Лг": 3 цифры + буквы из списка
    public int? OfficeId { get; set; }  // корпус: у админа — который он ведёт, у студента — где он учится
    public string Status { get; set; } = "Pending";   // "Pending" — на проверке, "Approved" — подтверждён
    public string? RejectReason { get; set; }         // старое поле: раньше отклонённые оставались в базе
    public DateTime? CreatedAt { get; set; }          // когда зарегистрировался (UTC); у старых учёток пусто

    // заявка на изменение данных (null — заявки нет). Новые данные лежат "в стороне", пока админ не подтвердит
    public string? PendingFullName { get; set; }
    public string? PendingGroup { get; set; }
    public int? PendingOfficeId { get; set; }      // новый корпус (перевод); заявку проверяет админ НОВОГО корпуса
    public string? PendingReason { get; set; }     // причина: «Перевод», «Ошибка при регистрации»...
}
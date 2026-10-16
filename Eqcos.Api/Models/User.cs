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
    public string Status { get; set; } = "Pending";   // "Pending" — на проверке, "Approved" — подтверждён, "Rejected" — отклонён
    public string? RejectReason { get; set; }         // почему отклонили (видит студент)
    public string? PendingFullName { get; set; }   // новое ФИО, ждёт проверки (null — заявки нет)
    public string? PendingGroup { get; set; }      // новая группа, ждёт проверки
}
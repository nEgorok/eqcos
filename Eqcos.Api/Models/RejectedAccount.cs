namespace Eqcos.Api.Models;

// отклонённая регистрация: учётка уже стёрта, номер свободен.
// 30 дней храним причину, чтобы показать её при входе с верным паролем.
public class RejectedAccount
{
    public int Id { get; set; }
    public string Login { get; set; } = "";         // номер студенческого
    public string PasswordHash { get; set; } = "";  // отпечаток пароля стёртой учётки
    public string Reason { get; set; } = "";
    public DateTime RejectedAt { get; set; }        // когда отклонили (UTC)
}
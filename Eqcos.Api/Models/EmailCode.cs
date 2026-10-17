namespace Eqcos.Api.Models;

// код из письма: привязка почты или сброс пароля.
// Сам код не храним — только его отпечаток (как с паролем).
public class EmailCode
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Purpose { get; set; } = "";   // "bind" — привязать/сменить почту, "reset" — сбросить пароль
    public string Email { get; set; } = "";     // куда отправили код
    public string CodeHash { get; set; } = "";  // отпечаток кода
    public int Attempts { get; set; }           // сколько раз ввели неверно
    public bool Used { get; set; }              // код уже сработал или его заменил новый
    public DateTime CreatedAt { get; set; }     // когда отправили (UTC)
    public DateTime ExpiresAt { get; set; }     // до какого момента действует (UTC)
}
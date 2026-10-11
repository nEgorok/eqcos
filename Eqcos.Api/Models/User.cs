namespace Eqcos.Api.Models;

public class User
{
    public int Id { get; set; }
    public string Login { get; set; } = "";        // у студента = номер студенческого
    public string PasswordHash { get; set; } = ""; // пароль храним НЕ открыто, а в зашифрованном виде
    public string Role { get; set; } = "Student";  // "Student" или "Admin"
    public string FullName { get; set; } = "";
    public string Group { get; set; } = "";
}
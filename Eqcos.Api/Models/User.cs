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

    // ПОРЯДОЧНОСТЬ (правила — в Services/Conduct.cs)
    public int Level { get; set; }                // уровень 0–6: 0 — зелёный, 6 — пожизненный бан
    public DateTime? PunishFrom { get; set; }     // начало наказания (UTC)
    public DateTime? PunishUntil { get; set; }    // конец наказания; null — наказания нет (или пожизненное)
    public DateTime? CleanFrom { get; set; }      // с этого момента идёт «чистый срок» до улучшения уровня
    public DateTime? StrikesFrom { get; set; }    // неявки раньше этого момента уже подняли уровень — не считаем
    public DateTime? SpamFrom { get; set; }       // то же для предупреждений за спам записью
    public DateTime? DataFrom { get; set; }       // предупреждения за спам сменой данных раньше этого момента не считаем
    public bool Lifted { get; set; }              // уровень 6, но пожизненный бан снят учебной частью
    public DateTime? DataUntil { get; set; }      // до этого момента подать заявку на изменение данных нельзя (пауза за спам)
    public bool Blocked { get; set; }             // учётка заблокирована за подозрительные действия; снимает только админ

    // ПОЧТА И ВХОД
    public string? Email { get; set; }            // привязанная почта (маленькими буквами); null — не привязана
    public int TokenVersion { get; set; }         // «версия входа»: +1 при смене пароля — старые пропуска перестают работать
}
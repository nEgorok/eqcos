namespace Eqcos.Api.Models;

// буквы группы из списка: Лг, Кп, Тда...  (группа = 3 цифры + эти буквы, например 204Лг)
public class GroupCode
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
}
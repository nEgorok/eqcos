namespace Eqcos.Api.Models;

// событие порядочности студента: заявка на изменение данных, предупреждение, смена уровня.
// По ним считаются шкалы спама, и их видно в карточке студента.
public class ConductEvent
{
    public int Id { get; set; }
    public int UserId { get; set; }            // чей
    public string Kind { get; set; } = "";     // что случилось (ConductKind)
    public string Text { get; set; } = "";     // описание для карточки
    public DateTime At { get; set; }           // когда (UTC)
}

public static class ConductKind
{
    public const string Request = "Request";     // подал заявку на изменение данных
    public const string Strike = "Strike";       // неявка (предупреждение)
    public const string Spam = "Spam";           // предупреждение: больше 10 записей и отмен за день
    public const string DataSpam = "DataSpam";   // предупреждение: больше 3 заявок на изменение данных за месяц
    public const string Level = "Level";         // уровень поднялся или опустился, админ снял бан
}
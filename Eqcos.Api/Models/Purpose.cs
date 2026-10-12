namespace Eqcos.Api.Models;

// цель визита: зачем студент идёт в учебную часть
public class Purpose
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Minutes { get; set; }       // сколько длится приём
    public bool AdminOnly { get; set; }    // true = ставит только админ (вызов)
}
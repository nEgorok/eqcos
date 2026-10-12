namespace Eqcos.Api.Models;

// учебная часть в корпусе и её часы работы
public class Office
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public TimeOnly Open { get; set; }        // начало работы
    public TimeOnly Close { get; set; }       // конец работы
    public TimeOnly BreakStart { get; set; }  // начало перерыва
    public TimeOnly BreakEnd { get; set; }    // конец перерыва
}
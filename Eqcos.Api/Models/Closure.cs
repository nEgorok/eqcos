namespace Eqcos.Api.Models;

// закрытое время: в этот промежуток учебная часть не принимает
public class Closure
{
    public int Id { get; set; }
    public int OfficeId { get; set; }     // какой корпус
    public DateOnly Date { get; set; }    // какой день
    public TimeOnly Start { get; set; }   // с
    public TimeOnly End { get; set; }     // по
    public string Reason { get; set; } = "";   // причина: "Совещание"
}
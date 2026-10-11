namespace Eqcos.Api.Models;

public class Booking
{
    public int Id { get; set; }
    public TimeOnly Time { get; set; }
    public string StudentName { get; set; } = "";
    public string Group { get; set; } = "";
    public int StudentId { get; set; }

}
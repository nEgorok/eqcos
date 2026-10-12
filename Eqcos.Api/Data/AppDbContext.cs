using Eqcos.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Eqcos.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Booking> Bookings { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Office> Offices { get; set; }

    // начальные данные: три корпуса сразу попадут в базу при миграции
    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Office>().HasData(
            new Office { Id = 1, Name = "Главный корпус", Open = new TimeOnly(8, 0), Close = new TimeOnly(16, 30), BreakStart = new TimeOnly(11, 0), BreakEnd = new TimeOnly(13, 0) },
            new Office { Id = 2, Name = "Лабораторный корпус", Open = new TimeOnly(8, 0), Close = new TimeOnly(16, 30), BreakStart = new TimeOnly(11, 0), BreakEnd = new TimeOnly(13, 0) },
            new Office { Id = 3, Name = "Второй корпус", Open = new TimeOnly(8, 0), Close = new TimeOnly(16, 30), BreakStart = new TimeOnly(11, 0), BreakEnd = new TimeOnly(13, 0) }
        );
    }
}
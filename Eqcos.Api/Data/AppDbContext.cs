using Eqcos.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Eqcos.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Booking> Bookings { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Office> Offices { get; set; }
    public DbSet<Purpose> Purposes { get; set; }
    public DbSet<Closure> Closures { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<GroupCode> GroupCodes { get; set; }
    public DbSet<RejectedAccount> RejectedAccounts { get; set; }
    public DbSet<AdminLog> AdminLogs { get; set; }
    public DbSet<ConductEvent> ConductEvents { get; set; }
    public DbSet<EmailCode> EmailCodes { get; set; }
    // начальные данные: три корпуса сразу попадут в базу при миграции
    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Office>().HasData(
            new Office { Id = 1, Name = "Главный корпус", Open = new TimeOnly(8, 0), Close = new TimeOnly(16, 30), BreakStart = new TimeOnly(11, 0), BreakEnd = new TimeOnly(13, 0) },
            new Office { Id = 2, Name = "Лабораторный корпус", Open = new TimeOnly(8, 0), Close = new TimeOnly(16, 30), BreakStart = new TimeOnly(11, 0), BreakEnd = new TimeOnly(13, 0) },
            new Office { Id = 3, Name = "Второй корпус", Open = new TimeOnly(8, 0), Close = new TimeOnly(16, 30), BreakStart = new TimeOnly(11, 0), BreakEnd = new TimeOnly(13, 0) }
        );
        // начальные цели визита
        mb.Entity<Purpose>().HasData(
            new Purpose { Id = 1, Name = "Подпись заявления на уход с пар", Minutes = 5 },
            new Purpose { Id = 2, Name = "Направление на исправление оценки", Minutes = 5 },
            new Purpose { Id = 3, Name = "Форма №6", Minutes = 5 },
            new Purpose { Id = 4, Name = "Профилактическая беседа", Minutes = 30, AdminOnly = true }
        );
        // буквы групп (админ сможет добавлять свои в «Настройках», шаг 27)
        mb.Entity<GroupCode>().HasData(
            new GroupCode { Id = 1, Code = "А" },
            new GroupCode { Id = 2, Code = "Б" },
            new GroupCode { Id = 3, Code = "Бп" },
            new GroupCode { Id = 4, Code = "В" },
            new GroupCode { Id = 5, Code = "К" },
            new GroupCode { Id = 6, Code = "Кп" },
            new GroupCode { Id = 7, Code = "Л" },
            new GroupCode { Id = 8, Code = "Лг" },
            new GroupCode { Id = 9, Code = "М" },
            new GroupCode { Id = 10, Code = "Н" },
            new GroupCode { Id = 11, Code = "Нп" },
            new GroupCode { Id = 12, Code = "П" },
            new GroupCode { Id = 13, Code = "Св" },
            new GroupCode { Id = 14, Code = "Тда" },
            new GroupCode { Id = 15, Code = "Тэ" },
            new GroupCode { Id = 16, Code = "Х" },
            new GroupCode { Id = 17, Code = "Э" }
        );
    }
}
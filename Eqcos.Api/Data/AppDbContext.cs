using Eqcos.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Eqcos.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Booking> Bookings { get; set; }   // станет таблицей Bookings
}
using Microsoft.EntityFrameworkCore;
using WebLoginDemo2.Models;

namespace WebLoginDemo2.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<AppUser> AppUsers { get; set; }
        public DbSet<SensorLog> SensorLogs { get; set; }
        public DbSet<SensorHourlyStats> SensorHourlyStats { get; set; }
    }
}
using Microsoft.EntityFrameworkCore;
using AutoServiceBot.Models;

namespace AutoServiceBot.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<Car> Cars => Set<Car>();
        public DbSet<Appointment> Appointments => Set<Appointment>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=autoservice.db");
        }
    }
}
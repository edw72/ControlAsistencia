using ControlAsistencia.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlAsistencia.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // Estas propiedades representan las tablas en SQLite
        public DbSet<Employee> Employees => Set<Employee>();
        public DbSet<TimeRecord> TimeRecords => Set<TimeRecord>();
        public DbSet<User> Users => Set<User>();
        public DbSet<AttendanceIncident> AttendanceIncidents => Set<AttendanceIncident>();
        public DbSet<Holiday> Holidays => Set<Holiday>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Regla importante: El código de tarjeta/QR debe ser ÚNICO
            // No pueden existir dos empleados con el mismo carné/RFID
            modelBuilder.Entity<Employee>()
                .HasIndex(e => e.CardCode)
                .IsUnique();

            // Índice para que las consultas de marcas por empleado y fecha sean ultra rápidas
            modelBuilder.Entity<TimeRecord>()
                .HasIndex(t => new { t.EmployeeId, t.TimestampUtc });
        }
    }
}

using ControlAsistencia.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlAsistencia.Models
{
    // Los diferentes tipos de marcas que admite el sistema
    public enum RecordType
    {
        ClockIn = 1,        // Entrada turno
        CoffeeStart = 2,    // Salida a café
        CoffeeEnd = 3,      // Regreso de café
        LunchStart = 4,     // Salida a almuerzo
        LunchEnd = 5,       // Regreso de almuerzo
        ClockOut = 6,       // Salida turno
        PersonalStart = 7,  // NUEVO: Salida a diligencia personal
        PersonalEnd = 8     // NUEVO: Regreso de diligencia personal
    }

    public class TimeRecord
    {
        public int Id { get; set; }

        [Required]
        public int EmployeeId { get; set; }

        [ForeignKey("EmployeeId")]
        public Employee? Employee { get; set; }

        [Required]
        public RecordType Type { get; set; }

        [Required]
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        [StringLength(250)]
        public string? Note { get; set; } // Opcional (ej: "Llegó 10 min tarde con justificación")
    }
}

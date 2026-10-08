using ControlAsistencia.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlAsistencia.Models
{
    public class AttendanceIncident
    {
        public int Id { get; set; }

        [Required]
        public int EmployeeId { get; set; }

        [ForeignKey("EmployeeId")]
        public Employee? Employee { get; set; }

        [Required]
        public DateTime Date { get; set; } // Día al que corresponde la novedad

        [Required]
        [StringLength(50)]
        public string Category { get; set; } = "Llegada Tardía Avisada"; // Cita médica, Transporte, Emergencia, etc.

        [Required]
        [StringLength(500)]
        public string Detail { get; set; } = string.Empty; // "Llamó a las 7:45 AM: problema con Uber..."

        [Required]
        [StringLength(50)]
        public string LoggedBy { get; set; } = string.Empty; // Supervisor que atendió la llamada

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow; // Momento exacto del registro
                                                                      
        public bool WaiveEarlyDeparture { get; set; } = false;  // Si es true, el supervisor perdonó la salida anticipada (no se descuenta de nómina)
    }
}
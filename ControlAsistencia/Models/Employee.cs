using Microsoft.EntityFrameworkCore.Migrations;
using System.ComponentModel.DataAnnotations;

namespace ControlAsistencia.Models
{
    public class Employee
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El código o carné es obligatorio")]
        [StringLength(50)]
        [Display(Name = "Código / RFID / QR")]
        public string CardCode { get; set; } = string.Empty; // Código único del QR o RFID

        [Required(ErrorMessage = "El nombre completo es obligatorio")]
        [StringLength(100)]
        [Display(Name = "Nombre completo")]
        public string FullName { get; set; } = string.Empty;

        [StringLength(6)]
        [Display(Name = "PIN de respaldo")]
        public string? Pin { get; set; } // PIN opcional si olvidó la tarjeta

        [Display(Name = "¿Está activo?")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Fecha de registro")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Display(Name = "Pausa Unificada (60 min Almuerzo - Sin Café)")]
        public bool HasUnifiedLunch { get; set; } = false;

        [Display(Name = "Fecha de Cumpleaños")]
        [DataType(DataType.Date)]
        public DateTime? BirthDate { get; set; }

        [EmailAddress(ErrorMessage = "Formato de correo inválido")]
        [StringLength(100)]
        [Display(Name = "Correo Electrónico")]
        public string? Email { get; set; }

        // Relación: Un empleado tiene muchas marcas de tiempo
        public ICollection<TimeRecord> TimeRecords { get; set; } = new List<TimeRecord>();
    }
}

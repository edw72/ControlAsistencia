using System.ComponentModel.DataAnnotations;

namespace ControlAsistencia.Models
{
    public class Holiday
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "La fecha del feriado es obligatoria")]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; }

        [Required(ErrorMessage = "El nombre del feriado es obligatorio")]
        [StringLength(100)]
        [Display(Name = "Nombre del Feriado")]
        public string Name { get; set; } = string.Empty; // Ej: "Navidad", "Año Nuevo"

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

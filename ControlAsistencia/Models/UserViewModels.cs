using System.ComponentModel.DataAnnotations;

namespace ControlAsistencia.Models
{
    public class UserCreateViewModel
    {
        [Required(ErrorMessage = "El nombre de usuario es obligatorio")]
        [StringLength(50)]
        [Display(Name = "Nombre de Usuario (Login)")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre completo es obligatorio")]
        [StringLength(100)]
        [Display(Name = "Nombre Completo")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "La contraseña es obligatoria")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Rol del Sistema")]
        public string Role { get; set; } = "Supervisor"; // "Admin" o "Supervisor"
    }

    public class UserEditViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre de usuario es obligatorio")]
        [StringLength(50)]
        [Display(Name = "Nombre de Usuario (Login)")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre completo es obligatorio")]
        [StringLength(100)]
        [Display(Name = "Nombre Completo")]
        public string FullName { get; set; } = string.Empty;

        [Display(Name = "Rol del Sistema")]
        public string Role { get; set; } = "Supervisor";

        // Campo opcional: solo se llena si se desea cambiar la contraseña
        [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres")]
        [DataType(DataType.Password)]
        [Display(Name = "Nueva Contraseña (Opcional)")]
        public string? NewPassword { get; set; }
    }
}

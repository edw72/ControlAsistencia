using ControlAsistencia.Models;
using ControlAsistencia.Services;
using Microsoft.AspNetCore.Mvc;

namespace ControlAsistencia.Controllers
{
    public class KioskController : Controller
    {
        private readonly AttendanceService _attendanceService;

        public KioskController(AttendanceService attendanceService)
        {
            _attendanceService = attendanceService;
        }

        // Pantalla principal del Kiosco
        public IActionResult Index()
        {
            return View();
        }

        // API para identificar al empleado por RFID, QR o PIN
        [HttpPost]
        public async Task<IActionResult> Identify([FromBody] IdentifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Code))
            {
                return BadRequest(new { success = false, message = "Código no ingresado" });
            }

            var status = await _attendanceService.GetStatusByCodeOrPinAsync(request.Code);

            if (status == null)
            {
                return NotFound(new { success = false, message = "Empleado no encontrado o inactivo" });
            }

            return Json(new { success = true, data = status });
        }

        // API para registrar la marca seleccionada
        [HttpPost]
        public async Task<IActionResult> Punch([FromBody] PunchRequest request)
        {
            var result = await _attendanceService.RegisterPunchAsync(request.EmployeeId, request.Type);

            if (!result.Success)
            {
                return BadRequest(new { success = false, message = result.Message });
            }

            return Json(new
            {
                success = true,
                message = result.Message,
                warning = result.WarningMessage // Enviamos la advertencia en rojo si existe
            });
        }
    }

    // Clases auxiliares para recibir los datos por JSON
    public class IdentifyRequest
    {
        public string Code { get; set; } = string.Empty;
    }

    public class PunchRequest
    {
        public int EmployeeId { get; set; }
        public RecordType Type { get; set; }
    }
}
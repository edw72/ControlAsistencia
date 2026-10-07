using ControlAsistencia.Data;
using ControlAsistencia.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlAsistencia.Web.Controllers
{
    [Authorize] // Solo accesible para supervisores/administradores autenticados
    public class AdjustmentsController : Controller
    {
        private readonly AppDbContext _context;

        public AdjustmentsController(AppDbContext context)
        {
            _context = context;
        }

        // 1. PANTALLA PRINCIPAL DE AJUSTES DEL DÍA PARA UN EMPLEADO
        public async Task<IActionResult> Index(int employeeId, DateTime? date)
        {
            var targetDate = date?.Date ?? DateTime.Today;

            var employee = await _context.Employees.FindAsync(employeeId);
            if (employee == null) return NotFound("Empleado no encontrado");

            var startUtc = targetDate.ToUniversalTime();
            var endUtc = targetDate.AddDays(1).AddTicks(-1).ToUniversalTime();

            var records = await _context.TimeRecords
                .Where(r => r.EmployeeId == employeeId && r.TimestampUtc >= startUtc && r.TimestampUtc <= endUtc)
                .OrderBy(r => r.TimestampUtc)
                .ToListAsync();

            var model = new AdjustmentViewModel
            {
                EmployeeId = employee.Id,
                EmployeeName = employee.FullName,
                CardCode = employee.CardCode,
                Date = targetDate,
                Records = records.Select(r => new PunchRecordDto
                {
                    Id = r.Id,
                    Type = r.Type,
                    TypeName = GetFriendlyTypeName(r.Type),
                    BadgeClass = GetBadgeClass(r.Type),
                    LocalTime = r.TimestampUtc.ToLocalTime(),
                    Note = r.Note
                }).ToList()
            };

            return View(model);
        }

        // 2. AGREGAR MARCA FALTANTE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPunch(int employeeId, DateTime date, RecordType type, string time, string justification)
        {
            if (string.IsNullOrWhiteSpace(time) || string.IsNullOrWhiteSpace(justification))
            {
                TempData["ErrorMessage"] = "Debe especificar la hora y la justificación del ajuste.";
                return RedirectToAction(nameof(Index), new { employeeId, date = date.ToString("yyyy-MM-dd") });
            }

            if (!TimeSpan.TryParse(time, out var parsedTime))
            {
                TempData["ErrorMessage"] = "Formato de hora inválido.";
                return RedirectToAction(nameof(Index), new { employeeId, date = date.ToString("yyyy-MM-dd") });
            }

            // Combinar la fecha local con la hora ingresada y convertir a UTC
            var localDateTime = date.Date.Add(parsedTime);
            var utcDateTime = localDateTime.ToUniversalTime();

            var supervisorName = User.Identity?.Name ?? "Supervisor";

            var newRecord = new TimeRecord
            {
                EmployeeId = employeeId,
                Type = type,
                TimestampUtc = utcDateTime,
                Note = $"[Agregado manualmente por {supervisorName}]: {justification}"
            };

            _context.TimeRecords.Add(newRecord);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Marca agregada exitosamente.";
            return RedirectToAction(nameof(Index), new { employeeId, date = date.ToString("yyyy-MM-dd") });
        }

        // 3. EDITAR HORA DE UNA MARCA EXISTENTE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPunch(int recordId, int employeeId, DateTime date, string time, string justification)
        {
            var record = await _context.TimeRecords.FindAsync(recordId);
            if (record == null) return NotFound();

            if (string.IsNullOrWhiteSpace(time) || string.IsNullOrWhiteSpace(justification))
            {
                TempData["ErrorMessage"] = "Debe ingresar una nueva hora y justificación.";
                return RedirectToAction(nameof(Index), new { employeeId, date = date.ToString("yyyy-MM-dd") });
            }

            if (!TimeSpan.TryParse(time, out var parsedTime))
            {
                TempData["ErrorMessage"] = "Formato de hora inválido.";
                return RedirectToAction(nameof(Index), new { employeeId, date = date.ToString("yyyy-MM-dd") });
            }

            var localDateTime = date.Date.Add(parsedTime);
            var utcDateTime = localDateTime.ToUniversalTime();

            var supervisorName = User.Identity?.Name ?? "Supervisor";

            record.TimestampUtc = utcDateTime;
            record.Note = $"[Hora modificada por {supervisorName}]: {justification} (Original: {record.TimestampUtc.ToLocalTime():hh:mm tt})";

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Hora de marca actualizada con éxito.";
            return RedirectToAction(nameof(Index), new { employeeId, date = date.ToString("yyyy-MM-dd") });
        }

        // 4. ELIMINAR MARCA ACCIDENTAL / DUPLICADA
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePunch(int recordId, int employeeId, DateTime date)
        {
            var record = await _context.TimeRecords.FindAsync(recordId);
            if (record == null) return NotFound();

            _context.TimeRecords.Remove(record);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Marca eliminada correctamente.";
            return RedirectToAction(nameof(Index), new { employeeId, date = date.ToString("yyyy-MM-dd") });
        }

        private static string GetFriendlyTypeName(RecordType type) => type switch
        {
            RecordType.ClockIn => "🟢 Entrada de Turno",
            RecordType.CoffeeStart => "☕ Salida a Café",
            RecordType.CoffeeEnd => "☕ Regreso de Café",
            RecordType.LunchStart => "🍲 Salida a Almuerzo",
            RecordType.LunchEnd => "🍲 Regreso de Almuerzo",
            RecordType.PersonalStart => "🚶 Salida a Diligencia",
            RecordType.PersonalEnd => "🚶 Regreso de Diligencia",
            RecordType.ClockOut => "🔴 Salida de Turno",
            _ => type.ToString()
        };

        private static string GetBadgeClass(RecordType type) => type switch
        {
            RecordType.ClockIn => "bg-success",
            RecordType.CoffeeStart => "bg-warning text-dark",
            RecordType.CoffeeEnd => "bg-warning text-dark",
            RecordType.LunchStart => "bg-info text-dark",
            RecordType.LunchEnd => "bg-info text-dark",
            RecordType.PersonalStart => "bg-dark text-white",
            RecordType.PersonalEnd => "bg-dark text-white",
            RecordType.ClockOut => "bg-danger",
            _ => "bg-secondary"
        };
    }
}
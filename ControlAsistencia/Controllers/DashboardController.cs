using ControlAsistencia.Data;
using ControlAsistencia.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlAsistencia.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly AttendanceService _attendanceService;
        private readonly EmailService _emailService; // <-- NUEVO
        private readonly AppDbContext _context;      // <-- NUEVO

        public DashboardController(AttendanceService attendanceService, EmailService emailService, AppDbContext context)
        {
            _attendanceService = attendanceService;
            _emailService = emailService;
            _context = context;
        }

        // Vista diaria en tiempo real
        public async Task<IActionResult> Index(DateTime? date)
        {
            var targetDate = date ?? DateTime.Today;
            var model = await _attendanceService.GetDailyDashboardAsync(targetDate);
            return View(model);
        }

        // Vista de Liquidación Quincenal e Impresión de Reporte
        public async Task<IActionResult> BiweeklyReport(int employeeId, int? year, int? month, int? quincena)
        {
            var y = year ?? DateTime.Today.Year;
            var m = month ?? DateTime.Today.Month;
            // Si hoy es día 15 o menos, por defecto la 1ra; si es 16 o más, la 2da
            var q = quincena ?? (DateTime.Today.Day <= 15 ? 1 : 2);

            var model = await _attendanceService.GetBiweeklyReportAsync(employeeId, y, m, q);

            if (model == null) return NotFound("Empleado no encontrado");

            ViewBag.SelectedYear = y;
            ViewBag.SelectedMonth = m;
            ViewBag.SelectedQuincena = q;

            return View(model);
        }

        // Descarga del archivo Excel (.xlsx)
        [HttpGet]
        public async Task<IActionResult> ExportBiweeklyToExcel(int employeeId, int year, int month, int quincena)
        {
            var model = await _attendanceService.GetBiweeklyReportAsync(employeeId, year, month, quincena);
            if (model == null) return NotFound("Empleado no encontrado");

            var excelBytes = _attendanceService.GenerateBiweeklyExcel(model);

            // Limpiamos el nombre para que sea un nombre de archivo válido
            string safeName = model.FullName.Replace(" ", "_");
            string fileName = $"Liquidacion_{safeName}_{model.PeriodTitle.Replace(" ", "_")}.xlsx";

            return File(
                excelBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // 1. ENVIAR REPORTE POR CORREO A UN SOLO COLABORADOR
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendBiweeklyEmail(int employeeId, int year, int month, int quincena)
        {
            var employee = await _context.Employees.FindAsync(employeeId);
            if (employee == null) return NotFound("Empleado no encontrado");

            if (string.IsNullOrWhiteSpace(employee.Email))
            {
                TempData["ErrorMessage"] = $"El colaborador {employee.FullName} no tiene un correo electrónico registrado en su ficha.";
                return RedirectToAction(nameof(BiweeklyReport), new { employeeId, year, month, quincena });
            }

            var report = await _attendanceService.GetBiweeklyReportAsync(employeeId, year, month, quincena);
            if (report == null) return NotFound();

            var excelBytes = _attendanceService.GenerateBiweeklyExcel(report);

            var result = await _emailService.SendBiweeklyReportEmailAsync(employee.Email, employee.FullName, report, excelBytes);

            if (result.Success)
            {
                TempData["SuccessMessage"] = $"Liquidación enviada con éxito al correo {employee.Email}.";
            }
            else
            {
                TempData["ErrorMessage"] = result.Message; // <-- Te mostrará el motivo exacto del error
            }

            return RedirectToAction(nameof(BiweeklyReport), new { employeeId, year, month, quincena });
        }

        // 2. ENVIAR LIQUIDACIONES POR CORREO A TODOS CON 1 CLIC
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendBulkBiweeklyEmails(int year, int month, int quincena)
        {
            var employees = await _context.Employees
                .Where(e => e.IsActive && !string.IsNullOrEmpty(e.Email))
                .ToListAsync();

            if (!employees.Any())
            {
                TempData["ErrorMessage"] = "No hay colaboradores activos con correo electrónico registrado.";
                return RedirectToAction(nameof(Index));
            }

            int successCount = 0;

            foreach (var emp in employees)
            {
                var report = await _attendanceService.GetBiweeklyReportAsync(emp.Id, year, month, quincena);
                if (report == null) continue;

                var excelBytes = _attendanceService.GenerateBiweeklyExcel(report);
                var result = await _emailService.SendBiweeklyReportEmailAsync(emp.Email!, emp.FullName, report, excelBytes);
                if (result.Success) successCount++;
            }

            TempData["SuccessMessage"] = $"Se enviaron exitosamente {successCount} reportes de liquidación con Excel adjunto por correo.";
            return RedirectToAction(nameof(Index));
        }

        // ACCIÓN PARA GENERAR RESPALDO MANUAL INMEDIATO
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TriggerManualBackup([FromServices] IConfiguration config)
        {
            var result = await DailyBackupService.ExecuteBackupProcedureAsync(HttpContext.RequestServices, config);

            if (result.Success)
            {
                TempData["SuccessMessage"] = "Respaldo generado y enviado con éxito a tu correo electrónico.";
            }
            else
            {
                TempData["ErrorMessage"] = result.Message;
            }

            return RedirectToAction(nameof(Index));
        }
    }

}
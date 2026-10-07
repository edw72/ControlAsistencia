using ControlAsistencia.Services;
using Microsoft.AspNetCore.Mvc;

namespace ControlAsistencia.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class DashboardController : Controller
    {
        private readonly AttendanceService _attendanceService;

        public DashboardController(AttendanceService attendanceService)
        {
            _attendanceService = attendanceService;
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
    }

}
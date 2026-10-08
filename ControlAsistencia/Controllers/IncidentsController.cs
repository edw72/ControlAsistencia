using ControlAsistencia.Data;
using ControlAsistencia.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlAsistencia.Controllers
{
    [Authorize] // Solo accesible para supervisores y admin
    public class IncidentsController : Controller
    {
        private readonly AppDbContext _context;

        public IncidentsController(AppDbContext context)
        {
            _context = context;
        }

        // 1. BITÁCORA DE CONSULTA GENERAL (Filtros por empleado, fecha o texto)
        public async Task<IActionResult> Index(int? employeeId, DateTime? fromDate, DateTime? toDate, string? search)
        {
            var query = _context.AttendanceIncidents
                .Include(i => i.Employee)
                .AsQueryable();

            if (employeeId.HasValue)
            {
                query = query.Where(i => i.EmployeeId == employeeId.Value);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(i => i.Date >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                query = query.Where(i => i.Date <= toDate.Value.Date.AddDays(1).AddTicks(-1));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim().ToLower();
                query = query.Where(i => i.Detail.ToLower().Contains(search) ||
                                         i.Category.ToLower().Contains(search) ||
                                         i.Employee!.FullName.ToLower().Contains(search));
            }

            var incidents = await query
                .OrderByDescending(i => i.Date)
                .ThenByDescending(i => i.CreatedAtUtc)
                .ToListAsync();

            ViewBag.Employees = await _context.Employees.Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync();
            ViewBag.SelectedEmployeeId = employeeId;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
            ViewBag.Search = search;

            return View(incidents);
        }

        // 2. REGISTRO RÁPIDO DESDE EL DASHBOARD (Al recibir la llamada)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateQuick(int employeeId, DateTime date, string category, string detail, bool waiveEarlyDeparture, string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                TempData["ErrorMessage"] = "Debe ingresar el detalle o motivo de la novedad.";
                return Redirect(returnUrl ?? "/Dashboard");
            }

            var incident = new AttendanceIncident
            {
                EmployeeId = employeeId,
                Date = date.Date,
                Category = category,
                Detail = detail.Trim(),
                WaiveEarlyDeparture = waiveEarlyDeparture, // <-- NUEVO
                LoggedBy = User.Identity?.Name ?? "Supervisor",
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.AttendanceIncidents.Add(incident);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = waiveEarlyDeparture
                ? "Salida anticipada justificada con goce de sueldo (no se descontará)."
                : "Novedad registrada correctamente en la bitácora.";

            return Redirect(returnUrl ?? "/Dashboard");
        }

        // 3. ELIMINAR NOVEDAD (Por error)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, string? returnUrl)
        {
            var incident = await _context.AttendanceIncidents.FindAsync(id);
            if (incident != null)
            {
                _context.AttendanceIncidents.Remove(incident);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Registro eliminado de la bitácora.";
            }

            return Redirect(returnUrl ?? "/Incidents");
        }
    }
}

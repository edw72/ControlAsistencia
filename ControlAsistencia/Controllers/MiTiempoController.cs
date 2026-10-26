using ControlAsistencia.Data;
using ControlAsistencia.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlAsistencia.Controllers
{
    [AllowAnonymous] // Acceso libre sin pedir usuario ni clave de supervisor
    public class MiTiempoController : Controller
    {
        private readonly AppDbContext _context;

        public MiTiempoController(AppDbContext context)
        {
            _context = context;
        }

        // Pantalla móvil individual
        public IActionResult Index()
        {
            return View();
        }

        // API para consultar el estado del descanso del colaborador
        [HttpPost]
        public async Task<IActionResult> CheckStatus([FromBody] MiTiempoRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Code))
            {
                return BadRequest(new { success = false, message = "Ingrese su número de código o carné." });
            }

            var code = request.Code.Trim();

            var employee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.IsActive && (e.CardCode == code || (!string.IsNullOrEmpty(e.Pin) && e.Pin == code)));

            if (employee == null)
            {
                return NotFound(new { success = false, message = "Colaborador no encontrado o inactivo." });
            }

            var todayUtc = DateTime.Today.ToUniversalTime();

            var lastRecord = await _context.TimeRecords
                .AsNoTracking()
                .Where(r => r.EmployeeId == employee.Id && r.TimestampUtc >= todayUtc)
                .OrderByDescending(r => r.TimestampUtc)
                .FirstOrDefaultAsync();

            bool isOnBreak = false;
            string breakType = "";
            int allowedMinutes = 0;
            int elapsedSeconds = 0;
            string startTimeLocal = "--:--";

            if (lastRecord != null && (lastRecord.Type == RecordType.CoffeeStart || lastRecord.Type == RecordType.LunchStart))
            {
                isOnBreak = true;
                bool isCoffee = lastRecord.Type == RecordType.CoffeeStart;
                breakType = isCoffee ? "Café" : "Almuerzo";

                bool isHoliday = await _context.Holidays.AnyAsync(h => h.Date.Date == DateTime.Today);
                bool isWeekdayNormal = DateTime.Today.DayOfWeek >= DayOfWeek.Monday && DateTime.Today.DayOfWeek <= DayOfWeek.Friday && !isHoliday;

                // Límite según perfil y día
                allowedMinutes = isCoffee ? 15 : ((employee.HasUnifiedLunch && isWeekdayNormal) ? 60 : 45);

                var recordUtc = DateTime.SpecifyKind(lastRecord.TimestampUtc, DateTimeKind.Utc);
                elapsedSeconds = (int)Math.Max(0, (DateTime.UtcNow - recordUtc).TotalSeconds);
                startTimeLocal = recordUtc.ToLocalTime().ToString("hh:mm tt");
            }

            string currentStatusText = lastRecord?.Type switch
            {
                RecordType.ClockIn => "🟢 Laborando en jornada",
                RecordType.CoffeeEnd => "🟢 Laborando en jornada",
                RecordType.LunchEnd => "🟢 Laborando en jornada",
                RecordType.PersonalEnd => "🟢 Laborando en jornada",
                RecordType.PersonalStart => "🚶 En Diligencia Personal",
                RecordType.ClockOut => "🔴 Jornada Finalizada",
                _ => (isOnBreak ? $"En descanso de {breakType}" : "⚪ Sin marcas registradas hoy")
            };

            return Json(new
            {
                success = true,
                fullName = employee.FullName,
                cardCode = employee.CardCode,
                isOnBreak,
                breakType,
                allowedMinutes,
                elapsedSeconds,
                startTimeLocal,
                currentStatus = currentStatusText
            });
        }
    }

    public class MiTiempoRequest
    {
        public string Code { get; set; } = string.Empty;
    }
}
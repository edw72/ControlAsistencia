using ControlAsistencia.Data;
using ControlAsistencia.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Speech.Synthesis;

namespace ControlAsistencia.Web.Controllers
{
    public class ComedorController : Controller
    {
        private readonly AppDbContext _context;

        public ComedorController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetActiveBreaks()
        {
            var startOfLocalDayUtc = DateTime.Today.ToUniversalTime();
            bool isSaturday = DateTime.Today.DayOfWeek == DayOfWeek.Saturday;

            var employees = await _context.Employees
                .AsNoTracking()
                .Where(e => e.IsActive)
                .ToListAsync();

            var todayRecords = await _context.TimeRecords
                .AsNoTracking()
                .Where(r => r.TimestampUtc >= startOfLocalDayUtc)
                .OrderBy(r => r.TimestampUtc)
                .ToListAsync();

            var activeBreaks = new List<ActiveBreakDto>();
            var nowUtc = DateTime.UtcNow;

            foreach (var emp in employees)
            {
                var empRecs = todayRecords.Where(r => r.EmployeeId == emp.Id).ToList();
                if (!empRecs.Any()) continue;

                var lastRecord = empRecs.Last();

                if (lastRecord.Type == RecordType.CoffeeStart || lastRecord.Type == RecordType.LunchStart)
                {
                    var recordUtc = DateTime.SpecifyKind(lastRecord.TimestampUtc, DateTimeKind.Utc);
                    var elapsedSeconds = (int)(nowUtc - recordUtc).TotalSeconds;
                    if (elapsedSeconds < 0) elapsedSeconds = 0;

                    bool isCoffee = lastRecord.Type == RecordType.CoffeeStart;

                    // REGLA: Si es sábado, horario estándar para todos (45m almuerzo). 
                    // De L-V, si tiene pausa unificada son 60m.
                    int allowed = isCoffee ? 15 : ((emp.HasUnifiedLunch && !isSaturday) ? 60 : 45);

                    activeBreaks.Add(new ActiveBreakDto
                    {
                        EmployeeId = emp.Id,
                        FullName = emp.FullName,
                        BreakType = isCoffee ? "Café" : "Almuerzo",
                        StartTimeLocal = recordUtc.ToLocalTime().ToString("hh:mm tt"),
                        ElapsedSeconds = elapsedSeconds,
                        AllowedMinutes = allowed
                    });
                }
            }

            return Json(activeBreaks);
        }

        // GENERADOR DE AUDIO DE VOZ PARA EL SMART TV
        [HttpGet]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")] // <-- Agrega esta línea
        public IActionResult Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return BadRequest();

            try
            {
                using var synth = new SpeechSynthesizer();
                synth.Volume = 100; // Volumen al máximo
                synth.Rate = 0;     // Velocidad natural

                // Buscar voz en español instalada en Windows
                var spanishVoice = synth.GetInstalledVoices()
                    .FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase));

                if (spanishVoice != null)
                {
                    synth.SelectVoice(spanishVoice.VoiceInfo.Name);
                }

                using var ms = new MemoryStream();
                synth.SetOutputToWaveStream(ms);
                synth.Speak(text);
                ms.Position = 0;

                return File(ms.ToArray(), "audio/wav");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }

    public class ActiveBreakDto
    {
        public int EmployeeId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string BreakType { get; set; } = string.Empty;
        public string StartTimeLocal { get; set; } = string.Empty;
        public int ElapsedSeconds { get; set; }
        public int AllowedMinutes { get; set; }
    }
}
using ControlAsistencia.Data;
using ControlAsistencia.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlAsistencia.Controllers
{
    [Authorize] // Solo supervisores y administradores
    public class HolidaysController : Controller
    {
        private readonly AppDbContext _context;

        public HolidaysController(AppDbContext context)
        {
            _context = context;
        }

        // Listado de días feriados
        public async Task<IActionResult> Index()
        {
            var holidays = await _context.Holidays
                .OrderBy(h => h.Date)
                .ToListAsync();

            return View(holidays);
        }

        // Registrar nuevo feriado
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Holiday holiday)
        {
            if (await _context.Holidays.AnyAsync(h => h.Date.Date == holiday.Date.Date))
            {
                ModelState.AddModelError("Date", "Ya existe un feriado registrado para esta fecha.");
            }

            if (ModelState.IsValid)
            {
                holiday.Date = holiday.Date.Date;
                holiday.CreatedAt = DateTime.UtcNow;
                _context.Holidays.Add(holiday);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Feriado '{holiday.Name}' registrado exitosamente.";
                return RedirectToAction(nameof(Index));
            }

            var holidays = await _context.Holidays.OrderBy(h => h.Date).ToListAsync();
            return View(nameof(Index), holidays);
        }

        // Eliminar feriado
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var holiday = await _context.Holidays.FindAsync(id);
            if (holiday != null)
            {
                _context.Holidays.Remove(holiday);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Día feriado eliminado correctamente.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}

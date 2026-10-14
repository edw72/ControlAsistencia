using ControlAsistencia.Data;
using ControlAsistencia.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlAsistencia.Web.Controllers
{
    [Authorize]
    public class HolidaysController : Controller
    {
        private readonly AppDbContext _context;

        public HolidaysController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var holidays = await _context.Holidays
                .OrderBy(h => h.Date)
                .ToListAsync();

            return View(holidays);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Holiday holiday)
        {
            if (await _context.Holidays.AnyAsync(h => h.Date.Date == holiday.Date.Date))
            {
                TempData["ErrorMessage"] = "Ya existe un feriado registrado para esta fecha.";
                return RedirectToAction(nameof(Index));
            }

            if (ModelState.IsValid)
            {
                holiday.Date = holiday.Date.Date;
                holiday.CreatedAt = DateTime.UtcNow;
                _context.Holidays.Add(holiday);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Feriado '{holiday.Name}' registrado exitosamente.";
            }

            return RedirectToAction(nameof(Index));
        }

        // NUEVA ACCIÓN: EDITAR FECHA O NOMBRE DEL FERIADO
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DateTime date, string name)
        {
            var holiday = await _context.Holidays.FindAsync(id);
            if (holiday == null) return NotFound();

            // Validar que la nueva fecha no coincida con otro feriado existente
            if (await _context.Holidays.AnyAsync(h => h.Date.Date == date.Date && h.Id != id))
            {
                TempData["ErrorMessage"] = "Ya existe otro feriado registrado en esa misma fecha.";
                return RedirectToAction(nameof(Index));
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                holiday.Date = date.Date;
                holiday.Name = name.Trim();
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Feriado '{holiday.Name}' actualizado correctamente.";
            }

            return RedirectToAction(nameof(Index));
        }

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
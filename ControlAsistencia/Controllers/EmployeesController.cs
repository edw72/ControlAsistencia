using ControlAsistencia.Data;
using ControlAsistencia.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlAsistencia.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class EmployeesController : Controller
    {
        private readonly AppDbContext _context;

        public EmployeesController(AppDbContext context)
        {
            _context = context;
        }

        // 1. LISTADO DE EMPLEADOS
        public async Task<IActionResult> Index(string? search)
        {
            var query = _context.Employees.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim().ToLower();
                query = query.Where(e => e.FullName.ToLower().Contains(search) || e.CardCode.Contains(search));
            }

            var employees = await query.OrderBy(e => e.FullName).ToListAsync();
            ViewBag.CurrentSearch = search;
            return View(employees);
        }

        // 2. CREAR EMPLEADO (GET)
        public IActionResult Create()
        {
            return View();
        }

        // 2. CREAR EMPLEADO (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Employee employee)
        {
            // Validar si el código RFID / QR ya pertenece a alguien más
            if (await _context.Employees.AnyAsync(e => e.CardCode == employee.CardCode))
            {
                ModelState.AddModelError("CardCode", "Este código de tarjeta o QR ya está asignado a otro empleado.");
            }

            if (ModelState.IsValid)
            {
                employee.CreatedAt = DateTime.UtcNow;
                employee.IsActive = true;
                _context.Employees.Add(employee);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Empleado {employee.FullName} registrado con éxito.";
                return RedirectToAction(nameof(Index));
            }

            return View(employee);
        }

        // 3. EDITAR EMPLEADO (GET)
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();

            return View(employee);
        }

        // 3. EDITAR EMPLEADO (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Employee employee)
        {
            if (id != employee.Id) return NotFound();

            // Validar que el código no esté duplicado en otro empleado distinto
            if (await _context.Employees.AnyAsync(e => e.CardCode == employee.CardCode && e.Id != id))
            {
                ModelState.AddModelError("CardCode", "Este código de tarjeta o QR ya está asignado a otro empleado.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existing = await _context.Employees.FindAsync(id);
                    if (existing == null) return NotFound();

                    existing.FullName = employee.FullName;
                    existing.CardCode = employee.CardCode;
                    existing.Pin = employee.Pin;
                    existing.Email = employee.Email;
                    existing.IsActive = employee.IsActive;
                    existing.HasUnifiedLunch = employee.HasUnifiedLunch;
                    existing.BirthDate = employee.BirthDate;

                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Datos del empleado actualizados con éxito.";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Employees.AnyAsync(e => e.Id == employee.Id)) return NotFound();
                    throw;
                }
            }

            return View(employee);
        }

        // 4. ACTIVAR / DESACTIVAR (Soft Delete)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();

            employee.IsActive = !employee.IsActive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = employee.IsActive
                ? $"Empleado {employee.FullName} ha sido activado."
                : $"Empleado {employee.FullName} ha sido desactivado.";

            return RedirectToAction(nameof(Index));
        }

        // 5. CARNÉ DE IDENTIFICACIÓN CON CÓDIGO QR PARA IMPRIMIR
        public async Task<IActionResult> Badge(int id)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();

            return View(employee);
        }
    }
}
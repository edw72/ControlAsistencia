using ControlAsistencia.Data;
using ControlAsistencia.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ControlAsistencia.Controllers
{
    [Authorize(Roles = "Admin")] // ¡SOLO ADMINISTRADORES PUEDEN ENTRAR AQUÍ!
    public class UsersController : Controller
    {
        private readonly AppDbContext _context;

        public UsersController(AppDbContext context)
        {
            _context = context;
        }

        // 1. LISTADO DE USUARIOS
        public async Task<IActionResult> Index()
        {
            var users = await _context.Users
                .OrderBy(u => u.FullName)
                .ToListAsync();

            return View(users);
        }

        // 2. CREAR USUARIO (GET)
        public IActionResult Create()
        {
            return View();
        }

        // 2. CREAR USUARIO (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UserCreateViewModel model)
        {
            // Validar que el nombre de usuario no esté duplicado
            if (await _context.Users.AnyAsync(u => u.Username.ToLower() == model.Username.Trim().ToLower()))
            {
                ModelState.AddModelError("Username", "Este nombre de usuario ya está en uso.");
            }

            if (ModelState.IsValid)
            {
                var newUser = new User
                {
                    Username = model.Username.Trim().ToLower(),
                    FullName = model.FullName.Trim(),
                    Role = model.Role
                };

                // Encriptación segura de contraseña
                var hasher = new PasswordHasher<User>();
                newUser.PasswordHash = hasher.HashPassword(newUser, model.Password);

                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Usuario '{newUser.FullName}' creado exitosamente.";
                return RedirectToAction(nameof(Index));
            }

            return View(model);
        }

        // 3. EDITAR USUARIO / CAMBIAR CONTRASEÑA (GET)
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            var model = new UserEditViewModel
            {
                Id = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Role = user.Role
            };

            return View(model);
        }

        // 3. EDITAR USUARIO / CAMBIAR CONTRASEÑA (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, UserEditViewModel model)
        {
            if (id != model.Id) return NotFound();

            // Validar nombre de usuario duplicado en otro usuario
            if (await _context.Users.AnyAsync(u => u.Username.ToLower() == model.Username.Trim().ToLower() && u.Id != id))
            {
                ModelState.AddModelError("Username", "Este nombre de usuario ya está asignado a otra persona.");
            }

            if (ModelState.IsValid)
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null) return NotFound();

                user.Username = model.Username.Trim().ToLower();
                user.FullName = model.FullName.Trim();
                user.Role = model.Role;

                // Si escribió una nueva contraseña, la actualizamos encriptada
                if (!string.IsNullOrWhiteSpace(model.NewPassword))
                {
                    var hasher = new PasswordHasher<User>();
                    user.PasswordHash = hasher.HashPassword(user, model.NewPassword);
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Datos del usuario '{user.FullName}' actualizados correctamente.";
                return RedirectToAction(nameof(Index));
            }

            return View(model);
        }

        // 4. ELIMINAR USUARIO
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            // Evitar que te borres a ti mismo por error
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId == id.ToString())
            {
                TempData["ErrorMessage"] = "No puedes eliminar tu propia cuenta mientras estás conectado.";
                return RedirectToAction(nameof(Index));
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Usuario '{user.FullName}' eliminado del sistema.";
            return RedirectToAction(nameof(Index));
        }
    }
}
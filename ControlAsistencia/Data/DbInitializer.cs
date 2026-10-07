using ControlAsistencia.Data;
using ControlAsistencia.Models;
using Microsoft.AspNetCore.Identity;

namespace ControlAsistencia.Data
{
    public static class DbInitializer
    {
        public static void Seed(AppDbContext context)
        {
            // 1. Sembrar Administrador por defecto
            if (!context.Users.Any())
            {
                var hasher = new PasswordHasher<User>();
                var adminUser = new User
                {
                    Username = "admin",
                    FullName = "Administrador del Sistema",
                    Role = "Admin"
                };
                adminUser.PasswordHash = hasher.HashPassword(adminUser, "Admin123*");

                context.Users.Add(adminUser);
            }

            // 2. Sembrar Empleados de prueba
            if (!context.Employees.Any())
            {
                var employees = new Employee[]
                {
                    new Employee { FullName = "Carlos Rodríguez", CardCode = "1001", Pin = "1234", IsActive = true },
                    new Employee { FullName = "Ana Gómez", CardCode = "1002", Pin = "5678", IsActive = true }
                };
                context.Employees.AddRange(employees);
            }

            context.SaveChanges();
        }
    }
}
using ControlAsistencia.Data;
using ControlAsistencia.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Host.UseWindowsService();

// 1. SOLUCIÓN AL RETRASO DE 15s: Escuchar en IPv4 e IPv6 a la vez
builder.WebHost.UseUrls("http://*:5000", "http://*:80");

builder.Services.AddControllersWithViews();

// 2. SOLUCIÓN A LA VELOCIDAD DE SQLITE: Conexión optimizada
var dbPath = Path.Combine(AppContext.BaseDirectory, "asistencia.db");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath};Cache=Shared"));

builder.Services.AddScoped<AttendanceService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

var app = builder.Build();

// 3. ACTIVAR MODO WAL EN SQLITE (Multi-lectura ultra rápida)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        context.Database.Migrate();

        // Modo WAL: Lecturas y escrituras concurrentes instantáneas
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
        context.Database.ExecuteSqlRaw("PRAGMA synchronous = NORMAL;");

        DbInitializer.Seed(context);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Ocurrió un error al migrar o sembrar la base de datos.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Kiosk}/{action=Index}/{id?}");

app.Run();
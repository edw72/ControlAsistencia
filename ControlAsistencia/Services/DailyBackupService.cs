using ControlAsistencia.Data;
using ControlAsistencia.Services;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

namespace ControlAsistencia.Services
{
    public class DailyBackupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DailyBackupService> _logger;
        private readonly IConfiguration _config;

        public DailyBackupService(IServiceProvider serviceProvider, ILogger<DailyBackupService> logger, IConfiguration config)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Servicio de Respaldo Diario Automático Iniciado.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var next11Pm = DateTime.Today.AddHours(23); // 23:00 = 11:00 PM

                // Si ya pasaron las 11:00 PM de hoy, programar para las 11:00 PM de mañana
                if (now >= next11Pm)
                {
                    next11Pm = next11Pm.AddDays(1);
                }

                var delay = next11Pm - now;
                _logger.LogInformation("Próximo respaldo programado para: {Fecha} (en {Horas} horas)", next11Pm, delay.TotalHours.ToString("F1"));

                // Esperar dormido hasta las 11:00 PM
                await Task.Delay(delay, stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                {
                    await PerformBackupAsync();
                }
            }
        }

        // MÉTODO ESTÁTICO COMPARTIDO PARA EJECUTAR EL BACKUP (Automático o Manual)
        public static async Task<(bool Success, string Message)> ExecuteBackupProcedureAsync(IServiceProvider serviceProvider, IConfiguration config)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();

            string backupDir = Path.Combine(AppContext.BaseDirectory, "Backups");
            if (!Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string tempDbPath = Path.Combine(backupDir, $"asistencia_temp_{timestamp}.db");
            string zipPath = Path.Combine(backupDir, $"Backup_Asistencia_{timestamp}.zip");

            try
            {
                // 1. Snapshot atómico y seguro de SQLite usando VACUUM INTO
                string normalizedPath = tempDbPath.Replace("\\", "/");
                await context.Database.ExecuteSqlRawAsync($"VACUUM INTO '{normalizedPath}';");

                // 2. Comprimir en archivo .zip
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(tempDbPath, "asistencia.db", CompressionLevel.Optimal);
                }

                byte[] zipBytes = await File.ReadAllBytesAsync(zipPath);

                // 3. Enviar por correo al remitente configurado en appsettings
                string recipientEmail = config["SmtpSettings:SenderEmail"] ?? "tu_correo@gmail.com";
                string zipFileName = $"Backup_Asistencia_{DateTime.Now:yyyyMMdd}.zip";

                var result = await emailService.SendDatabaseBackupEmailAsync(recipientEmail, zipBytes, zipFileName);
                return result;
            }
            catch (Exception ex)
            {
                return (false, $"Fallo en respaldo: {ex.Message}");
            }
            finally
            {
                // Limpieza de archivos temporales locales para no llenar el disco duro
                if (File.Exists(tempDbPath)) File.Delete(tempDbPath);
                if (File.Exists(zipPath)) File.Delete(zipPath);
            }
        }

        private async Task PerformBackupAsync()
        {
            _logger.LogInformation("Ejecutando respaldo automático de las 11:00 PM...");
            var result = await ExecuteBackupProcedureAsync(_serviceProvider, _config);
            if (result.Success)
            {
                _logger.LogInformation("Respaldo de las 11:00 PM enviado con éxito por correo.");
            }
            else
            {
                _logger.LogError("Error en respaldo de las 11:00 PM: {Mensaje}", result.Message);
            }
        }
    }
}

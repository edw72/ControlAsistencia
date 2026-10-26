using ControlAsistencia.Services;

namespace ControlAsistencia.Web.Services
{
    public class NightlySyncService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<NightlySyncService> _logger;

        public NightlySyncService(IServiceProvider serviceProvider, ILogger<NightlySyncService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Servicio de Sincronización Nocturna con AWS RDS Iniciado.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var next1130Pm = DateTime.Today.AddHours(23).AddMinutes(30); // 11:30 PM

                if (now >= next1130Pm)
                {
                    next1130Pm = next1130Pm.AddDays(1);
                }

                var delay = next1130Pm - now;
                _logger.LogInformation("Próxima sincronización con AWS programada para: {Fecha}", next1130Pm);

                await Task.Delay(delay, stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var syncService = scope.ServiceProvider.GetRequiredService<MySqlSyncService>();
                    var result = await syncService.SyncToAwsAsync();

                    if (result.Success)
                    {
                        _logger.LogInformation("Sincronización automática nocturna con AWS completada con éxito.");
                    }
                    else
                    {
                        _logger.LogError("Fallo en sincronización nocturna con AWS: {Mensaje}", result.Message);
                    }
                }
            }
        }
    }
}

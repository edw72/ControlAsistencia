using ControlAsistencia.Data;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ControlAsistencia.Services
{
    public class MySqlSyncService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private readonly ILogger<MySqlSyncService> _logger;

        public MySqlSyncService(AppDbContext context, IConfiguration config, ILogger<MySqlSyncService> logger)
        {
            _context = context;
            _config = config;
            _logger = logger;
        }

        private string GetConnectionString()
        {
            var sec = _config.GetSection("AwsMySqlSettings");
            var builder = new MySqlConnectionStringBuilder
            {
                Server = sec["Server"],
                Port = uint.Parse(sec["Port"] ?? "3306"),
                Database = sec["Database"],
                UserID = sec["Username"],
                Password = sec["Password"],
                SslMode = MySqlSslMode.Preferred,
                ConnectionTimeout = 20
            };
            return builder.ConnectionString;
        }

        // MÉTODO PRINCIPAL DE SINCRONIZACIÓN LOCAL -> NUBE AWS
        public async Task<(bool Success, string Message)> SyncToAwsAsync()
        {
            try
            {
                var connStr = GetConnectionString();
                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();

                await using var trans = await conn.BeginTransactionAsync();

                // 1. Desactivar temporalmente revisión de llaves foráneas para evitar bloqueos de orden
                await using (var cmd = new MySqlCommand("SET FOREIGN_KEY_CHECKS = 0;", conn, trans))
                {
                    await cmd.ExecuteNonQueryAsync();
                }

                // 2. SINCRONIZAR Timeclock_Users
                var users = await _context.Users.AsNoTracking().ToListAsync();
                foreach (var u in users)
                {
                    const string sql = @"
                        INSERT INTO Timeclock_Users (Id, Username, PasswordHash, FullName, Role)
                        VALUES (@Id, @Username, @PasswordHash, @FullName, @Role)
                        ON DUPLICATE KEY UPDATE 
                            Username = VALUES(Username), PasswordHash = VALUES(PasswordHash), 
                            FullName = VALUES(FullName), Role = VALUES(Role);";

                    await using var cmd = new MySqlCommand(sql, conn, trans);
                    cmd.Parameters.AddWithValue("@Id", u.Id);
                    cmd.Parameters.AddWithValue("@Username", u.Username);
                    cmd.Parameters.AddWithValue("@PasswordHash", u.PasswordHash);
                    cmd.Parameters.AddWithValue("@FullName", u.FullName);
                    cmd.Parameters.AddWithValue("@Role", u.Role);
                    await cmd.ExecuteNonQueryAsync();
                }

                // 3. SINCRONIZAR Timeclock_Holidays
                var holidays = await _context.Holidays.AsNoTracking().ToListAsync();
                foreach (var h in holidays)
                {
                    const string sql = @"
                        INSERT INTO Timeclock_Holidays (Id, Date, Name, CreatedAt)
                        VALUES (@Id, @Date, @Name, @CreatedAt)
                        ON DUPLICATE KEY UPDATE 
                            Date = VALUES(Date), Name = VALUES(Name);";

                    await using var cmd = new MySqlCommand(sql, conn, trans);
                    cmd.Parameters.AddWithValue("@Id", h.Id);
                    cmd.Parameters.AddWithValue("@Date", h.Date);
                    cmd.Parameters.AddWithValue("@Name", h.Name);
                    cmd.Parameters.AddWithValue("@CreatedAt", h.CreatedAt);
                    await cmd.ExecuteNonQueryAsync();
                }

                // 4. SINCRONIZAR Timeclock_Employees
                var employees = await _context.Employees.AsNoTracking().ToListAsync();
                foreach (var e in employees)
                {
                    const string sql = @"
                        INSERT INTO Timeclock_Employees 
                            (Id, CardCode, FullName, Identification, Email, Pin, MonthlySalary, BirthDate, HasUnifiedLunch, IsActive, CreatedAt)
                        VALUES 
                            (@Id, @CardCode, @FullName, @Identification, @Email, @Pin, @MonthlySalary, @BirthDate, @HasUnifiedLunch, @IsActive, @CreatedAt)
                        ON DUPLICATE KEY UPDATE 
                            CardCode = VALUES(CardCode), FullName = VALUES(FullName), Identification = VALUES(Identification),
                            Email = VALUES(Email), Pin = VALUES(Pin), MonthlySalary = VALUES(MonthlySalary),
                            BirthDate = VALUES(BirthDate), HasUnifiedLunch = VALUES(HasUnifiedLunch), IsActive = VALUES(IsActive);";

                    await using var cmd = new MySqlCommand(sql, conn, trans);
                    cmd.Parameters.AddWithValue("@Id", e.Id);
                    cmd.Parameters.AddWithValue("@CardCode", e.CardCode);
                    cmd.Parameters.AddWithValue("@FullName", e.FullName);
                    cmd.Parameters.AddWithValue("@Identification", e.Identification ?? "");
                    cmd.Parameters.AddWithValue("@Email", (object?)e.Email ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Pin", (object?)e.Pin ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@MonthlySalary", e.MonthlySalary);
                    cmd.Parameters.AddWithValue("@BirthDate", (object?)e.BirthDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@HasUnifiedLunch", e.HasUnifiedLunch ? 1 : 0);
                    cmd.Parameters.AddWithValue("@IsActive", e.IsActive ? 1 : 0);
                    cmd.Parameters.AddWithValue("@CreatedAt", e.CreatedAt);
                    await cmd.ExecuteNonQueryAsync();
                }

                // 5. SINCRONIZAR Timeclock_TimeRecords
                var records = await _context.TimeRecords.AsNoTracking().ToListAsync();
                foreach (var r in records)
                {
                    const string sql = @"
                        INSERT INTO Timeclock_TimeRecords (Id, EmployeeId, Type, TimestampUtc, Note)
                        VALUES (@Id, @EmployeeId, @Type, @TimestampUtc, @Note)
                        ON DUPLICATE KEY UPDATE 
                            Type = VALUES(Type), TimestampUtc = VALUES(TimestampUtc), Note = VALUES(Note);";

                    await using var cmd = new MySqlCommand(sql, conn, trans);
                    cmd.Parameters.AddWithValue("@Id", r.Id);
                    cmd.Parameters.AddWithValue("@EmployeeId", r.EmployeeId);
                    cmd.Parameters.AddWithValue("@Type", (int)r.Type);
                    cmd.Parameters.AddWithValue("@TimestampUtc", r.TimestampUtc);
                    cmd.Parameters.AddWithValue("@Note", (object?)r.Note ?? DBNull.Value);
                    await cmd.ExecuteNonQueryAsync();
                }

                // 6. SINCRONIZAR Timeclock_AttendanceIncidents
                var incidents = await _context.AttendanceIncidents.AsNoTracking().ToListAsync();
                foreach (var i in incidents)
                {
                    const string sql = @"
                        INSERT INTO Timeclock_AttendanceIncidents 
                            (Id, EmployeeId, Date, Category, Detail, LoggedBy, WaiveEarlyDeparture, CreatedAtUtc)
                        VALUES 
                            (@Id, @EmployeeId, @Date, @Category, @Detail, @LoggedBy, @WaiveEarlyDeparture, @CreatedAtUtc)
                        ON DUPLICATE KEY UPDATE 
                            Date = VALUES(Date), Category = VALUES(Category), Detail = VALUES(Detail),
                            LoggedBy = VALUES(LoggedBy), WaiveEarlyDeparture = VALUES(WaiveEarlyDeparture);";

                    await using var cmd = new MySqlCommand(sql, conn, trans);
                    cmd.Parameters.AddWithValue("@Id", i.Id);
                    cmd.Parameters.AddWithValue("@EmployeeId", i.EmployeeId);
                    cmd.Parameters.AddWithValue("@Date", i.Date);
                    cmd.Parameters.AddWithValue("@Category", i.Category);
                    cmd.Parameters.AddWithValue("@Detail", i.Detail);
                    cmd.Parameters.AddWithValue("@LoggedBy", i.LoggedBy);
                    cmd.Parameters.AddWithValue("@WaiveEarlyDeparture", i.WaiveEarlyDeparture ? 1 : 0);
                    cmd.Parameters.AddWithValue("@CreatedAtUtc", i.CreatedAtUtc);
                    await cmd.ExecuteNonQueryAsync();
                }

                // 7. Reactivar llaves foráneas y confirmar transacción
                await using (var cmd = new MySqlCommand("SET FOREIGN_KEY_CHECKS = 1;", conn, trans))
                {
                    await cmd.ExecuteNonQueryAsync();
                }

                await trans.CommitAsync();

                string summary = $"Sincronización exitosa con AWS: {employees.Count} empleados, {records.Count} marcas, {incidents.Count} novedades, {holidays.Count} feriados y {users.Count} usuarios.";
                _logger.LogInformation(summary);
                return (true, summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al sincronizar con AWS RDS");
                return (false, $"Error al conectar con AWS MySQL: {ex.Message}");
            }
        }
    }
}

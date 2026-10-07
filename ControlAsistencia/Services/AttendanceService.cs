using ClosedXML.Excel;
using ControlAsistencia.Data;
using ControlAsistencia.Models;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace ControlAsistencia.Services
{
    public class EmployeeStatusDto
    {
        public int EmployeeId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public RecordType? LastRecordType { get; set; }
        public DateTime? LastRecordTimeLocal { get; set; }
        public List<RecordType> AllowedActions { get; set; } = new();
    }

    public class PunchResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? WarningMessage { get; set; }
    }

    public class AttendanceService
    {
        private readonly AppDbContext _context;

        public const int MaxCoffeeMinutes = 15;
        public const int MaxLunchMinutes = 45;
        public const int EarlyArrivalGraceMinutes = 20;

        public AttendanceService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<EmployeeStatusDto?> GetStatusByCodeOrPinAsync(string inputCode)
        {
            inputCode = inputCode.Trim();

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.IsActive && (e.CardCode == inputCode || (!string.IsNullOrEmpty(e.Pin) && e.Pin == inputCode)));

            if (employee == null) return null;

            var recentRecords = await _context.TimeRecords
                .Where(t => t.EmployeeId == employee.Id)
                .OrderByDescending(t => t.TimestampUtc)
                .Take(25)
                .ToListAsync();

            var lastRecord = recentRecords.FirstOrDefault();

            // 1. Declarar la variable AQUÍ (AFUERA del return)
            bool appliesUnified = employee.HasUnifiedLunch && DateTime.Today.DayOfWeek != DayOfWeek.Saturday;

            // 2. Ahora sí retornar el objeto
            return new EmployeeStatusDto
            {
                EmployeeId = employee.Id,
                FullName = employee.FullName,
                LastRecordType = lastRecord?.Type,
                LastRecordTimeLocal = lastRecord?.TimestampUtc.ToLocalTime(),
                AllowedActions = CalculateAllowedActions(recentRecords, appliesUnified)
            };
        }

        // MÁQUINA DE ESTADOS (Detecta si tiene Pausa Unificada)
        private List<RecordType> CalculateAllowedActions(List<TimeRecord> records, bool hasUnifiedLunch)
        {
            var actions = new List<RecordType>();
            var lastRecord = records.FirstOrDefault();

            if (lastRecord == null || lastRecord.Type == RecordType.ClockOut)
            {
                actions.Add(RecordType.ClockIn);
                return actions;
            }

            if (lastRecord.Type == RecordType.PersonalStart)
            {
                actions.Add(RecordType.PersonalEnd);
                return actions;
            }

            if (lastRecord.Type == RecordType.CoffeeStart)
            {
                actions.Add(RecordType.CoffeeEnd);
                return actions;
            }

            if (lastRecord.Type == RecordType.LunchStart)
            {
                actions.Add(RecordType.LunchEnd);
                return actions;
            }

            var lastClockInIndex = records.FindIndex(r => r.Type == RecordType.ClockIn);
            var currentShiftRecords = lastClockInIndex >= 0 ? records.Take(lastClockInIndex + 1).ToList() : records;

            bool alreadyHadCoffee = currentShiftRecords.Any(r => r.Type == RecordType.CoffeeEnd);
            bool alreadyHadLunch = currentShiftRecords.Any(r => r.Type == RecordType.LunchEnd);

            if (hasUnifiedLunch)
            {
                // CASO ESPECIAL: OMITIR CAFÉ MATUTINO
                if (!alreadyHadLunch)
                {
                    actions.Add(RecordType.LunchStart); // Pasa directo a almuerzo
                }
            }
            else
            {
                // CASO ESTÁNDAR: SECUENCIA OBLIGATORIA
                if (!alreadyHadCoffee)
                {
                    actions.Add(RecordType.CoffeeStart);
                }
                else if (!alreadyHadLunch)
                {
                    actions.Add(RecordType.LunchStart);
                }
            }

            // Diligencias y Salida siempre disponibles mientras labora
            actions.Add(RecordType.PersonalStart);
            actions.Add(RecordType.ClockOut);

            return actions;
        }

        public async Task<PunchResult> RegisterPunchAsync(int employeeId, RecordType type)
        {
            var employee = await _context.Employees.FindAsync(employeeId);
            if (employee == null || !employee.IsActive)
                return new PunchResult { Success = false, Message = "Empleado inactivo o no encontrado" };

            var nowUtc = DateTime.UtcNow;
            var nowLocal = nowUtc.ToLocalTime();

            string? warning = null;
            string baseMessage = "";

            var recentRecords = await _context.TimeRecords
                .Where(t => t.EmployeeId == employeeId)
                .OrderByDescending(t => t.TimestampUtc)
                .Take(10)
                .ToListAsync();

            bool isSaturday = DateTime.Today.DayOfWeek == DayOfWeek.Saturday;
            int allowedLunch = (employee.HasUnifiedLunch && !isSaturday) ? 60 : MaxLunchMinutes;

            switch (type)
            {
                case RecordType.ClockIn:
                    baseMessage = "¡Bienvenido/a! Entrada registrada exitosamente.";
                    int lateMin = CalculateLateMinutes(nowLocal, nowLocal.DayOfWeek);
                    if (lateMin > 0)
                    {
                        warning = $"⚠️ ATENCIÓN: Registras una TARDÍA de {lateMin} {(lateMin == 1 ? "minuto" : "minutos")}.";
                    }
                    break;

                case RecordType.CoffeeEnd:
                    baseMessage = "¡Listo! Regreso de café registrado.";
                    var coffeeStart = recentRecords.FirstOrDefault(r => r.Type == RecordType.CoffeeStart);
                    if (coffeeStart != null)
                    {
                        int duration = (int)(nowUtc - coffeeStart.TimestampUtc).TotalMinutes;
                        if (duration > MaxCoffeeMinutes)
                        {
                            int excess = duration - MaxCoffeeMinutes;
                            warning = $"⚠️ CAFÉ EXCEDIDO: Te pasaste por {excess} min (Tomaste {duration} min de 15 permitidos).";
                        }
                    }
                    break;

                case RecordType.LunchEnd:
                    baseMessage = "¡Listo! Regreso de almuerzo registrado.";
                    var lunchStart = recentRecords.FirstOrDefault(r => r.Type == RecordType.LunchStart);
                    if (lunchStart != null)
                    {
                        int duration = (int)(nowUtc - lunchStart.TimestampUtc).TotalMinutes;
                        if (duration > allowedLunch)
                        {
                            int excess = duration - allowedLunch;
                            warning = $"⚠️ ALMUERZO EXCEDIDO: Te pasaste por {excess} min (Tomaste {duration} min de {allowedLunch} permitidos).";
                        }
                    }
                    break;

                case RecordType.PersonalEnd:
                    baseMessage = "¡Bienvenido de vuelta! Regreso de diligencia registrado.";
                    var personalStart = recentRecords.FirstOrDefault(r => r.Type == RecordType.PersonalStart);
                    if (personalStart != null)
                    {
                        int duration = (int)(nowUtc - personalStart.TimestampUtc).TotalMinutes;
                        warning = $"ℹ️ TIEMPO EN DILIGENCIA: {duration} min fuera (no remunerado).";
                    }
                    break;

                case RecordType.CoffeeStart:
                    baseMessage = "¡Buen provecho! Recuerda que tienes 15 minutos de café.";
                    break;

                case RecordType.LunchStart:
                    baseMessage = employee.HasUnifiedLunch
                        ? "¡Buen provecho! Cuentas con 60 minutos de almuerzo unificado."
                        : "¡Buen provecho! Recuerda que tienes 45 minutos de almuerzo.";
                    break;

                case RecordType.PersonalStart:
                    baseMessage = "¡Hasta pronto! Salida a diligencia personal registrada.";
                    break;

                case RecordType.ClockOut:
                    baseMessage = "¡Hasta luego! Jornada finalizada con éxito.";
                    break;
            }

            var newRecord = new TimeRecord
            {
                EmployeeId = employeeId,
                Type = type,
                TimestampUtc = nowUtc
            };

            _context.TimeRecords.Add(newRecord);
            await _context.SaveChangesAsync();

            return new PunchResult
            {
                Success = true,
                Message = baseMessage,
                WarningMessage = warning
            };
        }

        public int CalculateLateMinutes(DateTime localIn, DayOfWeek dayOfWeek)
        {
            TimeSpan officialStartTime;

            if (dayOfWeek >= DayOfWeek.Monday && dayOfWeek <= DayOfWeek.Friday)
                officialStartTime = new TimeSpan(8, 0, 0);
            else if (dayOfWeek == DayOfWeek.Saturday)
                officialStartTime = new TimeSpan(7, 0, 0);
            else
                return 0;

            var actualInTime = localIn.TimeOfDay;
            if (actualInTime > officialStartTime)
            {
                return (int)(actualInTime - officialStartTime).TotalMinutes;
            }

            return 0;
        }

        public int CalculateOvertimeMinutes(DateTime localIn, DateTime? localOut, DayOfWeek dayOfWeek)
        {
            int totalOvertime = 0;
            TimeSpan officialStartTime;
            TimeSpan officialEndTime;

            if (dayOfWeek >= DayOfWeek.Monday && dayOfWeek <= DayOfWeek.Friday)
            {
                officialStartTime = new TimeSpan(8, 0, 0);
                officialEndTime = new TimeSpan(17, 30, 0);
            }
            else if (dayOfWeek == DayOfWeek.Saturday)
            {
                officialStartTime = new TimeSpan(7, 0, 0);
                officialEndTime = new TimeSpan(12, 30, 0);
            }
            else
            {
                if (localOut.HasValue) return (int)(localOut.Value - localIn).TotalMinutes;
                return 0;
            }

            var actualInTime = localIn.TimeOfDay;
            if (actualInTime < officialStartTime)
            {
                var earlyDiffMinutes = (int)(officialStartTime - actualInTime).TotalMinutes;
                if (earlyDiffMinutes > EarlyArrivalGraceMinutes) totalOvertime += earlyDiffMinutes;
            }

            if (localOut.HasValue)
            {
                var actualOutTime = localOut.Value.TimeOfDay;
                if (actualOutTime > officialEndTime)
                {
                    totalOvertime += (int)(actualOutTime - officialEndTime).TotalMinutes;
                }
            }

            return totalOvertime;
        }

        // 1. DASHBOARD DIARIO
        public async Task<DashboardViewModel> GetDailyDashboardAsync(DateTime date)
        {
            var startOfDayLocal = date.Date;
            var endOfDayLocal = date.Date.AddDays(1).AddTicks(-1);

            var startUtc = startOfDayLocal.ToUniversalTime();
            var endUtc = endOfDayLocal.ToUniversalTime();

            var employees = await _context.Employees.Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync();
            var dayRecords = await _context.TimeRecords
                .Where(r => r.TimestampUtc >= startUtc && r.TimestampUtc <= endUtc)
                .OrderBy(r => r.TimestampUtc)
                .ToListAsync();

            // Leer las novedades o llamadas registradas en esta fecha
            var dayIncidents = await _context.AttendanceIncidents
                .Where(i => i.Date >= startOfDayLocal && i.Date <= endOfDayLocal)
                .ToListAsync();

            var vm = new DashboardViewModel
            {
                SelectedDate = date.Date,
                TotalEmployees = employees.Count
            };

            foreach (var emp in employees)
            {
                var empRecords = dayRecords.Where(r => r.EmployeeId == emp.Id).ToList();
                var detail = new EmployeeDailyDetailDto
                {
                    EmployeeId = emp.Id,
                    FullName = emp.FullName,
                    CardCode = emp.CardCode,
                    HasUnifiedLunch = emp.HasUnifiedLunch
                };
                // Asociar la novedad si existe para este empleado hoy
                var incident = dayIncidents.FirstOrDefault(i => i.EmployeeId == emp.Id);
                if (incident != null)
                {
                    detail.TodayIncidentCategory = incident.Category;
                    detail.TodayIncidentDetail = incident.Detail;
                }

                if (empRecords.Any())
                {
                    var firstIn = empRecords.FirstOrDefault(r => r.Type == RecordType.ClockIn);
                    var lastOut = empRecords.LastOrDefault(r => r.Type == RecordType.ClockOut);
                    var lastRecord = empRecords.Last();

                    if (firstIn != null) detail.ClockInTime = firstIn.TimestampUtc.ToLocalTime().ToString("hh:mm tt");
                    if (lastOut != null && lastRecord.Type == RecordType.ClockOut)
                        detail.ClockOutTime = lastOut.TimestampUtc.ToLocalTime().ToString("hh:mm tt");

                    switch (lastRecord.Type)
                    {
                        case RecordType.ClockIn:
                        case RecordType.CoffeeEnd:
                        case RecordType.LunchEnd:
                        case RecordType.PersonalEnd:
                            detail.CurrentStatus = "Laborando";
                            detail.StatusBadgeClass = "bg-success";
                            vm.WorkingCount++;
                            break;
                        case RecordType.CoffeeStart:
                            detail.CurrentStatus = "En Café";
                            detail.StatusBadgeClass = "bg-warning text-dark";
                            vm.CoffeeCount++;
                            break;
                        case RecordType.LunchStart:
                            detail.CurrentStatus = "En Almuerzo";
                            detail.StatusBadgeClass = "bg-info text-dark";
                            vm.LunchCount++;
                            break;
                        case RecordType.PersonalStart:
                            detail.CurrentStatus = "En Diligencia";
                            detail.StatusBadgeClass = "bg-dark text-white";
                            vm.PersonalCount++;
                            break;
                        case RecordType.ClockOut:
                            detail.CurrentStatus = "Jornada Finalizada";
                            detail.StatusBadgeClass = "bg-secondary";
                            vm.OutCount++;
                            break;
                    }

                    // Tiempos reales
                    int coffeeMin = CalculateDurationMinutes(empRecords, RecordType.CoffeeStart, RecordType.CoffeeEnd);
                    int lunchMin = CalculateDurationMinutes(empRecords, RecordType.LunchStart, RecordType.LunchEnd);
                    int personalMin = CalculateDurationMinutes(empRecords, RecordType.PersonalStart, RecordType.PersonalEnd);

                    detail.CoffeeMinutes = coffeeMin;
                    detail.LunchMinutes = lunchMin;
                    detail.PersonalErrandMinutes = personalMin;

                    // Límites según su perfil
                    int allowedCoffee = emp.HasUnifiedLunch ? 0 : MaxCoffeeMinutes;
                    int allowedLunch = emp.HasUnifiedLunch ? 60 : MaxLunchMinutes;

                    detail.ExcessCoffeeMinutes = Math.Max(0, coffeeMin - allowedCoffee);
                    detail.ExcessLunchMinutes = Math.Max(0, lunchMin - allowedLunch);

                    if (firstIn != null)
                    {
                        var localIn = firstIn.TimestampUtc.ToLocalTime();
                        DateTime? localOut = (lastOut != null && lastRecord.Type == RecordType.ClockOut) ? lastOut.TimestampUtc.ToLocalTime() : null;

                        detail.LateMinutes = CalculateLateMinutes(localIn, localIn.DayOfWeek);
                        detail.OvertimeMinutes = CalculateOvertimeMinutes(localIn, localOut, localIn.DayOfWeek);

                        DateTime endRef = (lastOut != null && lastRecord.Type == RecordType.ClockOut) ? lastOut.TimestampUtc : DateTime.UtcNow;
                        var totalSpan = endRef - firstIn.TimestampUtc;

                        // HORAS EFECTIVAS:
                        // L-V: Almuerzo y Café son no remunerados (se restan ambos de la jornada)
                        // Sábado: El café de 15m es pagado por la empresa (solo se resta exceso de café si lo hubiera)
                        int unpaidBreakMinutes = 0;
                        if (localIn.DayOfWeek == DayOfWeek.Saturday)
                        {
                            unpaidBreakMinutes = lunchMin + detail.ExcessCoffeeMinutes;
                        }
                        else
                        {
                            unpaidBreakMinutes = lunchMin + coffeeMin;
                        }

                        var netSpan = totalSpan - TimeSpan.FromMinutes(unpaidBreakMinutes) - TimeSpan.FromMinutes(personalMin);
                        if (netSpan < TimeSpan.Zero) netSpan = TimeSpan.Zero;
                        detail.NetWorkingHours = $"{(int)netSpan.TotalHours}h {netSpan.Minutes}m";
                    }
                }
                else
                {
                    detail.CurrentStatus = "Sin Ingreso";
                    detail.StatusBadgeClass = "bg-light text-muted border";
                    vm.OutCount++;
                }

                vm.Employees.Add(detail);
            }

            return vm;
        }

        // 2. REPORTE QUINCENAL
        public async Task<BiweeklyReportViewModel?> GetBiweeklyReportAsync(int employeeId, int year, int month, int quincena)
        {
            var employee = await _context.Employees.FindAsync(employeeId);
            if (employee == null) return null;

            DateTime startDate;
            DateTime endDate;

            if (quincena == 1)
            {
                startDate = new DateTime(year, month, 1);
                endDate = new DateTime(year, month, 15, 23, 59, 59);
            }
            else
            {
                int lastDayOfMonth = DateTime.DaysInMonth(year, month);
                startDate = new DateTime(year, month, 16);
                endDate = new DateTime(year, month, lastDayOfMonth, 23, 59, 59);
            }

            var startUtc = startDate.ToUniversalTime();
            var endUtc = endDate.ToUniversalTime();

            var records = await _context.TimeRecords
                .Where(r => r.EmployeeId == employeeId && r.TimestampUtc >= startUtc && r.TimestampUtc <= endUtc)
                .OrderBy(r => r.TimestampUtc)
                .ToListAsync();

            var report = new BiweeklyReportViewModel
            {
                EmployeeId = employee.Id,
                FullName = employee.FullName,
                CardCode = employee.CardCode,
                HasUnifiedLunch = employee.HasUnifiedLunch,
                StartDate = startDate,
                EndDate = endDate,
                PeriodTitle = $"{(quincena == 1 ? "1ra" : "2da")} Quincena - {startDate:MMMM yyyy}".ToUpper()
            };

            int allowedCoffee = employee.HasUnifiedLunch ? 0 : MaxCoffeeMinutes;
            int allowedLunch = employee.HasUnifiedLunch ? 60 : MaxLunchMinutes;

            for (var day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
            {
                var dayStartUtc = day.ToUniversalTime();
                var dayEndUtc = day.AddDays(1).AddTicks(-1).ToUniversalTime();

                var dayRecs = records.Where(r => r.TimestampUtc >= dayStartUtc && r.TimestampUtc <= dayEndUtc).ToList();
                if (!dayRecs.Any()) continue;

                var inPunch = dayRecs.FirstOrDefault(r => r.Type == RecordType.ClockIn);
                var outPunch = dayRecs.LastOrDefault(r => r.Type == RecordType.ClockOut);

                int coffee = CalculateDurationMinutes(dayRecs, RecordType.CoffeeStart, RecordType.CoffeeEnd);
                int lunch = CalculateDurationMinutes(dayRecs, RecordType.LunchStart, RecordType.LunchEnd);
                int personal = CalculateDurationMinutes(dayRecs, RecordType.PersonalStart, RecordType.PersonalEnd);

                int excessCoffee = Math.Max(0, coffee - allowedCoffee);
                int excessLunch = Math.Max(0, lunch - allowedLunch);

                int lateMin = 0;
                int dayOvertime = 0;
                string effHours = "0h 0m";

                if (inPunch != null)
                {
                    var localIn = inPunch.TimestampUtc.ToLocalTime();
                    DateTime? localOut = outPunch?.TimestampUtc.ToLocalTime();

                    lateMin = CalculateLateMinutes(localIn, localIn.DayOfWeek);
                    dayOvertime = CalculateOvertimeMinutes(localIn, localOut, localIn.DayOfWeek);

                    if (outPunch != null)
                    {
                        int unpaidBreakMinutes = (localIn.DayOfWeek == DayOfWeek.Saturday)
                            ? (lunch + excessCoffee)
                            : (lunch + coffee);

                        var net = (outPunch.TimestampUtc - inPunch.TimestampUtc)
                                  - TimeSpan.FromMinutes(unpaidBreakMinutes)
                                  - TimeSpan.FromMinutes(personal);

                        if (net < TimeSpan.Zero) net = TimeSpan.Zero;
                        effHours = $"{(int)net.TotalHours}h {net.Minutes}m";
                    }
                }

                int totalDayDeduction = lateMin + excessCoffee + excessLunch + personal;

                report.Days.Add(new DailyReportRow
                {
                    Date = day,
                    ClockIn = inPunch?.TimestampUtc.ToLocalTime().ToString("hh:mm tt") ?? "--:--",
                    ClockOut = outPunch?.TimestampUtc.ToLocalTime().ToString("hh:mm tt") ?? "--:--",
                    LateMinutes = lateMin,
                    CoffeeMinutes = coffee,
                    LunchMinutes = lunch,
                    PersonalErrandMinutes = personal,
                    ExcessMinutes = totalDayDeduction,
                    OvertimeMinutes = dayOvertime,
                    EffectiveHours = effHours
                });

                report.TotalLateMinutes += lateMin;
                report.TotalExcessCoffeeMinutes += excessCoffee;
                report.TotalExcessLunchMinutes += excessLunch;
                report.TotalPersonalErrandMinutes += personal;
                report.GrandTotalOvertimeMinutes += dayOvertime;
            }

            return report;
        }

        private int CalculateDurationMinutes(List<TimeRecord> records, RecordType startType, RecordType endType)
        {
            TimeSpan duration = TimeSpan.Zero;
            TimeRecord? start = null;

            foreach (var r in records)
            {
                if (r.Type == startType) start = r;
                else if (r.Type == endType && start != null)
                {
                    duration += (r.TimestampUtc - start.TimestampUtc);
                    start = null;
                }
            }
            if (start != null)
            {
                duration += (DateTime.UtcNow - start.TimestampUtc);
            }

            return (int)duration.TotalMinutes;
        }

        // GENERADOR DE EXCEL
        public byte[] GenerateBiweeklyExcel(BiweeklyReportViewModel report)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Liquidacion");

            ws.Cell("A1").Value = "REPORTE DE ASISTENCIA Y LIQUIDACIÓN DE TIEMPOS";
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;
            ws.Range("A1:J1").Merge();

            string lunchPolicy = report.HasUnifiedLunch ? "Almuerzo Unificado: 60m (Sin café)" : "Café: 15m | Almuerzo: 45m";
            ws.Cell("A2").Value = $"Colaborador: {report.FullName} (ID: {report.CardCode}) - Política: {lunchPolicy}";
            ws.Cell("A2").Style.Font.Bold = true;
            ws.Range("A2:E2").Merge();

            ws.Cell("F2").Value = $"Periodo: {report.PeriodTitle} ({report.StartDate:dd/MM/yyyy} al {report.EndDate:dd/MM/yyyy})";
            ws.Cell("F2").Style.Font.Bold = true;
            ws.Range("F2:J2").Merge();

            string[] headers = {
                "Fecha", "Entrada", "Tardanza (min)", "Salida", "Café (min)",
                $"Almuerzo ({report.AllowedLunchMinutes}m)", "Diligencias (min)", "Total Descuento (min)",
                "Horas Extras (min)", "Horas Efectivas"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0D6EFD");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int row = 5;
            foreach (var d in report.Days)
            {
                ws.Cell(row, 1).Value = d.Date.ToString("ddd dd/MM/yyyy");
                ws.Cell(row, 2).Value = d.ClockIn;
                ws.Cell(row, 3).Value = d.LateMinutes;
                ws.Cell(row, 4).Value = d.ClockOut;
                ws.Cell(row, 5).Value = d.CoffeeMinutes;
                ws.Cell(row, 6).Value = d.LunchMinutes;
                ws.Cell(row, 7).Value = d.PersonalErrandMinutes;
                ws.Cell(row, 8).Value = d.ExcessMinutes;
                ws.Cell(row, 9).Value = d.OvertimeMinutes;
                ws.Cell(row, 10).Value = d.EffectiveHours;

                ws.Range(row, 1, row, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                if (d.ExcessMinutes > 0)
                {
                    ws.Cell(row, 8).Style.Font.FontColor = XLColor.Red;
                    ws.Cell(row, 8).Style.Font.Bold = true;
                }

                if (d.OvertimeMinutes > 0)
                {
                    ws.Cell(row, 9).Style.Font.FontColor = XLColor.Green;
                    ws.Cell(row, 9).Style.Font.Bold = true;
                }

                row++;
            }

            ws.Range(4, 1, row - 1, 10).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(4, 1, row - 1, 10).Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            row++;
            ws.Cell(row, 1).Value = "DESGLOSE DEDUCCIONES:";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = $"Tardanzas: {report.TotalLateMinutes}m | Café excedido: {report.TotalExcessCoffeeMinutes}m | Almuerzo excedido: {report.TotalExcessLunchMinutes}m | Diligencias: {report.TotalPersonalErrandMinutes}m";
            ws.Range(row, 2, row, 10).Merge();

            row++;
            ws.Cell(row, 1).Value = "TOTAL A DEDUCIR EN NÓMINA:";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = report.FormattedTotalExcess;
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Font.FontColor = XLColor.Red;
            ws.Range(row, 2, row, 5).Merge();

            ws.Cell(row, 6).Value = "TOTAL HORAS EXTRAS A PAGAR:";
            ws.Cell(row, 6).Style.Font.Bold = true;
            ws.Cell(row, 7).Value = report.FormattedTotalOvertime;
            ws.Cell(row, 7).Style.Font.Bold = true;
            ws.Cell(row, 7).Style.Font.FontColor = XLColor.Green;
            ws.Range(row, 7, row, 10).Merge();

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}
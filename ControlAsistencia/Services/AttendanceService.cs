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
        public const int EarlyArrivalGraceMinutes = 10;

        public AttendanceService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<bool> IsHolidayAsync(DateTime date)
        {
            return await _context.Holidays.AnyAsync(h => h.Date.Date == date.Date);
        }

        // VERIFICACIÓN INTELIGENTE DE DÍA DE CUMPLEAÑOS
        public bool CheckIfBirthdayApplies(DateTime? birthDate, DateTime checkDate)
        {
            if (!birthDate.HasValue) return false;

            var bDay = birthDate.Value;

            // Si el cumpleaños cae en Domingo, el día libre se traslada al LUNES
            var birthdayThisYear = new DateTime(checkDate.Year, bDay.Month, bDay.Day);
            if (birthdayThisYear.DayOfWeek == DayOfWeek.Sunday)
            {
                var grantedMonday = birthdayThisYear.AddDays(1);
                return checkDate.Date == grantedMonday.Date;
            }

            // Si cae de lunes a sábado, se otorga en la fecha exacta
            return checkDate.Month == bDay.Month && checkDate.Day == bDay.Day;
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
            var today = DateTime.Today;
            bool isHoliday = await IsHolidayAsync(today);

            bool isWeekdayNormal = today.DayOfWeek >= DayOfWeek.Monday && today.DayOfWeek <= DayOfWeek.Friday && !isHoliday;
            bool appliesUnified = employee.HasUnifiedLunch && isWeekdayNormal;

            return new EmployeeStatusDto
            {
                EmployeeId = employee.Id,
                FullName = employee.FullName,
                LastRecordType = lastRecord?.Type,
                LastRecordTimeLocal = lastRecord?.TimestampUtc.ToLocalTime(),
                AllowedActions = CalculateAllowedActions(recentRecords, appliesUnified)
            };
        }

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
                if (!alreadyHadLunch) actions.Add(RecordType.LunchStart);
            }
            else
            {
                if (!alreadyHadCoffee) actions.Add(RecordType.CoffeeStart);
                else if (!alreadyHadLunch) actions.Add(RecordType.LunchStart);
            }

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
            bool isHoliday = await IsHolidayAsync(nowLocal.Date);
            bool isSpecialDay = nowLocal.DayOfWeek == DayOfWeek.Sunday || isHoliday;

            string? warning = null;
            string baseMessage = "";

            var recentRecords = await _context.TimeRecords
                .Where(t => t.EmployeeId == employeeId)
                .OrderByDescending(t => t.TimestampUtc)
                .Take(10)
                .ToListAsync();

            bool isWeekdayNormal = nowLocal.DayOfWeek >= DayOfWeek.Monday && nowLocal.DayOfWeek <= DayOfWeek.Friday && !isHoliday;
            int allowedLunch = (employee.HasUnifiedLunch && isWeekdayNormal) ? 60 : MaxLunchMinutes;

            switch (type)
            {
                case RecordType.ClockIn:
                    baseMessage = isSpecialDay
                        ? "¡Bienvenido/a! Entrada registrada (Jornada Extraordinaria 100%)."
                        : "¡Bienvenido/a! Entrada registrada exitosamente.";

                    if (!isSpecialDay)
                    {
                        int lateMin = CalculateLateMinutes(nowLocal, nowLocal.DayOfWeek);
                        if (lateMin > 0)
                        {
                            warning = $"⚠️ ATENCIÓN: Registras una TARDÍA de {lateMin} {(lateMin == 1 ? "minuto" : "minutos")}.";
                        }
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
                    baseMessage = (employee.HasUnifiedLunch && isWeekdayNormal)
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

        // 1. DASHBOARD DIARIO
        public async Task<DashboardViewModel> GetDailyDashboardAsync(DateTime date)
        {
            var startOfDayLocal = date.Date;
            var endOfDayLocal = date.Date.AddDays(1).AddTicks(-1);

            var startUtc = startOfDayLocal.ToUniversalTime();
            var endUtc = endOfDayLocal.ToUniversalTime();

            bool isHoliday = await IsHolidayAsync(date.Date);
            bool isSpecialDay = date.DayOfWeek == DayOfWeek.Sunday || isHoliday;

            var employees = await _context.Employees.Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync();
            var dayRecords = await _context.TimeRecords
                .Where(r => r.TimestampUtc >= startUtc && r.TimestampUtc <= endUtc)
                .OrderBy(r => r.TimestampUtc)
                .ToListAsync();

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
                var empRecs = dayRecords.Where(r => r.EmployeeId == emp.Id).ToList();
                var incident = dayIncidents.FirstOrDefault(i => i.EmployeeId == emp.Id);

                bool isBirthday = CheckIfBirthdayApplies(emp.BirthDate, date.Date);

                bool isWeekdayNormal = date.DayOfWeek >= DayOfWeek.Monday && date.DayOfWeek <= DayOfWeek.Friday && !isHoliday;
                bool hasUnifiedToday = emp.HasUnifiedLunch && isWeekdayNormal;

                var detail = new EmployeeDailyDetailDto
                {
                    EmployeeId = emp.Id,
                    FullName = emp.FullName,
                    CardCode = emp.CardCode,
                    HasUnifiedLunch = hasUnifiedToday,
                    IsBirthdayDayOff = isBirthday
                };

                if (incident != null)
                {
                    detail.TodayIncidentCategory = incident.Category;
                    detail.TodayIncidentDetail = incident.Detail;
                }

                if (empRecs.Any())
                {
                    var firstIn = empRecs.FirstOrDefault(r => r.Type == RecordType.ClockIn);
                    var lastOut = empRecs.LastOrDefault(r => r.Type == RecordType.ClockOut);
                    var lastRecord = empRecs.Last();

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

                    int coffeeMin = CalculateDurationMinutes(empRecs, RecordType.CoffeeStart, RecordType.CoffeeEnd);
                    int lunchMin = CalculateDurationMinutes(empRecs, RecordType.LunchStart, RecordType.LunchEnd);
                    int personalMin = CalculateDurationMinutes(empRecs, RecordType.PersonalStart, RecordType.PersonalEnd);

                    detail.CoffeeMinutes = coffeeMin;
                    detail.LunchMinutes = lunchMin;
                    detail.PersonalErrandMinutes = personalMin;

                    int allowedCoffee = hasUnifiedToday ? 0 : MaxCoffeeMinutes;
                    int allowedLunch = hasUnifiedToday ? 60 : MaxLunchMinutes;

                    detail.ExcessCoffeeMinutes = Math.Max(0, coffeeMin - allowedCoffee);
                    detail.ExcessLunchMinutes = Math.Max(0, lunchMin - allowedLunch);

                    if (firstIn != null)
                    {
                        var localIn = firstIn.TimestampUtc.ToLocalTime();
                        DateTime? localOut = (lastOut != null && lastRecord.Type == RecordType.ClockOut) ? lastOut.TimestampUtc.ToLocalTime() : null;

                        DateTime endRef = (lastOut != null && lastRecord.Type == RecordType.ClockOut) ? lastOut.TimestampUtc : DateTime.UtcNow;
                        var totalSpan = endRef - firstIn.TimestampUtc;

                        // EN DOMINGOS O FERIADOS:
                        if (isSpecialDay)
                        {
                            detail.LateMinutes = 0;
                            detail.EarlyDepartureMinutes = 0;

                            int unpaidBreaks = detail.ExcessCoffeeMinutes + detail.ExcessLunchMinutes + personalMin;
                            int totalExtraMin = (int)Math.Max(0, totalSpan.TotalMinutes - unpaidBreaks);

                            detail.HolidayOvertimeMinutes = totalExtraMin; // 100% EXTRAS DE FERIADO/DOMINGO
                            detail.RegularOvertimeMinutes = 0;
                            detail.NetWorkingHours = $"{(totalExtraMin / 60)}h {(totalExtraMin % 60)}m (100% Extra)";
                        }
                        // DÍAS ORDINARIOS:
                        else
                        {
                            detail.LateMinutes = CalculateLateMinutes(localIn, localIn.DayOfWeek);

                            int overtime = 0;
                            var officialEndTime = (localIn.DayOfWeek == DayOfWeek.Saturday) ? new TimeSpan(12, 30, 0) : new TimeSpan(17, 30, 0);

                            var officialStartTime = (localIn.DayOfWeek == DayOfWeek.Saturday) ? new TimeSpan(7, 0, 0) : new TimeSpan(8, 0, 0);
                            if (localIn.TimeOfDay < officialStartTime)
                            {
                                int earlyMin = (int)(officialStartTime - localIn.TimeOfDay).TotalMinutes;
                                if (earlyMin > EarlyArrivalGraceMinutes) overtime += earlyMin;
                            }
                            if (localOut.HasValue && localOut.Value.TimeOfDay > officialEndTime)
                            {
                                overtime += (int)(localOut.Value.TimeOfDay - officialEndTime).TotalMinutes;
                            }

                            detail.RegularOvertimeMinutes = overtime; // EXTRAS ORDINARIAS
                            detail.HolidayOvertimeMinutes = 0;

                            // Salida anticipada justa
                            if (lastOut != null && lastRecord.Type == RecordType.ClockOut)
                            {
                                var actualOutTime = localOut!.Value.TimeOfDay;
                                if (actualOutTime < officialEndTime)
                                {
                                    int remainingClockMinutes = (int)(officialEndTime - actualOutTime).TotalMinutes;

                                    if (localIn.DayOfWeek != DayOfWeek.Saturday)
                                    {
                                        if (lunchMin == 0) remainingClockMinutes -= (hasUnifiedToday ? 60 : MaxLunchMinutes);
                                        if (coffeeMin == 0 && !hasUnifiedToday) remainingClockMinutes -= MaxCoffeeMinutes;
                                    }

                                    detail.EarlyDepartureMinutes = Math.Max(0, remainingClockMinutes);
                                }
                                else
                                {
                                    detail.EarlyDepartureMinutes = 0;
                                }

                                if (incident != null && incident.WaiveEarlyDeparture)
                                {
                                    detail.IsEarlyDepartureWaived = true;
                                    detail.EarlyDepartureReason = incident.Detail;
                                }
                            }

                            int unpaidBreaks = (localIn.DayOfWeek == DayOfWeek.Saturday)
                                ? (detail.ExcessCoffeeMinutes + detail.ExcessLunchMinutes)
                                : (lunchMin + coffeeMin);

                            var netSpan = totalSpan - TimeSpan.FromMinutes(unpaidBreaks) - TimeSpan.FromMinutes(personalMin);
                            if (netSpan < TimeSpan.Zero) netSpan = TimeSpan.Zero;
                            detail.NetWorkingHours = $"{(int)netSpan.TotalHours}h {netSpan.Minutes}m";
                        }
                    }
                }
                else
                {
                    // Si no vino: ¿es su cumpleaños pagado?
                    if (isBirthday)
                    {
                        detail.CurrentStatus = "🎂 Libre por Cumpleaños";
                        detail.StatusBadgeClass = "bg-primary-subtle text-primary border border-primary-subtle";
                        detail.NetWorkingHours = (date.DayOfWeek == DayOfWeek.Saturday) ? "5h 30m (Día pagado)" : "8h 30m (Día pagado)";
                    }
                    else if (isSpecialDay)
                    {
                        detail.CurrentStatus = "Día Libre / Feriado";
                        detail.StatusBadgeClass = "bg-light text-muted border";
                    }
                    else
                    {
                        detail.CurrentStatus = "Sin Ingreso";
                        detail.StatusBadgeClass = "bg-light text-muted border";
                    }

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

            var holidays = await _context.Holidays
                .Where(h => h.Date >= startDate.Date && h.Date <= endDate.Date)
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

            for (var day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
            {
                var dayStartUtc = day.ToUniversalTime();
                var dayEndUtc = day.AddDays(1).AddTicks(-1).ToUniversalTime();

                var dayRecs = records.Where(r => r.TimestampUtc >= dayStartUtc && r.TimestampUtc <= dayEndUtc).ToList();
                bool isBirthday = CheckIfBirthdayApplies(employee.BirthDate, day.Date);

                // Si no vino, pero es su día de cumpleaños:
                if (!dayRecs.Any())
                {
                    if (isBirthday)
                    {
                        string normalPaid = (day.DayOfWeek == DayOfWeek.Saturday) ? "5h 30m" : "8h 30m";
                        report.Days.Add(new DailyReportRow
                        {
                            Date = day,
                            ClockIn = "🎂 CUMPLEAÑOS",
                            ClockOut = "DÍA PAGADO",
                            IsBirthdayDayOff = true,
                            EffectiveHours = $"{normalPaid} (Beneficio)"
                        });
                    }
                    continue;
                }

                bool isHoliday = holidays.Any(h => h.Date.Date == day.Date);
                bool isSpecialDay = day.DayOfWeek == DayOfWeek.Sunday || isHoliday;
                bool isWeekdayNormal = day.DayOfWeek >= DayOfWeek.Monday && day.DayOfWeek <= DayOfWeek.Friday && !isHoliday;
                bool hasUnifiedToday = employee.HasUnifiedLunch && isWeekdayNormal;

                var inPunch = dayRecs.FirstOrDefault(r => r.Type == RecordType.ClockIn);
                var outPunch = dayRecs.LastOrDefault(r => r.Type == RecordType.ClockOut);

                int coffee = CalculateDurationMinutes(dayRecs, RecordType.CoffeeStart, RecordType.CoffeeEnd);
                int lunch = CalculateDurationMinutes(dayRecs, RecordType.LunchStart, RecordType.LunchEnd);
                int personal = CalculateDurationMinutes(dayRecs, RecordType.PersonalStart, RecordType.PersonalEnd);

                int allowedCoffee = hasUnifiedToday ? 0 : MaxCoffeeMinutes;
                int allowedLunch = hasUnifiedToday ? 60 : MaxLunchMinutes;

                int excessCoffee = Math.Max(0, coffee - allowedCoffee);
                int excessLunch = Math.Max(0, lunch - allowedLunch);

                int lateMin = 0;
                int earlyDepMin = 0;
                bool isWaived = false;
                int regOvertime = 0;
                int holOvertime = 0;
                string effHours = "0h 0m";

                if (inPunch != null && outPunch != null)
                {
                    var localIn = inPunch.TimestampUtc.ToLocalTime();
                    var localOut = outPunch.TimestampUtc.ToLocalTime();
                    var totalDaySpan = outPunch.TimestampUtc - inPunch.TimestampUtc;

                    if (isSpecialDay)
                    {
                        lateMin = 0;
                        earlyDepMin = 0;
                        int unpaidExcess = excessCoffee + excessLunch + personal;
                        holOvertime = (int)Math.Max(0, totalDaySpan.TotalMinutes - unpaidExcess);
                        effHours = $"{(holOvertime / 60)}h {(holOvertime % 60)}m (100% Extra)";
                    }
                    else
                    {
                        lateMin = CalculateLateMinutes(localIn, localIn.DayOfWeek);

                        var officialStart = (localIn.DayOfWeek == DayOfWeek.Saturday) ? new TimeSpan(7, 0, 0) : new TimeSpan(8, 0, 0);
                        var officialEnd = (day.DayOfWeek == DayOfWeek.Saturday) ? new TimeSpan(12, 30, 0) : new TimeSpan(17, 30, 0);

                        if (localIn.TimeOfDay < officialStart)
                        {
                            int earlyM = (int)(officialStart - localIn.TimeOfDay).TotalMinutes;
                            if (earlyM > EarlyArrivalGraceMinutes) regOvertime += earlyM;
                        }
                        if (localOut.TimeOfDay > officialEnd)
                        {
                            regOvertime += (int)(localOut.TimeOfDay - officialEnd).TotalMinutes;
                        }

                        if (localOut.TimeOfDay < officialEnd)
                        {
                            int remClock = (int)(officialEnd - localOut.TimeOfDay).TotalMinutes;
                            if (day.DayOfWeek != DayOfWeek.Saturday)
                            {
                                if (lunch == 0) remClock -= (hasUnifiedToday ? 60 : MaxLunchMinutes);
                                if (coffee == 0 && !hasUnifiedToday) remClock -= MaxCoffeeMinutes;
                            }
                            earlyDepMin = Math.Max(0, remClock);
                        }

                        var dayInc = await _context.AttendanceIncidents
                            .FirstOrDefaultAsync(i => i.EmployeeId == employeeId && i.Date == day.Date && i.WaiveEarlyDeparture);
                        if (dayInc != null) isWaived = true;

                        int unpaidBreaks = (day.DayOfWeek == DayOfWeek.Saturday)
                            ? (excessCoffee + excessLunch)
                            : (lunch + coffee);

                        var net = totalDaySpan - TimeSpan.FromMinutes(unpaidBreaks) - TimeSpan.FromMinutes(personal);
                        if (net < TimeSpan.Zero) net = TimeSpan.Zero;
                        effHours = $"{(int)net.TotalHours}h {net.Minutes}m";
                    }
                }

                int totalDayDeduction = lateMin + (isWaived ? 0 : earlyDepMin) + excessCoffee + excessLunch + personal;

                report.Days.Add(new DailyReportRow
                {
                    Date = day,
                    ClockIn = inPunch?.TimestampUtc.ToLocalTime().ToString("hh:mm tt") ?? "--:--",
                    ClockOut = outPunch?.TimestampUtc.ToLocalTime().ToString("hh:mm tt") ?? "--:--",
                    LateMinutes = lateMin,
                    EarlyDepartureMinutes = earlyDepMin,
                    IsEarlyDepartureWaived = isWaived,
                    CoffeeMinutes = coffee,
                    LunchMinutes = lunch,
                    PersonalErrandMinutes = personal,
                    ExcessMinutes = totalDayDeduction,
                    RegularOvertimeMinutes = regOvertime,
                    HolidayOvertimeMinutes = holOvertime,
                    EffectiveHours = effHours
                });

                report.TotalLateMinutes += lateMin;
                report.TotalEarlyDepartureMinutes += (isWaived ? 0 : earlyDepMin);
                report.TotalExcessCoffeeMinutes += excessCoffee;
                report.TotalExcessLunchMinutes += excessLunch;
                report.TotalPersonalErrandMinutes += personal;

                report.TotalRegularOvertimeMinutes += regOvertime;
                report.TotalHolidayOvertimeMinutes += holOvertime;
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

        // GENERADOR DE EXCEL CON COLUMNAS SEPARADAS
        public byte[] GenerateBiweeklyExcel(BiweeklyReportViewModel report)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Liquidacion");

            ws.Cell("A1").Value = "REPORTE DE ASISTENCIA Y LIQUIDACIÓN DE TIEMPOS";
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;
            ws.Range("A1:L1").Merge();

            ws.Cell("A2").Value = $"Colaborador: {report.FullName} (ID: {report.CardCode})";
            ws.Cell("A2").Style.Font.Bold = true;
            ws.Range("A2:E2").Merge();

            ws.Cell("F2").Value = $"Periodo: {report.PeriodTitle} ({report.StartDate:dd/MM/yyyy} al {report.EndDate:dd/MM/yyyy})";
            ws.Cell("F2").Style.Font.Bold = true;
            ws.Range("F2:L2").Merge();

            string[] headers = {
                "Fecha", "Entrada", "Tardanza (min)", "Salida", "Salida Antic.", "Café (min)",
                "Almuerzo (min)", "Diligencias (min)", "Total Descuento (min)",
                "Extras Ord. (min)", "Extras Dom/Fer (min)", "Horas Efectivas"
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
                ws.Cell(row, 5).Value = d.IsEarlyDepartureWaived ? "0 (Con goce)" : d.EarlyDepartureMinutes.ToString();
                ws.Cell(row, 6).Value = d.CoffeeMinutes;
                ws.Cell(row, 7).Value = d.LunchMinutes;
                ws.Cell(row, 8).Value = d.PersonalErrandMinutes;
                ws.Cell(row, 9).Value = d.ExcessMinutes;
                ws.Cell(row, 10).Value = d.RegularOvertimeMinutes;
                ws.Cell(row, 11).Value = d.HolidayOvertimeMinutes;
                ws.Cell(row, 12).Value = d.EffectiveHours;

                ws.Range(row, 1, row, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                if (d.ExcessMinutes > 0)
                {
                    ws.Cell(row, 9).Style.Font.FontColor = XLColor.Red;
                    ws.Cell(row, 9).Style.Font.Bold = true;
                }
                if (d.RegularOvertimeMinutes > 0)
                {
                    ws.Cell(row, 10).Style.Font.FontColor = XLColor.Green;
                    ws.Cell(row, 10).Style.Font.Bold = true;
                }
                if (d.HolidayOvertimeMinutes > 0)
                {
                    ws.Cell(row, 11).Style.Font.FontColor = XLColor.Purple;
                    ws.Cell(row, 11).Style.Font.Bold = true;
                }

                row++;
            }

            ws.Range(4, 1, row - 1, 12).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(4, 1, row - 1, 12).Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            row++;
            ws.Cell(row, 1).Value = "DESGLOSE DEDUCCIONES:";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = $"Tardanzas: {report.TotalLateMinutes}m | Salidas anticipadas: {report.TotalEarlyDepartureMinutes}m | Café excedido: {report.TotalExcessCoffeeMinutes}m | Almuerzo excedido: {report.TotalExcessLunchMinutes}m | Diligencias: {report.TotalPersonalErrandMinutes}m";
            ws.Range(row, 2, row, 12).Merge();

            row++;
            ws.Cell(row, 1).Value = "TOTAL A DEDUCIR EN NÓMINA:";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = report.FormattedTotalExcess;
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Font.FontColor = XLColor.Red;
            ws.Range(row, 2, row, 5).Merge();

            ws.Cell(row, 6).Value = "RESUMEN EXTRAS:";
            ws.Cell(row, 6).Style.Font.Bold = true;
            ws.Cell(row, 7).Value = $"Ordinarias: {report.FormattedRegularOvertime} | Dom/Feriados (100%): {report.FormattedHolidayOvertime}";
            ws.Range(row, 7, row, 12).Merge();

            row++;
            ws.Cell(row, 6).Value = "TOTAL HORAS EXTRAS A PAGAR:";
            ws.Cell(row, 6).Style.Font.Bold = true;
            ws.Cell(row, 7).Value = report.FormattedTotalOvertime;
            ws.Cell(row, 7).Style.Font.Bold = true;
            ws.Cell(row, 7).Style.Font.FontColor = XLColor.Green;
            ws.Range(row, 7, row, 12).Merge();

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}
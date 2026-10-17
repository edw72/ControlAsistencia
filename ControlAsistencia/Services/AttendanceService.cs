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

        public bool CheckIfBirthdayApplies(DateTime? birthDate, DateTime checkDate)
        {
            if (!birthDate.HasValue) return false;

            var bDay = birthDate.Value;
            int month = bDay.Month;
            int day = bDay.Day;

            if (month == 2 && day == 29 && !DateTime.IsLeapYear(checkDate.Year))
            {
                day = 28;
            }

            DateTime birthdayThisYear;
            try
            {
                birthdayThisYear = new DateTime(checkDate.Year, month, day);
            }
            catch
            {
                return false;
            }

            if (birthdayThisYear.DayOfWeek == DayOfWeek.Sunday)
            {
                var grantedMonday = birthdayThisYear.AddDays(1);
                return checkDate.Date == grantedMonday.Date;
            }

            return checkDate.Date == birthdayThisYear.Date;
        }

        // SEGMENTACIÓN EXACTA DE EXTRAS DIURNAS VS NOCTURNAS (CORTE A LAS 7:00 PM)
        public static (int DayMinutes, int NightMinutes) SplitOvertimeInterval(DateTime start, DateTime end)
        {
            if (end <= start) return (0, 0);

            int dayMin = 0;
            int nightMin = 0;

            // Recorrer minuto a minuto el intervalo de horas extras
            for (var t = start; t < end; t = t.AddMinutes(1))
            {
                var tod = t.TimeOfDay;
                // Franja Diurna: 05:00 AM a 07:00 PM (19:00)
                if (tod >= new TimeSpan(5, 0, 0) && tod < new TimeSpan(19, 0, 0))
                {
                    dayMin++;
                }
                else
                {
                    nightMin++;
                }
            }

            return (dayMin, nightMin);
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
                    Identification = emp.Identification,
                    MonthlySalary = emp.MonthlySalary,
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

                            detail.HolidayOvertimeMinutes = totalExtraMin;
                            detail.DayOvertimeMinutes = 0;
                            detail.NightOvertimeMinutes = 0;
                            detail.NetWorkingHours = $"{(totalExtraMin / 60)}h {(totalExtraMin % 60)}m (100% Extra)";
                        }
                        // DÍAS ORDINARIOS:
                        else
                        {
                            detail.LateMinutes = CalculateLateMinutes(localIn, localIn.DayOfWeek);

                            int totalDayOt = 0;
                            int totalNightOt = 0;

                            var officialEndTime = (localIn.DayOfWeek == DayOfWeek.Saturday) ? new TimeSpan(12, 30, 0) : new TimeSpan(17, 30, 0);
                            var officialStartTime = (localIn.DayOfWeek == DayOfWeek.Saturday) ? new TimeSpan(7, 0, 0) : new TimeSpan(8, 0, 0);

                            // Extras Temprano (Antes de inicio oficial)
                            if (localIn.TimeOfDay < officialStartTime)
                            {
                                int earlyMin = (int)(officialStartTime - localIn.TimeOfDay).TotalMinutes;
                                if (earlyMin > EarlyArrivalGraceMinutes)
                                {
                                    var (dM, nM) = SplitOvertimeInterval(localIn, localIn.Date.Add(officialStartTime));
                                    totalDayOt += dM;
                                    totalNightOt += nM;
                                }
                            }

                            // Extras Tarde (Segmentadas a las 7:00 PM)
                            if (localOut.HasValue && localOut.Value.TimeOfDay > officialEndTime)
                            {
                                var (dM, nM) = SplitOvertimeInterval(localOut.Value.Date.Add(officialEndTime), localOut.Value);
                                totalDayOt += dM;
                                totalNightOt += nM;
                            }

                            detail.DayOvertimeMinutes = totalDayOt;
                            detail.NightOvertimeMinutes = totalNightOt;
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
                    int shiftTarget = (date.DayOfWeek == DayOfWeek.Saturday) ? 330 : 510;

                    if (isBirthday)
                    {
                        detail.CurrentStatus = "🎂 Libre Cumpleaños";
                        detail.StatusBadgeClass = "bg-primary-subtle text-primary border border-primary-subtle";
                        detail.NetWorkingHours = (date.DayOfWeek == DayOfWeek.Saturday) ? "5h 30m (Pagado)" : "8h 30m (Pagado)";
                    }
                    else if (incident != null)
                    {
                        if (incident.WaiveEarlyDeparture)
                        {
                            detail.CurrentStatus = "🟢 Permiso con Goce";
                            detail.StatusBadgeClass = "bg-success-subtle text-success border border-success-subtle";
                            detail.NetWorkingHours = (date.DayOfWeek == DayOfWeek.Saturday) ? "5h 30m (Pagado)" : "8h 30m (Pagado)";
                            detail.IsEarlyDepartureWaived = true;
                            detail.EarlyDepartureReason = incident.Detail;
                        }
                        else
                        {
                            detail.CurrentStatus = "🔴 Falta sin Goce";
                            detail.StatusBadgeClass = "bg-danger-subtle text-danger border border-danger-subtle";
                            detail.EarlyDepartureMinutes = shiftTarget;
                            detail.NetWorkingHours = "0h 0m";
                        }
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
                var prevMonth = new DateTime(year, month, 1).AddMonths(-1);
                int lastDayPrevMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
                startDate = new DateTime(prevMonth.Year, prevMonth.Month, lastDayPrevMonth);
                endDate = new DateTime(year, month, 14, 23, 59, 59);
            }
            else
            {
                int lastDayOfMonth = DateTime.DaysInMonth(year, month);
                startDate = new DateTime(year, month, 15);
                endDate = new DateTime(year, month, lastDayOfMonth - 1, 23, 59, 59);
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
                Identification = employee.Identification,
                MonthlySalary = employee.MonthlySalary,
                HasUnifiedLunch = employee.HasUnifiedLunch,
                StartDate = startDate,
                EndDate = endDate,
                PeriodTitle = $"{(quincena == 1 ? "1RA QUINCENA (CORTE AL 14)" : "2DA QUINCENA (CORTE PENÚLTIMO DÍA)")} - {new DateTime(year, month, 1):MMMM yyyy}".ToUpper()
            };

            for (var day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
            {
                var dayStartUtc = day.ToUniversalTime();
                var dayEndUtc = day.AddDays(1).AddTicks(-1).ToUniversalTime();

                var dayRecs = records.Where(r => r.TimestampUtc >= dayStartUtc && r.TimestampUtc <= dayEndUtc).ToList();
                bool isBirthday = CheckIfBirthdayApplies(employee.BirthDate, day.Date);

                if (!dayRecs.Any())
                {
                    int shiftTarget = (day.DayOfWeek == DayOfWeek.Saturday) ? 330 : 510;
                    string shiftHoursStr = (day.DayOfWeek == DayOfWeek.Saturday) ? "5h 30m" : "8h 30m";

                    if (isBirthday)
                    {
                        report.Days.Add(new DailyReportRow
                        {
                            Date = day,
                            ClockIn = "🎂 CUMPLEAÑOS",
                            ClockOut = "DÍA PAGADO",
                            IsBirthdayDayOff = true,
                            EffectiveHours = $"{shiftHoursStr} (Beneficio)"
                        });
                        continue;
                    }

                    var dayIncident = await _context.AttendanceIncidents
                        .FirstOrDefaultAsync(i => i.EmployeeId == employeeId && i.Date == day.Date);

                    if (dayIncident != null)
                    {
                        if (dayIncident.WaiveEarlyDeparture)
                        {
                            report.Days.Add(new DailyReportRow
                            {
                                Date = day,
                                ClockIn = "PERMISO",
                                ClockOut = "CON GOCE",
                                IsEarlyDepartureWaived = true,
                                EffectiveHours = $"{shiftHoursStr} (Pagado)"
                            });
                        }
                        else
                        {
                            report.Days.Add(new DailyReportRow
                            {
                                Date = day,
                                ClockIn = "AUSENCIA",
                                ClockOut = "SIN GOCE",
                                EarlyDepartureMinutes = shiftTarget,
                                ExcessMinutes = shiftTarget,
                                EffectiveHours = "0h 0m"
                            });

                            report.TotalEarlyDepartureMinutes += shiftTarget;
                        }
                        continue;
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
                int dayOt = 0;
                int nightOt = 0;
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

                        // Temprano
                        if (localIn.TimeOfDay < officialStart)
                        {
                            int earlyM = (int)(officialStart - localIn.TimeOfDay).TotalMinutes;
                            if (earlyM > EarlyArrivalGraceMinutes)
                            {
                                var (dM, nM) = SplitOvertimeInterval(localIn, localIn.Date.Add(officialStart));
                                dayOt += dM;
                                nightOt += nM;
                            }
                        }

                        // Tarde (Segmentada a las 7:00 PM)
                        if (localOut.TimeOfDay > officialEnd)
                        {
                            var (dM, nM) = SplitOvertimeInterval(localOut.Date.Add(officialEnd), localOut);
                            dayOt += dM;
                            nightOt += nM;
                        }

                        // Salida anticipada
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
                    DayOvertimeMinutes = dayOt,
                    NightOvertimeMinutes = nightOt,
                    HolidayOvertimeMinutes = holOvertime,
                    EffectiveHours = effHours
                });

                report.TotalLateMinutes += lateMin;
                report.TotalEarlyDepartureMinutes += (isWaived ? 0 : earlyDepMin);
                report.TotalExcessCoffeeMinutes += excessCoffee;
                report.TotalExcessLunchMinutes += excessLunch;
                report.TotalPersonalErrandMinutes += personal;

                report.TotalDayOvertimeMinutes += dayOt;
                report.TotalNightOvertimeMinutes += nightOt;
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

        // 3. EXCEL INDIVIDUAL DE LA QUINCENA
        public byte[] GenerateBiweeklyExcel(BiweeklyReportViewModel report)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Liquidacion");

            ws.Cell("A1").Value = "REPORTE DE ASISTENCIA Y LIQUIDACIÓN DE TIEMPOS";
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;
            ws.Range("A1:M1").Merge();

            ws.Cell("A2").Value = $"Colaborador: {report.FullName} (Cédula: {report.Identification})";
            ws.Cell("A2").Style.Font.Bold = true;
            ws.Range("A2:E2").Merge();

            ws.Cell("F2").Value = $"Periodo: {report.PeriodTitle} ({report.StartDate:dd/MM/yyyy} al {report.EndDate:dd/MM/yyyy})";
            ws.Cell("F2").Style.Font.Bold = true;
            ws.Range("F2:M2").Merge();

            string[] headers = {
                "Fecha", "Entrada", "Tardanza (min)", "Salida", "Salida Antic.", "Café (min)",
                "Almuerzo (min)", "Diligencias (min)", "Total Descuento (min)",
                "Extras Diurnas (min)", "Extras Nocturnas (min)", "Extras Dom/Fer (min)", "Horas Efectivas"
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

            var colorRojo = XLColor.Red;
            var colorVerde = XLColor.FromHtml("#198754");
            var colorAzul = XLColor.FromHtml("#0D6EFD");

            int row = 5;
            foreach (var d in report.Days)
            {
                ws.Cell(row, 1).Value = d.Date.ToString("ddd dd/MM/yyyy");
                ws.Cell(row, 2).Value = d.ClockIn;

                ws.Cell(row, 3).Value = d.LateMinutes;
                if (d.LateMinutes > 0) { ws.Cell(row, 3).Style.Font.FontColor = colorRojo; ws.Cell(row, 3).Style.Font.Bold = true; }

                ws.Cell(row, 4).Value = d.ClockOut;

                if (d.EarlyDepartureMinutes > 0)
                {
                    if (d.IsEarlyDepartureWaived)
                    {
                        ws.Cell(row, 5).Value = "0 (Con goce)";
                        ws.Cell(row, 5).Style.Font.FontColor = colorVerde;
                    }
                    else
                    {
                        ws.Cell(row, 5).Value = d.EarlyDepartureMinutes;
                        ws.Cell(row, 5).Style.Font.FontColor = colorRojo;
                        ws.Cell(row, 5).Style.Font.Bold = true;
                    }
                }
                else { ws.Cell(row, 5).Value = 0; }

                int allowedCoffee = (report.HasUnifiedLunch && d.Date.DayOfWeek != DayOfWeek.Saturday) ? 0 : 15;
                int coffeeExcess = Math.Max(0, d.CoffeeMinutes - allowedCoffee);
                if (coffeeExcess > 0)
                {
                    ws.Cell(row, 6).Value = $"{d.CoffeeMinutes} (+{coffeeExcess}m)";
                    ws.Cell(row, 6).Style.Font.FontColor = colorRojo;
                    ws.Cell(row, 6).Style.Font.Bold = true;
                }
                else { ws.Cell(row, 6).Value = d.CoffeeMinutes; }

                int allowedLunch = (report.HasUnifiedLunch && d.Date.DayOfWeek != DayOfWeek.Saturday) ? 60 : 45;
                int lunchExcess = Math.Max(0, d.LunchMinutes - allowedLunch);
                if (lunchExcess > 0)
                {
                    ws.Cell(row, 7).Value = $"{d.LunchMinutes} (+{lunchExcess}m)";
                    ws.Cell(row, 7).Style.Font.FontColor = colorRojo;
                    ws.Cell(row, 7).Style.Font.Bold = true;
                }
                else { ws.Cell(row, 7).Value = d.LunchMinutes; }

                ws.Cell(row, 8).Value = d.PersonalErrandMinutes;
                if (d.PersonalErrandMinutes > 0) { ws.Cell(row, 8).Style.Font.FontColor = colorRojo; ws.Cell(row, 8).Style.Font.Bold = true; }

                ws.Cell(row, 9).Value = d.ExcessMinutes;
                if (d.ExcessMinutes > 0) { ws.Cell(row, 9).Style.Font.FontColor = colorRojo; ws.Cell(row, 9).Style.Font.Bold = true; }

                ws.Cell(row, 10).Value = d.DayOvertimeMinutes;
                if (d.DayOvertimeMinutes > 0) { ws.Cell(row, 10).Style.Font.FontColor = colorVerde; ws.Cell(row, 10).Style.Font.Bold = true; }

                ws.Cell(row, 11).Value = d.NightOvertimeMinutes;
                if (d.NightOvertimeMinutes > 0) { ws.Cell(row, 11).Style.Font.FontColor = colorVerde; ws.Cell(row, 11).Style.Font.Bold = true; }

                ws.Cell(row, 12).Value = d.HolidayOvertimeMinutes;
                if (d.HolidayOvertimeMinutes > 0) { ws.Cell(row, 12).Style.Font.FontColor = colorAzul; ws.Cell(row, 12).Style.Font.Bold = true; }

                ws.Cell(row, 13).Value = d.EffectiveHours;

                ws.Range(row, 1, row, 13).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                row++;
            }

            ws.Range(4, 1, row - 1, 13).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(4, 1, row - 1, 13).Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            // Bloque Resumen
            row++;
            ws.Cell(row, 1).Value = "DESGLOSE DEDUCCIONES:";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = $"Tardanzas: {report.TotalLateMinutes}m | Salidas anticipadas: {report.TotalEarlyDepartureMinutes}m | Café: {report.TotalExcessCoffeeMinutes}m | Almuerzo: {report.TotalExcessLunchMinutes}m | Diligencias: {report.TotalPersonalErrandMinutes}m";
            ws.Range(row, 2, row, 13).Merge();

            row++;
            ws.Cell(row, 1).Value = "TOTAL DEDUCCIÓN:";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = $"{report.FormattedTotalExcess}  ==>  Monto: ₡ {report.DeductionAmount:N2}";
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Font.FontColor = colorRojo;
            ws.Range(row, 2, row, 6).Merge();

            ws.Cell(row, 7).Value = "TOTAL EXTRAS EN NÓMINA:";
            ws.Cell(row, 7).Style.Font.Bold = true;
            ws.Cell(row, 8).Value = $"Diurnas: ₡ {report.DayOvertimeAmount:N2} | Nocturnas: ₡ {report.NightOvertimeAmount:N2}  ==>  TOTAL: ₡ {report.TotalOvertimeAmount:N2}";
            ws.Cell(row, 8).Style.Font.Bold = true;
            ws.Cell(row, 8).Style.Font.FontColor = colorVerde;
            ws.Range(row, 8, row, 13).Merge();

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // 4. NUEVO: GENERADOR DEL EXCEL CONSOLIDADO DE NÓMINA PARA RECURSOS HUMANOS
        public async Task<byte[]> GenerateHrPayrollExcelAsync(int year, int month, int quincena)
        {
            var employees = await _context.Employees
                .Where(e => e.IsActive)
                .OrderBy(e => e.FullName)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Nomina_RRHH");

            // Título
            ws.Cell("A1").Value = "HOJA CONSOLIDADA DE NÓMINA - RECURSOS HUMANOS";
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontSize = 14;
            ws.Range("A1:I1").Merge();

            string periodStr = (quincena == 1 ? "1RA QUINCENA (CORTE AL 14)" : "2DA QUINCENA (CORTE PENÚLTIMO DÍA)") + $" - {new DateTime(year, month, 1):MMMM yyyy}".ToUpper();
            ws.Cell("A2").Value = $"Periodo: {periodStr} | Moneda: Colones costarricenses (₡)";
            ws.Cell("A2").Style.Font.Bold = true;
            ws.Range("A2:I2").Merge();

            string[] headers = {
                "Cédula", "Nombre del Colaborador", "Minutos Deducción", "Monto Deducciones (₡)",
                "Minutos Extras Diurnas", "Monto Extras Diurnas (₡)", "Minutos Extras Nocturnas",
                "Monto Extras Nocturnas (₡)", "Monto Total Extras (₡)"
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
            foreach (var emp in employees)
            {
                var report = await GetBiweeklyReportAsync(emp.Id, year, month, quincena);
                if (report == null) continue;

                ws.Cell(row, 1).Value = string.IsNullOrWhiteSpace(emp.Identification) ? emp.CardCode : emp.Identification;
                ws.Cell(row, 2).Value = emp.FullName;

                // Deducciones
                ws.Cell(row, 3).Value = report.GrandTotalExcessMinutes;
                ws.Cell(row, 4).Value = report.DeductionAmount;
                ws.Cell(row, 4).Style.NumberFormat.Format = "₡ #,##0.00";
                if (report.DeductionAmount > 0)
                {
                    ws.Cell(row, 4).Style.Font.FontColor = XLColor.Red;
                    ws.Cell(row, 4).Style.Font.Bold = true;
                }

                // Extras Diurnas
                ws.Cell(row, 5).Value = report.TotalDayOvertimeMinutes;
                ws.Cell(row, 6).Value = report.DayOvertimeAmount;
                ws.Cell(row, 6).Style.NumberFormat.Format = "₡ #,##0.00";

                // Extras Nocturnas
                ws.Cell(row, 7).Value = report.TotalNightOvertimeMinutes;
                ws.Cell(row, 8).Value = report.NightOvertimeAmount;
                ws.Cell(row, 8).Style.NumberFormat.Format = "₡ #,##0.00";

                // Total Extras
                ws.Cell(row, 9).Value = report.TotalOvertimeAmount;
                ws.Cell(row, 9).Style.NumberFormat.Format = "₡ #,##0.00";
                if (report.TotalOvertimeAmount > 0)
                {
                    ws.Cell(row, 9).Style.Font.FontColor = XLColor.FromHtml("#198754");
                    ws.Cell(row, 9).Style.Font.Bold = true;
                }

                row++;
            }

            // Fila de Totales Generales de la Empresa
            ws.Cell(row, 2).Value = "TOTALES DE LA EMPRESA:";
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 4).FormulaA1 = $"=SUM(D5:D{row - 1})";
            ws.Cell(row, 4).Style.Font.Bold = true;
            ws.Cell(row, 4).Style.Font.FontColor = XLColor.Red;
            ws.Cell(row, 4).Style.NumberFormat.Format = "₡ #,##0.00";

            ws.Cell(row, 6).FormulaA1 = $"=SUM(F5:F{row - 1})";
            ws.Cell(row, 6).Style.Font.Bold = true;
            ws.Cell(row, 6).Style.NumberFormat.Format = "₡ #,##0.00";

            ws.Cell(row, 8).FormulaA1 = $"=SUM(H5:H{row - 1})";
            ws.Cell(row, 8).Style.Font.Bold = true;
            ws.Cell(row, 8).Style.NumberFormat.Format = "₡ #,##0.00";

            ws.Cell(row, 9).FormulaA1 = $"=SUM(I5:I{row - 1})";
            ws.Cell(row, 9).Style.Font.Bold = true;
            ws.Cell(row, 9).Style.Font.FontColor = XLColor.FromHtml("#198754");
            ws.Cell(row, 9).Style.NumberFormat.Format = "₡ #,##0.00";

            ws.Range(4, 1, row, 9).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(4, 1, row, 9).Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}
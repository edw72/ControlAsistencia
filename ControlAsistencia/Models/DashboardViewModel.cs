namespace ControlAsistencia.Models
{
    public class DashboardViewModel
    {
        public DateTime SelectedDate { get; set; }
        public int TotalEmployees { get; set; }
        public int WorkingCount { get; set; }
        public int CoffeeCount { get; set; }
        public int LunchCount { get; set; }
        public int PersonalCount { get; set; }
        public int OutCount { get; set; }
        public List<EmployeeDailyDetailDto> Employees { get; set; } = new();
    }

    public class EmployeeDailyDetailDto
    {
        public int EmployeeId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string CardCode { get; set; } = string.Empty;
        public string Identification { get; set; } = string.Empty;
        public decimal MonthlySalary { get; set; }
        public string CurrentStatus { get; set; } = "Fuera";
        public string StatusBadgeClass { get; set; } = "bg-secondary";

        public string ClockInTime { get; set; } = "--:--";
        public string ClockOutTime { get; set; } = "--:--";

        public int LateMinutes { get; set; }
        public int CoffeeMinutes { get; set; }
        public int LunchMinutes { get; set; }
        public int ExcessCoffeeMinutes { get; set; }
        public int ExcessLunchMinutes { get; set; }
        public int PersonalErrandMinutes { get; set; }

        public int EarlyDepartureMinutes { get; set; }
        public bool IsEarlyDepartureWaived { get; set; }
        public string? EarlyDepartureReason { get; set; }

        public bool HasUnifiedLunch { get; set; }
        public int AllowedLunchMinutes => HasUnifiedLunch ? 60 : 45;

        public bool IsBirthdayDayOff { get; set; }

        public int TotalExcessMinutes => LateMinutes
            + (IsEarlyDepartureWaived ? 0 : EarlyDepartureMinutes)
            + ExcessCoffeeMinutes
            + ExcessLunchMinutes
            + PersonalErrandMinutes;

        // Desglose de Extras
        public int DayOvertimeMinutes { get; set; }     // Extras Diurnas (antes de 7:00 PM)
        public int NightOvertimeMinutes { get; set; }   // Extras Nocturnas (después de 7:00 PM)
        public int RegularOvertimeMinutes => DayOvertimeMinutes + NightOvertimeMinutes;
        public int HolidayOvertimeMinutes { get; set; } // Domingos y Feriados (100%)
        public int TotalOvertimeMinutes => RegularOvertimeMinutes + HolidayOvertimeMinutes;
        public int OvertimeMinutes => TotalOvertimeMinutes;

        public string FormattedOvertime => TotalOvertimeMinutes > 0
            ? $"{(TotalOvertimeMinutes / 60)}h {(TotalOvertimeMinutes % 60)}m" : "0m";

        public string FormattedRegularOvertime => RegularOvertimeMinutes > 0
            ? $"{(RegularOvertimeMinutes / 60)}h {(RegularOvertimeMinutes % 60)}m" : "0m";

        public string FormattedHolidayOvertime => HolidayOvertimeMinutes > 0
            ? $"{(HolidayOvertimeMinutes / 60)}h {(HolidayOvertimeMinutes % 60)}m" : "0m";

        public string NetWorkingHours { get; set; } = "0h 0m";
        public string? TodayIncidentCategory { get; set; }
        public string? TodayIncidentDetail { get; set; }
    }

    public class BiweeklyReportViewModel
    {
        public int EmployeeId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string CardCode { get; set; } = string.Empty;
        public string Identification { get; set; } = string.Empty;
        public decimal MonthlySalary { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string PeriodTitle { get; set; } = string.Empty;

        public bool HasUnifiedLunch { get; set; }
        public int AllowedLunchMinutes => HasUnifiedLunch ? 60 : 45;

        public List<DailyReportRow> Days { get; set; } = new();

        // Minutos de Deducción
        public int TotalLateMinutes { get; set; }
        public int TotalEarlyDepartureMinutes { get; set; }
        public int TotalExcessCoffeeMinutes { get; set; }
        public int TotalExcessLunchMinutes { get; set; }
        public int TotalPersonalErrandMinutes { get; set; }

        public int GrandTotalExcessMinutes => TotalLateMinutes
            + TotalEarlyDepartureMinutes
            + TotalExcessCoffeeMinutes
            + TotalExcessLunchMinutes
            + TotalPersonalErrandMinutes;

        public string FormattedTotalExcess => $"{(GrandTotalExcessMinutes / 60)}h {(GrandTotalExcessMinutes % 60)}m ({GrandTotalExcessMinutes} min)";

        // Minutos de Extras
        public int TotalDayOvertimeMinutes { get; set; }   // Extras Diurnas
        public int TotalNightOvertimeMinutes { get; set; } // Extras Nocturnas
        public int TotalRegularOvertimeMinutes => TotalDayOvertimeMinutes + TotalNightOvertimeMinutes;
        public int TotalHolidayOvertimeMinutes { get; set; }
        public int GrandTotalOvertimeMinutes => TotalRegularOvertimeMinutes; // A pagar en nómina ordinaria

        public string FormattedRegularOvertime => $"{(TotalRegularOvertimeMinutes / 60)}h {(TotalRegularOvertimeMinutes % 60)}m ({TotalRegularOvertimeMinutes} min)";
        public string FormattedHolidayOvertime => $"{(TotalHolidayOvertimeMinutes / 60)}h {(TotalHolidayOvertimeMinutes % 60)}m ({TotalHolidayOvertimeMinutes} min)";
        public string FormattedTotalOvertime => $"{(GrandTotalOvertimeMinutes / 60)}h {(GrandTotalOvertimeMinutes % 60)}m ({GrandTotalOvertimeMinutes} min a pagar en nómina)";

        // CÁLCULOS MONETARIOS (EN COLONES - COSTA RICA)
        public decimal DailyWage => MonthlySalary > 0 ? (MonthlySalary / 30m) : 0m;
        public decimal DayHourlyRate => DailyWage > 0 ? (DailyWage / 8m) : 0m;
        public decimal NightHourlyRate => DailyWage > 0 ? (DailyWage / 6m) : 0m;

        public decimal DeductionAmount => Math.Round(GrandTotalExcessMinutes * (DayHourlyRate / 60m), 2);
        public decimal DayOvertimeAmount => Math.Round(TotalDayOvertimeMinutes * ((DayHourlyRate * 1.5m) / 60m), 2);
        public decimal NightOvertimeAmount => Math.Round(TotalNightOvertimeMinutes * ((NightHourlyRate * 1.5m) / 60m), 2);
        public decimal TotalOvertimeAmount => DayOvertimeAmount + NightOvertimeAmount;
    }

    public class DailyReportRow
    {
        public DateTime Date { get; set; }
        public string ClockIn { get; set; } = "--:--";
        public string ClockOut { get; set; } = "--:--";
        public int LateMinutes { get; set; }
        public int EarlyDepartureMinutes { get; set; }
        public bool IsEarlyDepartureWaived { get; set; }
        public int CoffeeMinutes { get; set; }
        public int LunchMinutes { get; set; }
        public int PersonalErrandMinutes { get; set; }
        public int ExcessMinutes { get; set; }

        public int DayOvertimeMinutes { get; set; }
        public int NightOvertimeMinutes { get; set; }
        public int RegularOvertimeMinutes => DayOvertimeMinutes + NightOvertimeMinutes;
        public int OvertimeMinutes => RegularOvertimeMinutes;
        public int HolidayOvertimeMinutes { get; set; }

        public bool IsBirthdayDayOff { get; set; }
        public string EffectiveHours { get; set; } = "0h 0m";
    }

    // DTO para la hoja consolidada de Recursos Humanos
    public class HrPayrollRowDto
    {
        public string Identification { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int DeductionMinutes { get; set; }
        public decimal DeductionAmount { get; set; }
        public int DayOvertimeMinutes { get; set; }
        public decimal DayOvertimeAmount { get; set; }
        public int NightOvertimeMinutes { get; set; }
        public decimal NightOvertimeAmount { get; set; }
        public decimal TotalOvertimeAmount => DayOvertimeAmount + NightOvertimeAmount;
    }
}
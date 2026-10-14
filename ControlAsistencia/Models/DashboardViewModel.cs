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

        // HORAS EXTRAS (Generales y Separadas para compatibilidad)
        public int OvertimeMinutes { get; set; }
        public int RegularOvertimeMinutes { get; set; }
        public int HolidayOvertimeMinutes { get; set; }
        public int TotalOvertimeMinutes => OvertimeMinutes > 0 ? OvertimeMinutes : (RegularOvertimeMinutes + HolidayOvertimeMinutes);

        public string FormattedOvertime => OvertimeMinutes > 0
            ? $"{(OvertimeMinutes / 60)}h {(OvertimeMinutes % 60)}m" : "0m";

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
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string PeriodTitle { get; set; } = string.Empty;

        public bool HasUnifiedLunch { get; set; }
        public int AllowedLunchMinutes => HasUnifiedLunch ? 60 : 45;

        public List<DailyReportRow> Days { get; set; } = new();

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

        public string FormattedTotalExcess => $"{(GrandTotalExcessMinutes / 60)}h {(GrandTotalExcessMinutes % 60)}m ({GrandTotalExcessMinutes} min totales a descontar)";

        // TOTALES DE HORAS EXTRAS
        public int TotalRegularOvertimeMinutes { get; set; }
        public int TotalHolidayOvertimeMinutes { get; set; }

        // REGLA: El gran total a pagar en nómina toma ÚNICAMENTE las horas ordinarias
        // (los feriados y domingos se liquidan y pagan el mismo día)
        public int GrandTotalOvertimeMinutes => TotalRegularOvertimeMinutes;

        public string FormattedRegularOvertime => $"{(TotalRegularOvertimeMinutes / 60)}h {(TotalRegularOvertimeMinutes % 60)}m ({TotalRegularOvertimeMinutes} min)";
        public string FormattedHolidayOvertime => $"{(TotalHolidayOvertimeMinutes / 60)}h {(TotalHolidayOvertimeMinutes % 60)}m ({TotalHolidayOvertimeMinutes} min)";
        public string FormattedTotalOvertime => $"{(GrandTotalOvertimeMinutes / 60)}h {(GrandTotalOvertimeMinutes % 60)}m ({GrandTotalOvertimeMinutes} min a pagar en nómina)";

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

        // Horas extras compatibles
        public int OvertimeMinutes { get; set; }
        public int RegularOvertimeMinutes { get; set; }
        public int HolidayOvertimeMinutes { get; set; }

        public bool IsBirthdayDayOff { get; set; }
        public string EffectiveHours { get; set; } = "0h 0m";
    }
}
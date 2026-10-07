namespace ControlAsistencia.Models
{
    public class DashboardViewModel
    {
        public DateTime SelectedDate { get; set; }
        public int TotalEmployees { get; set; }
        public int WorkingCount { get; set; }
        public int CoffeeCount { get; set; }
        public int LunchCount { get; set; }
        public int PersonalCount { get; set; } // NUEVO: En diligencia
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
        public bool HasUnifiedLunch { get; set; }
        public int AllowedLunchMinutes => HasUnifiedLunch ? 60 : 45;

        // NUEVO: Tiempo total acumulado en diligencias personales (100% descontable)
        public int PersonalErrandMinutes { get; set; }

        // TOTAL A DEDUCIR: Tardanza + Exceso café + Exceso almuerzo + 100% Diligencias
        public int TotalExcessMinutes => LateMinutes + ExcessCoffeeMinutes + ExcessLunchMinutes + PersonalErrandMinutes;

        public int OvertimeMinutes { get; set; }
        public string FormattedOvertime => OvertimeMinutes > 0
            ? $"{(OvertimeMinutes / 60)}h {(OvertimeMinutes % 60)}m"
            : "0m";

        public string NetWorkingHours { get; set; } = "0h 0m";

        // Novedad o justificación del día (si la hubiera)
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

        public List<DailyReportRow> Days { get; set; } = new();

        public int TotalLateMinutes { get; set; }
        public int TotalExcessCoffeeMinutes { get; set; }
        public int TotalExcessLunchMinutes { get; set; }
        public int TotalPersonalErrandMinutes { get; set; } // NUEVO: Acumulado diligencias
        public bool HasUnifiedLunch { get; set; }
        public int AllowedLunchMinutes => HasUnifiedLunch ? 60 : 45;

        public int GrandTotalExcessMinutes => TotalLateMinutes + TotalExcessCoffeeMinutes + TotalExcessLunchMinutes + TotalPersonalErrandMinutes;
        public string FormattedTotalExcess => $"{(GrandTotalExcessMinutes / 60)}h {(GrandTotalExcessMinutes % 60)}m ({GrandTotalExcessMinutes} min totales a descontar)";

        public int GrandTotalOvertimeMinutes { get; set; }
        public string FormattedTotalOvertime => $"{(GrandTotalOvertimeMinutes / 60)}h {(GrandTotalOvertimeMinutes % 60)}m ({GrandTotalOvertimeMinutes} min a pagar)";
    }

    public class DailyReportRow
    {
        public DateTime Date { get; set; }
        public string ClockIn { get; set; } = "--:--";
        public string ClockOut { get; set; } = "--:--";
        public int LateMinutes { get; set; }
        public int CoffeeMinutes { get; set; }
        public int LunchMinutes { get; set; }
        public int PersonalErrandMinutes { get; set; } // NUEVO
        public int ExcessMinutes { get; set; }
        public int OvertimeMinutes { get; set; }
        public string EffectiveHours { get; set; } = "0h 0m";
    }
}
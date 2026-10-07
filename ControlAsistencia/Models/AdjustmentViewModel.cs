using ControlAsistencia.Models;

namespace ControlAsistencia.Models
{
    public class AdjustmentViewModel
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string CardCode { get; set; } = string.Empty;
        public DateTime Date { get; set; }

        // Lista de marcas registradas en este día
        public List<PunchRecordDto> Records { get; set; } = new();
    }

    public class PunchRecordDto
    {
        public int Id { get; set; }
        public RecordType Type { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public string BadgeClass { get; set; } = "bg-secondary";
        public DateTime LocalTime { get; set; }
        public string FormattedTime => LocalTime.ToString("hh:mm:ss tt");
        public string? Note { get; set; }
    }
}

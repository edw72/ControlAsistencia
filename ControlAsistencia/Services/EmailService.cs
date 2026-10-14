using ControlAsistencia.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ControlAsistencia.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task<(bool Success, string Message)> SendBiweeklyReportEmailAsync(
            string recipientEmail,
            string recipientName,
            BiweeklyReportViewModel report,
            byte[] excelBytes)
        {
            try
            {
                var smtp = _config.GetSection("SmtpSettings");
                var server = smtp["Server"];
                var portStr = smtp["Port"] ?? "587";
                var senderName = smtp["SenderName"] ?? "Recursos Humanos";
                var senderEmail = smtp["SenderEmail"];
                var username = smtp["Username"];
                var password = smtp["Password"];

                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    return (false, "Faltan datos de configuración en appsettings.json (Servidor, Usuario o Contraseña).");
                }

                int port = int.Parse(portStr);

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(senderName, senderEmail));
                message.To.Add(new MailboxAddress(recipientName, recipientEmail));
                message.Subject = $"📄 Liquidación de Asistencia - {report.PeriodTitle} - {report.FullName}";

                var builder = new BodyBuilder();

                builder.HtmlBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 650px; margin: 0 auto; color: #1e293b; border: 1px solid #e2e8f0; border-radius: 12px; overflow: hidden;'>
                    <div style='background: #0d6efd; color: white; padding: 20px; text-align: center;'>
                        <h2 style='margin: 0;'>LIQUIDACIÓN DE ASISTENCIA Y TIEMPOS</h2>
                        <p style='margin: 5px 0 0 0; opacity: 0.9;'>{report.PeriodTitle}</p>
                    </div>
                    <div style='padding: 25px;'>
                        <p>Estimado/a <strong>{report.FullName}</strong> (ID: {report.CardCode}),</p>
                        <p>Adjunto a este correo encontrarás el detalle oficial de asistencia correspondiente al periodo del <strong>{report.StartDate:dd/MM/yyyy}</strong> al <strong>{report.EndDate:dd/MM/yyyy}</strong>.</p>
                        
                        <div style='background: #f8fafc; border-left: 4px solid #ef4444; padding: 15px; margin: 20px 0;'>
                            <h4 style='margin: 0 0 10px 0; color: #b91c1c;'>Resumen de Deducciones Aplicadas:</h4>
                            <ul style='margin: 0; padding-left: 20px; font-size: 14px;'>
                                <li>Tardanzas de entrada: <strong>{report.TotalLateMinutes} min</strong></li>
                                <li>Salidas anticipadas no justificadas: <strong>{report.TotalEarlyDepartureMinutes} min</strong></li>
                                <li>Exceso en café: <strong>{report.TotalExcessCoffeeMinutes} min</strong></li>
                                <li>Exceso en almuerzo: <strong>{report.TotalExcessLunchMinutes} min</strong></li>
                                <li>Diligencias personales: <strong>{report.TotalPersonalErrandMinutes} min</strong></li>
                            </ul>
                            <p style='margin: 10px 0 0 0; font-weight: bold; color: #b91c1c;'>TOTAL A DEDUCIR: {report.FormattedTotalExcess}</p>
                        </div>

                        <!-- Tarjeta de Horas Extras -->
                        <div style='background: #f0fdf4; border-left: 4px solid #10b981; border-radius: 6px; padding: 15px; margin: 20px 0;'>
                            <h4 style='margin: 0 0 10px 0; color: #047857;'>Resumen de Horas Extras:</h4>
                            <ul style='margin: 0; padding-left: 20px; font-size: 14px; color: #475569;'>
                                <li>Extras Ordinarias (A pagar en nómina): <strong>{report.FormattedRegularOvertime}</strong></li>
                                <li>Extras Domingos y Feriados (Pagadas en fecha): <strong>{report.FormattedHolidayOvertime}</strong></li>
                            </ul>
                            <p style='margin: 10px 0 0 0; font-weight: bold; color: #047857; font-size: 15px;'>TOTAL EXTRAS A PAGAR EN NÓMINA: {report.FormattedTotalOvertime}</p>
                        </div>

                        <p style='font-size: 13px; color: #64748b;'>Este es un comprobante digital generado automáticamente con su archivo Excel adjunto.</p>
                    </div>
                </div>";

                string safeName = report.FullName.Replace(" ", "_");
                string fileName = $"Liquidacion_{safeName}_{report.PeriodTitle.Replace(" ", "_")}.xlsx";
                builder.Attachments.Add(fileName, excelBytes, new ContentType("application", "vnd.openxmlformats-officedocument.spreadsheetml.sheet"));

                message.Body = builder.ToMessageBody();

                using var client = new SmtpClient();
                client.Timeout = 15000; // 15 segundos de timeout

                client.CheckCertificateRevocation = false;

                // Conexión segura con Gmail / Outlook
                await client.ConnectAsync(server, port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(username, password);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                return (true, "Correo enviado correctamente.");
            }
            catch (Exception ex)
            {
                return (false, $"Error al conectar con Gmail: {ex.Message}");
            }
        }
    }
}
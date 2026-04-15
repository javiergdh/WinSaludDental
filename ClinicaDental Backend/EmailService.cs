using MailKit.Net.Smtp; // Para el cliente de correo
using MimeKit;           // Para crear el mensaje
using System.Threading.Tasks; // <--- ESTO FALTA: Necesario para usar 'Task'

public class EmailService
{
    // Usa tus datos reales aquí como hiciste en el otro archivo
    private readonly string _emailEmisor = "javiergdh26@gmail.com";
    private readonly string _passwordApp = "xqcg bdcz ulmg crys"; 

    public async Task EnviarConfirmacionCita(string emailDestino, string nombre, string fecha, string hora)
    {
        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress("Win Salud Dental", _emailEmisor));
        mensaje.To.Add(new MailboxAddress(nombre, emailDestino));
        mensaje.Subject = "✅ Confirmación de tu cita - Win Salud Dental";

        mensaje.Body = new TextPart("html") {
            Text = $@"
                <div style='font-family: Arial, sans-serif; color: #333;'>
                    <h1>¡Hola, {nombre}!</h1>
                    <p>Tu cita en <b>Win Salud Dental</b> ha sido confirmada con éxito.</p>
                    <ul>
                        <li><b>Día:</b> {fecha}</li>
                        <li><b>Hora:</b> {hora}</li>
                    </ul>
                    <p>Si necesitas modificarla, puedes hacerlo desde nuestra web con tu DNI.</p>
                </div>"
        };

        using var client = new SmtpClient();
        try 
        {
            await client.ConnectAsync("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_emailEmisor, _passwordApp);
            await client.SendAsync(mensaje);
        }
        finally 
        {
            await client.DisconnectAsync(true);
        }
    }

    public async Task EnviarCancelacionCita(string emailDestino, string nombre, string fecha)
    {
        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress("Win Salud Dental", _emailEmisor));
        mensaje.To.Add(new MailboxAddress(nombre, emailDestino));
        mensaje.Subject = "❌ Cita Cancelada - Win Salud Dental";

        mensaje.Body = new TextPart("html") {
            Text = $@"<div style='font-family: Arial, sans-serif;'>
                        <h1>Cita Cancelada</h1>
                        <p>Hola {nombre}, te confirmamos que tu cita para el día {fecha} ha sido cancelada correctamente.</p>
                      </div>"
        };

        using var client = new SmtpClient();
        try 
        {
            await client.ConnectAsync("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_emailEmisor, _passwordApp);
            await client.SendAsync(mensaje);
        }
        finally 
        {
            await client.DisconnectAsync(true);
        }
    }
    public async Task EnviarAgradecimientoPostCita(string emailDestino, string nombre)
    {
        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress("Win Salud Dental", _emailEmisor));
        mensaje.To.Add(new MailboxAddress(nombre, emailDestino));
        mensaje.Subject = "✨ ¡Gracias por visitarnos! - Win Salud Dental";

        mensaje.Body = new TextPart("html") {
            Text = $@"<div style='font-family: Arial, sans-serif;'>
                        <h1>¡Gracias por tu visita, {nombre}!</h1>
                        <p>Ha sido un placer atenderte hoy. Esperamos que tu experiencia haya sido excelente.</p>
                        <p>¡Nos vemos en tu próxima revisión!</p>
                    </div>"
        };
        await EnviarCorreoAsync(mensaje);
    }

    public async Task EnviarRecordatorioNoAsistencia(string emailDestino, string nombre)
    {
        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress("Win Salud Dental", _emailEmisor));
        mensaje.To.Add(new MailboxAddress(nombre, emailDestino));
        mensaje.Subject = "🔔 Te hemos echado de menos - Win Salud Dental";

        mensaje.Body = new TextPart("html") {
            Text = $@"<div style='font-family: Arial, sans-serif;'>
                        <h1>Hola {nombre}, notamos que no pudiste asistir</h1>
                        <p>Lamentamos que no pudieras venir a tu cita hoy.</p>
                        <p>Te rogamos que, si no puedes asistir, nos avises con antelación o canceles desde la web para que otro niño pueda usar ese horario.</p>
                        <p>Puedes agendar una nueva cita cuando quieras en nuestra página oficial.</p>
                    </div>"
        };
        await EnviarCorreoAsync(mensaje);
    }

    // Método auxiliar para no repetir código de conexión
    private async Task EnviarCorreoAsync(MimeMessage mensaje) {
        using var client = new SmtpClient();
        try {
            await client.ConnectAsync("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_emailEmisor, _passwordApp);
            await client.SendAsync(mensaje);
        } catch (Exception ex) {
            // Esto evita que la app explote si falla el mail
            Console.WriteLine($"[SMTP ERROR] {ex.Message}");
        } finally {
            await client.DisconnectAsync(true);
        }
    }
}
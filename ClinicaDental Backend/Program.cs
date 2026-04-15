using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors();
builder.Services.ConfigureHttpJsonOptions(opt => opt.SerializerOptions.PropertyNameCaseInsensitive = true);

builder.Services.AddSingleton<EmailService>();

var app = builder.Build();
app.UseCors(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

string rutaDB = Path.Combine(AppContext.BaseDirectory, "clinicaWin.db");
Console.WriteLine($"DEBUG: Buscando base de datos en: {rutaDB}");
string connectionString = $"Data Source={rutaDB};Cache=Shared";

// ---------------------------------------------------------
// VERIFICAR DISPONIBILIDAD (Modificado para Fecha y Hora)
// ---------------------------------------------------------
app.MapGet("/verificar-disponibilidad", async (string dia, string hora) => {
    using var conn = new SqliteConnection(connectionString);
    await conn.OpenAsync();
    var cmd = conn.CreateCommand();
    
    // Ahora buscamos en columnas separadas
    cmd.CommandText = "SELECT COUNT(*) FROM Citas WHERE Fecha = @f AND Hora = @h AND Estado = 'Pendiente'";
    cmd.Parameters.AddWithValue("@f", dia);
    cmd.Parameters.AddWithValue("@h", hora);
    
    long count = (long)await cmd.ExecuteScalarAsync();
    return Results.Ok(new { disponible = count == 0 });
});

// ---------------------------------------------------------
// 1. ENDPOINT: AGENDAR CITA (Modificado para Fecha y Hora)
// ---------------------------------------------------------
app.MapPost("/agendar-cita", async (CitaRequest request, EmailService emailService) => {
    using var conn = new SqliteConnection(connectionString);
    await conn.OpenAsync();
    
    using var trans = conn.BeginTransaction();
    try {
        var cmdPac = conn.CreateCommand();
        cmdPac.CommandText = @"
            INSERT INTO Pacientes (Nombre, DNI, Telefono, Email) 
            VALUES (@n, @d, @t, @e) 
            ON CONFLICT(DNI) DO UPDATE SET Telefono=@t, Email=@e, Nombre=@n;
            SELECT PacienteID FROM Pacientes WHERE DNI=@d;";
        
        cmdPac.Parameters.AddWithValue("@n", request.Nombre);
        cmdPac.Parameters.AddWithValue("@d", request.DNI);
        cmdPac.Parameters.AddWithValue("@t", request.Telefono);
        cmdPac.Parameters.AddWithValue("@e", request.Email);
        int pId = Convert.ToInt32(await cmdPac.ExecuteScalarAsync());

        var cmdCita = conn.CreateCommand();
        // Insertamos en las nuevas columnas Fecha y Hora
        cmdCita.CommandText = "INSERT INTO Citas (Motivo, Fecha, Hora, Estado) VALUES (@m, @f, @h, 'Pendiente'); SELECT last_insert_rowid();";
        cmdCita.Parameters.AddWithValue("@m", request.Motivo);
        cmdCita.Parameters.AddWithValue("@f", request.Dia);
        cmdCita.Parameters.AddWithValue("@h", request.Horario);
        int cId = Convert.ToInt32(await cmdCita.ExecuteScalarAsync());

        var cmdVin = conn.CreateCommand();
        cmdVin.CommandText = "INSERT INTO AsignacionCitas (PacienteID, CitaID) VALUES (@p, @c);";
        cmdVin.Parameters.AddWithValue("@p", pId);
        cmdVin.Parameters.AddWithValue("@c", cId);
        await cmdVin.ExecuteNonQueryAsync();

        trans.Commit();

        await emailService.EnviarConfirmacionCita(request.Email, request.Nombre, request.Dia, request.Horario);

        return Results.Ok();
    } catch { 
        trans.Rollback(); 
        return Results.BadRequest("Error al guardar."); 
    }
});

// ---------------------------------------------------------
// 2. ENDPOINT: CANCELAR CITA
// ---------------------------------------------------------
app.MapDelete("/eliminar-cita/{id}", async (int id, EmailService emailService) => {
    using var conn = new SqliteConnection(connectionString);
    await conn.OpenAsync();
    
    string emailDestino = "";
    string nombrePaciente = "";
    string fechaCita = "";

    var cmdBusca = conn.CreateCommand();
    cmdBusca.CommandText = @"
        SELECT p.Email, p.Nombre, c.Fecha, c.Hora 
        FROM Pacientes p 
        JOIN AsignacionCitas ac ON p.PacienteID = ac.PacienteID 
        JOIN Citas c ON ac.CitaID = c.CitaID 
        WHERE c.CitaID = @id";
    cmdBusca.Parameters.AddWithValue("@id", id);
    
    using (var reader = await cmdBusca.ExecuteReaderAsync()) {
        if (await reader.ReadAsync()) {
            emailDestino = reader.GetString(0);
            nombrePaciente = reader.GetString(1);
            fechaCita = $"{reader.GetString(2)} {reader.GetString(3)}";
        }
    }

    using var trans = conn.BeginTransaction();
    try {
        var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "UPDATE Citas SET Estado = 'Cancelada' WHERE CitaID = @id";
        cmd2.Parameters.AddWithValue("@id", id);
        await cmd2.ExecuteNonQueryAsync();

        trans.Commit();

        if (!string.IsNullOrEmpty(emailDestino)) {
            await emailService.EnviarCancelacionCita(emailDestino, nombrePaciente, fechaCita);
        }

        return Results.Ok("Cancelada y notificada.");
    } catch (Exception ex) {
        trans.Rollback();
        return Results.BadRequest(ex.Message);
    }
});

// ---------------------------------------------------------
// 3. ENDPOINT: CONSULTAR CITAS POR DNI
// ---------------------------------------------------------
app.MapGet("/consultar-citas/{dni}", async (string dni) => {
    using var conn = new SqliteConnection(connectionString);
    await conn.OpenAsync();
    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        SELECT c.CitaID, c.Fecha, c.Hora, c.Motivo 
        FROM Citas c 
        JOIN AsignacionCitas ac ON c.CitaID = ac.CitaID 
        JOIN Pacientes p ON ac.PacienteID = p.PacienteID 
        WHERE p.DNI = @dni AND c.Estado = 'Pendiente'";
    cmd.Parameters.AddWithValue("@dni", dni);

    using var reader = await cmd.ExecuteReaderAsync();
    var citas = new List<object>();
    while (await reader.ReadAsync()) {
        citas.Add(new { 
            id = reader.GetInt32(0), 
            fecha = reader.GetString(1), 
            hora = reader.GetString(2),
            motivo = reader.GetString(3) 
        });
    }
    return Results.Ok(citas);
});

// ---------------------------------------------------------
// 4. PANEL DE ADMINISTRACIÓN: TODAS LAS CITAS
// ---------------------------------------------------------
app.MapGet("/consultar-todas-las-citas", async () => {
    using var conn = new SqliteConnection(connectionString);
    await conn.OpenAsync();
    var cmd = conn.CreateCommand();
    
    cmd.CommandText = @"
        SELECT p.Nombre, p.DNI, p.Telefono, p.Email, c.Fecha, c.Hora, c.Motivo, c.CitaID, c.Estado 
        FROM Citas c 
        JOIN AsignacionCitas ac ON c.CitaID = ac.CitaID 
        JOIN Pacientes p ON ac.PacienteID = p.PacienteID 
        WHERE c.Estado IN ('Pendiente', 'Completada', 'No Asistió')
        ORDER BY c.Fecha ASC, c.Hora ASC";

    using var reader = await cmd.ExecuteReaderAsync();
    var citas = new List<object>();
    
    while (await reader.ReadAsync()) {
        citas.Add(new { 
            nombre = reader.GetString(0),
            dni = reader.GetString(1),
            telefono = reader.GetString(2),
            email = reader.GetString(3),
            fecha = reader.GetString(4),
            horario = reader.GetString(5),
            motivo = reader.GetString(6),
            id = reader.GetInt32(7),
            estado = reader.IsDBNull(8) ? "Pendiente" : reader.GetString(8)
        });
    }
    return Results.Ok(citas);
});

// ---------------------------------------------------------
// 5. GESTIÓN ADMIN: MARCAR ASISTENCIA
// ---------------------------------------------------------
app.MapPost("/gestionar-cita-admin", async (int citaId, string nuevoEstado, EmailService emailService) => {
    using var conn = new SqliteConnection(connectionString);
    await conn.OpenAsync();
    
    var cmdBusca = conn.CreateCommand();
    cmdBusca.CommandText = @"
        SELECT p.Email, p.Nombre FROM Pacientes p 
        JOIN AsignacionCitas ac ON p.PacienteID = ac.PacienteID 
        WHERE ac.CitaID = @id";
    cmdBusca.Parameters.AddWithValue("@id", citaId);
    
    string email = "";
    string nombre = "";
    using (var reader = await cmdBusca.ExecuteReaderAsync()) {
        if (await reader.ReadAsync()) {
            email = reader.GetString(0);
            nombre = reader.GetString(1);
        }
    }

    var cmdUpd = conn.CreateCommand();
    cmdUpd.CommandText = "UPDATE Citas SET Estado = @estado WHERE CitaID = @id";
    cmdUpd.Parameters.AddWithValue("@estado", nuevoEstado);
    cmdUpd.Parameters.AddWithValue("@id", citaId);
    await cmdUpd.ExecuteNonQueryAsync();

    if (nuevoEstado == "Completada" && !string.IsNullOrEmpty(email)) {
        await emailService.EnviarAgradecimientoPostCita(email, nombre);
    } else if (nuevoEstado == "No Asistió" && !string.IsNullOrEmpty(email)) {
        await emailService.EnviarRecordatorioNoAsistencia(email, nombre);
    }

    return Results.Ok(new { mensaje = $"Cita marcada como {nuevoEstado}" });
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";

// Escuchar en 0.0.0.0 (todas las interfaces) para que sea accesible desde internet
app.Run($"http://0.0.0.0:{port}");

public record CitaRequest(string Nombre, string DNI, string Telefono, string Email, string Dia, string Horario, string Motivo);
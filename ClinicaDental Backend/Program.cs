using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Configurar logs básicos para evitar el error de TypeLoadException
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddCors();
builder.Services.AddSingleton<EmailService>();

var app = builder.Build();

app.UseCors(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());


// --- CONFIGURACIÓN DE BASE DE DATOS ---
string dbDirectory = "/app/data";
string dbPath = Directory.Exists(dbDirectory) 
    ? Path.Combine(dbDirectory, "clinicaWin.db") 
    : Path.Combine(AppContext.BaseDirectory, "clinicaWin.db");

string connectionString = $"Data Source={dbPath}";

// ESTE BLOQUE ES EL QUE FALTA: Crea las tablas si el archivo está vacío
try {
    using var conn = new SqliteConnection(connectionString);
    await conn.OpenAsync();
    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS Pacientes (
            PacienteID INTEGER PRIMARY KEY AUTOINCREMENT,
            Nombre TEXT, DNI TEXT UNIQUE, Telefono TEXT, Email TEXT
        );
        CREATE TABLE IF NOT EXISTS Citas (
            CitaID INTEGER PRIMARY KEY AUTOINCREMENT,
            Motivo TEXT, Fecha TEXT, Hora TEXT, Estado TEXT
        );
        CREATE TABLE IF NOT EXISTS AsignacionCitas (
            PacienteID INTEGER, CitaID INTEGER
        );";
    await cmd.ExecuteNonQueryAsync();
    Console.WriteLine($"[EXITO] Tablas verificadas/creadas en: {dbPath}");
} catch (Exception ex) {
    Console.WriteLine($"[ERROR AL CREAR TABLAS]: {ex.Message}");
}

// --- ENDPOINT VERIFICACIÓN CORREGIDO ---
app.MapGet("/verificar-disponibilidad", async (string dia, string hora) => {
    try {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        // Usamos una consulta más limpia
        cmd.CommandText = "SELECT COUNT(*) FROM Citas WHERE Fecha = @f AND Hora = @h AND Estado = 'Pendiente'";
        cmd.Parameters.AddWithValue("@f", dia);
        cmd.Parameters.AddWithValue("@h", hora);
        
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        return Results.Ok(new { disponible = count == 0 });
    } catch (Exception ex) {
        Console.WriteLine($"Error en verificación: {ex.Message}");
        return Results.Ok(new { disponible = true }); // En caso de error de DB, permitimos intentar agendar
    }
});
// ---------------------------------------------------------
// ENDPOINTS (MINIMAL APIS)
// ---------------------------------------------------------

app.MapGet("/verificar-disponibilidad", async (string dia, string hora) => {
    try {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Citas WHERE Fecha = @f AND Hora = @h AND Estado = 'Pendiente'";
        cmd.Parameters.AddWithValue("@f", dia);
        cmd.Parameters.AddWithValue("@h", hora);
        long count = (long)await cmd.ExecuteScalarAsync();
        return Results.Ok(new { disponible = count == 0 });
    } catch (Exception ex) {
        return Results.Problem($"Error DB: {ex.Message}");
    }
});

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
    } catch (Exception ex) { 
        trans.Rollback(); 
        return Results.BadRequest($"Error al guardar: {ex.Message}"); 
    }
});

app.MapDelete("/eliminar-cita/{id}", async (int id, EmailService emailService) => {
    using var conn = new SqliteConnection(connectionString);
    await conn.OpenAsync();
    // ... resto de tu código de eliminar cita
    return Results.Ok("Procesado"); 
});

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
        citas.Add(new { id = reader.GetInt32(0), fecha = reader.GetString(1), hora = reader.GetString(2), motivo = reader.GetString(3) });
    }
    return Results.Ok(citas);
});

app.MapGet("/consultar-todas-las-citas", async () => {
    using var conn = new SqliteConnection(connectionString);
    await conn.OpenAsync();
    var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT p.Nombre, p.DNI, p.Telefono, p.Email, c.Fecha, c.Hora, c.Motivo, c.CitaID, c.Estado FROM Citas c JOIN AsignacionCitas ac ON c.CitaID = ac.CitaID JOIN Pacientes p ON ac.PacienteID = p.PacienteID WHERE c.Estado IN ('Pendiente', 'Completada', 'No Asistió') ORDER BY c.Fecha ASC, c.Hora ASC";
    using var reader = await cmd.ExecuteReaderAsync();
    var citas = new List<object>();
    while (await reader.ReadAsync()) {
        citas.Add(new { nombre = reader.GetString(0), dni = reader.GetString(1), fecha = reader.GetString(4), horario = reader.GetString(5), motivo = reader.GetString(6), id = reader.GetInt32(7), estado = reader.IsDBNull(8) ? "Pendiente" : reader.GetString(8) });
    }
    return Results.Ok(citas);
});

app.MapPost("/gestionar-cita-admin", async (int citaId, string nuevoEstado, EmailService emailService) => {
    using var conn = new SqliteConnection(connectionString);
    await conn.OpenAsync();
    // ... resto de tu código de gestión admin
    return Results.Ok(new { mensaje = $"Cita marcada como {nuevoEstado}" });
});

// 4. LANZAMIENTO
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

// DTOs
public record CitaRequest(string Nombre, string DNI, string Telefono, string Email, string Dia, string Horario, string Motivo);
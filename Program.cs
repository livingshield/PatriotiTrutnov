using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using DotNetEnv;

var builder = WebApplication.CreateBuilder(args);

// Load .env from local directory or fallback to parent directory
string localEnv = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(localEnv))
{
    Env.Load(localEnv);
}
else
{
    Env.Load(Path.Combine(Directory.GetCurrentDirectory(), "..", ".env"));
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddCors();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

// Database Setup - Patrioti Trutnov
using (var scope = app.Services.CreateScope())
{
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    string dbType = Environment.GetEnvironmentVariable("DB_TYPE") ?? "MSSQL";
    string? connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
    
    if (!string.IsNullOrEmpty(connectionString))
    {
        try
        {
            using var connection = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                ? (DbConnection)new MySqlConnection(connectionString)
                : (DbConnection)new SqlConnection(connectionString);
                
            connection.Open();
            
            string createTableSql = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                ? @"CREATE TABLE IF NOT EXISTS patriotitrutnov_leads (
                        Id INT AUTO_INCREMENT PRIMARY KEY,
                        FullName VARCHAR(200) NOT NULL,
                        Email VARCHAR(200) NOT NULL,
                        Phone VARCHAR(50),
                        Topic VARCHAR(500),
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                    )"
                : @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='patriotitrutnov_leads' AND xtype='U')
                    BEGIN
                        CREATE TABLE patriotitrutnov_leads (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            FullName NVARCHAR(200) NOT NULL,
                            Email NVARCHAR(200) NOT NULL,
                            Phone NVARCHAR(50),
                            Topic NVARCHAR(500),
                            CreatedAt DATETIME DEFAULT GETDATE()
                        )
                    END";
                    
            using var command = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                ? (DbCommand)new MySqlCommand(createTableSql, (MySqlConnection)connection)
                : (DbCommand)new SqlCommand(createTableSql, (SqlConnection)connection);
                
            command.ExecuteNonQuery();
            Console.WriteLine($"[DB] {dbType} Database table is ready.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] Error ({dbType}): " + ex.Message);
        }
    }
}

app.MapPost("/api/leads", async (LeadModel lead, IConfiguration config) =>
{
    var smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST") ?? config["Smtp:Host"];
    var smtpPort = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var port) ? port : (int.TryParse(config["Smtp:Port"], out var p) ? p : 587);
    var smtpUser = Environment.GetEnvironmentVariable("SMTP_USER") ?? config["Smtp:Username"];
    var smtpPass = Environment.GetEnvironmentVariable("SMTP_PASS") ?? config["Smtp:Password"];
    
    // Sender address (configured in env files)
    var fromEmail = Environment.GetEnvironmentVariable("SMTP_FROM") ?? config["Smtp:FromEmail"] ?? "noreply@patriotitrutnov.cz";
    var fromName = config["Smtp:FromName"] ?? "Patrioti Trutnov";

    // Admin address for BCC copy
    var adminEmail = Environment.GetEnvironmentVariable("TARGET_EMAIL") ?? config["Smtp:FromEmail"];

    var dbType = Environment.GetEnvironmentVariable("DB_TYPE") ?? "MSSQL";
    var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

    try
    {
        // 1. Save to Database
        if (!string.IsNullOrEmpty(connectionString))
        {
            using (var connection = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                ? (DbConnection)new MySqlConnection(connectionString)
                : (DbConnection)new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                string insertSql = "INSERT INTO patriotitrutnov_leads (FullName, Email, Phone, Topic) VALUES (@FullName, @Email, @Phone, @Topic)";
                
                using (var command = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                    ? (DbCommand)new MySqlCommand(insertSql, (MySqlConnection)connection)
                    : (DbCommand)new SqlCommand(insertSql, (SqlConnection)connection))
                {
                    if (command is MySqlCommand myCmd)
                    {
                        myCmd.Parameters.AddWithValue("@FullName", lead.FullName);
                        myCmd.Parameters.AddWithValue("@Email", lead.Email);
                        myCmd.Parameters.AddWithValue("@Phone", (object?)lead.Phone ?? DBNull.Value);
                        myCmd.Parameters.AddWithValue("@Topic", (object?)lead.Topic ?? DBNull.Value);
                    }
                    else if (command is SqlCommand msCmd)
                    {
                        msCmd.Parameters.AddWithValue("@FullName", lead.FullName);
                        msCmd.Parameters.AddWithValue("@Email", lead.Email);
                        msCmd.Parameters.AddWithValue("@Phone", (object?)lead.Phone ?? DBNull.Value);
                        msCmd.Parameters.AddWithValue("@Topic", (object?)lead.Topic ?? DBNull.Value);
                    }
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        // 2. Send Email
        if (smtpUser != null && smtpPass != null && smtpHost != null)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromEmail));
            
            // Recipient is the one who filled out the form ("prijemce, ten kdo je vyplnen")
            message.To.Add(new MailboxAddress(lead.FullName, lead.Email));

            // BCC a copy to the admin (website owner)
            if (!string.IsNullOrEmpty(adminEmail))
            {
                message.Bcc.Add(new MailboxAddress("Admin Copy", adminEmail));
            }

            message.Subject = "Potvrzení přijetí zprávy | Patrioti Trutnov";

            message.Body = new TextPart("plain")
            {
                Text = $"Dobrý den,\n\n" +
                       $"děkujeme za váš zájem o komunitu Patrioti Trutnov. Vaši zprávu jsme úspěšně přijali a brzy se vám ozveme zpět.\n\n" +
                       $"Rekapitulace odeslaných údajů:\n" +
                       $"- Jméno: {lead.FullName}\n" +
                       $"- E-mail: {lead.Email}\n" +
                       $"- Telefon: {lead.Phone ?? "neuveden"}\n" +
                       $"- Téma: {lead.Topic ?? "neuvedeno"}\n\n" +
                       $"---\nS pozdravem,\nTým Patrioti Trutnov\nhttp://www.patriotitrutnov.cz/"
            };

            using (var client = new SmtpClient())
            {
                await client.ConnectAsync(smtpHost, smtpPort, MailKit.Security.SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(smtpUser, smtpPass);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
        }

        return Results.Ok(new { message = "Success" });
    }
    catch (Exception ex)
    {
        return Results.Problem("Error: " + ex.Message);
    }
});

app.Run();

public record LeadModel(string FullName, string Email, string? Phone, string? Topic);


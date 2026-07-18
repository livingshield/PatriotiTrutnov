using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using DotNetEnv;

var builder = WebApplication.CreateBuilder(args);

// Load .env from assembly base directory or fallback to current directory
string baseDir = AppContext.BaseDirectory;
string localEnv = Path.Combine(baseDir, ".env");
if (File.Exists(localEnv))
{
    Env.Load(localEnv);
}
else
{
    Env.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddCors();

var app = builder.Build();

// Handle nested IIS application paths (like /patriotitrutnov on test server)
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/patriotitrutnov", out var remainingPath))
    {
        context.Request.PathBase = "/patriotitrutnov";
        context.Request.Path = remainingPath;
    }
    await next();
});

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
                        Message TEXT,
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
                            Message NVARCHAR(MAX),
                            CreatedAt DATETIME DEFAULT GETDATE()
                        )
                    END";
                    
            using var command = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                ? (DbCommand)new MySqlCommand(createTableSql, (MySqlConnection)connection)
                : (DbCommand)new SqlCommand(createTableSql, (SqlConnection)connection);
                
            command.ExecuteNonQuery();
            
            // Safely add Message column if it is missing in the database table (due to incremental update)
            try
            {
                using var alterCmd = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                    ? (DbCommand)new MySqlCommand("ALTER TABLE patriotitrutnov_leads ADD COLUMN Message TEXT NULL;", (MySqlConnection)connection)
                    : (DbCommand)new SqlCommand(@"IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('patriotitrutnov_leads') AND name = 'Message')
                                                  ALTER TABLE patriotitrutnov_leads ADD Message NVARCHAR(MAX) NULL;", (SqlConnection)connection);
                alterCmd.ExecuteNonQuery();
            }
            catch
            {
                // Ignore if it already exists or fails
            }

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
    var fromName = "Patrioti Trutnov";

    // If SMTP_PASS is empty or matches the placeholder, fall back to config settings (using working scio@ekobio.org credentials)
    if (string.IsNullOrEmpty(smtpPass) || smtpPass == "DOPLNTE_HESLO_K_EMAILU_ZDE")
    {
        smtpHost = config["Smtp:Host"];
        smtpPort = int.TryParse(config["Smtp:Port"], out var fallbackPort) ? fallbackPort : 587;
        smtpUser = config["Smtp:Username"];
        smtpPass = config["Smtp:Password"];
        fromEmail = config["Smtp:FromEmail"] ?? "scio@ekobio.org";
    }

    // Admin address for BCC copy
    var adminEmail = Environment.GetEnvironmentVariable("TARGET_EMAIL") ?? fromEmail;

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
                string insertSql = "INSERT INTO patriotitrutnov_leads (FullName, Email, Phone, Topic, Message) VALUES (@FullName, @Email, @Phone, @Topic, @Message)";
                
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
                        myCmd.Parameters.AddWithValue("@Message", (object?)lead.Message ?? DBNull.Value);
                    }
                    else if (command is SqlCommand msCmd)
                    {
                        msCmd.Parameters.AddWithValue("@FullName", lead.FullName);
                        msCmd.Parameters.AddWithValue("@Email", lead.Email);
                        msCmd.Parameters.AddWithValue("@Phone", (object?)lead.Phone ?? DBNull.Value);
                        msCmd.Parameters.AddWithValue("@Topic", (object?)lead.Topic ?? DBNull.Value);
                        msCmd.Parameters.AddWithValue("@Message", (object?)lead.Message ?? DBNull.Value);
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

            var bodyBuilder = new BodyBuilder();
            
            // Build raw details row for message optionally
            string messageRow = "";
            if (!string.IsNullOrEmpty(lead.Message))
            {
                messageRow = $@"
                    <tr>
                        <td style='padding: 8px 0; color: #64748b; font-weight: 600; vertical-align: top;'>Zpráva / vzkaz:</td>
                        <td style='padding: 8px 0; color: #334155; line-height: 1.5; white-space: pre-line;'>{lead.Message}</td>
                    </tr>";
            }

            bodyBuilder.HtmlBody = $@"
                <div style='background-color: #f3f4f6; padding: 30px 15px; font-family: ""Segoe UI"", Helvetica, Arial, sans-serif;'>
                    <div style='max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.05); border: 1px solid #e5e7eb;'>
                        <!-- Header -->
                        <div style='background: linear-gradient(135deg, #0b132b 0%, #1c2541 100%); padding: 30px 20px; text-align: center;'>
                            <img src='https://www.patriotitrutnov.cz/img/PatriotiLogoBlack.png' alt='Patrioti Trutnov Logo' style='height: 90px; width: auto; display: block; margin: 0 auto 15px auto;'>
                            <h1 style='color: #ffffff; margin: 0; font-size: 22px; font-weight: 600; letter-spacing: 0.5px;'>Děkujeme za Váš zájem</h1>
                            <p style='color: #94a3b8; margin: 5px 0 0 0; font-size: 14px;'>Iniciativa Patrioti Trutnov</p>
                        </div>
                        
                        <!-- Content -->
                        <div style='padding: 30px 25px;'>
                            <p style='font-size: 16px; color: #1f2937; line-height: 1.6; margin-top: 0;'>
                                Dobrý den, <strong style='color: #0f172a;'>{lead.FullName}</strong>,<br><br>
                                velice si vážíme Vašeho zájmu o zapojení se do naší komunity <strong>Patrioti Trutnov</strong>. Vaše zpráva byla úspěšně doručena. Brzy se Vám ozveme zpět a domluvíme se na dalším postupu.
                            </p>
                            
                            <div style='margin: 25px 0; background-color: #f8fafc; border-radius: 8px; border: 1px solid #e2e8f0; padding: 20px;'>
                                <h3 style='margin-top: 0; color: #1e3a8a; font-size: 14px; text-transform: uppercase; letter-spacing: 0.5px; border-bottom: 1px solid #e2e8f0; padding-bottom: 8px;'>Rekapitulace odeslaných údajů</h3>
                                
                                <table style='width: 100%; border-collapse: collapse; font-size: 14px;'>
                                    <tr>
                                        <td style='padding: 8px 0; color: #64748b; width: 130px; font-weight: 600;'>Jméno:</td>
                                        <td style='padding: 8px 0; color: #0f172a;'>{lead.FullName}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px 0; color: #64748b; font-weight: 600;'>E-mail:</td>
                                        <td style='padding: 8px 0; color: #0f172a;'><a href='mailto:{lead.Email}' style='color: #2563eb; text-decoration: none;'>{lead.Email}</a></td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px 0; color: #64748b; font-weight: 600;'>Telefon:</td>
                                        <td style='padding: 8px 0; color: #0f172a;'>{lead.Phone ?? "neuveden"}</td>
                                    </tr>
                                    <tr>
                                        <td style='padding: 8px 0; color: #64748b; font-weight: 600;'>Zajímá mě:</td>
                                        <td style='padding: 8px 0; color: #1e3a8a; font-weight: 600;'>{lead.Topic ?? "neuvedeno"}</td>
                                    </tr>
                                    {messageRow}
                                </table>
                            </div>
                            
                            <p style='font-size: 15px; color: #475569; line-height: 1.6;'>
                                Těšíme se na spolupráci. Společně můžeme pro naše město udělat spoustu skvělých věcí!
                            </p>
                            
                            <div style='margin-top: 30px; padding-top: 20px; border-top: 1px solid #e2e8f0; color: #475569;'>
                                <p style='margin: 0; font-size: 14px; font-weight: 600;'>S pozdravem,</p>
                                <p style='margin: 3px 0 0 0; font-size: 15px; font-weight: 700; color: #1e3a8a;'>Tým Patrioti Trutnov</p>
                                <p style='margin: 5px 0 0 0; font-size: 13px;'><a href='https://www.patriotitrutnov.cz' style='color: #2563eb; text-decoration: none;'>www.patriotitrutnov.cz</a></p>
                            </div>
                        </div>
                        
                        <!-- Footer -->
                        <div style='background-color: #f8fafc; padding: 20px; text-align: center; border-top: 1px solid #e5e7eb; font-size: 12px; color: #94a3b8;'>
                            Tento e-mail byl automaticky vygenerován na základě Vašeho vyplnění formuláře na webu Patrioti Trutnov.
                        </div>
                    </div>
                </div>" ;

            message.Body = bodyBuilder.ToMessageBody();

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

public record LeadModel(string FullName, string Email, string? Phone, string? Topic, string? Message);


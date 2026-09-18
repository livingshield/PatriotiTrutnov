using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
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
            
            // 1. Leads Table
            string createLeadsSql = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                ? @"CREATE TABLE IF NOT EXISTS patriotitrutnov_leads (
                        Id INT AUTO_INCREMENT PRIMARY KEY,
                        FullName VARCHAR(200) NOT NULL,
                        Email VARCHAR(200) NOT NULL,
                        Phone VARCHAR(50),
                        Topic VARCHAR(500),
                        Message TEXT,
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                    );"
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
                        );
                    END";
                    
            using (var command = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                ? (DbCommand)new MySqlCommand(createLeadsSql, (MySqlConnection)connection)
                : (DbCommand)new SqlCommand(createLeadsSql, (SqlConnection)connection))
            {
                command.ExecuteNonQuery();
            }

            // Safely add Message column if it is missing in the database table (due to incremental update)
            try
            {
                using var alterCmd = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                    ? (DbCommand)new MySqlCommand("ALTER TABLE patriotitrutnov_leads ADD COLUMN Message TEXT NULL;", (MySqlConnection)connection)
                    : (DbCommand)new SqlCommand(@"IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('patriotitrutnov_leads') AND name = 'Message')
                                                  ALTER TABLE patriotitrutnov_leads ADD Message NVARCHAR(MAX) NULL;", (SqlConnection)connection);
                alterCmd.ExecuteNonQuery();
            }
            catch { }

            // 2. Settings Table (Key-Value)
            string createSettingsSql = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                ? @"CREATE TABLE IF NOT EXISTS patriotitrutnov_settings (
                        KeyName VARCHAR(100) PRIMARY KEY,
                        KeyValue TEXT NOT NULL,
                        UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                    );"
                : @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='patriotitrutnov_settings' AND xtype='U')
                    BEGIN
                        CREATE TABLE patriotitrutnov_settings (
                            KeyName NVARCHAR(100) PRIMARY KEY,
                            KeyValue NVARCHAR(MAX) NOT NULL,
                            UpdatedAt DATETIME DEFAULT GETDATE()
                        );
                    END";

            using (var settingsCmd = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                ? (DbCommand)new MySqlCommand(createSettingsSql, (MySqlConnection)connection)
                : (DbCommand)new SqlCommand(createSettingsSql, (SqlConnection)connection))
            {
                settingsCmd.ExecuteNonQuery();
            }

            // Seed default notification_emails if missing
            string checkDefaultSql = "SELECT COUNT(*) FROM patriotitrutnov_settings WHERE KeyName = 'notification_emails'";
            using (var checkCmd = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                ? (DbCommand)new MySqlCommand(checkDefaultSql, (MySqlConnection)connection)
                : (DbCommand)new SqlCommand(checkDefaultSql, (SqlConnection)connection))
            {
                long count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count == 0)
                {
                    string initialEmail = Environment.GetEnvironmentVariable("TARGET_EMAIL") ?? config["Smtp:TargetEmail"] ?? "info@patriotitrutnov.cz";
                    string seedSql = "INSERT INTO patriotitrutnov_settings (KeyName, KeyValue) VALUES ('notification_emails', @val);";
                    using var seedCmd = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                        ? (DbCommand)new MySqlCommand(seedSql, (MySqlConnection)connection)
                        : (DbCommand)new SqlCommand(seedSql, (SqlConnection)connection);
                    
                    var p = seedCmd.CreateParameter();
                    p.ParameterName = "@val";
                    p.Value = initialEmail;
                    seedCmd.Parameters.Add(p);
                    seedCmd.ExecuteNonQuery();
                }
            }

            Console.WriteLine($"[DB] {dbType} Database tables ready.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] Error ({dbType}): " + ex.Message);
        }
    }
}

// ==========================================
// Settings Helper Functions
// ==========================================
string settingsFilePath = Path.Combine(AppContext.BaseDirectory, "settings.json");

List<string> GetNotificationEmails(IConfiguration config)
{
    // 1. Try DB
    string? connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
    string dbType = Environment.GetEnvironmentVariable("DB_TYPE") ?? "MSSQL";
    if (!string.IsNullOrEmpty(connectionString))
    {
        try
        {
            using var connection = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                ? (DbConnection)new MySqlConnection(connectionString)
                : (DbConnection)new SqlConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT KeyValue FROM patriotitrutnov_settings WHERE KeyName = 'notification_emails'";
            var val = cmd.ExecuteScalar() as string;
            if (!string.IsNullOrWhiteSpace(val))
            {
                var emails = val.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(e => e.Trim())
                                .Where(e => !string.IsNullOrEmpty(e) && e.Contains("@"))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();
                if (emails.Count > 0) return emails;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[SETTINGS] Error reading from DB: " + ex.Message);
        }
    }

    // 2. Try settings.json file
    try
    {
        if (File.Exists(settingsFilePath))
        {
            var json = File.ReadAllText(settingsFilePath);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (parsed != null && parsed.TryGetValue("notification_emails", out var val) && !string.IsNullOrWhiteSpace(val))
            {
                var emails = val.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(e => e.Trim())
                                .Where(e => !string.IsNullOrEmpty(e) && e.Contains("@"))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();
                if (emails.Count > 0) return emails;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("[SETTINGS] Error reading settings.json: " + ex.Message);
    }

    // 3. Fallback to TARGET_EMAIL or default
    var fallback = Environment.GetEnvironmentVariable("TARGET_EMAIL") ?? config["Smtp:TargetEmail"] ?? "info@patriotitrutnov.cz";
    return fallback.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(e => e.Trim())
                   .Where(e => !string.IsNullOrEmpty(e) && e.Contains("@"))
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToList();
}

async Task SaveNotificationEmailsAsync(List<string> emails, IConfiguration config)
{
    string val = string.Join(",", emails.Select(e => e.Trim().ToLowerInvariant()).Where(e => !string.IsNullOrEmpty(e) && e.Contains("@")).Distinct());
    if (string.IsNullOrWhiteSpace(val))
    {
        val = "info@patriotitrutnov.cz";
    }

    // Save to settings.json
    try
    {
        var dict = new Dictionary<string, string>();
        if (File.Exists(settingsFilePath))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(settingsFilePath));
                if (existing != null) dict = existing;
            }
            catch { }
        }
        dict["notification_emails"] = val;
        await File.WriteAllTextAsync(settingsFilePath, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
    }
    catch (Exception ex)
    {
        Console.WriteLine("[SETTINGS] Error saving to settings.json: " + ex.Message);
    }

    // Save to DB
    string? connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
    string dbType = Environment.GetEnvironmentVariable("DB_TYPE") ?? "MSSQL";
    if (!string.IsNullOrEmpty(connectionString))
    {
        try
        {
            using var connection = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                ? (DbConnection)new MySqlConnection(connectionString)
                : (DbConnection)new SqlConnection(connectionString);
            await connection.OpenAsync();

            string upsertSql = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                ? @"INSERT INTO patriotitrutnov_settings (KeyName, KeyValue, UpdatedAt) 
                    VALUES ('notification_emails', @Val, NOW()) 
                    ON DUPLICATE KEY UPDATE KeyValue = @Val, UpdatedAt = NOW();"
                : @"IF EXISTS (SELECT 1 FROM patriotitrutnov_settings WHERE KeyName = 'notification_emails')
                        UPDATE patriotitrutnov_settings SET KeyValue = @Val, UpdatedAt = GETDATE() WHERE KeyName = 'notification_emails';
                    ELSE
                        INSERT INTO patriotitrutnov_settings (KeyName, KeyValue, UpdatedAt) VALUES ('notification_emails', @Val, GETDATE());";

            using var cmd = connection.CreateCommand();
            cmd.CommandText = upsertSql;
            var param = cmd.CreateParameter();
            param.ParameterName = "@Val";
            param.Value = val;
            cmd.Parameters.Add(param);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[SETTINGS] Error saving to DB: " + ex.Message);
        }
    }
}

// ==========================================
// Admin Authentication Helper Functions
// ==========================================
string GetAdminSecret(IConfiguration config) =>
    Environment.GetEnvironmentVariable("ADMIN_JWT_SECRET") ?? config["Authentication:AdminSecret"] ?? "PatriotiTrutnovSecretAdminKey2026_SecureHmac";

bool ValidateAdmin(string? username, string? password, IConfiguration config)
{
    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return false;
    string rawAdmins = Environment.GetEnvironmentVariable("ADMIN_USERS") ?? config["Authentication:AdminUsers"] ?? "admin:patrioti2026,jankytyr:brzsilpot7";
    var pairs = rawAdmins.Split(',', StringSplitOptions.RemoveEmptyEntries);
    foreach (var pair in pairs)
    {
        var parts = pair.Trim().Split(':', 2);
        if (parts.Length == 2 && parts[0].Equals(username.Trim(), StringComparison.OrdinalIgnoreCase) && parts[1] == password)
        {
            return true;
        }
    }
    return false;
}

string GenerateAdminToken(string username, IConfiguration config)
{
    string secret = GetAdminSecret(config);
    long timestamp = DateTime.UtcNow.Ticks;
    string payload = $"{username}|{timestamp}";
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    string sig = Convert.ToBase64String(hash);
    return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}|{sig}"));
}

bool ValidateAdminToken(string? token, IConfiguration config, out string username)
{
    username = "";
    if (string.IsNullOrWhiteSpace(token)) return false;
    try
    {
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token.Substring(7).Trim();
        }

        string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split('|', 3);
        if (parts.Length != 3) return false;

        string user = parts[0];
        long timestamp = long.Parse(parts[1]);
        string sig = parts[2];

        // Valid for 7 days
        if (TimeSpan.FromTicks(DateTime.UtcNow.Ticks - timestamp).TotalDays > 7) return false;

        string secret = GetAdminSecret(config);
        string payload = $"{user}|{timestamp}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        string expectedSig = Convert.ToBase64String(hash);

        if (sig == expectedSig)
        {
            username = user;
            return true;
        }
    }
    catch { }
    return false;
}

// Redirect /admin to /admin.html
app.MapGet("/admin", (HttpContext ctx) =>
{
    var pathBase = ctx.Request.PathBase.Value ?? "";
    return Results.Redirect(string.IsNullOrEmpty(pathBase) ? "/admin.html" : $"{pathBase}/admin.html");
});

// ==========================================
// Admin API Endpoints
// ==========================================
app.MapPost("/api/admin/login", ([FromBody] AdminLoginRequest req, IConfiguration config) =>
{
    if (ValidateAdmin(req.Username, req.Password, config))
    {
        string token = GenerateAdminToken(req.Username.Trim(), config);
        return Results.Ok(new { success = true, token, username = req.Username.Trim() });
    }
    return Results.Json(new { success = false, message = "Neplatné přihlašovací údaje." }, statusCode: 401);
});

app.MapGet("/api/admin/check", (HttpRequest request, IConfiguration config) =>
{
    string? authHeader = request.Headers["Authorization"].FirstOrDefault();
    if (ValidateAdminToken(authHeader, config, out string username))
    {
        return Results.Ok(new { valid = true, username });
    }
    return Results.Json(new { valid = false, message = "Neplatný nebo vypršený token." }, statusCode: 401);
});

app.MapGet("/api/admin/settings", (HttpRequest request, IConfiguration config) =>
{
    string? authHeader = request.Headers["Authorization"].FirstOrDefault();
    if (!ValidateAdminToken(authHeader, config, out _))
    {
        return Results.Json(new { message = "Neautorizovaný přístup." }, statusCode: 401);
    }

    var emails = GetNotificationEmails(config);
    return Results.Ok(new { notification_emails = emails });
});

app.MapPost("/api/admin/settings", async ([FromBody] AdminSettingsUpdateRequest req, HttpRequest request, IConfiguration config) =>
{
    string? authHeader = request.Headers["Authorization"].FirstOrDefault();
    if (!ValidateAdminToken(authHeader, config, out _))
    {
        return Results.Json(new { message = "Neautorizovaný přístup." }, statusCode: 401);
    }

    if (req.NotificationEmails == null || req.NotificationEmails.Count == 0)
    {
        req.NotificationEmails = new List<string> { "info@patriotitrutnov.cz" };
    }

    await SaveNotificationEmailsAsync(req.NotificationEmails, config);
    var updated = GetNotificationEmails(config);
    return Results.Ok(new { success = true, notification_emails = updated });
});

app.MapGet("/api/admin/leads", async (HttpRequest request, IConfiguration config) =>
{
    string? authHeader = request.Headers["Authorization"].FirstOrDefault();
    if (!ValidateAdminToken(authHeader, config, out _))
    {
        return Results.Json(new { message = "Neautorizovaný přístup." }, statusCode: 401);
    }

    var list = new List<object>();
    string? connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
    string dbType = Environment.GetEnvironmentVariable("DB_TYPE") ?? "MSSQL";

    if (!string.IsNullOrEmpty(connectionString))
    {
        try
        {
            using var connection = dbType.Equals("MYSQL", StringComparison.OrdinalIgnoreCase)
                ? (DbConnection)new MySqlConnection(connectionString)
                : (DbConnection)new SqlConnection(connectionString);
            await connection.OpenAsync();

            string query = "SELECT Id, FullName, Email, Phone, Topic, Message, CreatedAt FROM patriotitrutnov_leads ORDER BY Id DESC";
            using var cmd = connection.CreateCommand();
            cmd.CommandText = query;
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new
                {
                    Id = reader.GetInt32(0),
                    FullName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Email = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Phone = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Topic = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Message = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    CreatedAt = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6)
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ADMIN LEADS] Error: " + ex.Message);
        }
    }

    return Results.Ok(list);
});

// ==========================================
// Public Lead Submission Endpoint
// ==========================================
app.MapPost("/api/leads", async (LeadModel lead, IConfiguration config) =>
{
    var smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST") ?? config["Smtp:Host"] ?? "smtp.forpsi.com";
    var smtpPort = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var port) ? port : (int.TryParse(config["Smtp:Port"], out var p) ? p : 587);
    var smtpUser = Environment.GetEnvironmentVariable("SMTP_USER") ?? config["Smtp:Username"] ?? "postmaster@patriotitrutnov.cz";
    var smtpPass = Environment.GetEnvironmentVariable("SMTP_PASS") ?? config["Smtp:Password"] ?? "R.mnEtu6Xn";
    
    // Sender address: must ALWAYS be info@patriotitrutnov.cz
    var fromEmail = "info@patriotitrutnov.cz";
    var fromName = "Patrioti Trutnov";

    // If SMTP_PASS is empty or matches placeholder, fall back to postmaster credentials
    if (string.IsNullOrEmpty(smtpPass) || smtpPass == "DOPLNTE_HESLO_K_EMAILU_ZDE")
    {
        smtpHost = config["Smtp:Host"] ?? "smtp.forpsi.com";
        smtpPort = int.TryParse(config["Smtp:Port"], out var fallbackPort) ? fallbackPort : 587;
        smtpUser = config["Smtp:Username"] ?? "postmaster@patriotitrutnov.cz";
        smtpPass = config["Smtp:Password"] ?? "R.mnEtu6Xn";
    }

    // Retrieve recipient email list configured in administration
    var recipientEmails = GetNotificationEmails(config);
    if (recipientEmails.Count == 0)
    {
        recipientEmails.Add("info@patriotitrutnov.cz");
    }

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

        // 2. Send Emails via SMTP
        if (smtpUser != null && smtpPass != null && smtpHost != null)
        {
            using var client = new SmtpClient();
            client.ServerCertificateValidationCallback = (s, c, h, e) => true;
            await client.ConnectAsync(smtpHost, smtpPort, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(smtpUser, smtpPass);

            string messageRow = "";
            if (!string.IsNullOrEmpty(lead.Message))
            {
                messageRow = $@"
                    <tr>
                        <td style='padding: 8px 0; color: #64748b; font-weight: 600; vertical-align: top;'>Zpráva / vzkaz:</td>
                        <td style='padding: 8px 0; color: #334155; line-height: 1.5; white-space: pre-line; word-break: break-word; overflow-wrap: break-word;'>{lead.Message}</td>
                    </tr>";
            }

            // A) Incoming notification email for administrators (with requested prefix PatriotiTrutnov_)
            try
            {
                var adminMessage = new MimeMessage();
                adminMessage.From.Add(new MailboxAddress(fromName, fromEmail));
                if (!string.IsNullOrWhiteSpace(lead.Email))
                {
                    adminMessage.ReplyTo.Add(new MailboxAddress(lead.FullName, lead.Email));
                }

                foreach (var rec in recipientEmails)
                {
                    adminMessage.To.Add(new MailboxAddress("Správce Patrioti Trutnov", rec));
                }

                string topicSubject = string.IsNullOrWhiteSpace(lead.Topic) ? "Zpráva z webu" : lead.Topic;
                adminMessage.Subject = $"PatriotiTrutnov_ Nová zpráva: {lead.FullName} ({topicSubject})";

                var adminBody = new BodyBuilder();
                adminBody.HtmlBody = $@"
                    <div style='background-color: #0b132b; padding: 30px 15px; font-family: ""Segoe UI"", Helvetica, Arial, sans-serif;'>
                        <div style='max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.2);'>
                            <div style='background: linear-gradient(135deg, #0b132b 0%, #1c2541 100%); padding: 25px 20px; text-align: center; border-bottom: 3px solid #d4af37;'>
                                <span style='display: inline-block; background-color: #d4af37; color: #0b132b; font-weight: 800; font-size: 11px; padding: 4px 10px; border-radius: 20px; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 8px;'>Nová zpráva z webu</span>
                                <h2 style='color: #ffffff; margin: 0; font-size: 20px;'>PatriotiTrutnov - Kontaktní formulář</h2>
                            </div>
                            <div style='padding: 25px;'>
                                <p style='font-size: 15px; color: #334155; margin-top: 0;'>
                                    Na webu <strong>patriotitrutnov.cz</strong> byla odeslána nová zpráva od občana:
                                </p>
                                <div style='background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 18px; margin: 20px 0;'>
                                    <table style='width: 100%; border-collapse: collapse; font-size: 14px;'>
                                        <tr>
                                            <td style='padding: 8px 0; color: #64748b; width: 130px; font-weight: 600;'>Jméno:</td>
                                            <td style='padding: 8px 0; color: #0f172a; font-weight: 700;'>{lead.FullName}</td>
                                        </tr>
                                        <tr>
                                            <td style='padding: 8px 0; color: #64748b; font-weight: 600;'>E-mail:</td>
                                            <td style='padding: 8px 0; color: #0f172a;'><a href='mailto:{lead.Email}' style='color: #2563eb; font-weight: 600; text-decoration: none;'>{lead.Email}</a></td>
                                        </tr>
                                        <tr>
                                            <td style='padding: 8px 0; color: #64748b; font-weight: 600;'>Telefon:</td>
                                            <td style='padding: 8px 0; color: #0f172a;'><a href='tel:{lead.Phone}' style='color: #0f172a; text-decoration: none;'>{lead.Phone ?? "neuveden"}</a></td>
                                        </tr>
                                        <tr>
                                            <td style='padding: 8px 0; color: #64748b; font-weight: 600;'>Téma / Zájem:</td>
                                            <td style='padding: 8px 0; color: #1e3a8a; font-weight: 700;'>{lead.Topic ?? "neuvedeno"}</td>
                                        </tr>
                                        {messageRow}
                                        <tr>
                                            <td style='padding: 8px 0; color: #64748b; font-weight: 600;'>Datum a čas:</td>
                                            <td style='padding: 8px 0; color: #64748b;'>{DateTime.Now:dd.MM.yyyy HH:mm}</td>
                                        </tr>
                                    </table>
                                </div>
                                <div style='text-align: center; margin-top: 25px;'>
                                    <a href='mailto:{lead.Email}?subject=Re:%20Patrioti%20Trutnov' style='display: inline-block; background-color: #1e3a8a; color: #ffffff; padding: 12px 24px; border-radius: 6px; text-decoration: none; font-weight: 600; font-size: 14px;'>Odpovědět občanovi</a>
                                </div>
                            </div>
                            <div style='background-color: #f1f5f9; padding: 15px; text-align: center; font-size: 12px; color: #64748b; border-top: 1px solid #e2e8f0;'>
                                Tato notifikace byla automaticky odeslána na e-mailové adresy nastavené v administraci Patrioti Trutnov.
                            </div>
                        </div>
                    </div>";

                adminMessage.Body = adminBody.ToMessageBody();
                await client.SendAsync(adminMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SMTP ADMIN ERROR] " + ex.Message);
            }

            // B) Confirmation email for the citizen
            try
            {
                var citizenMessage = new MimeMessage();
                citizenMessage.From.Add(new MailboxAddress(fromName, fromEmail));
                citizenMessage.ReplyTo.Add(new MailboxAddress(fromName, fromEmail));
                citizenMessage.To.Add(new MailboxAddress(lead.FullName, lead.Email));
                citizenMessage.Subject = "Potvrzení přijetí zprávy | Patrioti Trutnov";

                var citizenBody = new BodyBuilder();
                citizenBody.HtmlBody = $@"
                    <div style='background-color: #f3f4f6; padding: 30px 15px; font-family: ""Segoe UI"", Helvetica, Arial, sans-serif;'>
                        <div style='max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.05); border: 1px solid #e5e7eb; word-break: break-word; overflow-wrap: break-word;'>
                            <div style='background: linear-gradient(135deg, #0b132b 0%, #1c2541 100%); padding: 30px 20px; text-align: center;'>
                                <img src='https://www.patriotitrutnov.cz/img/PatriotiLogoBlack.png' alt='Patrioti Trutnov Logo' style='height: 90px; width: auto; display: block; margin: 0 auto 15px auto;'>
                                <h1 style='color: #ffffff; margin: 0; font-size: 22px; font-weight: 600; letter-spacing: 0.5px;'>Děkujeme za Váš zájem</h1>
                                <p style='color: #94a3b8; margin: 5px 0 0 0; font-size: 14px;'>Iniciativa Patrioti Trutnov</p>
                            </div>
                            
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
                                    <p style='margin: 5px 0 0 0; font-size: 13px;'><a href='https://www.patriotitrutnov.cz' style='color: #2563eb; text-decoration: none;'>www.patriotitrutnov.cz</a> | <a href='mailto:info@patriotitrutnov.cz' style='color: #2563eb; text-decoration: none;'>info@patriotitrutnov.cz</a> | <a href='https://www.facebook.com/patriotitrutnov' style='color: #1877f2; text-decoration: none; font-weight: 600;'>Facebook</a></p>
                                </div>
                            </div>
                            
                            <div style='background-color: #f8fafc; padding: 20px; text-align: center; border-top: 1px solid #e5e7eb; font-size: 12px; color: #94a3b8;'>
                                Tento e-mail byl automaticky vygenerován na základě Vašeho vyplnění formuláře na webu Patrioti Trutnov.
                            </div>
                        </div>
                    </div>";

                citizenMessage.Body = citizenBody.ToMessageBody();
                await client.SendAsync(citizenMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SMTP CITIZEN ERROR] " + ex.Message);
            }

            await client.DisconnectAsync(true);
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
public record AdminLoginRequest(string Username, string Password);
public class AdminSettingsUpdateRequest
{
    public List<string>? NotificationEmails { get; set; }
}


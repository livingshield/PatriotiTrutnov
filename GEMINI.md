# Instrukce projektu / Project Instructions

## Pravidla nasazování / Deployment Rules (ASPONE Hosting)

> [!CRITICAL]
> **PŘÍSNÝ ZÁKAZ NAHRÁVÁNÍ SOUBORŮ .EXE NA FTP ASPONE:**
> 1. Na FTP server ASPONE (windows11.aspone.cz ani jakýkoliv jiný server poskytovatele ASPONE) se **za žádných okolností nesmí nahrávat soubory s příponou .exe** (včetně přejmenovaných jako *.exe.bak apod.).
> 2. Poskytovatel hostingu ASPONE má přísná bezpečnostní pravidla zakazující přítomnost jakýchkoliv spustitelných .exe souborů.
> 3. **Pro .NET aplikace:** Na IIS hostingu ASPONE se aplikace spouští výhradně přes ASP.NET Core modul pomocí .dll knihoven. Výchozí konzolový launcher *.exe generovaný kompilátorem se na FTP **NIKDY** nenahrává.
> 4. **Pro externí nástroje (FFmpeg, yt-dlp apod.):** Na serveru se nespouští žádné .exe binárky. Vše musí běžet buď na straně klienta (např. WebAssembly v prohlížeči), nebo přes čisté .NET/C# knihovny (např. YoutubeExplode.dll).
> 5. **Deploy skripty:** Jakýkoliv nasazovací skript (PowerShell, bash, CI/CD) musí mít explicitní filtr vylučující jakékoliv *.exe soubory.

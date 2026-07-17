# Load environment variables from .env.prod if present
$envFile = "$PSScriptRoot\.env.prod"
if (Test-Path $envFile) {
    Get-Content $envFile | Where-Object { $_ -match '=' -and $_ -notmatch '^#' } | ForEach-Object {
        $name, $value = $_ -split '=', 2
        $name = $name.Trim()
        $value = $value.Trim().Trim('"').Trim("'")
        Set-Variable -Name $name -Value $value -Scope Script
    }
}

# Configuration
$ftpHost = if ($FTP_HOST) { $FTP_HOST } else { "d112wh.forpsi.com" }
$user = if ($FTP_USER) { $FTP_USER } else { "f0466488.multi" }
$pass = if ($FTP_PASS) { $FTP_PASS } else { "A39JSxTA-E" }
$projectName = "patriotitrutnov"
$localPath = "$PSScriptRoot\publish"
$remoteBase = if ($FTP_REMOTE_BASE) { $FTP_REMOTE_BASE } else { "subwebs/patriotitrutnov.cz/" }

function Upload-File($localFile, $remoteFile) {
    Write-Host "  $($localFile | Split-Path -Leaf) -> $remoteFile"
    try {
        $webClient = New-Object System.Net.WebClient
        $webClient.Credentials = New-Object System.Net.NetworkCredential($user, $pass)
        $uri = New-Object System.Uri("ftp://$ftpHost/$remoteFile")
        $webClient.UploadFile($uri, $localFile)
    }
    catch {
        Write-Host "    ERROR: $($_.Exception.Message)"
    }
}

function Ensure-RemoteDir($remotePath) {
    try {
        $req = [System.Net.WebRequest]::Create("ftp://$ftpHost/$remotePath")
        $req.Credentials = New-Object System.Net.NetworkCredential($user, $pass)
        $req.Method = [System.Net.WebRequestMethods+Ftp]::MakeDirectory
        $req.GetResponse().Close()
    }
    catch {}
}

function Delete-RemoteFile($remoteFile) {
    try {
        $req = [System.Net.WebRequest]::Create("ftp://$ftpHost/$remoteFile")
        $req.Credentials = New-Object System.Net.NetworkCredential($user, $pass)
        $req.Method = [System.Net.WebRequestMethods+Ftp]::DeleteFile
        $resp = $req.GetResponse()
        $resp.Close()
    }
    catch {
        Write-Host "    Warning: Could not delete $remoteFile. $($_.Exception.Message)"
    }
}

function Upload-Folder($localPath, $remotePath) {
    $items = Get-ChildItem $localPath
    foreach ($item in $items) {
        $target = $remotePath + $item.Name
        if ($item.PSIsContainer) {
            Ensure-RemoteDir $target
            Upload-Folder $item.FullName ($target + "/")
        }
        else {
            Upload-File $item.FullName $target
        }
    }
}

Write-Host "=== Deploying $projectName to PRODUCTION FTP (Forpsi) ==="

# We need to copy .env.prod into the publish folder as .env so that it is deployed to production
Write-Host "Preparing environment variables..."
Copy-Item "$PSScriptRoot\.env.prod" "$PSScriptRoot\.env" -Force

Write-Host "Step 1: Publishing project..."
dotnet publish -c Release -o publish

Write-Host "Step 2: Uploading to FTP..."
# Create temporary app_offline.htm locally
$appOfflinePath = "$localPath\app_offline.htm"
$appOfflineContent = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <title>Aktualizace webu</title>
</head>
<body style="font-family: sans-serif; text-align: center; margin-top: 100px; background: #0a0f1a; color: #f1f5f9;">
    <h2>Webové stránky právě aktualizujeme</h2>
    <p>Nahráváme novou verzi. Prosíme o strpení, za chvíli budeme zpět online.</p>
</body>
</html>
"@
Set-Content -Path $appOfflinePath -Value $appOfflineContent -Encoding utf8

# Upload app_offline.htm first to shut down the app and unlock DLLs
Write-Host "Putting app offline to release file locks..."
Ensure-RemoteDir $remoteBase
Upload-File $appOfflinePath ($remoteBase + "app_offline.htm")
Write-Host "Waiting 5 seconds for IIS process to shutdown..."
Start-Sleep -Seconds 5

# Upload folder contents
Upload-Folder $localPath $remoteBase

# Delete app_offline.htm from FTP to bring the site back online
Write-Host "Bringing app online..."
Delete-RemoteFile ($remoteBase + "app_offline.htm")

# Clean up local temporary file
if (Test-Path $appOfflinePath) { Remove-Item $appOfflinePath -Force }

Write-Host ""
Write-Host "=== Deploy complete! ==="
Write-Host "URL: http://patriotitrutnov.cz/"

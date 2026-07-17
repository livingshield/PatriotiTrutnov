# Load environment variables from .env.test if present
$envFile = "$PSScriptRoot\.env.test"
if (Test-Path $envFile) {
    Get-Content $envFile | Where-Object { $_ -match '=' -and $_ -notmatch '^#' } | ForEach-Object {
        $name, $value = $_ -split '=', 2
        $name = $name.Trim()
        $value = $value.Trim().Trim('"').Trim("'")
        Set-Variable -Name $name -Value $value -Scope Script
    }
}

# Configuration
$ftpHost = if ($FTP_HOST) { $FTP_HOST } else { "windows11.aspone.cz" }
$user = if ($FTP_USER) { $FTP_USER } else { "EkoBio.org_lordkikin" }
$pass = if ($FTP_PASS) { $FTP_PASS } else { "Brzsilpot7!" }
$projectName = "patriotitrutnov"
$localPath = "$PSScriptRoot\publish"
$remoteBase = if ($FTP_REMOTE_BASE) { $FTP_REMOTE_BASE } else { "www/wwwroot/$projectName/" }

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

Write-Host "=== Deploying $projectName to TEST FTP (Aspone) ==="

# We need to copy .env.test into the publish folder as .env so that it is deployed to production
Write-Host "Preparing environment variables..."
Copy-Item "$PSScriptRoot\.env.test" "$PSScriptRoot\.env" -Force

Write-Host "Step 1: Publishing project..."
dotnet publish -c Release -o publish

Write-Host "Step 2: Uploading to FTP..."
Ensure-RemoteDir $remoteBase
Upload-Folder $localPath $remoteBase

Write-Host ""
Write-Host "=== Deploy complete! ==="
Write-Host "URL: https://www.ekobio.org/$projectName/"

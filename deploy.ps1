Write-Host ""
Write-Host "=========================================="
Write-Host "  PATRIOTI TRUTNOV - MULTI-DEPLOYMENT     "
Write-Host "=========================================="
Write-Host "Spoustim nasazeni na obe prostredi soucasne..."
Write-Host ""

Write-Host ">>> KROK 1: Nasazeni na TEST (Aspone) <<<"
powershell -ExecutionPolicy Bypass -File "$PSScriptRoot\deploy-test.ps1"

Write-Host ""
Write-Host ">>> KROK 2: Nasazeni na PRODUKCI (Forpsi) <<<"
powershell -ExecutionPolicy Bypass -File "$PSScriptRoot\deploy-prod.ps1"

Write-Host ""
Write-Host "=== Obes nasazeni byla dokoncena! ==="




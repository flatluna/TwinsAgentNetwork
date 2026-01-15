# =======================================================
# ?? Script de Pruebas - AgentTwinCommunicate
# =======================================================

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  ?? TESTS: AgentTwinCommunicate" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Navegar al directorio de tests
$testsPath = "C:\TwinSourceServer\TwinAIFunctions\TwinAgentsNetworkTests"
Set-Location $testsPath

Write-Host "?? Directorio: $testsPath" -ForegroundColor Gray
Write-Host ""

# Función para ejecutar un grupo de tests
function Run-TestGroup {
    param(
        [string]$GroupName,
        [string]$Filter,
        [string]$Color = "Yellow"
    )
    
    Write-Host "????????????????????????????????????????" -ForegroundColor $Color
    Write-Host "  $GroupName" -ForegroundColor $Color
    Write-Host "????????????????????????????????????????" -ForegroundColor $Color
    Write-Host ""
    
    dotnet test --filter $Filter --logger "console;verbosity=normal"
    
    Write-Host ""
}

# Menú interactivo
Write-Host "Selecciona qué ejecutar:" -ForegroundColor White
Write-Host ""
Write-Host "  [1] ?? Tests Básicos (3 tests - ~5 seg)" -ForegroundColor Green
Write-Host "  [2] ?? Tests Intermedios (3 tests - ~18 seg)" -ForegroundColor Yellow
Write-Host "  [3] ?? Tests Avanzados (3 tests - ~40 seg)" -ForegroundColor Red
Write-Host "  [4] ? Test Completo Individual (Test09)" -ForegroundColor Magenta
Write-Host "  [5] ?? TODOS los tests (10 tests - ~60 seg)" -ForegroundColor Cyan
Write-Host "  [0] ? Salir" -ForegroundColor Gray
Write-Host ""

$choice = Read-Host "Ingresa tu opción (0-5)"

switch ($choice) {
    "1" {
        Write-Host ""
        Run-TestGroup "?? TESTS BÁSICOS" "Test01|Test03|Test07" "Green"
    }
    "2" {
        Write-Host ""
        Run-TestGroup "?? TESTS INTERMEDIOS" "Test04|Test05|Test08" "Yellow"
    }
    "3" {
        Write-Host ""
        Run-TestGroup "?? TESTS AVANZADOS" "Test06|Test09|Test10" "Red"
    }
    "4" {
        Write-Host ""
        Run-TestGroup "? TEST COMPLETO" "Test09_CompleteWorkflow" "Magenta"
    }
    "5" {
        Write-Host ""
        Run-TestGroup "?? TODOS LOS TESTS" "AgentTwinCommunicateTests" "Cyan"
    }
    "0" {
        Write-Host ""
        Write-Host "?? ¡Hasta luego!" -ForegroundColor Gray
        Write-Host ""
        exit
    }
    default {
        Write-Host ""
        Write-Host "? Opción inválida" -ForegroundColor Red
        Write-Host ""
        exit
    }
}

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  ? Tests completados" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Preguntar si quiere ver el reporte
$viewReport = Read-Host "¿Ver reporte detallado? (s/n)"

if ($viewReport -eq "s") {
    Write-Host ""
    Write-Host "Ejecutando con logs detallados..." -ForegroundColor Gray
    Write-Host ""
    
    dotnet test --filter "AgentTwinCommunicateTests" --logger "console;verbosity=detailed"
}

Write-Host ""
Write-Host "?? ¡Listo!" -ForegroundColor Green
Write-Host ""

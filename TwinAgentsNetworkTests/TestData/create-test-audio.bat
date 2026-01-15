@echo off
echo.
echo ========================================
echo   Crear Archivo de Audio de Prueba
echo ========================================
echo.

REM Verificar si PowerShell está disponible
where powershell >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: PowerShell no encontrado
    echo Por favor instala PowerShell
    pause
    exit /b 1
)

REM Ejecutar el script de PowerShell
powershell -ExecutionPolicy Bypass -File "%~dp0create-test-audio.ps1"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================
    echo   Proceso completado
    echo ========================================
) else (
    echo.
    echo ========================================
    echo   Error durante el proceso
    echo ========================================
)

echo.
pause

# Script para crear un archivo de audio de prueba
# Este script genera un archivo WAV simple con un tono de prueba

$outputPath = "C:\Data\Recording.wav"
$testDataPath = "$PSScriptRoot\Recording.wav"

Write-Host "?? Creando archivo de audio de prueba..." -ForegroundColor Cyan

# Crear directorio si no existe
$outputDir = Split-Path $outputPath -Parent
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    Write-Host "?? Directorio creado: $outputDir" -ForegroundColor Green
}

# Verificar si ya existe un archivo de prueba en TestData
if (Test-Path $testDataPath) {
    Write-Host "?? Copiando archivo de prueba existente..." -ForegroundColor Yellow
    Copy-Item $testDataPath $outputPath -Force
    Write-Host "? Archivo copiado: $outputPath" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "??  No se encontró un archivo de prueba en TestData" -ForegroundColor Yellow
Write-Host ""
Write-Host "?? Opciones para crear un archivo de prueba:" -ForegroundColor Cyan
Write-Host ""
Write-Host "OPCIÓN 1: Grabar tu propia voz (RECOMENDADO)" -ForegroundColor Green
Write-Host "   1. Abre la aplicación 'Grabadora de Voz' de Windows" -ForegroundColor White
Write-Host "   2. Graba algo en español, por ejemplo: 'Hola, esta es una prueba de transcripción'" -ForegroundColor White
Write-Host "   3. Guarda el archivo como: $outputPath" -ForegroundColor White
Write-Host ""

Write-Host "OPCIÓN 2: Usar archivo existente" -ForegroundColor Green
Write-Host "   Si tienes un archivo WAV, cópialo manualmente a:" -ForegroundColor White
Write-Host "   $outputPath" -ForegroundColor White
Write-Host ""

Write-Host "OPCIÓN 3: Generar audio sintético con PowerShell" -ForegroundColor Green
$response = Read-Host "¿Deseas generar un archivo WAV sintético con un tono de prueba? (S/N)"

if ($response -eq 'S' -or $response -eq 's') {
    Write-Host "?? Generando archivo WAV sintético..." -ForegroundColor Cyan
    
    # Generar un archivo WAV simple (16 kHz, 16-bit, mono)
    # Encabezado WAV estándar
    $sampleRate = 16000
    $duration = 3  # 3 segundos
    $numSamples = $sampleRate * $duration
    $bitsPerSample = 16
    $numChannels = 1
    $byteRate = $sampleRate * $numChannels * ($bitsPerSample / 8)
    $blockAlign = $numChannels * ($bitsPerSample / 8)
    $dataSize = $numSamples * $blockAlign
    
    # Crear array de bytes para el archivo WAV
    $wav = New-Object System.Collections.Generic.List[byte]
    
    # RIFF header
    $wav.AddRange([System.Text.Encoding]::ASCII.GetBytes("RIFF"))
    $wav.AddRange([System.BitConverter]::GetBytes([int]($dataSize + 36)))
    $wav.AddRange([System.Text.Encoding]::ASCII.GetBytes("WAVE"))
    
    # fmt chunk
    $wav.AddRange([System.Text.Encoding]::ASCII.GetBytes("fmt "))
    $wav.AddRange([System.BitConverter]::GetBytes([int]16))  # Subchunk1Size
    $wav.AddRange([System.BitConverter]::GetBytes([short]1)) # AudioFormat (PCM)
    $wav.AddRange([System.BitConverter]::GetBytes([short]$numChannels))
    $wav.AddRange([System.BitConverter]::GetBytes([int]$sampleRate))
    $wav.AddRange([System.BitConverter]::GetBytes([int]$byteRate))
    $wav.AddRange([System.BitConverter]::GetBytes([short]$blockAlign))
    $wav.AddRange([System.BitConverter]::GetBytes([short]$bitsPerSample))
    
    # data chunk header
    $wav.AddRange([System.Text.Encoding]::ASCII.GetBytes("data"))
    $wav.AddRange([System.BitConverter]::GetBytes([int]$dataSize))
    
    # Generar audio (tono de 440 Hz - nota La)
    Write-Host "?? Generando tono de 440 Hz (nota La)..." -ForegroundColor Yellow
    $frequency = 440.0
    $amplitude = 16000  # 50% del rango 16-bit
    
    for ($i = 0; $i -lt $numSamples; $i++) {
        $t = $i / $sampleRate
        $value = [Math]::Sin(2 * [Math]::PI * $frequency * $t) * $amplitude
        $sample = [System.BitConverter]::GetBytes([short]$value)
        $wav.AddRange($sample)
    }
    
    # Guardar archivo
    [System.IO.File]::WriteAllBytes($outputPath, $wav.ToArray())
    
    Write-Host "? Archivo WAV sintético creado: $outputPath" -ForegroundColor Green
    Write-Host "?? Duración: $duration segundos" -ForegroundColor Cyan
    Write-Host "?? Sample Rate: $sampleRate Hz" -ForegroundColor Cyan
    Write-Host "?? Tamaño: $($wav.Count) bytes" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "??  NOTA: Este es un tono puro sin voz, no se transcribirá texto" -ForegroundColor Yellow
    Write-Host "   Para probar la transcripción de voz, usa la OPCIÓN 1 (grabar tu voz)" -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "??  Generación cancelada" -ForegroundColor Yellow
    Write-Host "   Por favor, crea manualmente un archivo WAV con voz en: $outputPath" -ForegroundColor White
}

Write-Host ""
Write-Host "?? Ubicación del archivo: $outputPath" -ForegroundColor Cyan
Write-Host ""

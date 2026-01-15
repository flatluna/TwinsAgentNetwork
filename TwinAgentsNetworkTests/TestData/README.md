# ?? Test Data for AgentTelefonia Tests

Este directorio contiene archivos de prueba para los tests de `AgentTelefonia`.

## ?? Archivos Requeridos

### Recording.wav
- **Ubicación:** `C:\Data\Recording.wav`
- **Formato:** WAV, 16 kHz, 16-bit, mono
- **Contenido:** Audio con voz en español o inglés
- **Duración:** 3-10 segundos recomendado

## ?? Cómo Crear el Archivo de Prueba

### Opción 1: Ejecutar el Script (Recomendado)

```powershell
cd TwinAgentsNetworkTests\TestData
.\create-test-audio.ps1
```

El script te guiará por las opciones disponibles.

### Opción 2: Grabar Manualmente (Mejor para Transcripción Real)

1. **Abrir Grabadora de Voz de Windows:**
   - Presiona `Win + S`
   - Busca "Grabadora de Voz" o "Voice Recorder"
   - Abre la aplicación

2. **Grabar Audio:**
   - Haz clic en el botón de grabar (??)
   - Di algo en español, por ejemplo:
     - "Hola, esta es una prueba de transcripción de voz"
     - "Buenos días, hoy vamos a probar el sistema de reconocimiento de voz"
   - Detén la grabación

3. **Exportar el Archivo:**
   - Guarda el archivo como `Recording.wav`
   - Cópialo a `C:\Data\Recording.wav`

### Opción 3: Usar un Archivo Existente

Si ya tienes un archivo de audio WAV:

```powershell
# Crear directorio si no existe
New-Item -ItemType Directory -Path "C:\Data" -Force

# Copiar tu archivo
Copy-Item "ruta\a\tu\archivo.wav" "C:\Data\Recording.wav"
```

### Opción 4: Descargar Muestra de Audio

Puedes descargar una muestra de audio gratuita de sitios como:
- [Freesound.org](https://freesound.org/)
- [BBC Sound Effects](https://sound-effects.bbcrewind.co.uk/)

Asegúrate de que sea formato WAV con voz humana.

## ?? Especificaciones del Archivo

Para mejores resultados con Azure Speech SDK:

```
Formato:       WAV (RIFF)
Codec:         PCM
Sample Rate:   16000 Hz (16 kHz)
Bit Depth:     16-bit
Channels:      1 (mono)
Duración:      3-10 segundos
Contenido:     Voz humana clara
Idioma:        Español (es-MX) o Inglés (en-US)
```

## ?? Tests Disponibles

### TestTranscribeLocalFileAsyncTest
- Transcribe el archivo `C:\Data\Recording.wav` en español (es-MX)
- Valida que la transcripción sea exitosa
- Imprime el texto transcrito

### TestTranscribeLocalFile_WithEnglish
- Transcribe el mismo archivo pero configurado para inglés (en-US)
- Útil si grabaste en inglés

### TestTranscribeLocalFile_FileNotFound
- Verifica que el método maneje correctamente cuando el archivo no existe
- Solo pasa si el archivo NO existe

## ?? Troubleshooting

### El test dice "Archivo de prueba no encontrado"
- Verifica que el archivo existe en `C:\Data\Recording.wav`
- Verifica los permisos de lectura del archivo
- Ejecuta el script `create-test-audio.ps1` o graba manualmente

### El test pasa pero dice "No speech could be recognized"
- El archivo puede estar dañado o en formato incorrecto
- Verifica que sea formato WAV válido
- Asegúrate de que contenga voz humana (no solo música o ruido)
- Verifica que el archivo no esté vacío o corrupto

### Error de transcripción
- Verifica las credenciales de Azure Speech SDK en `local.settings.json`:
  ```json
  {
    "Values": {
      "AZURE_SPEECH_KEY": "tu-key-aquí",
      "AZURE_SPEECH_REGION": "eastus"
    }
  }
  ```

## ?? Ejemplo de Salida Exitosa

```
?? TEST: Transcribiendo archivo local de audio
?? Archivo: C:\Data\Recording.wav
?? Tamaño del archivo: 245760 bytes
? Estado: EXITOSO
?? Texto transcrito: Hola, esta es una prueba de transcripción de voz
?? Idioma: es-MX
?? Tamaño de audio: 245760 bytes
?? Tiempo de procesamiento: 1.23 segundos
?? Confianza: 95%
?? Ruta del archivo: C:\Data\Recording.wav
?? Procesado: 2025-01-01 12:00:00
```

## ?? Mejores Prácticas

1. **Grabar con buena calidad:**
   - Usa un micrófono decente
   - Minimiza ruido de fondo
   - Habla claramente

2. **Contenido apropiado:**
   - Frases completas en español o inglés
   - Evita música o efectos de sonido
   - Evita largas pausas

3. **Formato correcto:**
   - Preferir WAV sobre MP3
   - 16 kHz sample rate es ideal
   - Mono es suficiente

## ?? Referencias

- [Azure Speech SDK Documentation](https://learn.microsoft.com/en-us/azure/cognitive-services/speech-service/)
- [WAV File Format Specification](https://en.wikipedia.org/wiki/WAV)
- [AgentTelefonia Implementation](../../TwinAgentsNetwork/Agents/AgentTelefonia.cs)

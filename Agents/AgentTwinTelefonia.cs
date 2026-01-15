using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Azure.Identity;
using System.Net.Http;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// Agente de Telefonía - Transcripción de Audio a Texto usando Azure Speech SDK y Whisper
    /// Clase simplificada para pruebas de reconocimiento de voz
    /// </summary>
    public class AgentTwinTelefonia
    {
        private readonly ILogger<AgentTwinTelefonia> _logger;
        private readonly IConfiguration _configuration;

        public AgentTwinTelefonia(ILogger<AgentTwinTelefonia> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Transcribe usando Azure OpenAI Whisper API - Más preciso que Azure Speech SDK
        /// </summary>
        public async Task<VoiceTranscriptionResult> TranscribeWithWhisperAsync(
            byte[] audioBytes,
            string language = "es-MX")
        {
            var startTime = DateTime.UtcNow;

            _logger.LogInformation("?? Whisper: Starting transcription. Audio size: {Size} bytes", audioBytes.Length);

            try
            {
                if (audioBytes == null || audioBytes.Length == 0)
                {
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = "Audio bytes cannot be null or empty",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                string endpoint = _configuration["AZURE_OPENAI_ENDPOINT"] ??
                                Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ??
                                throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");

                string deploymentName = "whisper";

                var openAIClient = new AzureOpenAIClient(
                    new Uri(endpoint),
                    new DefaultAzureCredential());

                var audioClient = openAIClient.GetAudioClient(deploymentName);

                string tempFile = Path.GetTempFileName() + ".wav";
                await File.WriteAllBytesAsync(tempFile, audioBytes);

                var result = await audioClient.TranscribeAudioAsync(tempFile);

                File.Delete(tempFile);

                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;

                string transcribedText = result.Value.Text;

                _logger.LogInformation("? Whisper SUCCESS in {Time}s", processingTime);
                _logger.LogInformation("?? Text: {Text}", transcribedText);

                return new VoiceTranscriptionResult
                {
                    Success = true,
                    TranscribedText = transcribedText,
                    Confidence = 0.98,
                    DurationSeconds = processingTime,
                    AudioSizeBytes = audioBytes.Length,
                    Language = language,
                    ProcessedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
                _logger.LogError(ex, "? Whisper exception after {Time}s", processingTime);

                return new VoiceTranscriptionResult
                {
                    Success = false,
                    ErrorMessage = $"Whisper error: {ex.Message}",
                    TranscribedText = string.Empty,
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Transcribe un mensaje de voz recibido como un arreglo de bytes.
        /// Este método es útil para integrar transcripciones en tiempo real desde fuentes como llamadas telefónicas.
        /// </summary>
        /// <param name="audioBytes">Arreglo de bytes que representan el audio a transcribir.</param>
        /// <param name="language">Código del idioma (por defecto: "es-MX").</param>
        /// <param name="audioFormat">Formato del audio (por defecto: "wav").</param>
        /// <returns>Resultado de la transcripción con éxito o error.</returns>
        public async Task<VoiceTranscriptionResult> TranscribeVoiceMessageAsync(
            byte[] audioBytes,
            string language = "es-MX",
            string audioFormat = "wav")
        {
            var startTime = DateTime.UtcNow;

            _logger.LogInformation("?? Starting voice transcription. Audio size: {Size} bytes, Language: {Language}, Format: {Format}",
                audioBytes.Length, language, audioFormat);

            try
            {
                if (audioBytes == null || audioBytes.Length == 0)
                {
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = "Audio bytes cannot be null or empty",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                // Guardar audio en archivo local para verificación con timestamp único
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string localPath = $@"C:\Data\demo_{timestamp}.wav";
                await File.WriteAllBytesAsync(localPath, audioBytes);
                _logger.LogInformation("?? Audio saved to: {Path}", localPath);
                _logger.LogInformation("?? Audio hash (first 20 bytes): {Hash}",
                    string.Join("", audioBytes.Take(20).Select(b => b.ToString("X2"))));

                var speechKey = _configuration["Values:AZURE_SPEECH_KEY"] ??
                               _configuration["AZURE_SPEECH_KEY"] ??
                               Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ??
                               throw new InvalidOperationException("AZURE_SPEECH_KEY is not configured. Please set it in local.settings.json or environment variables.");

                var speechRegion = _configuration["Values:AZURE_SPEECH_REGION"] ??
                                  _configuration["AZURE_SPEECH_REGION"] ??
                                  Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ??
                                  "eastus";

                var config = SpeechConfig.FromSubscription(speechKey, speechRegion);
                config.SpeechRecognitionLanguage = language;

                config.OutputFormat = OutputFormat.Detailed;

                var audioStreamFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
                using var audioConfigStream = AudioInputStream.CreatePushStream(audioStreamFormat);
                using var audioConfig = AudioConfig.FromStreamInput(audioConfigStream);
                using var recognizer = new SpeechRecognizer(config, audioConfig);

                var allRecognizedText = new StringBuilder();
                var recognitionComplete = new TaskCompletionSource<bool>();
                bool hasError = false;
                string errorDetails = string.Empty;

                recognizer.Recognized += (s, e) =>
                {
                    if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
                    {
                        _logger.LogInformation("?? Segment: {Text}", e.Result.Text);
                        allRecognizedText.Append(e.Result.Text);
                        allRecognizedText.Append(" ");
                    }
                    else if (e.Result.Reason == ResultReason.NoMatch)
                    {
                        _logger.LogWarning("?? NOMATCH for segment");
                    }
                };

                recognizer.Canceled += (s, e) =>
                {
                    if (e.Reason == CancellationReason.Error)
                    {
                        hasError = true;
                        errorDetails = $"ErrorCode: {e.ErrorCode}, Details: {e.ErrorDetails}";
                        _logger.LogError("? ErrorCode: {ErrorCode}", e.ErrorCode);
                        _logger.LogError("? ErrorDetails: {ErrorDetails}", e.ErrorDetails);
                    }
                    recognitionComplete.TrySetResult(false);
                };

                recognizer.SessionStopped += (s, e) =>
                {
                    _logger.LogInformation("? Session stopped - Audio complete");
                    recognitionComplete.TrySetResult(true);
                };

                await recognizer.StartContinuousRecognitionAsync();

                audioConfigStream.Write(audioBytes, audioBytes.Length);
                audioConfigStream.Close();

                var completedTask = await Task.WhenAny(
                    recognitionComplete.Task,
                    Task.Delay(TimeSpan.FromMinutes(10))
                );

                await recognizer.StopContinuousRecognitionAsync();

                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;

                if (completedTask != recognitionComplete.Task)
                {
                    hasError = true;
                    errorDetails = "Recognition timed out";
                }

                var finalText = allRecognizedText.ToString().Trim();

                _logger.LogInformation("?? Final transcription result: '{Text}'", finalText);

                if (!hasError && !string.IsNullOrEmpty(finalText))
                {
                    _logger.LogInformation("? SUCCESS in {Time}s", processingTime);

                    return new VoiceTranscriptionResult
                    {
                        Success = true,
                        TranscribedText = finalText,
                        Confidence = 0.95,
                        DurationSeconds = processingTime,
                        AudioSizeBytes = audioBytes.Length,
                        Language = language,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
                else if (hasError)
                {
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"Recognition error: {errorDetails}",
                        TranscribedText = finalText,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
                else
                {
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = "NOMATCH: No speech could be recognized",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex)
            {
                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
                _logger.LogError(ex, "? Exception after {Time}s", processingTime);

                return new VoiceTranscriptionResult
                {
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}",
                    TranscribedText = string.Empty,
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Test de reconocimiento de voz con reconocimiento CONTINUO para archivos completos
        /// Basado en el ejemplo oficial de Microsoft Azure Speech SDK
        /// </summary>
        public async Task<VoiceTranscriptionResult> TestRecognizeSpeechSimpleAsync(
            string filePath = @"C:\Data\tts_20251231_183354.wav",
            string language = "es-MX")
        {
            var startTime = DateTime.UtcNow;

            _logger.LogInformation("?? TEST: Starting continuous speech recognition");
            _logger.LogInformation("?? File: {FilePath}", filePath);

            try
            {
                // Validar que el archivo existe
                if (!File.Exists(filePath))
                {
                    _logger.LogError("? File not found: {FilePath}", filePath);
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"File not found: {filePath}",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                var fileInfo = new FileInfo(filePath);
                _logger.LogInformation("?? File size: {Size} bytes ({SizeKB} KB)", fileInfo.Length, fileInfo.Length / 1024.0);

                // NUEVO: Verificar header WAV
                byte[] headerBytes = new byte[Math.Min(44, (int)fileInfo.Length)];
                using (var fs = File.OpenRead(filePath))
                {
                    await fs.ReadAsync(headerBytes, 0, headerBytes.Length);
                }

                // Validar que es un WAV real (debe empezar con "RIFF" y contener "WAVE")
                string riffMarker = System.Text.Encoding.ASCII.GetString(headerBytes, 0, 4);
                string waveMarker = headerBytes.Length >= 12 ? System.Text.Encoding.ASCII.GetString(headerBytes, 8, 4) : "";

                _logger.LogInformation("?? File header: RIFF={Riff}, WAVE={Wave}", riffMarker, waveMarker);

                if (riffMarker != "RIFF" || waveMarker != "WAVE")
                {
                    _logger.LogError("? Invalid WAV file - Header mismatch. Expected RIFF/WAVE, got {Riff}/{Wave}", riffMarker, waveMarker);
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"Invalid WAV file format. File does not have valid RIFF/WAVE header.",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                _logger.LogInformation("? WAV header validated successfully");

                // Obtener credenciales de Azure Speech
                var speechKey = _configuration["Values:AZURE_SPEECH_KEY"] ??
                               _configuration["AZURE_SPEECH_KEY"] ??
                               Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ??
                               throw new InvalidOperationException("AZURE_SPEECH_KEY is not configured. Please set it in local.settings.json or environment variables.");

                var speechRegion = _configuration["Values:AZURE_SPEECH_REGION"] ??
                                  _configuration["AZURE_SPEECH_REGION"] ??
                                  Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ??
                                  "eastus";

                _logger.LogInformation("?? Using Azure Speech - Region: {Region}", speechRegion);

                // Crear configuración (como en el ejemplo de Microsoft)
                var config = SpeechConfig.FromSubscription(speechKey, speechRegion);
                config.SpeechRecognitionLanguage = language;

                _logger.LogInformation("?? Language: {Language}", language);

                // Crear AudioConfig desde archivo WAV (como en el ejemplo de Microsoft)
                using var audioConfig = AudioConfig.FromWavFileInput(filePath);

                // Crear recognizer (como en el ejemplo de Microsoft)
                using var recognizer = new SpeechRecognizer(config, audioConfig);

                _logger.LogInformation("?? Starting CONTINUOUS recognition for complete audio...");

                // Variables para capturar todo el texto
                var allRecognizedText = new StringBuilder();
                var recognitionComplete = new TaskCompletionSource<bool>();
                bool hasError = false;
                string errorDetails = string.Empty;

                // Evento: Recognized (texto final de cada segmento)
                recognizer.Recognized += (s, e) =>
                {
                    if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
                    {
                        _logger.LogInformation("?? Segment: {Text}", e.Result.Text);
                        allRecognizedText.Append(e.Result.Text);
                        allRecognizedText.Append(" ");
                    }
                };

                // Evento: Canceled (error)
                recognizer.Canceled += (s, e) =>
                {
                    _logger.LogWarning("?? Canceled: {Reason}", e.Reason);
                    if (e.Reason == CancellationReason.Error)
                    {
                        hasError = true;
                        errorDetails = $"ErrorCode: {e.ErrorCode}, Details: {e.ErrorDetails}";
                        _logger.LogError("? ErrorCode: {ErrorCode}", e.ErrorCode);
                        _logger.LogError("? ErrorDetails: {ErrorDetails}", e.ErrorDetails);
                    }
                    recognitionComplete.TrySetResult(false);
                };

                // Evento: SessionStopped (fin del audio)
                recognizer.SessionStopped += (s, e) =>
                {
                    _logger.LogInformation("? Session stopped - Audio complete");
                    recognitionComplete.TrySetResult(true);
                };

                // INICIAR reconocimiento continuo
                await recognizer.StartContinuousRecognitionAsync();
                _logger.LogInformation("? Processing complete audio file...");

                // ESPERAR a que termine (con timeout de 10 minutos)
                var completedTask = await Task.WhenAny(
                    recognitionComplete.Task,
                    Task.Delay(TimeSpan.FromMinutes(10))
                );

                // DETENER reconocimiento
                await recognizer.StopContinuousRecognitionAsync();
                _logger.LogInformation("?? Recognition stopped");

                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;

                // Verificar timeout
                if (completedTask != recognitionComplete.Task)
                {
                    _logger.LogWarning("?? Timeout after 10 minutes");
                    hasError = true;
                    errorDetails = "Recognition timed out";
                }

                var finalText = allRecognizedText.ToString().Trim();

                // RESULTADO
                if (!hasError && !string.IsNullOrEmpty(finalText))
                {
                    _logger.LogInformation("? SUCCESS in {Time}s", processingTime);
                    _logger.LogInformation("?? Text ({Length} chars): {Preview}",
                        finalText.Length,
                        finalText.Length > 200 ? finalText.Substring(0, 200) + "..." : finalText);

                    return new VoiceTranscriptionResult
                    {
                        Success = true,
                        TranscribedText = finalText,
                        Confidence = 0.95,
                        DurationSeconds = processingTime,
                        AudioSizeBytes = fileInfo.Length,
                        Language = language,
                        ProcessedAt = DateTime.UtcNow,
                        AudioPath = filePath
                    };
                }
                else if (hasError)
                {
                    _logger.LogError("? Recognition error: {Error}", errorDetails);

                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"Recognition error: {errorDetails}",
                        TranscribedText = finalText,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
                else
                {
                    _logger.LogWarning("?? NOMATCH: No speech recognized");

                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = "NOMATCH: No speech could be recognized",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex)
            {
                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
                _logger.LogError(ex, "? Exception after {Time}s", processingTime);

                return new VoiceTranscriptionResult
                {
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}",
                    TranscribedText = string.Empty,
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Transcribe audio desde archivo guardado en Data Lake usando reconocimiento CONTINUO
        /// Descarga el archivo del Data Lake usando SAS URL y lo transcribe completamente
        /// </summary>
        public async Task<VoiceTranscriptionResult> TranscribeFromDataLakeFileAsync(
            string sasUrl,
            string language = "es-MX")
        {
            var startTime = DateTime.UtcNow;

            _logger.LogInformation("?? Starting transcription from Data Lake SAS URL");
            _logger.LogInformation("?? SAS URL: {Url}", sasUrl?.Length > 100 ? sasUrl.Substring(0, 100) + "..." : sasUrl);

            string? tempFilePath = null;

            try
            {
                if (string.IsNullOrEmpty(sasUrl))
                {
                    _logger.LogError("? SAS URL is required");
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = "SAS URL is required",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                // Crear archivo temporal para descargar el audio
                tempFilePath = Path.Combine(Path.GetTempPath(), $"datalake_{Guid.NewGuid():N}.wav");
                
                _logger.LogInformation("?? Downloading from Data Lake to: {TempPath}", tempFilePath);

                // Descargar archivo usando HttpClient
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5);

                var downloadResponse = await httpClient.GetAsync(sasUrl);
                
                if (!downloadResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("? Failed to download from Data Lake. Status: {Status}", downloadResponse.StatusCode);
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"Failed to download audio from Data Lake. HTTP {downloadResponse.StatusCode}",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                // Guardar contenido descargado en archivo temporal
                var audioBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(tempFilePath, audioBytes);
                
                _logger.LogInformation("? File downloaded successfully. Size: {Size} bytes ({SizeKB} KB)", 
                    audioBytes.Length, audioBytes.Length / 1024.0);

                // Verificar header WAV
                byte[] headerBytes = new byte[Math.Min(44, audioBytes.Length)];
                Array.Copy(audioBytes, headerBytes, headerBytes.Length);

                string riffMarker = System.Text.Encoding.ASCII.GetString(headerBytes, 0, 4);
                string waveMarker = headerBytes.Length >= 12 ? System.Text.Encoding.ASCII.GetString(headerBytes, 8, 4) : "";

                _logger.LogInformation("?? File header: RIFF={Riff}, WAVE={Wave}", riffMarker, waveMarker);

                if (riffMarker != "RIFF" || waveMarker != "WAVE")
                {
                    _logger.LogError("? Invalid WAV file - Header mismatch. Expected RIFF/WAVE, got {Riff}/{Wave}", riffMarker, waveMarker);
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"Invalid WAV file format. File does not have valid RIFF/WAVE header.",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                _logger.LogInformation("? WAV header validated successfully");

                // Obtener credenciales de Azure Speech
                var speechKey = _configuration["Values:AZURE_SPEECH_KEY"] ??
                               _configuration["AZURE_SPEECH_KEY"] ??
                               Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ??
                               throw new InvalidOperationException("AZURE_SPEECH_KEY is not configured. Please set it in local.settings.json or environment variables.");

                var speechRegion = _configuration["Values:AZURE_SPEECH_REGION"] ??
                                  _configuration["AZURE_SPEECH_REGION"] ??
                                  Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ??
                                  "eastus";

                _logger.LogInformation("?? Using Azure Speech - Region: {Region}", speechRegion);

                // Crear configuración
                var config = SpeechConfig.FromSubscription(speechKey, speechRegion);
                config.SpeechRecognitionLanguage = language;

                _logger.LogInformation("?? Language: {Language}", language);

                // Crear AudioConfig desde archivo WAV temporal
                using var audioConfig = AudioConfig.FromWavFileInput(tempFilePath);

                // Crear recognizer
                using var recognizer = new SpeechRecognizer(config, audioConfig);

                _logger.LogInformation("?? Starting CONTINUOUS recognition for Data Lake audio...");

                // Variables para capturar todo el texto
                var allRecognizedText = new StringBuilder();
                var recognitionComplete = new TaskCompletionSource<bool>();
                bool hasError = false;
                string errorDetails = string.Empty;

                // Evento: Recognized
                recognizer.Recognized += (s, e) =>
                {
                    if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
                    {
                        _logger.LogInformation("?? Segment: {Text}", e.Result.Text);
                        allRecognizedText.Append(e.Result.Text);
                        allRecognizedText.Append(" ");
                    }
                };

                // Evento: Canceled
                recognizer.Canceled += (s, e) =>
                {
                    _logger.LogWarning("?? Canceled: {Reason}", e.Reason);
                    if (e.Reason == CancellationReason.Error)
                    {
                        hasError = true;
                        errorDetails = $"ErrorCode: {e.ErrorCode}, Details: {e.ErrorDetails}";
                        _logger.LogError("? ErrorCode: {ErrorCode}", e.ErrorCode);
                        _logger.LogError("? ErrorDetails: {ErrorDetails}", e.ErrorDetails);
                    }
                    recognitionComplete.TrySetResult(false);
                };

                // Evento: SessionStopped
                recognizer.SessionStopped += (s, e) =>
                {
                    _logger.LogInformation("? Session stopped - Audio complete");
                    recognitionComplete.TrySetResult(true);
                };

                // INICIAR reconocimiento continuo
                await recognizer.StartContinuousRecognitionAsync();
                _logger.LogInformation("? Processing Data Lake audio file...");

                // ESPERAR a que termine (con timeout de 10 minutos)
                var completedTask = await Task.WhenAny(
                    recognitionComplete.Task,
                    Task.Delay(TimeSpan.FromMinutes(10))
                );

                // DETENER reconocimiento
                await recognizer.StopContinuousRecognitionAsync();
                _logger.LogInformation("?? Recognition stopped");

                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;

                // Verificar timeout
                if (completedTask != recognitionComplete.Task)
                {
                    _logger.LogWarning("?? Timeout after 10 minutes");
                    hasError = true;
                    errorDetails = "Recognition timed out";
                }

                var finalText = allRecognizedText.ToString().Trim();

                // RESULTADO
                if (!hasError && !string.IsNullOrEmpty(finalText))
                {
                    _logger.LogInformation("? Data Lake transcription SUCCESS in {Time}s", processingTime);
                    _logger.LogInformation("?? Text ({Length} chars): {Preview}",
                        finalText.Length,
                        finalText.Length > 200 ? finalText.Substring(0, 200) + "..." : finalText);

                    return new VoiceTranscriptionResult
                    {
                        Success = true,
                        TranscribedText = finalText,
                        Confidence = 0.95,
                        DurationSeconds = processingTime,
                        AudioSizeBytes = audioBytes.Length,
                        Language = language,
                        ProcessedAt = DateTime.UtcNow,
                        AudioPath = sasUrl
                    };
                }
                else if (hasError)
                {
                    _logger.LogError("? Data Lake recognition error: {Error}", errorDetails);

                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"Recognition error: {errorDetails}",
                        TranscribedText = finalText,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
                else
                {
                    _logger.LogWarning("?? NOMATCH: No speech recognized from Data Lake file");

                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = "NOMATCH: No speech could be recognized",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex)
            {
                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
                _logger.LogError(ex, "? Data Lake transcription exception after {Time}s", processingTime);

                return new VoiceTranscriptionResult
                {
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}",
                    TranscribedText = string.Empty,
                    ProcessedAt = DateTime.UtcNow
                };
            }
            finally
            {
                // Limpiar archivo temporal
                if (!string.IsNullOrEmpty(tempFilePath) && File.Exists(tempFilePath))
                {
                    try
                    {
                        File.Delete(tempFilePath);
                        _logger.LogInformation("??? Temporary file deleted: {Path}", tempFilePath);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(cleanupEx, "?? Failed to delete temporary file: {Path}", tempFilePath);
                    }
                }
            }
        }

    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using TwinAgentsLibrary.Services;
using TwinAgentsLibrary.Models;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// Agente de Telefonía - Transcripción de Voz a Texto usando GPT-4o Audio Transcribe REST API
    /// Convierte mensajes de voz en texto usando el endpoint especializado de Azure OpenAI
    /// </summary>
    public class AgentTelefonia
    {
        private readonly ILogger<AgentTelefonia> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _deploymentName = "gpt-4o-transcribe";
        private readonly string _apiVersion = "2025-03-01-preview";

        public AgentTelefonia(ILogger<AgentTelefonia> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = new HttpClient();

            try
            {
                // Obtener configuración específica para audio transcription
                _endpoint = configuration["Values:AzureOpenAI:AudioEndpoint"] ??
                           configuration["AzureOpenAI:AudioEndpoint"] ??
                           Environment.GetEnvironmentVariable("AZURE_OPENAI_AUDIO_ENDPOINT") ??
                           throw new InvalidOperationException("AZURE_OPENAI_AUDIO_ENDPOINT is not configured. Please set it in local.settings.json or environment variables.");

                _apiKey = configuration["Values:AzureOpenAI:AudioApiKey"] ??
                         configuration["AzureOpenAI:AudioApiKey"] ??
                         Environment.GetEnvironmentVariable("AZURE_OPENAI_AUDIO_KEY") ??
                         throw new InvalidOperationException("AZURE_OPENAI_AUDIO_KEY is not configured. Please set it in local.settings.json or environment variables.");

                // Configurar HttpClient SOLO con Authorization Bearer (según ejemplo de cURL oficial)
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
                _httpClient.Timeout = TimeSpan.FromMinutes(5);

                _logger.LogInformation("🎤 Initializing AgentTelefonia with GPT-4o Audio Transcribe:");
                _logger.LogInformation("   • Endpoint: {Endpoint}", _endpoint);
                _logger.LogInformation("   • Deployment: {Deployment}", _deploymentName);
                _logger.LogInformation("   • API Version: {Version}", _apiVersion);

                _logger.LogInformation("✅ AgentTelefonia initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize AgentTelefonia");
                throw;
            }
        }

        /// <summary>
        /// Transcribe un archivo de audio a texto usando GPT-4o Audio Transcribe REST API
        /// Y OPCIONALMENTE envía un mensaje con AgentHablemos y guarda el audio en Data Lake
        /// </summary>
        public async Task<VoiceTranscriptionResult> TranscribeVoiceMessageAsync(
            byte[] audioBytes,
            string language = "es-MX",
            string audioFormat = "wav",
            bool sendMessage = false,
            EnviarMensajeRequest? messageRequest = null)
        {
            var startTime = DateTime.UtcNow;

            _logger.LogInformation("🎤 Starting voice transcription. Audio size: {Size} bytes, Language: {Language}, Format: {Format}",
                audioBytes.Length, language, audioFormat);

            try
            {
                // Validar entrada
                if (audioBytes == null || audioBytes.Length == 0)
                {
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = "Audio bytes cannot be null or empty",
                        TranscribedText = string.Empty
                    };
                }

                // Determinar el tipo MIME según el formato
                string mimeType = audioFormat.ToLower() switch
                {
                    "wav" => "audio/wav",
                    "mp3" => "audio/mpeg",
                    "ogg" => "audio/ogg",
                    "webm" => "audio/webm",
                    "m4a" => "audio/m4a",
                    _ => "audio/wav"
                };

                _logger.LogInformation("🔊 Transcribing audio with GPT-4o Audio Transcribe REST API...");
                _logger.LogInformation("🔍 Audio details - Size: {Size} bytes, MIME: {Mime}", audioBytes.Length, mimeType);

                // Crear multipart/form-data content
                using var form = new MultipartFormDataContent();

                // 1. PRIMERO agregar el parámetro model (requerido según ejemplo de Azure)
                form.Add(new StringContent(_deploymentName), "model");

                // 2. Agregar el audio desde bytes (NO desde archivo)
                var audioContent = new ByteArrayContent(audioBytes);
                audioContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
                form.Add(audioContent, "file", $"audio.{audioFormat}");

                // Log del boundary del multipart
                _logger.LogInformation("📦 MultipartFormData boundary: {Boundary}", form.Headers.ContentType?.Parameters.FirstOrDefault(p => p.Name == "boundary")?.Value);

                // Construir URL del endpoint
                var url = $"{_endpoint}/openai/deployments/{_deploymentName}/audio/transcriptions?api-version={_apiVersion}";

                _logger.LogInformation("📡 Posting to: {Url}", url);
                _logger.LogInformation("🔑 API Key (first 10 chars): {KeyPreview}...", _apiKey.Substring(0, Math.Min(10, _apiKey.Length)));

                // Hacer la petición POST
                var response = await _httpClient.PostAsync(url, form);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("📥 Response Status: {StatusCode}", response.StatusCode);
                _logger.LogInformation("📄 Response Content Length: {Length}", responseContent?.Length ?? 0);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ Transcription API error: {StatusCode} - {Content}",
                        response.StatusCode, responseContent);
                    
                    // Log response headers para debugging
                    foreach (var header in response.Headers)
                    {
                        _logger.LogWarning("Response Header: {Key} = {Value}", header.Key, string.Join(", ", header.Value));
                    }

                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"API Error {response.StatusCode}: {responseContent}",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                // Parsear la respuesta JSON
                using var doc = JsonDocument.Parse(responseContent);
                var transcribedText = doc.RootElement.GetProperty("text").GetString() ?? string.Empty;

                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;

                _logger.LogInformation("✅ Audio transcribed successfully in {ProcessingTime}s", processingTime);
                _logger.LogInformation("📝 Transcribed text: {Text}",
                    transcribedText.Length > 100 ? transcribedText.Substring(0, 100) + "..." : transcribedText);

                var transcriptionResult = new VoiceTranscriptionResult
                {
                    Success = true,
                    TranscribedText = transcribedText,
                    Confidence = 0.95,
                    DurationSeconds = processingTime,
                    AudioSizeBytes = audioBytes.Length,
                    Language = language,
                    ProcessedAt = DateTime.UtcNow
                };

                // Si la transcripción fue exitosa, procesar guardado y envío de mensaje
                if (transcriptionResult.Success && sendMessage && messageRequest != null)
                {
                    string? audioFilePath = null;
                    string? audioSasUrl = null;

                    try
                    {
                        // 1. Guardar audio en Data Lake
                        _logger.LogInformation("💾 Saving voice audio to Data Lake");

                        var storageSettings = new AzureStorageSettings
                        {
                            AccountName = _configuration["Values:AzureStorage:AccountName"] ??
                                         _configuration["AzureStorage:AccountName"] ??
                                         "flatbitdatalake",
                            AccountKey = _configuration["Values:AzureStorage:AccountKey"] ??
                                        _configuration["AzureStorage:AccountKey"] ??
                                        Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_KEY") ??
                                        throw new InvalidOperationException("Azure Storage Account Key is required")
                        };

                        var dataLakeLogger = LoggerFactory.Create(builder => builder.AddConsole())
                            .CreateLogger<DataLakeClient>();
                        var dataLakeClient = new DataLakeClient(messageRequest.DuenoAppMicrosoftOID, dataLakeLogger, storageSettings);

                        string clientId = messageRequest.DeQuien;
                        string fileName = $"voice_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}.{audioFormat}";
                        string directoryPath = $"Documents/hablemos/{clientId}/voz";
                        audioFilePath = $"{directoryPath}/{fileName}";
                        string containerName = messageRequest.DuenoAppMicrosoftOID.ToLowerInvariant();

                        var audioMetadata = new Dictionary<string, string>
                        {
                            ["transcribedText"] = transcriptionResult.TranscribedText,
                            ["language"] = language,
                            ["audioFormat"] = audioFormat,
                            ["model"] = _deploymentName,
                            ["processingTime"] = processingTime.ToString("F2"),
                            ["sizeBytes"] = audioBytes.Length.ToString(),
                            ["deQuien"] = messageRequest.DeQuien,
                            ["paraQuien"] = messageRequest.ParaQuien,
                            ["uploadedAt"] = DateTime.UtcNow.ToString("O")
                        };

                        using var audioStream = new MemoryStream(audioBytes);
                        var uploadSuccess = await dataLakeClient.UploadFileAsync(
                            containerName,
                            directoryPath,
                            fileName,
                            audioStream,
                            mimeType,
                            audioMetadata
                        );

                        if (uploadSuccess)
                        {
                            audioSasUrl = await dataLakeClient.GenerateSasUrlAsync(audioFilePath, TimeSpan.FromHours(24));
                            transcriptionResult.AudioUrl = audioSasUrl;
                            transcriptionResult.AudioPath = audioFilePath;

                            _logger.LogInformation("✅ Audio saved successfully. Path: {Path}", audioFilePath);
                        }
                    }
                    catch (Exception dlEx)
                    {
                        _logger.LogError(dlEx, "❌ Error saving audio to Data Lake");
                    }

                    // 2. Enviar mensaje con AgentHablemos
                    try
                    {
                        _logger.LogInformation("💬 Sending message with transcribed text");

                        var agentLogger = LoggerFactory.Create(builder => builder.AddConsole())
                            .CreateLogger<AgentHablemos>();
                        var hablemosAgent = new AgentHablemos(agentLogger, _configuration);

                        string mensajeCompleto = transcriptionResult.TranscribedText;

                        if (!string.IsNullOrEmpty(audioFilePath))
                        {
                            mensajeCompleto += $"\n[🎤 Audio: {audioFilePath}]";
                        }

                        if (!string.IsNullOrEmpty(audioSasUrl))
                        {
                            mensajeCompleto += $"\n[🔗 {audioSasUrl}]";
                        }

                        messageRequest.Mensaje = mensajeCompleto;

                        var mensajeResult = await hablemosAgent.EnviarMensajeAsync(messageRequest);

                        if (mensajeResult.Success)
                        {
                            transcriptionResult.MessageSent = true;
                            transcriptionResult.MessageId = mensajeResult.Mensaje?.MessageId;
                            transcriptionResult.PairId = mensajeResult.PairId;

                            _logger.LogInformation("✅ Message sent successfully");
                        }
                    }
                    catch (Exception msgEx)
                    {
                        _logger.LogError(msgEx, "❌ Error sending message");
                    }
                }

                return transcriptionResult;
            }
            catch (Exception ex)
            {
                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
                _logger.LogError(ex, "❌ Error transcribing voice message after {ProcessingTime}s", processingTime);

                return new VoiceTranscriptionResult
                {
                    Success = false,
                    ErrorMessage = $"Transcription error: {ex.Message}",
                    TranscribedText = string.Empty,
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        public async Task<VoiceTranscriptionResult> TranscribeVoiceMessageFromStreamAsync(
            Stream audioStream,
            string language = "es-MX")
        {
            using var memoryStream = new MemoryStream();
            await audioStream.CopyToAsync(memoryStream);
            byte[] audioBytes = memoryStream.ToArray();

            return await TranscribeVoiceMessageAsync(audioBytes, language);
        }

        /// <summary>
        /// Transcribe un archivo de audio a texto usando Azure Speech SDK (alternativa más confiable)
        /// Usa Microsoft.CognitiveServices.Speech para Speech-to-Text
        /// NUEVO MÉTODO - Más estable que el endpoint REST de GPT-4o
        /// </summary>
        public async Task<VoiceTranscriptionResult> TranscribeVoiceWithSpeechSDKAsync(
            byte[] audioBytes,
            string language = "es-MX",
            string audioFormat = "wav",
            bool sendMessage = false,
            EnviarMensajeRequest? messageRequest = null)
        {
            var startTime = DateTime.UtcNow;

            _logger.LogInformation("🎤 Starting voice transcription with Azure Speech SDK. Audio size: {Size} bytes, Language: {Language}, Format: {Format}",
                audioBytes.Length, language, audioFormat);

            try
            {
                // Validar entrada
                if (audioBytes == null || audioBytes.Length == 0)
                {
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = "Audio bytes cannot be null or empty",
                        TranscribedText = string.Empty
                    };
                }

                // Obtener credenciales de Azure Speech
                var speechKey = _configuration["Values:AZURE_SPEECH_KEY"] ??
                               _configuration["AZURE_SPEECH_KEY"] ??
                               Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ??
                               throw new InvalidOperationException("AZURE_SPEECH_KEY is not configured. Please set it in local.settings.json or environment variables.");

                var speechRegion = _configuration["Values:AZURE_SPEECH_REGION"] ??
                                  _configuration["AZURE_SPEECH_REGION"] ??
                                  Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ??
                                  "eastus";

                _logger.LogInformation("🔊 Using Azure Speech SDK - Region: {Region}", speechRegion);

                // Configurar Speech SDK
                var speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
                
                // IMPORTANTE: Configurar idioma español CORRECTAMENTE
                if (language == "auto-detected" || string.IsNullOrEmpty(language))
                {
                    // Si no se especifica idioma, usar español mexicano por defecto
                    speechConfig.SpeechRecognitionLanguage = "es-MX";
                    _logger.LogInformation("🌐 Using default language: es-MX");
                }
                else
                {
                    speechConfig.SpeechRecognitionLanguage = language;
                    _logger.LogInformation("🌐 Using specified language: {Language}", language);
                }

                _logger.LogInformation("📊 Audio format: {Format}, Size: {Size} bytes", audioFormat, audioBytes.Length);

                // IMPORTANTE: Azure Speech SDK requiere formato específico
                // - WAV: 16 kHz, 16-bit, mono, PCM
                // Si el audio no cumple, puede haber errores de transcripción
                
                // Crear formato de audio específico para mejor reconocimiento
                var audioStreamFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1); // 16kHz, 16-bit, mono
                
                // Crear stream de audio desde bytes
                using var pushStream = AudioInputStream.CreatePushStream(audioStreamFormat);
                
                // Escribir bytes al stream
                pushStream.Write(audioBytes);
                pushStream.Close();

                // Configurar audio input con formato específico
                using var audioConfig = AudioConfig.FromStreamInput(pushStream);
                
                // Crear recognizer
                using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

                _logger.LogInformation("🎯 Starting speech recognition...");

                // Reconocer audio
                var result = await recognizer.RecognizeOnceAsync();

                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;

                if (result.Reason == ResultReason.RecognizedSpeech)
                {
                    _logger.LogInformation("✅ Speech recognized successfully in {ProcessingTime}s", processingTime);
                    _logger.LogInformation("📝 Transcribed text: {Text}",
                        result.Text.Length > 100 ? result.Text.Substring(0, 100) + "..." : result.Text);

                    var transcriptionResult = new VoiceTranscriptionResult
                    {
                        Success = true,
                        TranscribedText = result.Text,
                        Confidence = 0.95, // Azure Speech no proporciona confidence directamente
                        DurationSeconds = processingTime,
                        AudioSizeBytes = audioBytes.Length,
                        Language = language,
                        ProcessedAt = DateTime.UtcNow
                    };

                    // Si la transcripción fue exitosa, procesar guardado y envío de mensaje
                    if (transcriptionResult.Success && sendMessage && messageRequest != null)
                    {
                      //  await ProcessMessageAndDataLake(transcriptionResult, audioBytes, audioFormat, language, processingTime, messageRequest);
                    }

                    return transcriptionResult;
                }
                else if (result.Reason == ResultReason.NoMatch)
                {
                    _logger.LogWarning("⚠️ No speech could be recognized");
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = "No speech could be recognized. Audio may be too noisy or silent.",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
                else if (result.Reason == ResultReason.Canceled)
                {
                    var cancellation = CancellationDetails.FromResult(result);
                    _logger.LogError("❌ Speech recognition canceled: {Reason}", cancellation.Reason);
                    
                    if (cancellation.Reason == CancellationReason.Error)
                    {
                        _logger.LogError("❌ Error Code: {ErrorCode}", cancellation.ErrorCode);
                        _logger.LogError("❌ Error Details: {ErrorDetails}", cancellation.ErrorDetails);
                        
                        // Sugerir conversión a WAV si hay error de formato
                        _logger.LogError("💡 Suggestion: The M4A format may not be fully supported.");
                        _logger.LogError("   Please convert the audio to WAV format (16kHz, 16-bit, mono)");
                        _logger.LogError("   You can use FFmpeg: ffmpeg -i Recording.m4a -ar 16000 -ac 1 -sample_fmt s16 Recording.wav");
                    }

                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"Recognition canceled: {cancellation.ErrorDetails}",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
                else
                {
                    _logger.LogError("❌ Unexpected result reason: {Reason}", result.Reason);
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"Unexpected result: {result.Reason}",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex)
            {
                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
                _logger.LogError(ex, "❌ Error transcribing with Speech SDK after {ProcessingTime}s", processingTime);

                return new VoiceTranscriptionResult
                {
                    Success = false,
                    ErrorMessage = $"Speech SDK error: {ex.Message}",
                    TranscribedText = string.Empty,
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// MÉTODO DE PRUEBA - Transcribe un archivo de audio local usando Azure Speech SDK
        /// Lee el archivo Recording.m4a desde C:\Data\ y lo transcribe
        /// </summary>
        public async Task<VoiceTranscriptionResult> TestTranscribeLocalFileAsync(
            string language = "es-MX")
        {
            var startTime = DateTime.UtcNow;
            string filePath = @"C:\Data\Recording.m4a";

            _logger.LogInformation("🧪 TEST: Starting transcription of local file: {FilePath}", filePath);

            try
            {
                // Validar que el archivo existe
                if (!File.Exists(filePath))
                {
                    _logger.LogError("❌ File not found: {FilePath}", filePath);
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"File not found: {filePath}",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                // Obtener info del archivo
                var fileInfo = new FileInfo(filePath);
                string fileExtension = fileInfo.Extension.ToLower().TrimStart('.');
                
                _logger.LogInformation("📂 File loaded. Size: {Size} bytes, Extension: {Extension}", 
                    fileInfo.Length, fileExtension);

                // Obtener credenciales de Azure Speech
                var speechKey = _configuration["Values:AZURE_SPEECH_KEY"] ??
                               _configuration["AZURE_SPEECH_KEY"] ??
                               Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ??
                               throw new InvalidOperationException("AZURE_SPEECH_KEY is not configured. Please set it in local.settings.json or environment variables.");

                var speechRegion = _configuration["Values:AZURE_SPEECH_REGION"] ??
                                  _configuration["AZURE_SPEECH_REGION"] ??
                                  Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ??
                                  "eastus";

                _logger.LogInformation("🔊 Using Azure Speech SDK - Region: {Region}", speechRegion);

                // Configurar Speech SDK
                var speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
                
                // IMPORTANTE: Configurar idioma español CORRECTAMENTE
                if (language == "auto-detected" || string.IsNullOrEmpty(language))
                {
                    // Si no se especifica idioma, usar español mexicano por defecto
                    speechConfig.SpeechRecognitionLanguage = "es-MX";
                    _logger.LogInformation("🌐 Using default language: es-MX");
                }
                else
                {
                    speechConfig.SpeechRecognitionLanguage = language;
                    _logger.LogInformation("🌐 Using specified language: {Language}", language);
                }

                // ESTRATEGIA: Para M4A y otros formatos comprimidos, usar AudioConfig.FromWavFileInput
                // Azure Speech SDK maneja automáticamente la decodificación de formatos compatibles
                AudioConfig audioConfig;

                if (fileExtension == "wav")
                {
                    // Para WAV, usar directamente FromWavFileInput
                    _logger.LogInformation("📊 Audio format: WAV - using direct file input");
                    audioConfig = AudioConfig.FromWavFileInput(filePath);
                }
                else
                {
                    // Para M4A, MP3, y otros formatos comprimidos
                    // Necesitamos usar el formato comprimido apropiado
                    _logger.LogInformation("📊 Audio format: {Format} - using compressed format", fileExtension);
                    
                    // Leer el archivo como bytes
                    byte[] audioBytes = await File.ReadAllBytesAsync(filePath);
                    _logger.LogInformation("📂 File read successfully. Size: {Size} bytes", audioBytes.Length);

                    // Para formatos comprimidos, intentar con formato ANY
                    // Esto permite que Azure Speech SDK detecte automáticamente el formato
                    var audioStreamFormat = AudioStreamFormat.GetCompressedFormat(AudioStreamContainerFormat.ANY);
                    
                    // Crear stream de audio desde bytes
                    var pushStream = AudioInputStream.CreatePushStream(audioStreamFormat);
                    
                    // Escribir bytes al stream
                    pushStream.Write(audioBytes);
                    pushStream.Close();

                    // Configurar audio input con formato comprimido
                    audioConfig = AudioConfig.FromStreamInput(pushStream);
                }
                
                // Crear recognizer
                using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

                _logger.LogInformation("🎯 Starting speech recognition...");

                // Reconocer audio
                var result = await recognizer.RecognizeOnceAsync();

                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;

                if (result.Reason == ResultReason.RecognizedSpeech)
                {
                    _logger.LogInformation("✅ Speech recognized successfully in {ProcessingTime}s", processingTime);
                    _logger.LogInformation("📝 Transcribed text: {Text}", result.Text);
                    _logger.LogInformation("🎤 Audio file: {FilePath}", filePath);

                    var transcriptionResult = new VoiceTranscriptionResult
                    {
                        Success = true,
                        TranscribedText = result.Text,
                        Confidence = 0.95, // Azure Speech no proporciona confidence directamente
                        DurationSeconds = processingTime,
                        AudioSizeBytes = fileInfo.Length,
                        Language = language,
                        ProcessedAt = DateTime.UtcNow,
                        AudioPath = filePath
                    };

                    // Dispose audioConfig if it's disposable
                    (audioConfig as IDisposable)?.Dispose();

                    return transcriptionResult;
                }
                else if (result.Reason == ResultReason.NoMatch)
                {
                    _logger.LogWarning("⚠️ No speech could be recognized");
                    _logger.LogWarning("   Possible reasons:");
                    _logger.LogWarning("   • Audio file may be in unsupported format");
                    _logger.LogWarning("   • Audio quality may be too low");
                    _logger.LogWarning("   • Audio may not contain clear speech");
                    _logger.LogWarning("   • Try converting to WAV (16kHz, 16-bit, mono) format");
                    
                    (audioConfig as IDisposable)?.Dispose();
                    
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"No speech could be recognized. Audio format: {fileExtension}. Try converting to WAV format.",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
                else if (result.Reason == ResultReason.Canceled)
                {
                    var cancellation = CancellationDetails.FromResult(result);
                    _logger.LogError("❌ Speech recognition canceled: {Reason}", cancellation.Reason);
                    
                    if (cancellation.Reason == CancellationReason.Error)
                    {
                        _logger.LogError("❌ Error Code: {ErrorCode}", cancellation.ErrorCode);
                        _logger.LogError("❌ Error Details: {ErrorDetails}", cancellation.ErrorDetails);
                        
                        // Sugerir conversión a WAV si hay error de formato
                        _logger.LogError("💡 Suggestion: The M4A format may not be fully supported.");
                        _logger.LogError("   Please convert the audio to WAV format (16kHz, 16-bit, mono)");
                        _logger.LogError("   You can use FFmpeg: ffmpeg -i Recording.m4a -ar 16000 -ac 1 -sample_fmt s16 Recording.wav");
                    }

                    (audioConfig as IDisposable)?.Dispose();

                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"Recognition canceled: {cancellation.ErrorDetails}",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
                else
                {
                    _logger.LogError("❌ Unexpected result reason: {Reason}", result.Reason);
                    (audioConfig as IDisposable)?.Dispose();
                    
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"Unexpected result: {result.Reason}",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex)
            {
                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
                _logger.LogError(ex, "❌ Error transcribing local file after {ProcessingTime}s", processingTime);

                return new VoiceTranscriptionResult
                {
                    Success = false,
                    ErrorMessage = $"Test transcription error: {ex.Message}",
                    TranscribedText = string.Empty,
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// MÉTODO DE PRUEBA SIMPLIFICADO - Usa FromEndpoint y FromWavFileInput directamente
        /// Basado en el ejemplo oficial de Azure Speech SDK
        /// AHORA CON RECONOCIMIENTO CONTINUO para archivos largos
        /// </summary>
        public async Task<VoiceTranscriptionResult> TestRecognizeSpeechSimpleAsync(
            string filePath = @"C:\Data\Recording.wav",
            string language = "es-MX")
        {
            var startTime = DateTime.UtcNow;

            _logger.LogInformation("🧪 TEST SIMPLE: Starting speech recognition with FromEndpoint");
            _logger.LogInformation("📂 File: {FilePath}", filePath);

            try
            {
                // Validar que el archivo existe
                if (!File.Exists(filePath))
                {
                    _logger.LogError("❌ File not found: {FilePath}", filePath);
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"File not found: {filePath}",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                var fileInfo = new FileInfo(filePath);
                _logger.LogInformation("📊 File size: {Size} bytes ({SizeKB} KB)", fileInfo.Length, fileInfo.Length / 1024.0);

                // Obtener credenciales de Azure Speech
                var speechKey = _configuration["Values:AZURE_SPEECH_KEY"] ??
                               _configuration["AZURE_SPEECH_KEY"] ??
                               Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ??
                               throw new InvalidOperationException("AZURE_SPEECH_KEY is not configured. Please set it in local.settings.json or environment variables.");

                var speechRegion = _configuration["Values:AZURE_SPEECH_REGION"] ??
                                  _configuration["AZURE_SPEECH_REGION"] ??
                                  Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ??
                                  "eastus";

                // Construir endpoint en el formato correcto
                string endpoint = $"wss://{speechRegion}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1";
                _logger.LogInformation("🔊 Using Azure Speech - Region: {Region}", speechRegion);
                _logger.LogInformation("🔗 Endpoint: {Endpoint}", endpoint);

                // Crear config desde endpoint (como en el ejemplo oficial)
                var config = SpeechConfig.FromEndpoint(new Uri(endpoint), speechKey);
                
                // Configurar idioma
                config.SpeechRecognitionLanguage = language;
                _logger.LogInformation("🌐 Language: {Language}", language);

                // Crear AudioConfig desde archivo WAV directamente
                using var audioConfig = AudioConfig.FromWavFileInput(filePath);
                
                // Crear recognizer
                using var recognizer = new SpeechRecognizer(config, audioConfig);

                _logger.LogInformation("🎯 Starting CONTINUOUS speech recognition for long audio...");

                // Variables para capturar el texto completo
                var allRecognizedText = new System.Text.StringBuilder();
                var recognitionComplete = new TaskCompletionSource<bool>();

                // Suscribirse a eventos de reconocimiento continuo
                recognizer.Recognized += (s, e) =>
                {
                    if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
                    {
                        _logger.LogInformation("📝 Recognized segment: {Text}", e.Result.Text);
                        allRecognizedText.Append(e.Result.Text);
                        allRecognizedText.Append(" "); // Espacio entre segmentos
                    }
                    else if (e.Result.Reason == ResultReason.NoMatch)
                    {
                        _logger.LogWarning("⚠️ NOMATCH for segment");
                    }
                };

                recognizer.Canceled += (s, e) =>
                {
                    _logger.LogWarning("🛑 Recognition canceled: {Reason}", e.Reason);
                    if (e.Reason == CancellationReason.Error)
                    {
                        _logger.LogError("❌ ErrorCode: {ErrorCode}", e.ErrorCode);
                        _logger.LogError("❌ ErrorDetails: {ErrorDetails}", e.ErrorDetails);
                    }
                    recognitionComplete.TrySetResult(false);
                };

                recognizer.SessionStopped += (s, e) =>
                {
                    _logger.LogInformation("✅ Session stopped - Recognition complete");
                    recognitionComplete.TrySetResult(true);
                };

                // Iniciar reconocimiento continuo
                await recognizer.StartContinuousRecognitionAsync();

                // Esperar a que termine el audio
                var success = await recognitionComplete.Task;

                // Detener reconocimiento
                await recognizer.StopContinuousRecognitionAsync();

                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;

                var finalText = allRecognizedText.ToString().Trim();

                if (success && !string.IsNullOrEmpty(finalText))
                {
                    _logger.LogInformation("✅ Speech recognized successfully in {ProcessingTime}s", processingTime);
                    _logger.LogInformation("📝 Full transcribed text ({Length} chars): {Text}", 
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
                else if (string.IsNullOrEmpty(finalText))
                {
                    _logger.LogWarning("⚠️ NOMATCH: No speech could be recognized");
                    
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = "NOMATCH: Speech could not be recognized",
                        TranscribedText = string.Empty,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
                else
                {
                    _logger.LogError("❌ Recognition was canceled or failed");
                    return new VoiceTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = "Recognition canceled or failed",
                        TranscribedText = finalText,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex)
            {
                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
                _logger.LogError(ex, "❌ Error in TestRecognizeSpeechSimpleAsync after {ProcessingTime}s", processingTime);

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
        /// MÉTODO DE PRUEBA - Convierte texto a voz y guarda el archivo WAV localmente
        /// Genera un archivo de audio en C:\Data\tts_output.wav
        /// </summary>
        /// <param name="text">Texto a convertir</param>
        /// <param name="voiceName">Voz a utilizar</param>
        /// <param name="outputPath">Ruta donde guardar el archivo (default: C:\Data\tts_output.wav)</param>
        /// <returns>Resultado de la operación</returns>
        public async Task<TextToSpeechResult> TestTextToSpeechAsync(
            string text = "Hola, esta es una prueba de conversión de texto a voz usando Azure Speech SDK",
            string voiceName = "es-MX-DaliaNeural",
            string outputPath = @"C:\Data\tts_output.wav")
        {
            _logger.LogInformation("🧪 TEST: Starting text-to-speech test");
            _logger.LogInformation("📝 Text: {Text}", text);
            _logger.LogInformation("🎙️ Voice: {Voice}", voiceName);
            _logger.LogInformation("📂 Output: {Path}", outputPath);

            try
            {
                // Asegurar que el directorio existe
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.LogInformation("📁 Created directory: {Directory}", directory);
                }

                // Convertir texto a voz
                var result = await ConvertTextToSpeechAsync(text, voiceName);

                if (result.Success && result.AudioData != null)
                {
                    // Guardar el archivo
                    await File.WriteAllBytesAsync(outputPath, result.AudioData);
                    
                    _logger.LogInformation("✅ Audio file saved successfully");
                    _logger.LogInformation("📂 File path: {Path}", outputPath);
                    _logger.LogInformation("📊 File size: {Size} bytes ({SizeKB} KB)", 
                        result.AudioData.Length, result.AudioData.Length / 1024.0);

                    // Actualizar el resultado con la ruta del archivo
                    result.ErrorMessage = $"Audio saved to: {outputPath}";
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in test text-to-speech");
                return new TextToSpeechResult
                {
                    Success = false,
                    ErrorMessage = $"Test TTS error: {ex.Message}",
                    AudioData = null,
                    VoiceName = voiceName,
                    TextLength = text?.Length ?? 0
                };
            }
        }

        /// <summary>
        /// Convierte texto a voz con opciones avanzadas usando SSML
        /// Permite control fino sobre la síntesis: velocidad, tono, énfasis, pausas, etc.
        /// </summary>
        /// <param name="ssmlText">Texto en formato SSML</param>
        /// <returns>Resultado con el audio generado</returns>
        public async Task<TextToSpeechResult> ConvertTextToSpeechWithSSMLAsync(string ssmlText)
        {
            var startTime = DateTime.UtcNow;

            _logger.LogInformation("🔊 Starting text-to-speech with SSML");
            _logger.LogInformation("📝 SSML length: {Length} characters", ssmlText?.Length ?? 0);

            try
            {
                // Validar entrada
                if (string.IsNullOrWhiteSpace(ssmlText))
                {
                    return new TextToSpeechResult
                    {
                        Success = false,
                        ErrorMessage = "SSML text cannot be null or empty",
                        AudioData = null
                    };
                }

                // Obtener credenciales de Azure Speech
                var speechKey = _configuration["Values:AZURE_SPEECH_KEY"] ??
                               _configuration["AZURE_SPEECH_KEY"] ??
                               Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ??
                               throw new InvalidOperationException("AZURE_SPEECH_KEY is not configured. Please set it in local.settings.json or environment variables.");

                var speechRegion = _configuration["Values:AZURE_SPEECH_REGION"] ??
                                  _configuration["AZURE_SPEECH_REGION"] ??
                                  Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ??
                                  "eastus";

                // Configurar Speech SDK
                var speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
                speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Riff16Khz16BitMonoPcm);

                // Crear synthesizer
                using var synthesizer = new SpeechSynthesizer(speechConfig, null);

                _logger.LogInformation("🎯 Starting SSML speech synthesis...");

                // Sintetizar usando SSML
                using var result = await synthesizer.SpeakSsmlAsync(ssmlText);

                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;

                if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                {
                    _logger.LogInformation("✅ SSML speech synthesis completed in {ProcessingTime}s", processingTime);
                    _logger.LogInformation("📊 Audio size: {Size} bytes", result.AudioData.Length);

                    return new TextToSpeechResult
                    {
                        Success = true,
                        AudioData = result.AudioData,
                        AudioFormat = "wav",
                        VoiceName = "SSML",
                        TextLength = ssmlText.Length,
                        AudioSizeBytes = result.AudioData.Length
                    };
                }
                else if (result.Reason == ResultReason.Canceled)
                {
                    var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                    _logger.LogError("❌ SSML synthesis canceled: {Reason} - {Details}", 
                        cancellation.Reason, cancellation.ErrorDetails);

                    return new TextToSpeechResult
                    {
                        Success = false,
                        ErrorMessage = $"SSML synthesis canceled: {cancellation.ErrorDetails}",
                        AudioData = null,
                        TextLength = ssmlText.Length
                    };
                }
                else
                {
                    _logger.LogError("❌ Unexpected SSML synthesis result: {Reason}", result.Reason);
                    return new TextToSpeechResult
                    {
                        Success = false,
                        ErrorMessage = $"Unexpected SSML result: {result.Reason}",
                        AudioData = null,
                        TextLength = ssmlText.Length
                    };
                }
            }
            catch (Exception ex)
            {
                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
                _logger.LogError(ex, "❌ Error in SSML text-to-speech after {ProcessingTime}s", processingTime);

                return new TextToSpeechResult
                {
                    Success = false,
                    ErrorMessage = $"SSML TTS error: {ex.Message}",
                    AudioData = null,
                    TextLength = ssmlText?.Length ?? 0
                };
            }
        }

        /// <summary>
        /// Convierte texto a voz usando Azure Speech SDK
        /// Genera audio WAV desde texto con voz natural
        /// </summary>
        public async Task<TextToSpeechResult> ConvertTextToSpeechAsync(
            string text,
            string voiceName = "es-MX-DaliaNeural",
            string language = "es-MX")
        {
            var startTime = DateTime.UtcNow;

            _logger.LogInformation("🔊 Starting text-to-speech conversion");
            _logger.LogInformation("📝 Text length: {Length} characters", text?.Length ?? 0);
            _logger.LogInformation("🎙️ Voice: {Voice}", voiceName);

            try
            {
                // Validar entrada
                if (string.IsNullOrWhiteSpace(text))
                {
                    return new TextToSpeechResult
                    {
                        Success = false,
                        ErrorMessage = "Text cannot be null or empty",
                        AudioData = null
                    };
                }

                // Obtener credenciales de Azure Speech
                var speechKey = _configuration["Values:AZURE_SPEECH_KEY"] ??
                               _configuration["AZURE_SPEECH_KEY"] ??
                               Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ??
                               throw new InvalidOperationException("AZURE_SPEECH_KEY is not configured. Please set it in local.settings.json or environment variables.");

                var speechRegion = _configuration["Values:AZURE_SPEECH_REGION"] ??
                                  _configuration["AZURE_SPEECH_REGION"] ??
                                  Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ??
                                  "eastus";

                // Configurar Speech SDK
                var speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
                speechConfig.SpeechSynthesisVoiceName = voiceName;
                speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Riff16Khz16BitMonoPcm);

                // Crear synthesizer
                using var synthesizer = new SpeechSynthesizer(speechConfig, null);

                _logger.LogInformation("🎯 Starting synthesis...");

                // Sintetizar
                using var result = await synthesizer.SpeakTextAsync(text);

                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;

                if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                {
                    _logger.LogInformation("✅ Synthesis completed in {Time}s", processingTime);
                    _logger.LogInformation("📊 Audio size: {Size} bytes", result.AudioData.Length);

                    return new TextToSpeechResult
                    {
                        Success = true,
                        AudioData = result.AudioData,
                        AudioFormat = "wav",
                        VoiceName = voiceName,
                        TextLength = text.Length,
                        AudioSizeBytes = result.AudioData.Length
                    };
                }
                else if (result.Reason == ResultReason.Canceled)
                {
                    var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                    _logger.LogError("❌ Synthesis canceled: {Reason} - {Details}", 
                        cancellation.Reason, cancellation.ErrorDetails);

                    return new TextToSpeechResult
                    {
                        Success = false,
                        ErrorMessage = $"Synthesis canceled: {cancellation.ErrorDetails}",
                        AudioData = null,
                        VoiceName = voiceName,
                        TextLength = text.Length
                    };
                }
                else
                {
                    _logger.LogError("❌ Unexpected result: {Reason}", result.Reason);
                    return new TextToSpeechResult
                    {
                        Success = false,
                        ErrorMessage = $"Unexpected result: {result.Reason}",
                        AudioData = null,
                        VoiceName = voiceName,
                        TextLength = text.Length
                    };
                }
            }
            catch (Exception ex)
            {
                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
                _logger.LogError(ex, "❌ TTS error after {Time}s", processingTime);

                return new TextToSpeechResult
                {
                    Success = false,
                    ErrorMessage = $"TTS error: {ex.Message}",
                    AudioData = null,
                    VoiceName = voiceName,
                    TextLength = text?.Length ?? 0
                };
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class VoiceTranscriptionResult
    {
        public bool Success { get; set; }
        public string TranscribedText { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public double DurationSeconds { get; set; }
        public long AudioSizeBytes { get; set; }
        public string Language { get; set; } = string.Empty;
        public string? DetectedLanguage { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime ProcessedAt { get; set; }
        public string? AudioPath { get; set; }
        public string? AudioUrl { get; set; }
        public bool MessageSent { get; set; }
        public string? MessageId { get; set; }
        public string? PairId { get; set; }
    }

    public class TextToSpeechResult
    {
        public bool Success { get; set; }
        public byte[]? AudioData { get; set; }
        public string AudioFormat { get; set; } = "wav";
        public string VoiceName { get; set; } = string.Empty;
        public int TextLength { get; set; }
        public long AudioSizeBytes { get; set; }
        public string? ErrorMessage { get; set; }
        public string? SavedFilePath { get; set; } // Ruta donde se guardó el archivo automáticamente
    }
}

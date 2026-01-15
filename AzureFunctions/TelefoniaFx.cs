using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;
using TwinAgentsLibrary.Services;
using TwinAgentsLibrary.Models;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Functions para funcionalidad de telefonía y mensajes de voz
    /// Permite transcripción de voz a texto y texto a voz
    /// </summary>
    public class TelefoniaFx
    {
        private readonly ILogger<TelefoniaFx> _logger;
        private readonly IConfiguration _configuration;

        public TelefoniaFx(ILogger<TelefoniaFx> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        #region Voice Transcription Functions

        /// <summary>
        /// OPTIONS handler for voice transcription
        /// </summary>
        [Function("TranscribeVoiceOptions")]
        public async Task<HttpResponseData> HandleTranscribeVoiceOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "telefonia/transcribe/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for telefonia/transcribe/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Transcribe un mensaje de voz a texto
        /// POST /api/telefonia/transcribe/{twinId}
        /// Body: JSON con audioBase64 o multipart/form-data con archivo de audio
        /// AHORA GUARDA EL ARCHIVO EN DATA LAKE PRIMERO
        /// </summary>
        [Function("TranscribeVoice")]
        public async Task<HttpResponseData> TranscribeVoice(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "telefonia/transcribe/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🎤 TranscribeVoice function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, errorMessage = "Twin ID parameter is required" });
                    return badResponse;
                }

                byte[] audioBytes = Array.Empty<byte>();
                string language = "es-MX";
                string audioFormat = "wav";
                bool sendMessage = false;
                string clientePrimeroID = string.Empty;
                EnviarMensajeRequest? messageRequest = null;

                // Detectar si es multipart/form-data o JSON
                var contentType = req.Headers.GetValues("Content-Type")?.FirstOrDefault() ?? "";
                
                if (contentType.Contains("multipart/form-data"))
                {
                    _logger.LogInformation("📦 Processing multipart/form-data");
                    
                    // Leer el boundary del Content-Type
                    var boundary = contentType.Split(';')
                        .Select(x => x.Trim())
                        .FirstOrDefault(x => x.StartsWith("boundary="))?
                        .Replace("boundary=", "");

                    if (string.IsNullOrEmpty(boundary))
                    {
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse, req);
                        await badResponse.WriteAsJsonAsync(new { success = false, errorMessage = "Invalid multipart boundary" });
                        return badResponse;
                    }

                    // Leer el body completo como bytes (NO COMO TEXTO)
                    using var memStream = new MemoryStream();
                    await req.Body.CopyToAsync(memStream);
                    byte[] bodyBytes = memStream.ToArray();
                    
                    _logger.LogInformation($"📦 Read {bodyBytes.Length} bytes from request body");
                    
                    // Convertir a string solo para buscar boundaries y headers
                    string body = System.Text.Encoding.UTF8.GetString(bodyBytes);
                    
                    // Extraer el archivo del multipart
                    var boundaryDelimiter = $"--{boundary}";
                    var parts = body.Split(new[] { boundaryDelimiter }, StringSplitOptions.RemoveEmptyEntries);
                    
                    TranscribeVoiceRequest? transcribeRequest = null;
                    
                    // PRIMER LOOP: Extraer archivo de audio
                    foreach (var part in parts)
                    {
                        if (part.Contains("Content-Disposition") && part.Contains("filename="))
                        {
                            // Encontrar el inicio del contenido binario
                            var headerEnd = part.IndexOf("\r\n\r\n");
                            if (headerEnd > 0)
                            {
                                // El contenido empieza después de \r\n\r\n
                                var contentStartInPart = headerEnd + 4;
                                
                                // Calcular la posición real en bodyBytes
                                // Necesitamos encontrar dónde está este part en el body original
                                var partHeaderBytes = System.Text.Encoding.UTF8.GetBytes(part.Substring(0, headerEnd + 4));
                                
                                // Buscar el patrón del header en bodyBytes
                                int partStartIndex = FindBytePattern(bodyBytes, partHeaderBytes);
                                
                                if (partStartIndex >= 0)
                                {
                                    int audioStartIndex = partStartIndex + partHeaderBytes.Length;
                                    
                                    // Encontrar el final del contenido (antes del siguiente boundary)
                                    var nextBoundaryBytes = System.Text.Encoding.UTF8.GetBytes($"\r\n--{boundary}");
                                    int audioEndIndex = FindBytePattern(bodyBytes, nextBoundaryBytes, audioStartIndex);
                                    
                                    if (audioEndIndex < 0)
                                    {
                                        // Si no encuentra el siguiente boundary, usar hasta el final
                                        audioEndIndex = bodyBytes.Length;
                                    }
                                    
                                    int audioLength = audioEndIndex - audioStartIndex;
                                    audioBytes = new byte[audioLength];
                                    Array.Copy(bodyBytes, audioStartIndex, audioBytes, 0, audioLength);
                                    
                                    _logger.LogInformation($"📁 Audio file extracted from multipart. Size: {audioBytes.Length} bytes");
                                    
                                    // Verificar que sean bytes de audio válidos (verificar header WAV/RIFF si es wav)
                                    if (audioBytes.Length >= 4)
                                    {
                                        string fileHeader = System.Text.Encoding.ASCII.GetString(audioBytes, 0, 4);
                                        _logger.LogInformation($"🔍 Audio file header: {fileHeader}");
                                    }
                                }
                                break;
                            }
                        }
                    }

                    // SEGUNDO LOOP: Buscar campo "data" con JSON completo
                    foreach (var part in parts)
                    {
                        if (part.Contains("Content-Disposition") && part.Contains("name=\"data\"") && !part.Contains("filename="))
                        {
                            var valueStart = part.IndexOf("\r\n\r\n") + 4;
                            if (valueStart > 3)
                            {
                                var jsonData = part.Substring(valueStart).Trim();
                                if (!string.IsNullOrEmpty(jsonData))
                                {
                                    try
                                    {
                                        transcribeRequest = JsonSerializer.Deserialize<TranscribeVoiceRequest>(jsonData, new JsonSerializerOptions
                                        {
                                            PropertyNameCaseInsensitive = true
                                        });
                                        _logger.LogInformation("📋 JSON data extracted from multipart field 'data': {Json}", jsonData);
                                    }
                                    catch (Exception jsonEx)
                                    {
                                        _logger.LogError(jsonEx, "❌ Failed to parse JSON from 'data' field: {Json}", jsonData);
                                    }
                                }
                            }
                            break;
                        }
                    }

                    // Aplicar parámetros del JSON si se encontraron
                    if (transcribeRequest != null)
                    {
                        language = transcribeRequest.Language ?? "es-MX";
                        audioFormat = transcribeRequest.AudioFormat ?? "wav";
                        sendMessage = transcribeRequest.SendMessage;
                        clientePrimeroID = transcribeRequest.ClientePrimeroID ?? string.Empty;
                        
                        if (sendMessage && transcribeRequest.MessageData != null)
                        {
                            messageRequest = new EnviarMensajeRequest
                            {
                                ClientePrimeroID = transcribeRequest.MessageData.ClientePrimeroID,
                                ClienteSegundoID = transcribeRequest.MessageData.ClienteSegundoID,
                                DuenoAppTwinID = transcribeRequest.MessageData.DuenoAppTwinID,
                                DuenoAppMicrosoftOID = transcribeRequest.MessageData.DuenoAppMicrosoftOID,
                                Mensaje = string.Empty,
                                DeQuien = transcribeRequest.MessageData.DeQuien,
                                ParaQuien = transcribeRequest.MessageData.ParaQuien,
                                Origin = transcribeRequest.MessageData.Origin ?? "voice"
                            };
                        }
                        clientePrimeroID = messageRequest.ClientePrimeroID;
                        _logger.LogInformation("✅ Using parameters from JSON data field");
                    }
                    else
                    {
                        // Fallback: Extraer parámetros individuales del form (método anterior)
                        _logger.LogInformation("📝 Falling back to individual form fields");
                        
                        foreach (var part in parts)
                        {
                            if (part.Contains("Content-Disposition") && part.Contains("name=\"language\""))
                            {
                                var valueStart = part.IndexOf("\r\n\r\n") + 4;
                                if (valueStart > 3)
                                {
                                    var value = part.Substring(valueStart).Trim();
                                    if (!string.IsNullOrEmpty(value)) language = value;
                                }
                            }
                            if (part.Contains("Content-Disposition") && part.Contains("name=\"audioFormat\""))
                            {
                                var valueStart = part.IndexOf("\r\n\r\n") + 4;
                                if (valueStart > 3)
                                {
                                    var value = part.Substring(valueStart).Trim();
                                    if (!string.IsNullOrEmpty(value)) audioFormat = value;
                                }
                            }
                            if (part.Contains("Content-Disposition") && part.Contains("name=\"clientePrimeroID\""))
                            {
                                var valueStart = part.IndexOf("\r\n\r\n") + 4;
                                if (valueStart > 3)
                                {
                                    var value = part.Substring(valueStart).Trim();
                                    if (!string.IsNullOrEmpty(value)) clientePrimeroID = value;
                                }
                            }
                        }
                    }
                    
                    if (audioBytes == null || audioBytes.Length == 0)
                    {
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse, req);
                        await badResponse.WriteAsJsonAsync(new { success = false, errorMessage = "No audio file found in multipart data" });
                        return badResponse;
                    }
                }
                else
                {
                    _logger.LogInformation("📄 Processing JSON");
                    
                    // Leer request body como JSON (legacy)
                    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    var transcribeRequest = JsonSerializer.Deserialize<TranscribeVoiceRequest>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (transcribeRequest == null || string.IsNullOrEmpty(transcribeRequest.AudioBase64))
                    {
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse, req);
                        await badResponse.WriteAsJsonAsync(new { success = false, errorMessage = "Audio data is required (audioBase64 field)" });
                        return badResponse;
                    }

                    // Decodificar audio base64
                    try
                    {
                        audioBytes = Convert.FromBase64String(transcribeRequest.AudioBase64);
                        _logger.LogInformation("📁 Audio decoded. Size: {Size} bytes", audioBytes.Length);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Failed to decode base64 audio");
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse, req);
                        await badResponse.WriteAsJsonAsync(new { success = false, errorMessage = "Invalid base64 audio data" });
                        return badResponse;
                    }

                    language = transcribeRequest.Language ?? "es-MX";
                    audioFormat = transcribeRequest.AudioFormat ?? "wav";
                    sendMessage = transcribeRequest.SendMessage;
                    clientePrimeroID = transcribeRequest.ClientePrimeroID ?? string.Empty;

                    if (sendMessage && transcribeRequest.MessageData != null)
                    {
                        messageRequest = new EnviarMensajeRequest
                        {
                            ClientePrimeroID = transcribeRequest.MessageData.ClientePrimeroID,
                            ClienteSegundoID = transcribeRequest.MessageData.ClienteSegundoID,
                            DuenoAppTwinID = transcribeRequest.MessageData.DuenoAppTwinID,
                            DuenoAppMicrosoftOID = transcribeRequest.MessageData.DuenoAppMicrosoftOID,
                            Mensaje = string.Empty,
                            DeQuien = transcribeRequest.MessageData.DeQuien,
                            ParaQuien = transcribeRequest.MessageData.ParaQuien,
                            Origin = transcribeRequest.MessageData.Origin ?? "voice"
                        };
                        clientePrimeroID = messageRequest.ClientePrimeroID;
                    }
                }

                // 💾 PASO 1: GUARDAR AUDIO EN DATA LAKE PRIMERO
                string? audioFilePath = null;
                string? audioSasUrl = null;

                if (!string.IsNullOrEmpty(clientePrimeroID))
                {
                    try
                    {
                        _logger.LogInformation("💾 Saving audio to Data Lake for Cliente: {ClienteID}", clientePrimeroID);

                        // Configurar Azure Storage
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

                        // Crear DataLakeClient
                        var dataLakeLogger = LoggerFactory.Create(builder => builder.AddConsole())
                            .CreateLogger<DataLakeClient>();
                        var dataLakeClient = new DataLakeClient(twinId, dataLakeLogger, storageSettings);

                        // Generar nombre de archivo con timestamp
                        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                        string fileName = $"voice_{timestamp}_{Guid.NewGuid().ToString("N").Substring(0, 8)}.{audioFormat}";
                        
                        // Construir path: Documents/hablemos/ClientePrimeroID/voz/
                        string directoryPath = $"Documents/hablemos/{clientePrimeroID}/voz";
                        audioFilePath = $"{directoryPath}/{fileName}";
                        string containerName = twinId.ToLowerInvariant();

                        // Determinar MIME type
                        string mimeType = audioFormat.ToLower() switch
                        {
                            "wav" => "audio/wav",
                            "mp3" => "audio/mpeg",
                            "ogg" => "audio/ogg",
                            "webm" => "audio/webm",
                            "m4a" => "audio/m4a",
                            _ => "audio/wav"
                        };

                        // Metadata del archivo
                        var audioMetadata = new Dictionary<string, string>
                        {
                            ["clientePrimeroID"] = clientePrimeroID,
                            ["language"] = language,
                            ["audioFormat"] = audioFormat,
                            ["sizeBytes"] = audioBytes.Length.ToString(),
                            ["uploadedAt"] = DateTime.UtcNow.ToString("O"),
                            ["source"] = "telefonia_transcribe"
                        };

                        // Subir archivo al Data Lake
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
                            // Generar SAS URL con 24 horas de validez
                            audioSasUrl = await dataLakeClient.GenerateSasUrlAsync(audioFilePath, TimeSpan.FromHours(24));
                            
                            _logger.LogInformation("✅ Audio saved to Data Lake. Path: {Path}", audioFilePath);
                            
                            if (!string.IsNullOrEmpty(audioSasUrl))
                            {
                                var urlPreview = audioSasUrl.Length > 100 ? audioSasUrl.Substring(0, 100) + "..." : audioSasUrl;
                                _logger.LogInformation("🔗 SAS URL generated: {Url}", urlPreview);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to upload audio to Data Lake");
                        }
                    }
                    catch (Exception dlEx)
                    {
                        _logger.LogError(dlEx, "❌ Error saving audio to Data Lake");
                        // Continuar con la transcripción aunque falle el guardado
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ ClientePrimeroID not provided - skipping Data Lake upload");
                }

                // PASO 2: Crear instancia del agente de telefonía y transcribir
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var agentLogger = loggerFactory.CreateLogger<AgentTwinTelefonia>();
                var telefoniaAgent = new AgentTwinTelefonia(agentLogger, _configuration);

                _logger.LogInformation("🔊 Starting transcription with Azure Speech SDK...");

                // Transcribir audio usando Azure Speech SDK
                var transcriptionResult =
                    await telefoniaAgent.TranscribeFromDataLakeFileAsync(audioSasUrl, language);

                var processingTime = DateTime.UtcNow - startTime;

                if (!transcriptionResult.Success)
                {
                    _logger.LogError("❌ Transcription failed: {Error}", transcriptionResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        errorMessage = transcriptionResult.ErrorMessage,
                        processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                        audioFilePath = audioFilePath,
                        audioUrl = audioSasUrl
                    });
                    return errorResponse;
                }

                _logger.LogInformation("✅ Transcription successful: {Text}", transcriptionResult.TranscribedText);

                // 💬 PASO 3: ENVIAR MENSAJE CON AGENTHABLEMOS (SI SE SOLICITA)
                bool messageSent = false;
                string? messageId = null;
                string? pairId = null;

                if (sendMessage && messageRequest != null)
                {
                    try
                    {
                        _logger.LogInformation("💬 Sending transcribed message to AgentHablemos");

                        // Crear instancia de AgentHablemos
                        var hablemosLogger = LoggerFactory.Create(builder => builder.AddConsole())
                            .CreateLogger<AgentHablemos>();
                        var hablemosAgent = new AgentHablemos(hablemosLogger, _configuration);

                        // Actualizar el mensaje con el texto transcrito
                        messageRequest.Mensaje = transcriptionResult.TranscribedText;

                        // 🎙️ Agregar información del audio al request
                        messageRequest.vozNombreArchivo = !string.IsNullOrEmpty(audioFilePath) 
                            ? Path.GetFileName(audioFilePath) 
                            : string.Empty;
                        messageRequest.VozPath = audioFilePath;
                        messageRequest.ClientID = clientePrimeroID;
                        messageRequest.SASURLVOZ = audioSasUrl ?? string.Empty;

                        // Enviar mensaje usando EnviarMensajeAsync
                        var mensajeResult = await hablemosAgent.EnviarMensajeAsync(messageRequest);

                        if (mensajeResult.Success && mensajeResult.Mensaje != null)
                        {
                            messageSent = true;
                            messageId = mensajeResult.Mensaje.MessageId;
                            pairId = mensajeResult.PairId;

                            _logger.LogInformation("✅ Message sent successfully with voice info. MessageId: {MessageId}, PairId: {PairId}, AudioFile: {AudioFile}", 
                                messageId, pairId, messageRequest.vozNombreArchivo);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to send message: {Error}", mensajeResult.ErrorMessage);
                        }
                    }
                    catch (Exception msgEx)
                    {
                        _logger.LogError(msgEx, "❌ Error sending message with AgentHablemos");
                        // Continuar - no falla la transcripción si falla el envío del mensaje
                    }
                }

                // Crear respuesta exitosa
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinId = twinId,
                    transcribedText = transcriptionResult.TranscribedText,
                    confidence = transcriptionResult.Confidence,
                    durationSeconds = transcriptionResult.DurationSeconds,
                    audioSizeBytes = transcriptionResult.AudioSizeBytes,
                    language = transcriptionResult.Language,
                    detectedLanguage = transcriptionResult.DetectedLanguage,
                    processedAt = transcriptionResult.ProcessedAt,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    audioFilePath = audioFilePath,
                    audioUrl = audioSasUrl,
                    messageSent = messageSent,
                    messageId = messageId,
                    pairId = pairId,
                    message = "Voice message transcribed successfully with Azure Speech SDK"
                });

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error transcribing voice message");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    errorMessage = ex.Message,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                });
                return errorResponse;
            }
        }

        #endregion

        #region Text-to-Speech Functions

        /// <summary>
        /// OPTIONS handler for text-to-speech
        /// </summary>
        [Function("TextToSpeechOptions")]
        public async Task<HttpResponseData> HandleTextToSpeechOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "telefonia/text-to-speech/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for telefonia/text-to-speech/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Convierte texto a voz
        /// POST /api/telefonia/text-to-speech/{twinId}
        /// Body: JSON con text y voiceName
        /// </summary>
        [Function("TextToSpeech")]
        public async Task<HttpResponseData> TextToSpeech(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "telefonia/text-to-speech/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔊 TextToSpeech function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        errorMessage = "Twin ID parameter is required"
                    });
                    return badResponse;
                }

                // Leer request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var ttsRequest = JsonSerializer.Deserialize<TextToSpeechRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (ttsRequest == null || string.IsNullOrEmpty(ttsRequest.Text))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        errorMessage = "Text is required"
                    });
                    return badResponse;
                }

                // Crear instancia del agente de telefonía
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var agentLogger = loggerFactory.CreateLogger<AgentTelefonia>();
                var telefoniaAgent = new AgentTelefonia(agentLogger, _configuration);

                // Determinar voz
                string voiceName = ttsRequest.VoiceName ?? "es-MX-DaliaNeural";

                _logger.LogInformation("🔊 Converting text to speech. Voice: {Voice}, Text length: {Length}",
                    voiceName, ttsRequest.Text.Length);

                // Convertir texto a voz
                var ttsResult = await telefoniaAgent.ConvertTextToSpeechAsync(ttsRequest.Text, voiceName);

                var processingTime = DateTime.UtcNow - startTime;

                if (!ttsResult.Success)
                {
                    _logger.LogError("❌ Text-to-speech failed: {Error}", ttsResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        errorMessage = ttsResult.ErrorMessage,
                        processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    });
                    return errorResponse;
                }

                _logger.LogInformation("✅ Text-to-speech successful. Audio size: {Size} bytes", ttsResult.AudioSizeBytes);

                // Crear respuesta exitosa
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                // Convertir audio a base64
                string audioBase64 = Convert.ToBase64String(ttsResult.AudioData!);

                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    twinId = twinId,
                    audioBase64 = audioBase64,
                    audioFormat = ttsResult.AudioFormat,
                    voiceName = ttsResult.VoiceName,
                    textLength = ttsResult.TextLength,
                    audioSizeBytes = ttsResult.AudioSizeBytes,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    message = "Text converted to speech successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error converting text to speech");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    errorMessage = ex.Message,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                });
                return errorResponse;
            }
        }

        #endregion

        #region CORS Helper

        private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
            var origin = originHeader.Key != null ? originHeader.Value?.FirstOrDefault() : null;
            var allowedOrigins = new[] { "http://localhost:5173", "http://localhost:3000", "http://127.0.0.1:5173", "http://127.0.0.1:3000" };

            if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
            {
                response.Headers.Add("Access-Control-Allow-Origin", origin);
            }
            else
            {
                response.Headers.Add("Access-Control-Allow-Origin", "*");
            }

            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent");
            response.Headers.Add("Access-Control-Max-Age", "3600");
        }

        /// <summary>
        /// Busca un patrón de bytes dentro de un array de bytes
        /// </summary>
        /// <param name="source">Array donde buscar</param>
        /// <param name="pattern">Patrón a buscar</param>
        /// <param name="startIndex">Índice de inicio de búsqueda</param>
        /// <returns>Índice donde se encuentra el patrón, o -1 si no se encuentra</returns>
        private static int FindBytePattern(byte[] source, byte[] pattern, int startIndex = 0)
        {
            if (source == null || pattern == null || pattern.Length == 0 || startIndex < 0)
                return -1;

            for (int i = startIndex; i <= source.Length - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                    return i;
            }
            return -1;
        }

        #endregion
    }

    #region Request/Response Models

    /// <summary>
    /// Request para transcripción de voz
    /// </summary>
    public class TranscribeVoiceRequest
    {
        public string AudioBase64 { get; set; } = string.Empty;
        public string? Language { get; set; }
        public string? AudioFormat { get; set; }
        public bool AutoDetectLanguage { get; set; }
        public string[]? CandidateLanguages { get; set; }
        
        // Campo para identificar el cliente (para path del archivo en Data Lake)
        public string? ClientePrimeroID { get; set; }
        
        // Campos para enviar mensaje
        public bool SendMessage { get; set; }
        public EnviarMensajeRequestForVoice? MessageData { get; set; }
    }

    /// <summary>
    /// Datos del mensaje para enviar después de transcripción
    /// </summary>
    public class EnviarMensajeRequestForVoice
    {
        public string ClientePrimeroID { get; set; } = string.Empty;
        public string ClienteSegundoID { get; set; } = string.Empty;
        public string DuenoAppTwinID { get; set; } = string.Empty;
        public string DuenoAppMicrosoftOID { get; set; } = string.Empty;
        public string DeQuien { get; set; } = string.Empty;
        public string ParaQuien { get; set; } = string.Empty;
        public string Origin { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request para conversión de texto a voz
    /// </summary>
    public class TextToSpeechRequest
    {
        public string Text { get; set; } = string.Empty;
        public string? VoiceName { get; set; }
    }

    #endregion
}

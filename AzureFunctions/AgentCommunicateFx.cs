using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Functions para gestionar comunicación tipo WhatsApp
    /// Permite crear sesiones, enviar mensajes y mantener conversaciones
    /// </summary>
    public class AgentCommunicateFx
    {
        private readonly ILogger<AgentCommunicateFx> _logger;
        private readonly IConfiguration _configuration;

        public AgentCommunicateFx(
            ILogger<AgentCommunicateFx> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        #region Create Session

        [Function("CreateCommunicationSessionOptions")]
        public async Task<HttpResponseData> HandleCreateSessionOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "communication/session/create")] HttpRequestData req)
        {
            _logger.LogInformation("?? OPTIONS preflight request for communication/session/create");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("CreateCommunicationSession")]
        public async Task<HttpResponseData> CreateSession(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "communication/session/create")] HttpRequestData req)
        {
            _logger.LogInformation("?? CreateCommunicationSession function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("? Request body is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "Request body is required" });
                    return badResponse;
                }

                var request = JsonConvert.DeserializeObject<CreateSessionRequest>(requestBody);

                if (request == null || request.Participants == null || request.Participants.Count < 2)
                {
                    _logger.LogError("? At least 2 participants are required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "At least 2 participants are required" });
                    return badResponse;
                }

                _logger.LogInformation("?? Creating session with {Count} participants", request.Participants.Count);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var agentLogger = loggerFactory.CreateLogger<AgentTwinCommunicate>();
                var agent = new AgentTwinCommunicate(agentLogger, _configuration);
                var result = await agent.CreateSessionAsync(
                    request.SessionId ?? Guid.NewGuid().ToString(),
                    request.Participants,
                    request.SessionName ?? "");

                if (!result.Success)
                {
                    _logger.LogError("? Error creating session: {ErrorMessage}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                // Save to Cosmos DB
                var cosmosLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = cosmosLoggerFactory.CreateLogger<AgentCommunicationCosmosDB>();
                var cosmosService = new AgentCommunicationCosmosDB(cosmosLogger, _configuration);
                
                var saveResult = await cosmosService.SaveSessionAsync(result.Session!);

                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogInformation("? Session created successfully. Session ID: {SessionId}", result.SessionId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Session created successfully",
                    sessionId = result.SessionId,
                    sessionName = result.Session?.SessionName,
                    participantCount = result.Session?.Participants.Count,
                    serializedThread = result.Session?.SerializedThread,
                    ruConsumed = saveResult.RUConsumed,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "? Unexpected error in CreateCommunicationSession");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while creating the session",
                    details = ex.Message,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                });
                return errorResponse;
            }
        }

        #endregion

        #region Send Message

        [Function("SendMessageOptions")]
        public async Task<HttpResponseData> HandleSendMessageOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "communication/message/send")] HttpRequestData req)
        {
            _logger.LogInformation("?? OPTIONS preflight request for communication/message/send");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("SendMessage")]
        public async Task<HttpResponseData> SendMessage(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "communication/message/send")] HttpRequestData req)
        {
            _logger.LogInformation("?? SendMessage function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("? Request body is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "Request body is required" });
                    return badResponse;
                }

                var request = JsonConvert.DeserializeObject<SendMessageRequest>(requestBody);

                if (request == null || string.IsNullOrEmpty(request.SessionId) || 
                    string.IsNullOrEmpty(request.SenderTwinId) || string.IsNullOrEmpty(request.MessageContent))
                {
                    _logger.LogError("? SessionId, SenderTwinId, and MessageContent are required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new 
                    { 
                        success = false, 
                        error = "SessionId, SenderTwinId, and MessageContent are required" 
                    });
                    return badResponse;
                }

                _logger.LogInformation("?? Sending message in session: {SessionId}", request.SessionId);

                // Get session from Cosmos DB
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentCommunicationCosmosDB>();
                var cosmosService = new AgentCommunicationCosmosDB(cosmosLogger, _configuration);
                
                var getSessionResult = await cosmosService.GetSessionByIdAsync(request.SessionId);
                
                if (!getSessionResult.Success || getSessionResult.Session == null)
                {
                    _logger.LogError("? Session not found: {SessionId}", request.SessionId);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteAsJsonAsync(new { success = false, error = "Session not found" });
                    return notFoundResponse;
                }

                var session = getSessionResult.Session;

                // Send message using agent
                var loggerFactory2 = LoggerFactory.Create(builder => builder.AddConsole());
                var agentLogger2 = loggerFactory2.CreateLogger<AgentTwinCommunicate>();
                var agent = new AgentTwinCommunicate(agentLogger2, _configuration);
                var result = await agent.SendMessageAsync(
                    request.SessionId,
                    request.SenderTwinId,
                    request.MessageContent,
                    session.SerializedThread,
                    request.RecipientTwinId);

                if (!result.Success)
                {
                    _logger.LogError("? Error sending message: {ErrorMessage}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                // Update session with new message
                session.SerializedThread = result.UpdatedSerializedThread!;
                var addMessageResult = await cosmosService.AddMessageToSessionAsync(request.SessionId, result.Message!);

                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogInformation("? Message sent successfully in session: {SessionId}", request.SessionId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Message sent successfully",
                    messageId = result.Message?.MessageId,
                    sentAt = result.Message?.SentAt,
                    aiResponse = result.AIResponse,
                    ruConsumed = addMessageResult.RUConsumed,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "? Unexpected error in SendMessage");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while sending the message",
                    details = ex.Message,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                });
                return errorResponse;
            }
        }

        #endregion

        #region Get Sessions

        [Function("GetUserSessionsOptions")]
        public async Task<HttpResponseData> HandleGetUserSessionsOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "communication/sessions/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? OPTIONS preflight request for communication/sessions/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetUserSessions")]
        public async Task<HttpResponseData> GetUserSessions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "communication/sessions/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? GetUserSessions function triggered for TwinId: {TwinId}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "TwinId is required" });
                    return badResponse;
                }

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentCommunicationCosmosDB>();
                var cosmosService = new AgentCommunicationCosmosDB(cosmosLogger, _configuration);
                
                var result = await cosmosService.GetSessionsByParticipantAsync(twinId);

                if (!result.Success)
                {
                    _logger.LogError("? Error getting sessions: {ErrorMessage}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                _logger.LogInformation("? Retrieved {Count} sessions for TwinId: {TwinId}", result.SessionCount, twinId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinId = twinId,
                    sessionCount = result.SessionCount,
                    sessions = result.Sessions,
                    ruConsumed = result.RUConsumed,
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in GetUserSessions");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while retrieving sessions"
                });
                return errorResponse;
            }
        }

        #endregion

        #region Get Session Messages

        [Function("GetSessionMessagesOptions")]
        public async Task<HttpResponseData> HandleGetSessionMessagesOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "communication/session/{sessionId}/messages/{twinId}")] HttpRequestData req,
            string sessionId,
            string twinId)
        {
            _logger.LogInformation("?? OPTIONS preflight request for communication/session/{SessionId}/messages/{TwinId}", sessionId, twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Azure Function para obtener mensajes de una sesión
        /// MEJORADO: Usa AgentTwinCommunicate para enriquecer mensajes
        /// </summary>
        [Function("GetSessionMessages")]
        public async Task<HttpResponseData> GetSessionMessages(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "communication/session/{sessionId}/messages/{twinId}")] HttpRequestData req,
            string sessionId,
            string twinId)
        {
            _logger.LogInformation("?? GetSessionMessages function triggered for SessionId: {SessionId}, TwinId: {TwinId}", 
                sessionId, twinId);

            try
            {
                if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(twinId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "SessionId and TwinId are required" });
                    return badResponse;
                }

                // Obtener sesión de Cosmos DB
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentCommunicationCosmosDB>();
                var cosmosService = new AgentCommunicationCosmosDB(cosmosLogger, _configuration);
                
                var sessionResult = await cosmosService.GetSessionByIdAsync(sessionId);

                if (!sessionResult.Success || sessionResult.Session == null)
                {
                    _logger.LogWarning("?? Session not found: {SessionId}", sessionId);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteAsJsonAsync(new { success = false, error = "Session not found" });
                    return notFoundResponse;
                }

                // ?? NUEVO: Usar AgentTwinCommunicate para enriquecer mensajes
                var agentLogger = loggerFactory.CreateLogger<AgentTwinCommunicate>();
                var agent = new AgentTwinCommunicate(agentLogger, _configuration);
                
                var enrichedResult = agent.GetSessionMessagesEnriched(sessionResult.Session, twinId);

                if (!enrichedResult.Success)
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = enrichedResult.ErrorMessage });
                    return errorResponse;
                }

                _logger.LogInformation("? Retrieved {Count} messages for SessionId: {SessionId}, {UnreadCount} unread", 
                    enrichedResult.TotalCount, sessionId, enrichedResult.UnreadCount);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    sessionId = sessionId,
                    sessionName = sessionResult.Session.SessionName,
                    requestingTwinId = twinId,
                    messageCount = enrichedResult.Messages.Count,
                    unreadCount = enrichedResult.UnreadCount,
                    messages = enrichedResult.Messages,
                    participants = sessionResult.Session.Participants,
                    ruConsumed = sessionResult.RUConsumed,
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in GetSessionMessages");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while retrieving messages"
                });
                return errorResponse;
            }
        }

        #endregion

        #region Mark Messages as Read

        [Function("MarkMessagesAsReadOptions")]
        public async Task<HttpResponseData> HandleMarkMessagesAsReadOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "communication/messages/read")] HttpRequestData req)
        {
            _logger.LogInformation("?? OPTIONS preflight request for communication/messages/read");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("MarkMessagesAsRead")]
        public async Task<HttpResponseData> MarkMessagesAsRead(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "communication/messages/read")] HttpRequestData req)
        {
            _logger.LogInformation("? MarkMessagesAsRead function triggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var request = JsonConvert.DeserializeObject<MarkAsReadRequest>(requestBody);

                if (request == null || string.IsNullOrEmpty(request.SessionId) || 
                    string.IsNullOrEmpty(request.TwinId) || request.MessageIds == null || !request.MessageIds.Any())
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new 
                    { 
                        success = false, 
                        error = "SessionId, TwinId, and MessageIds are required" 
                    });
                    return badResponse;
                }

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentCommunicationCosmosDB>();
                var cosmosService = new AgentCommunicationCosmosDB(cosmosLogger, _configuration);
                
                var result = await cosmosService.MarkMessagesAsReadAsync(
                    request.SessionId,
                    request.TwinId,
                    request.MessageIds);

                if (!result.Success)
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                _logger.LogInformation("? Marked {Count} messages as read", result.UpdatedCount);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = $"{result.UpdatedCount} messages marked as read",
                    updatedCount = result.UpdatedCount,
                    ruConsumed = result.RUConsumed,
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in MarkMessagesAsRead");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while marking messages as read"
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

        #endregion
    }

    #region Request Models

    public class CreateSessionRequest
    {
        [JsonProperty("sessionId")]
        public string? SessionId { get; set; }

        [JsonProperty("sessionName")]
        public string? SessionName { get; set; }

        [JsonProperty("participants")]
        public List<SessionParticipant> Participants { get; set; } = new();
    }

    public class SendMessageRequest
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        [JsonProperty("senderTwinId")]
        public string SenderTwinId { get; set; } = string.Empty;

        [JsonProperty("recipientTwinId")]
        public string? RecipientTwinId { get; set; }

        [JsonProperty("messageContent")]
        public string MessageContent { get; set; } = string.Empty;
    }

    public class MarkAsReadRequest
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        [JsonProperty("twinId")]
        public string TwinId { get; set; } = string.Empty;

        [JsonProperty("messageIds")]
        public List<string> MessageIds { get; set; } = new();
    }

    #endregion
}

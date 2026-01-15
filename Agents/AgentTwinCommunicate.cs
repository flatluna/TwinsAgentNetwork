using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// Agente de comunicación estilo WhatsApp con soporte para sesiones persistentes
    /// Permite mantener conversaciones entre dos o más participantes
    /// </summary>
    public class AgentTwinCommunicate
    {
        private readonly ILogger<AgentTwinCommunicate> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;

        public AgentTwinCommunicate(ILogger<AgentTwinCommunicate> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            _azureOpenAIEndpoint = configuration["Values:AZURE_OPENAI_ENDPOINT"] ??
                                  configuration["AZURE_OPENAI_ENDPOINT"] ??
                                  Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ??
                                  throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");

            _azureOpenAIModelName = configuration["Values:AZURE_OPENAI_MODEL_NAME"] ??
                                   configuration["AZURE_OPENAI_MODEL_NAME"] ??
                                   Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ??
                                   throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");

            _logger.LogInformation("?? AgentTwinCommunicate initialized");
        }

        /// <summary>
        /// Crea una nueva sesión de comunicación entre participantes
        /// </summary>
        /// <param name="sessionId">ID único de la sesión</param>
        /// <param name="participants">Lista de participantes (TwinIDs)</param>
        /// <param name="sessionName">Nombre opcional de la sesión</param>
        /// <returns>Información de la sesión creada</returns>
        public async Task<CreateSessionResult> CreateSessionAsync(
            string sessionId,
            List<SessionParticipant> participants,
            string sessionName = "")
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    sessionId = Guid.NewGuid().ToString();
                }

                if (participants == null || participants.Count < 2)
                {
                    return new CreateSessionResult
                    {
                        Success = false,
                        ErrorMessage = "A session must have at least 2 participants",
                        SessionId = null
                    };
                }

                _logger.LogInformation("?? Creating communication session: {SessionId} with {Count} participants",
                    sessionId, participants.Count);

                // Crear el agente de IA para la sesión
                var chatClient = new AzureOpenAIClient(
                    new Uri(_azureOpenAIEndpoint),
                    new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName);

                // Instrucciones para el agente - actúa como moderador/facilitador
                var instructions = $@"You are a communication facilitator for a conversation session.
Session Name: {(string.IsNullOrEmpty(sessionName) ? "Group Chat" : sessionName)}
Participants: {string.Join(", ", participants.Select(p => p.DisplayName))}

Your role:
- Help facilitate smooth communication between participants
- Provide context when messages are unclear
- Suggest responses when asked
- Maintain a friendly and professional tone
- Do NOT impersonate participants
- Only provide assistance when explicitly requested with @assistant";

                AIAgent agent = chatClient.CreateAIAgent(
                    instructions: instructions,
                    name: "CommunicationAssistant");

                // Crear nuevo thread para la sesión
                AgentThread thread = agent.GetNewThread();

                // Serializar el thread para persistencia
                string serializedThread = thread.Serialize(System.Text.Json.JsonSerializerOptions.Web).GetRawText();

                var session = new CommunicationSession
                {
                    SessionId = sessionId,
                    SessionName = sessionName,
                    Participants = participants,
                    CreatedAt = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow,
                    IsActive = true,
                    SerializedThread = serializedThread,
                    Messages = new List<SessionMessage>()
                };

                _logger.LogInformation("? Communication session created successfully: {SessionId}", sessionId);

                return new CreateSessionResult
                {
                    Success = true,
                    SessionId = sessionId,
                    Session = session,
                    Message = $"Session '{sessionName}' created with {participants.Count} participants"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error creating communication session");
                return new CreateSessionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    SessionId = null
                };
            }
        }

        /// <summary>
        /// Envía un mensaje en una sesión existente
        /// </summary>
        /// <param name="sessionId">ID de la sesión</param>
        /// <param name="senderTwinId">TwinID del remitente</param>
        /// <param name="messageContent">Contenido del mensaje</param>
        /// <param name="serializedThread">Thread serializado de la sesión</param>
        /// <param name="recipientTwinId">TwinID del destinatario (opcional, para mensajes directos)</param>
        /// <returns>Resultado del envío con el mensaje procesado</returns>
        public async Task<SendMessageResult> SendMessageAsync(
            string sessionId,
            string senderTwinId,
            string messageContent,
            string serializedThread,
            string recipientTwinId = null)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(senderTwinId) || string.IsNullOrEmpty(messageContent))
                {
                    return new SendMessageResult
                    {
                        Success = false,
                        ErrorMessage = "SessionId, SenderTwinId, and MessageContent are required",
                        Message = null
                    };
                }

                _logger.LogInformation("?? Sending message in session: {SessionId} from {Sender}",
                    sessionId, senderTwinId);

                // Crear el agente
                var chatClient = new AzureOpenAIClient(
                    new Uri(_azureOpenAIEndpoint),
                    new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName);

                AIAgent agent = chatClient.CreateAIAgent(
                    instructions: "You are a communication assistant.",
                    name: "CommunicationAssistant");

                // Deserializar el thread existente si existe
                AgentThread thread;
                if (!string.IsNullOrEmpty(serializedThread))
                {
                    var jsonElement = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(
                        serializedThread,
                        System.Text.Json.JsonSerializerOptions.Web);
                    thread = agent.DeserializeThread(jsonElement, System.Text.Json.JsonSerializerOptions.Web);
                    _logger.LogInformation("?? Thread deserializado para continuar conversación");
                }
                else
                {
                    thread = agent.GetNewThread();
                    _logger.LogInformation("?? Nuevo thread creado");
                }

                // Crear el mensaje
                var message = new SessionMessage
                {
                    MessageId = Guid.NewGuid().ToString(),
                    SessionId = sessionId,
                    SenderTwinId = senderTwinId,
                    RecipientTwinId = recipientTwinId,
                    Content = messageContent,
                    SentAt = DateTime.UtcNow,
                    IsDelivered = false,
                    IsRead = false
                };

                // Si el mensaje menciona al asistente con @assistant, procesarlo con IA
                string aiResponse = null;
                if (messageContent.Contains("@assistant", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("?? Message mentions assistant, processing with AI");
                    
                    var response = await agent.RunAsync(messageContent, thread);
                    var lastMessage = response.Messages.LastOrDefault();
                    if (lastMessage != null)
                    {
                        // Get text content from message
                        var textContent = lastMessage.Text;
                        aiResponse = textContent;
                    }
                    
                    _logger.LogInformation("? AI response generated: {Response}", aiResponse?.Substring(0, Math.Min(50, aiResponse?.Length ?? 0)));
                }

                // Serializar el thread actualizado
                string updatedSerializedThread = thread.Serialize(System.Text.Json.JsonSerializerOptions.Web).GetRawText();

                _logger.LogInformation("? Message sent successfully in session: {SessionId}", sessionId);

                return new SendMessageResult
                {
                    Success = true,
                    Message = message,
                    AIResponse = aiResponse,
                    UpdatedSerializedThread = updatedSerializedThread,
                    ProcessedMessage = $"Message from {senderTwinId}: {messageContent}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error sending message");
                return new SendMessageResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Message = null
                };
            }
        }

        /// <summary>
        /// Obtiene el historial de mensajes de una sesión filtrados por usuario
        /// MEJORADO: Ahora conecta con Cosmos DB y enriquece con flags útiles
        /// </summary>
        /// <param name="sessionId">ID de la sesión</param>
        /// <param name="twinId">TwinID del usuario solicitante</param>
        /// <param name="unreadOnly">Si es true, solo retorna mensajes no leídos</param>
        /// <returns>Historial de mensajes enriquecido</returns>
        public GetMessagesEnrichedResult GetSessionMessagesEnriched(
            CommunicationSession session,
            string twinId,
            bool unreadOnly = false)
        {
            try
            {
                if (session == null || string.IsNullOrEmpty(twinId))
                {
                    return new GetMessagesEnrichedResult
                    {
                        Success = false,
                        ErrorMessage = "Session and TwinId are required",
                        Messages = new List<EnrichedMessage>()
                    };
                }

                _logger.LogInformation("?? Enriching {Count} messages for TwinId: {TwinId}", 
                    session.Messages.Count, twinId);

                // Enriquecer mensajes con flags útiles
                var enrichedMessages = session.Messages.Select(msg => new EnrichedMessage
                {
                    MessageId = msg.MessageId,
                    SessionId = msg.SessionId,
                    SenderTwinId = msg.SenderTwinId,
                    RecipientTwinId = msg.RecipientTwinId,
                    Content = msg.Content,
                    MessageType = msg.MessageType,
                    SentAt = msg.SentAt,
                    IsDelivered = msg.IsDelivered,
                    DeliveredAt = msg.DeliveredAt,
                    IsRead = msg.IsRead,
                    ReadAt = msg.ReadAt,
                    
                    // ?? FLAGS ÚTILES
                    IsMine = msg.SenderTwinId == twinId,
                    IsForMe = msg.RecipientTwinId == twinId || msg.RecipientTwinId == null,
                    NeedsReadConfirmation = msg.SenderTwinId != twinId && 
                                          !msg.IsRead && 
                                          (msg.RecipientTwinId == twinId || msg.RecipientTwinId == null)
                }).ToList();

                // Filtrar solo no leídos si se solicita
                if (unreadOnly)
                {
                    enrichedMessages = enrichedMessages.Where(m => m.NeedsReadConfirmation).ToList();
                }

                var unreadCount = enrichedMessages.Count(m => m.NeedsReadConfirmation);

                _logger.LogInformation("? Enriched {Count} messages, {UnreadCount} unread", 
                    enrichedMessages.Count, unreadCount);

                return new GetMessagesEnrichedResult
                {
                    Success = true,
                    Messages = enrichedMessages,
                    UnreadCount = unreadCount,
                    TotalCount = session.Messages.Count,
                    RequestingTwinId = twinId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error enriching session messages");
                return new GetMessagesEnrichedResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Messages = new List<EnrichedMessage>()
                };
            }
        }

        /// <summary>
        /// Obtiene solo los mensajes NO LEÍDOS para un usuario específico
        /// </summary>
        public GetMessagesEnrichedResult GetUnreadMessages(CommunicationSession session, string twinId)
        {
            _logger.LogInformation("?? Getting unread messages for {TwinId}", twinId);
            
            return GetSessionMessagesEnriched(session, twinId, unreadOnly: true);
        }

        /// <summary>
        /// Marca mensajes como leídos
        /// </summary>
        public async Task<MarkAsReadResult> MarkMessagesAsReadAsync(
            string sessionId,
            string twinId,
            List<string> messageIds)
        {
            try
            {
                _logger.LogInformation("? Marking {Count} messages as read for {TwinId} in session {SessionId}",
                    messageIds.Count, twinId, sessionId);

                return new MarkAsReadResult
                {
                    Success = true,
                    MarkedCount = messageIds.Count,
                    Message = $"{messageIds.Count} messages marked as read"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error marking messages as read");
                return new MarkAsReadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    MarkedCount = 0
                };
            }
        }
    }

    #region Data Models

    /// <summary>
    /// Representa una sesión de comunicación
    /// </summary>
    public class CommunicationSession
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        [JsonProperty("sessionName")]
        public string SessionName { get; set; } = string.Empty;

        [JsonProperty("participants")]
        public List<SessionParticipant> Participants { get; set; } = new();

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("lastActivityAt")]
        public DateTime LastActivityAt { get; set; }

        [JsonProperty("isActive")]
        public bool IsActive { get; set; }

        [JsonProperty("serializedThread")]
        public string SerializedThread { get; set; } = string.Empty;

        [JsonProperty("messages")]
        public List<SessionMessage> Messages { get; set; } = new();

        [JsonProperty("metadata")]
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Representa un participante en una sesión
    /// </summary>
    public class SessionParticipant
    {
        [JsonProperty("twinId")]
        public string TwinId { get; set; } = string.Empty;

        [JsonProperty("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonProperty("avatarUrl")]
        public string AvatarUrl { get; set; } = string.Empty;

        [JsonProperty("joinedAt")]
        public DateTime JoinedAt { get; set; }

        [JsonProperty("isOnline")]
        public bool IsOnline { get; set; }

        [JsonProperty("lastSeenAt")]
        public DateTime LastSeenAt { get; set; }
    }

    /// <summary>
    /// Representa un mensaje en una sesión
    /// </summary>
    public class SessionMessage
    {
        [JsonProperty("messageId")]
        public string MessageId { get; set; } = string.Empty;

        [JsonProperty("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        [JsonProperty("senderTwinId")]
        public string SenderTwinId { get; set; } = string.Empty;

        [JsonProperty("recipientTwinId")]
        public string? RecipientTwinId { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; } = string.Empty;

        [JsonProperty("messageType")]
        public string MessageType { get; set; } = "text"; // text, image, file, etc.

        [JsonProperty("sentAt")]
        public DateTime SentAt { get; set; }

        [JsonProperty("isDelivered")]
        public bool IsDelivered { get; set; }

        [JsonProperty("deliveredAt")]
        public DateTime? DeliveredAt { get; set; }

        [JsonProperty("isRead")]
        public bool IsRead { get; set; }

        [JsonProperty("readAt")]
        public DateTime? ReadAt { get; set; }

        [JsonProperty("metadata")]
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    #endregion

    #region Result Models

    public class CreateSessionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public string? SessionId { get; set; }
        public CommunicationSession? Session { get; set; }
    }

    public class SendMessageResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ProcessedMessage { get; set; }
        public SessionMessage? Message { get; set; }
        public string? AIResponse { get; set; }
        public string? UpdatedSerializedThread { get; set; }
    }

    public class GetMessagesResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public List<SessionMessage> Messages { get; set; } = new();
    }

    public class MarkAsReadResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public int MarkedCount { get; set; }
    }

    /// <summary>
    /// Mensaje enriquecido con flags útiles para el frontend
    /// </summary>
    public class EnrichedMessage
    {
        public string MessageId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string SenderTwinId { get; set; } = string.Empty;
        public string? RecipientTwinId { get; set; }
        public string Content { get; set; } = string.Empty;
        public string MessageType { get; set; } = "text";
        public DateTime SentAt { get; set; }
        public bool IsDelivered { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
        
        // ?? FLAGS ÚTILES PARA EL FRONTEND
        public bool IsMine { get; set; }  // ¿Yo envié este mensaje?
        public bool IsForMe { get; set; }  // ¿Este mensaje es para mí?
        public bool NeedsReadConfirmation { get; set; }  // ¿Debo marcarlo como leído?
    }

    /// <summary>
    /// Resultado de obtener mensajes enriquecidos
    /// </summary>
    public class GetMessagesEnrichedResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<EnrichedMessage> Messages { get; set; } = new();
        public int UnreadCount { get; set; }
        public int TotalCount { get; set; }
        public string RequestingTwinId { get; set; } = string.Empty;
    }

    #endregion
}

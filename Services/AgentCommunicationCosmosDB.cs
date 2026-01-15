using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;

namespace TwinAgentsNetwork.Services
{
    /// <summary>
    /// Servicio para gestionar sesiones de comunicación y mensajes en Azure Cosmos DB
    /// Database: twinmicasadb
    /// Container: twinagentecommunication
    /// Partition Key: /SessionId
    /// </summary>
    public class AgentCommunicationCosmosDB
    {
        private readonly ILogger<AgentCommunicationCosmosDB> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "twinagentecommunication";
        private CosmosClient _cosmosClient;

        public AgentCommunicationCosmosDB(ILogger<AgentCommunicationCosmosDB> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            _cosmosEndpoint = configuration["Values:MICASA_COSMOS_ENDPOINT"] ??
                             configuration["MICASA_COSMOS_ENDPOINT"] ??
                             Environment.GetEnvironmentVariable("MICASA_COSMOS_ENDPOINT") ??
                             "https://twinmicasacosmosdb.documents.azure.com:443/";

            _cosmosKey = configuration["Values:MICASA_COSMOS_KEY"] ??
                        configuration["MICASA_COSMOS_KEY"] ??
                        Environment.GetEnvironmentVariable("MICASA_COSMOS_KEY") ??
                        string.Empty;
        }

        private async Task InitializeCosmosClientAsync()
        {
            if (_cosmosClient == null)
            {
                try
                {
                    if (string.IsNullOrEmpty(_cosmosKey))
                    {
                        throw new InvalidOperationException("MICASA_COSMOS_KEY environment variable is not configured.");
                    }

                    _cosmosClient = new CosmosClient(_cosmosEndpoint, _cosmosKey);
                    var database = _cosmosClient.GetDatabase(_databaseName);
                    await database.ReadAsync();

                    _logger.LogInformation("? Successfully connected to Communication Cosmos DB");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "? Error connecting to Cosmos DB");
                    throw;
                }
            }
        }

        /// <summary>
        /// Guarda o actualiza una sesión de comunicación
        /// </summary>
        public async Task<SaveSessionResult> SaveSessionAsync(CommunicationSession session)
        {
            if (session == null || string.IsNullOrEmpty(session.SessionId))
            {
                return new SaveSessionResult
                {
                    Success = false,
                    ErrorMessage = "Session and SessionId are required"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                session.LastActivityAt = DateTime.UtcNow;

                _logger.LogInformation("?? Saving communication session: {SessionId}", session.SessionId);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);
                
                // Crear documento con structure para Cosmos DB
                var document = new
                {
                    id = session.SessionId,
                    SessionId = session.SessionId,
                    SessionName = session.SessionName,
                    Participants = session.Participants,
                    CreatedAt = session.CreatedAt,
                    LastActivityAt = session.LastActivityAt,
                    IsActive = session.IsActive,
                    SerializedThread = session.SerializedThread,
                    Messages = session.Messages,
                    Metadata = session.Metadata,
                    type = "communication_session"
                };

                var response = await container.UpsertItemAsync(document, new PartitionKey(session.SessionId));

                _logger.LogInformation("? Session saved successfully. RU consumed: {RU}", response.RequestCharge);

                return new SaveSessionResult
                {
                    Success = true,
                    SessionId = session.SessionId,
                    RUConsumed = response.RequestCharge
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error saving session");
                return new SaveSessionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Obtiene una sesión por ID
        /// </summary>
        public async Task<GetSessionResult> GetSessionByIdAsync(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return new GetSessionResult
                {
                    Success = false,
                    ErrorMessage = "SessionId is required"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("?? Getting session: {SessionId}", sessionId);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);
                var response = await container.ReadItemAsync<CommunicationSession>(sessionId, new PartitionKey(sessionId));

                _logger.LogInformation("? Session retrieved successfully. RU consumed: {RU}", response.RequestCharge);

                return new GetSessionResult
                {
                    Success = true,
                    Session = response.Resource,
                    RUConsumed = response.RequestCharge
                };
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("?? Session not found: {SessionId}", sessionId);
                return new GetSessionResult
                {
                    Success = false,
                    ErrorMessage = "Session not found"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting session");
                return new GetSessionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Obtiene todas las sesiones de un participante
        /// </summary>
        public async Task<GetSessionsResult> GetSessionsByParticipantAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetSessionsResult
                {
                    Success = false,
                    ErrorMessage = "TwinId is required"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("?? Getting sessions for participant: {TwinId}", twinId);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query sessions where participant exists
                var queryDefinition = new QueryDefinition(
                    @"SELECT * FROM c 
                      WHERE ARRAY_CONTAINS(c.Participants, {'twinId': @twinId}, true)")
                    .WithParameter("@twinId", twinId);

                var query = container.GetItemQueryIterator<CommunicationSession>(queryDefinition);

                var sessions = new List<CommunicationSession>();
                double totalRU = 0;

                while (query.HasMoreResults)
                {
                    var response = await query.ReadNextAsync();
                    sessions.AddRange(response.ToList());
                    totalRU += response.RequestCharge;
                }

                // Sort by last activity
                sessions = sessions.OrderByDescending(s => s.LastActivityAt).ToList();

                _logger.LogInformation("? Retrieved {Count} sessions. Total RU consumed: {RU}", sessions.Count, totalRU);

                return new GetSessionsResult
                {
                    Success = true,
                    Sessions = sessions,
                    SessionCount = sessions.Count,
                    RUConsumed = totalRU
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error getting sessions by participant");
                return new GetSessionsResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Agrega un mensaje a una sesión
        /// </summary>
        public async Task<AddMessageResult> AddMessageToSessionAsync(string sessionId, SessionMessage message)
        {
            if (string.IsNullOrEmpty(sessionId) || message == null)
            {
                return new AddMessageResult
                {
                    Success = false,
                    ErrorMessage = "SessionId and Message are required"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("?? Adding message to session: {SessionId}", sessionId);

                // Get session
                var getResult = await GetSessionByIdAsync(sessionId);
                if (!getResult.Success || getResult.Session == null)
                {
                    return new AddMessageResult
                    {
                        Success = false,
                        ErrorMessage = "Session not found"
                    };
                }

                var session = getResult.Session;
                session.Messages.Add(message);
                session.LastActivityAt = DateTime.UtcNow;

                // Save updated session
                var saveResult = await SaveSessionAsync(session);

                if (saveResult.Success)
                {
                    _logger.LogInformation("? Message added successfully to session: {SessionId}", sessionId);
                }

                return new AddMessageResult
                {
                    Success = saveResult.Success,
                    ErrorMessage = saveResult.ErrorMessage,
                    MessageId = message.MessageId,
                    RUConsumed = saveResult.RUConsumed
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error adding message to session");
                return new AddMessageResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Marca mensajes como leídos
        /// </summary>
        public async Task<UpdateMessagesResult> MarkMessagesAsReadAsync(string sessionId, string twinId, List<string> messageIds)
        {
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(twinId) || messageIds == null || !messageIds.Any())
            {
                return new UpdateMessagesResult
                {
                    Success = false,
                    ErrorMessage = "SessionId, TwinId, and MessageIds are required"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("? Marking messages as read in session: {SessionId}", sessionId);

                var getResult = await GetSessionByIdAsync(sessionId);
                if (!getResult.Success || getResult.Session == null)
                {
                    return new UpdateMessagesResult
                    {
                        Success = false,
                        ErrorMessage = "Session not found"
                    };
                }

                var session = getResult.Session;
                int updatedCount = 0;

                foreach (var messageId in messageIds)
                {
                    var message = session.Messages.FirstOrDefault(m => m.MessageId == messageId);
                    if (message != null && message.RecipientTwinId == twinId)
                    {
                        message.IsRead = true;
                        message.ReadAt = DateTime.UtcNow;
                        updatedCount++;
                    }
                }

                if (updatedCount > 0)
                {
                    var saveResult = await SaveSessionAsync(session);
                    
                    _logger.LogInformation("? Marked {Count} messages as read", updatedCount);
                    
                    return new UpdateMessagesResult
                    {
                        Success = saveResult.Success,
                        UpdatedCount = updatedCount,
                        RUConsumed = saveResult.RUConsumed
                    };
                }

                return new UpdateMessagesResult
                {
                    Success = true,
                    UpdatedCount = 0,
                    Message = "No messages were updated"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error marking messages as read");
                return new UpdateMessagesResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Cierra una sesión
        /// </summary>
        public async Task<CloseSessionResult> CloseSessionAsync(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return new CloseSessionResult
                {
                    Success = false,
                    ErrorMessage = "SessionId is required"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("?? Closing session: {SessionId}", sessionId);

                var getResult = await GetSessionByIdAsync(sessionId);
                if (!getResult.Success || getResult.Session == null)
                {
                    return new CloseSessionResult
                    {
                        Success = false,
                        ErrorMessage = "Session not found"
                    };
                }

                var session = getResult.Session;
                session.IsActive = false;
                session.LastActivityAt = DateTime.UtcNow;

                var saveResult = await SaveSessionAsync(session);

                _logger.LogInformation("? Session closed successfully: {SessionId}", sessionId);

                return new CloseSessionResult
                {
                    Success = saveResult.Success,
                    ErrorMessage = saveResult.ErrorMessage,
                    RUConsumed = saveResult.RUConsumed
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error closing session");
                return new CloseSessionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }

    #region Result Models

    public class SaveSessionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? SessionId { get; set; }
        public double RUConsumed { get; set; }
    }

    public class GetSessionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public CommunicationSession? Session { get; set; }
        public double RUConsumed { get; set; }
    }

    public class GetSessionsResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<CommunicationSession> Sessions { get; set; } = new();
        public int SessionCount { get; set; }
        public double RUConsumed { get; set; }
    }

    public class AddMessageResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? MessageId { get; set; }
        public double RUConsumed { get; set; }
    }

    public class UpdateMessagesResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public int UpdatedCount { get; set; }
        public double RUConsumed { get; set; }
    }

    public class CloseSessionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public double RUConsumed { get; set; }
    }

    #endregion
}

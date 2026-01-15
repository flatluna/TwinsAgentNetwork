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
    /// Service for managing Twin Agenda Customer appointments in Azure Cosmos DB
    /// Database: twinagendacustomerdb
    /// Container: agendacustomercontainer
    /// Partition Key: /TwinID
    /// </summary>
    public class AgentTwinAgendaCustomerCosmosDB
    {
        private readonly ILogger<AgentTwinAgendaCustomerCosmosDB> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "twinagendacustomer";
        private CosmosClient _cosmosClient;

        public AgentTwinAgendaCustomerCosmosDB(ILogger<AgentTwinAgendaCustomerCosmosDB> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            
            _cosmosEndpoint = configuration["Values:MICASA_COSMOS_ENDPOINT:Endpoint"] ?? 
                              configuration["MICASA_COSMOS_ENDPOINT:Endpoint"] ?? 
                              Environment.GetEnvironmentVariable("MICASA_COSMOS_ENDPOINT") ?? 
                              "https://twinagendacustomerdb.documents.azure.com:443/";
            
            _cosmosKey = configuration["Values:MICASA_COSMOS_KEY:Key"] ?? 
                        configuration["MICASA_COSMOS_KEY:Key"] ?? 
                        Environment.GetEnvironmentVariable("MICASA_COSMOS_KEY") ?? 
                        string.Empty;
        }

        /// <summary>
        /// Initialize Cosmos DB client connection
        /// </summary>
        private async Task InitializeCosmosClientAsync()
        {
            if (_cosmosClient == null)
            {
                try
                {
                    if (string.IsNullOrEmpty(_cosmosKey))
                    {
                        throw new InvalidOperationException("Cosmos DB key is not configured. Please set it in configuration or environment variables.");
                    }

                    _cosmosClient = new CosmosClient(_cosmosEndpoint, _cosmosKey);
                    var database = _cosmosClient.GetDatabase(_databaseName);
                    await database.ReadAsync();
                    
                    _logger.LogInformation("✅ Successfully connected to Twin Agenda Customer Cosmos DB");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error connecting to Cosmos DB");
                    throw;
                }
            }
        }

        /// <summary>
        /// Saves an agenda appointment for a customer to Cosmos DB
        /// </summary>
        /// <param name="agendaData">Agenda data containing house information and appointment details</param>
        /// <param name="twinId">Twin ID for partitioning</param>
        /// <returns>Result with document ID</returns>
        public async Task<SaveAgendaResult> SaveAgendaAsync(DatosCasaExtraidos agendaData, string twinId)
        {
            if (agendaData == null)
            {
                return new SaveAgendaResult
                {
                    Success = false,
                    ErrorMessage = "Agenda data cannot be null",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new SaveAgendaResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    DocumentId = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Generate unique document ID
                string documentId = Guid.NewGuid().ToString();

                // Ensure TwinID and id are set
                agendaData.TwinID = twinId;
                agendaData.id = documentId;

                // Serialize and add metadata
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(agendaData));
                
                documentData["id"] = documentId;
                documentData["TwinID"] = twinId;
                documentData["type"] = "agenda_customer";
                documentData["fechaCreacion"] = DateTime.UtcNow;
                documentData["fechaVisita"] = agendaData.AgendarCasa?.FechaVisita ?? "";
                documentData["horaVisita"] = agendaData.AgendarCasa?.HoraVisita ?? "";

                // Save to Cosmos DB with partition key
                var response = await container.CreateItemAsync(documentData, new PartitionKey(twinId));

                _logger.LogInformation("✅ Agenda saved successfully. Document ID: {DocumentId}, Casa: {Direccion}", 
                    documentId, agendaData.DireccionCompleta);

                return new SaveAgendaResult
                {
                    Success = true,
                    DocumentId = documentId,
                    TwinId = twinId,
                    RUConsumed = response.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new SaveAgendaResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    DocumentId = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving agenda");

                return new SaveAgendaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = null
                };
            }
        }

        /// <summary>
        /// Retrieves an agenda by document ID
        /// </summary>
        /// <param name="documentId">Document ID to retrieve</param>
        /// <param name="twinId">TwinID for partition key</param>
        /// <returns>Result with agenda data</returns>
        public async Task<GetAgendaResult> GetAgendaByIdAsync(string documentId, string twinId)
        {
            if (string.IsNullOrEmpty(documentId))
            {
                return new GetAgendaResult
                {
                    Success = false,
                    ErrorMessage = "Document ID cannot be null or empty",
                    Agenda = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new GetAgendaResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Agenda = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.id = @documentId AND c.TwinID = @twinId";
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@documentId", documentId)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<DatosCasaExtraidos> feed = container.GetItemQueryIterator<DatosCasaExtraidos>(queryDefinition);

                DatosCasaExtraidos agenda = null;
                double totalRU = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<DatosCasaExtraidos> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    if (response.Count > 0)
                    {
                        agenda = response.FirstOrDefault();
                        break;
                    }
                }

                if (agenda == null)
                {
                    _logger.LogWarning("⚠️ Agenda not found: {DocumentId}", documentId);
                    return new GetAgendaResult
                    {
                        Success = false,
                        ErrorMessage = $"Agenda with ID '{documentId}' not found",
                        Agenda = null
                    };
                }

                _logger.LogInformation("✅ Retrieved agenda: {DocumentId}", documentId);

                return new GetAgendaResult
                {
                    Success = true,
                    Agenda = agenda,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetAgendaResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Agenda = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving agenda");
                return new GetAgendaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Agenda = null
                };
            }
        }

        /// <summary>
        /// Retrieves all agendas for a specific TwinID
        /// </summary>
        /// <param name="twinId">TwinID to search for</param>
        /// <returns>List of agendas</returns>
        public async Task<GetAgendasByTwinIdResult> GetAgendasByTwinIdAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetAgendasByTwinIdResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.fechaCreacion DESC";
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<DatosCasaExtraidos> feed = container.GetItemQueryIterator<DatosCasaExtraidos>(queryDefinition);

                var agendas = new List<DatosCasaExtraidos>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<DatosCasaExtraidos> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var agenda in response)
                    {
                        agendas.Add(agenda);
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} agendas for TwinID: {TwinId}. RU consumed: {RU:F2}", 
                    count, twinId, totalRU);

                return new GetAgendasByTwinIdResult
                {
                    Success = true,
                    Agendas = agendas,
                    AgendaCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetAgendasByTwinIdResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving agendas");

                return new GetAgendasByTwinIdResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }
        }

        /// <summary>
        /// Retrieves all agendas for a specific Cliente (buyer/customer)
        /// Useful when a customer wants to see all their scheduled property visits
        /// </summary>
        /// <param name="clienteId">Cliente ID to search for</param>
        /// <param name="twinId">TwinID for partition key</param>
        /// <returns>List of agendas for the specified cliente</returns>
        public async Task<GetAgendasByClienteIdResult> GetAgendasByClienteIdAsync(string clienteId, string twinId)
        {
            if (string.IsNullOrEmpty(clienteId))
            {
                return new GetAgendasByClienteIdResult
                {
                    Success = false,
                    ErrorMessage = "ClienteID cannot be null or empty",
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new GetAgendasByClienteIdResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query agendas for specific cliente
                // Note: Sorting done in-memory to avoid composite index requirement
                string query = @"SELECT * FROM c 
                    WHERE c.TwinID = @twinId 
                    AND c.clienteID = @clienteId";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@clienteId", clienteId);

                using FeedIterator<DatosCasaExtraidos> feed = container.GetItemQueryIterator<DatosCasaExtraidos>(queryDefinition);

                var agendas = new List<DatosCasaExtraidos>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<DatosCasaExtraidos> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var agenda in response)
                    {
                        agendas.Add(agenda);
                        count++;
                    }
                }

                // Sort in-memory by fechaVisita DESC, then horaVisita ASC
                agendas = agendas
                    .OrderByDescending(a => a.AgendarCasa?.FechaVisita ?? "")
                    .ThenBy(a => a.AgendarCasa?.HoraVisita ?? "")
                    .ToList();

                _logger.LogInformation("✅ Retrieved {Count} agendas for ClienteID: {ClienteId} (TwinID: {TwinId}). RU consumed: {RU:F2}", 
                    count, clienteId, twinId, totalRU);

                return new GetAgendasByClienteIdResult
                {
                    Success = true,
                    ClienteId = clienteId,
                    Agendas = agendas,
                    AgendaCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetAgendasByClienteIdResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving agendas for cliente");

                return new GetAgendasByClienteIdResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }
        }

        /// <summary>
        /// Retrieves all prospect agendas (estatusCasa = "Prospecto") for a specific Cliente and TwinID
        /// Useful when a customer wants to see only their properties marked as prospects
        /// </summary>
        /// <param name="clienteId">Cliente ID to search for</param>
        /// <param name="twinId">TwinID for partition key</param>
        /// <returns>List of prospect agendas for the specified cliente</returns>
        public async Task<GetProspectoAgendasResult> GetProspectoAgendasByClienteIdAsync(string clienteId, string twinId)
        {
            if (string.IsNullOrEmpty(clienteId))
            {
                return new GetProspectoAgendasResult
                {
                    Success = false,
                    ErrorMessage = "ClienteID cannot be null or empty",
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new GetProspectoAgendasResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query agendas for specific cliente with estatusCasa = "Prospecto"
                // Note: Sorting done in-memory to avoid composite index requirement
                string query = @"SELECT * FROM c 
                    WHERE c.TwinID = @twinId 
                    AND c.clienteID = @clienteId 
                    AND c.estatusCasa = @estatusCasa";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@clienteId", clienteId)
                    .WithParameter("@estatusCasa", "Prospecto");

                using FeedIterator<DatosCasaExtraidos> feed = container.GetItemQueryIterator<DatosCasaExtraidos>(queryDefinition);

                var agendas = new List<DatosCasaExtraidos>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<DatosCasaExtraidos> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var agenda in response)
                    {
                        agendas.Add(agenda);
                        count++;
                    }
                }

                // Sort in-memory by fechaVisita DESC, then horaVisita ASC
                agendas = agendas
                    .OrderByDescending(a => a.AgendarCasa?.FechaVisita ?? "")
                    .ThenBy(a => a.AgendarCasa?.HoraVisita ?? "")
                    .ToList();

                _logger.LogInformation("✅ Retrieved {Count} prospect agendas for ClienteID: {ClienteId} (TwinID: {TwinId}). RU consumed: {RU:F2}", 
                    count, clienteId, twinId, totalRU);

                return new GetProspectoAgendasResult
                {
                    Success = true,
                    ClienteId = clienteId,
                    TwinId = twinId,
                    Agendas = agendas,
                    AgendaCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetProspectoAgendasResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving prospect agendas for cliente");

                return new GetProspectoAgendasResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }
        }

        /// <summary>
        /// Retrieves all scheduled agendas (estatusCasa = "Agendado") for a specific Cliente and TwinID
        /// Useful when a customer wants to see only their properties with scheduled visits
        /// </summary>
        /// <param name="clienteId">Cliente ID to search for</param>
        /// <param name="twinId">TwinID for partition key</param>
        /// <returns>List of scheduled agendas for the specified cliente</returns>
        public async Task<GetAgendadoAgendasResult> GetAgendadoAgendasByClienteIdAsync(string clienteId, string twinId)
        {
            if (string.IsNullOrEmpty(clienteId))
            {
                return new GetAgendadoAgendasResult
                {
                    Success = false,
                    ErrorMessage = "ClienteID cannot be null or empty",
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new GetAgendadoAgendasResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query agendas for specific cliente with estatusCasa = "Agendado"
                // Note: Sorting done in-memory to avoid composite index requirement
                string query = @"SELECT * FROM c 
                    WHERE c.TwinID = @twinId 
                    AND c.clienteID = @clienteId 
                    AND c.estatusCasa = @estatusCasa";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@clienteId", clienteId)
                    .WithParameter("@estatusCasa", "Agendado");

                using FeedIterator<DatosCasaExtraidos> feed = container.GetItemQueryIterator<DatosCasaExtraidos>(queryDefinition);

                var agendas = new List<DatosCasaExtraidos>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<DatosCasaExtraidos> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var agenda in response)
                    {
                        agendas.Add(agenda);
                        count++;
                    }
                }

                // Sort in-memory by fechaVisita DESC, then horaVisita ASC
                agendas = agendas
                    .OrderByDescending(a => a.AgendarCasa?.FechaVisita ?? "")
                    .ThenBy(a => a.AgendarCasa?.HoraVisita ?? "")
                    .ToList();

                _logger.LogInformation("✅ Retrieved {Count} scheduled agendas for ClienteID: {ClienteId} (TwinID: {TwinId}). RU consumed: {RU:F2}", 
                    count, clienteId, twinId, totalRU);

                return new GetAgendadoAgendasResult
                {
                    Success = true,
                    ClienteId = clienteId,
                    TwinId = twinId,
                    Agendas = agendas,
                    AgendaCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetAgendadoAgendasResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving scheduled agendas for cliente");

                return new GetAgendadoAgendasResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }
        }

        /// <summary>
        /// Retrieves all agendas for a specific MicrosoftOID (user identity)
        /// Useful when a user wants to see all their property visits across all clients
        /// </summary>
        /// <param name="microsoftOID">Microsoft Object ID to search for</param>
        /// <param name="twinId">TwinID for partition key</param>
        /// <returns>List of agendas for the specified Microsoft OID</returns>
        public async Task<GetAgendasByMicrosoftOIDResult> GetAgendasByMicrosoftOIDAsync(string microsoftOID, string twinId)
        {
            if (string.IsNullOrEmpty(microsoftOID))
            {
                return new GetAgendasByMicrosoftOIDResult
                {
                    Success = false,
                    ErrorMessage = "MicrosoftOID cannot be null or empty",
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new GetAgendasByMicrosoftOIDResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query agendas for specific Microsoft OID
                // Note: Sorting done in-memory to avoid composite index requirement
                string query = @"SELECT * FROM c 
                    WHERE c.TwinID = @twinId 
                    AND c.microsoftOID = @microsoftOID";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@microsoftOID", microsoftOID);

                using FeedIterator<DatosCasaExtraidos> feed = container.GetItemQueryIterator<DatosCasaExtraidos>(queryDefinition);

                var agendas = new List<DatosCasaExtraidos>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<DatosCasaExtraidos> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var agenda in response)
                    {
                        agendas.Add(agenda);
                        count++;
                    }
                }

                // Sort in-memory by fechaVisita DESC, then horaVisita ASC
                agendas = agendas
                    .OrderByDescending(a => a.AgendarCasa?.FechaVisita ?? "")
                    .ThenBy(a => a.AgendarCasa?.HoraVisita ?? "")
                    .ToList();

                _logger.LogInformation("✅ Retrieved {Count} agendas for MicrosoftOID: {MicrosoftOID} (TwinID: {TwinId}). RU consumed: {RU:F2}", 
                    count, microsoftOID, twinId, totalRU);

                return new GetAgendasByMicrosoftOIDResult
                {
                    Success = true,
                    MicrosoftOID = microsoftOID,
                    TwinId = twinId,
                    Agendas = agendas,
                    AgendaCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetAgendasByMicrosoftOIDResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving agendas by Microsoft OID");

                return new GetAgendasByMicrosoftOIDResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }
        }
        /// Retrieves all agendas for a specific MicrosoftOID (user identity)
        /// Useful when a user wants to see all their property visits across all clients
        /// </summary>
        /// <param name="microsoftOID">Microsoft Object ID to search for</param>
        /// <param name="twinId">TwinID for partition key</param>
        /// <returns>List of agendas for the specified Microsoft OID</returns>
        public async Task<GetAgendasByMicrosoftOIDResult> GetAgendasOIDClientIDAsync(string microsoftOID, string ClientID)
        {
            if (string.IsNullOrEmpty(microsoftOID))
            {
                return new GetAgendasByMicrosoftOIDResult
                {
                    Success = false,
                    ErrorMessage = "MicrosoftOID cannot be null or empty",
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }

            if (string.IsNullOrEmpty(ClientID))
            {
                return new GetAgendasByMicrosoftOIDResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query agendas for specific Microsoft OID
                // Note: Sorting done in-memory to avoid composite index requirement
                string query = @"SELECT * FROM c 
                    WHERE c.clienteID = @clientId 
                    AND c.microsoftOID = @microsoftOID";

                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@clientId", ClientID)
                    .WithParameter("@microsoftOID", microsoftOID);

                using FeedIterator<DatosCasaExtraidos> feed = container.GetItemQueryIterator<DatosCasaExtraidos>(queryDefinition);

                var agendas = new List<DatosCasaExtraidos>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<DatosCasaExtraidos> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var agenda in response)
                    {
                        agendas.Add(agenda);
                        count++;
                    }
                }

                // Sort in-memory by fechaVisita DESC, then horaVisita ASC
                agendas = agendas
                    .OrderByDescending(a => a.AgendarCasa?.FechaVisita ?? "")
                    .ThenBy(a => a.AgendarCasa?.HoraVisita ?? "")
                    .ToList();

        

                return new GetAgendasByMicrosoftOIDResult
                {
                    Success = true,
                    MicrosoftOID = microsoftOID, 
                    Agendas = agendas,
                    AgendaCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetAgendasByMicrosoftOIDResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving agendas by Microsoft OID");

                return new GetAgendasByMicrosoftOIDResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }
        }
        /// <summary>
        /// Retrieves agendas for a specific date (useful for daily schedule)
        /// </summary>
        /// <param name="twinId">TwinID for partition key</param>
        /// <param name="fechaVisita">Visit date (format: "DD/MM/YYYY")</param>
        /// <returns>List of agendas for the specified date</returns>
        public async Task<GetAgendasByDateResult> GetAgendasByDateAsync(string twinId, string fechaVisita)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetAgendasByDateResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }

            if (string.IsNullOrEmpty(fechaVisita))
            {
                return new GetAgendasByDateResult
                {
                    Success = false,
                    ErrorMessage = "FechaVisita cannot be null or empty",
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query agendas for specific date
                // Note: Sorting done in-memory to avoid composite index requirement
                string query = @"SELECT * FROM c 
                    WHERE c.TwinID = @twinId 
                    AND c.comoLlegar.fechaVisita = @fechaVisita";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@fechaVisita", fechaVisita);

                using FeedIterator<DatosCasaExtraidos> feed = container.GetItemQueryIterator<DatosCasaExtraidos>(queryDefinition);

                var agendas = new List<DatosCasaExtraidos>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<DatosCasaExtraidos> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var agenda in response)
                    {
                        agendas.Add(agenda);
                        count++;
                    }
                }

                // Sort in-memory by horaVisita ASC
                agendas = agendas
                    .OrderBy(a => a.AgendarCasa?.HoraVisita ?? "")
                    .ToList();

                _logger.LogInformation("✅ Retrieved {Count} agendas for date: {FechaVisita} (TwinID: {TwinId}). RU consumed: {RU:F2}", 
                    count, fechaVisita, twinId, totalRU);

                return new GetAgendasByDateResult
                {
                    Success = true,
                    FechaVisita = fechaVisita,
                    Agendas = agendas,
                    AgendaCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetAgendasByDateResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving agendas by date");

                return new GetAgendasByDateResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Agendas = new List<DatosCasaExtraidos>()
                };
            }
        }

        /// <summary>
        /// Updates an existing agenda
        /// </summary>
        /// <param name="documentId">Document ID to update</param>
        /// <param name="twinId">TwinID for partition key</param>
        /// <param name="updatedAgenda">Updated agenda data</param>
        /// <returns>Result of update operation</returns>
        public async Task<UpdateAgendaResult> UpdateAgendaAsync(string documentId, string twinId, DatosCasaExtraidos updatedAgenda)
        {
            if (string.IsNullOrEmpty(documentId))
            {
                return new UpdateAgendaResult
                {
                    Success = false,
                    ErrorMessage = "Document ID cannot be null or empty",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new UpdateAgendaResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    DocumentId = null
                };
            }

            if (updatedAgenda == null)
            {
                return new UpdateAgendaResult
                {
                    Success = false,
                    ErrorMessage = "Updated agenda data cannot be null",
                    DocumentId = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Ensure IDs are correct
                updatedAgenda.id = documentId;
                updatedAgenda.TwinID = twinId;

                // Update with partition key
                var response = await container.UpsertItemAsync(updatedAgenda, new PartitionKey(twinId));

                _logger.LogInformation("✅ Agenda updated successfully. Document ID: {DocumentId}", documentId);

                return new UpdateAgendaResult
                {
                    Success = true,
                    DocumentId = documentId,
                    TwinId = twinId,
                    RUConsumed = response.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new UpdateAgendaResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    DocumentId = documentId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating agenda");

                return new UpdateAgendaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = documentId
                };
            }
        }

        /// <summary>
        /// Deletes an agenda
        /// </summary>
        /// <param name="documentId">Document ID to delete</param>
        /// <param name="twinId">TwinID for partition key</param>
        /// <returns>Result of delete operation</returns>
        public async Task<DeleteAgendaResult> DeleteAgendaAsync(string documentId, string twinId)
        {
            if (string.IsNullOrEmpty(documentId))
            {
                return new DeleteAgendaResult
                {
                    Success = false,
                    ErrorMessage = "Document ID cannot be null or empty"
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new DeleteAgendaResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                var response = await container.DeleteItemAsync<DatosCasaExtraidos>(documentId, new PartitionKey(twinId));

                _logger.LogInformation("✅ Agenda deleted successfully. Document ID: {DocumentId}", documentId);

                return new DeleteAgendaResult
                {
                    Success = true,
                    DocumentId = documentId,
                    RUConsumed = response.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new DeleteAgendaResult
                {
                    Success = false,
                    ErrorMessage = errorMessage
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting agenda");

                return new DeleteAgendaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Closes Cosmos DB client connection
        /// </summary>
        public void Dispose()
        {
            _cosmosClient?.Dispose();
        }
    }

    #region Result Models

    /// <summary>
    /// Result of saving an agenda
    /// </summary>
    public class SaveAgendaResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string DocumentId { get; set; }
        public string TwinId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of retrieving an agenda by ID
    /// </summary>
    public class GetAgendaResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public DatosCasaExtraidos Agenda { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of retrieving agendas by TwinID
    /// </summary>
    public class GetAgendasByTwinIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<DatosCasaExtraidos> Agendas { get; set; } = new();
        public int AgendaCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of retrieving agendas by ClienteID
    /// </summary>
    public class GetAgendasByClienteIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string ClienteId { get; set; } = "";
        public List<DatosCasaExtraidos> Agendas { get; set; } = new();
        public int AgendaCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of retrieving prospect agendas (estatusCasa = "Prospecto") by ClienteID
    /// </summary>
    public class GetProspectoAgendasResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string ClienteId { get; set; } = "";
        public string TwinId { get; set; } = "";
        public List<DatosCasaExtraidos> Agendas { get; set; } = new();
        public int AgendaCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of retrieving scheduled agendas (estatusCasa = "Agendado") by ClienteID
    /// </summary>
    public class GetAgendadoAgendasResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string ClienteId { get; set; } = "";
        public string TwinId { get; set; } = "";
        public List<DatosCasaExtraidos> Agendas { get; set; } = new();
        public int AgendaCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of retrieving agendas by MicrosoftOID
    /// </summary>
    public class GetAgendasByMicrosoftOIDResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string MicrosoftOID { get; set; } = "";
        public string TwinId { get; set; } = "";
        public List<DatosCasaExtraidos> Agendas { get; set; } = new();
        public int AgendaCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of retrieving agendas by date
    /// </summary>
    public class GetAgendasByDateResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string FechaVisita { get; set; } = "";
        public List<DatosCasaExtraidos> Agendas { get; set; } = new();
        public int AgendaCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of updating an agenda
    /// </summary>
    public class UpdateAgendaResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string DocumentId { get; set; }
        public string TwinId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of deleting an agenda
    /// </summary>
    public class DeleteAgendaResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string DocumentId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace TwinAgentsNetwork.Services
{
    /// <summary>
    /// Servicio para gestionar intereses de compradores en propiedades de Azure Cosmos DB
    /// Database: twinmicasadb
    /// Container: twincasasinterescomprador
    /// Partition Key: /TwinID
    /// </summary>
    public class AgentCasaCompraInteresCosmosDB
    {
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "twincasasinterescomprador";
        private CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AgentCasaCompraInteresCosmosDB> _logger;

        public AgentCasaCompraInteresCosmosDB(ILogger<AgentCasaCompraInteresCosmosDB> logger, IConfiguration configuration = null)
        {
            _logger = logger;
            _configuration = configuration;

            _cosmosEndpoint = _configuration?["Values:MICASA_COSMOS_ENDPOINT"] ?? 
                             _configuration?["MICASA_COSMOS_ENDPOINT"] ?? 
                             Environment.GetEnvironmentVariable("MICASA_COSMOS_ENDPOINT") ?? 
                             "https://twinmicasacosmosdb.documents.azure.com:443/";
            
            _cosmosKey = _configuration?["Values:MICASA_COSMOS_KEY"] ?? 
                        _configuration?["MICASA_COSMOS_KEY"] ?? 
                        Environment.GetEnvironmentVariable("MICASA_COSMOS_KEY") ?? 
                        string.Empty;
        }

        /// <summary>
        /// Inicializa el cliente de Cosmos DB
        /// </summary>
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
                    
                    _logger.LogInformation("✅ Successfully connected to Casa Interes Comprador Cosmos DB");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error connecting to Cosmos DB");
                    throw;
                }
            }
        }

        /// <summary>
        /// Guarda un nuevo interés de propiedad en Cosmos DB
        /// </summary>
        /// <param name="interes">Datos del interés en la propiedad</param>
        /// <param name="twinID">ID del twin/agente</param>
        /// <returns>Resultado de la operación con ID del documento creado</returns>
        public async Task<SaveInteresResult> SaveInteresAsync(CasaInteresRequest interes, string twinID)
        {
            if (interes == null)
            {
                return new SaveInteresResult
                {
                    Success = false,
                    ErrorMessage = "Interes data cannot be null",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(twinID))
            {
                return new SaveInteresResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(interes.CustomerID))
            {
                return new SaveInteresResult
                {
                    Success = false,
                    ErrorMessage = "CustomerID cannot be null or empty",
                    DocumentId = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Generar ID único para el documento
                string documentId = Guid.NewGuid().ToString();

                // Serializar y adicionar metadatos
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(interes));
                
                documentData["id"] = documentId;
                documentData["TwinID"] = twinID;
                documentData["type"] = "interes_propiedad";
                documentData["fechaCreado"] = DateTime.UtcNow;
                documentData["fechaActualizado"] = DateTime.UtcNow;

                var response = await container.CreateItemAsync(documentData, new PartitionKey(twinID));

                _logger.LogInformation("✅ Interés saved successfully. Document ID: {DocumentId}, URL: {URL}", 
                    documentId, interes.UrlPropiedad);

                return new SaveInteresResult
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

                return new SaveInteresResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    DocumentId = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving interés");
                return new SaveInteresResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = null
                };
            }
        }

        /// <summary>
        /// Obtiene todos los intereses para un TwinID específico
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <returns>Lista de intereses encontrados</returns>
        public async Task<GetInteresesResult> GetInteresesByTwinIdAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetInteresesResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Intereses = new List<CasaInteresRequest>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.fechaCreado DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<CasaInteresRequest> feed = container.GetItemQueryIterator<CasaInteresRequest>(queryDefinition);

                var intereses = new List<CasaInteresRequest>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<CasaInteresRequest> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var interes in response)
                    {
                        intereses.Add(interes);
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} intereses for TwinID: {TwinId}. RU consumed: {RU:F2}", 
                    count, twinId, totalRU);

                return new GetInteresesResult
                {
                    Success = true,
                    Intereses = intereses,
                    InteresCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetInteresesResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Intereses = new List<CasaInteresRequest>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving intereses");
                return new GetInteresesResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Intereses = new List<CasaInteresRequest>()
                };
            }
        }

        /// <summary>
        /// Obtiene todos los intereses para un TwinID y CustomerID específicos
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <param name="customerId">ID del cliente comprador</param>
        /// <returns>Lista de intereses encontrados para el cliente</returns>
        public async Task<GetInteresesResult> GetInteresesByTwinIdAndCustomerIdAsync(string twinId, string customerId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetInteresesResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Intereses = new List<CasaInteresRequest>()
                };
            }

            if (string.IsNullOrEmpty(customerId))
            {
                return new GetInteresesResult
                {
                    Success = false,
                    ErrorMessage = "CustomerID cannot be null or empty",
                    Intereses = new List<CasaInteresRequest>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId AND c.CustomerID = @customerId ORDER BY c.fechaCreado DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@customerId", customerId);

                using FeedIterator<CasaInteresRequest> feed = container.GetItemQueryIterator<CasaInteresRequest>(queryDefinition);

                var intereses = new List<CasaInteresRequest>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<CasaInteresRequest> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var interes in response)
                    {
                        intereses.Add(interes);
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} intereses for TwinID: {TwinId} and CustomerID: {CustomerId}. RU consumed: {RU:F2}", 
                    count, twinId, customerId, totalRU);

                return new GetInteresesResult
                {
                    Success = true,
                    Intereses = intereses,
                    InteresCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetInteresesResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Intereses = new List<CasaInteresRequest>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving intereses by customer");
                return new GetInteresesResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Intereses = new List<CasaInteresRequest>()
                };
            }
        }

        /// <summary>
        /// Obtiene todos los intereses para un TwinID y Microsoft OID específicos
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <param name="microsoftOID">Microsoft Object ID del usuario</param>
        /// <returns>Lista de intereses encontrados para el usuario</returns>
        public async Task<GetInteresesByOIDResult> GetInteresesByMicrosoftOIDAsync(string twinId, string microsoftOID)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetInteresesByOIDResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Intereses = new List<CasaInteresRequest>()
                };
            }

            if (string.IsNullOrEmpty(microsoftOID))
            {
                return new GetInteresesByOIDResult
                {
                    Success = false,
                    ErrorMessage = "Microsoft OID cannot be null or empty",
                    Intereses = new List<CasaInteresRequest>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId AND c.microsoftOID = @microsoftOID ORDER BY c.fechaCreado DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@microsoftOID", microsoftOID);

                using FeedIterator<CasaInteresRequest> feed = container.GetItemQueryIterator<CasaInteresRequest>(queryDefinition);

                var intereses = new List<CasaInteresRequest>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<CasaInteresRequest> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var interes in response)
                    {
                        intereses.Add(interes);
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} intereses for TwinID: {TwinId} and Microsoft OID: {MicrosoftOID}. RU consumed: {RU:F2}", 
                    count, twinId, microsoftOID, totalRU);

                return new GetInteresesByOIDResult
                {
                    Success = true,
                    Intereses = intereses,
                    InteresCount = count,
                    MicrosoftOID = microsoftOID,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetInteresesByOIDResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Intereses = new List<CasaInteresRequest>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving intereses by Microsoft OID");
                return new GetInteresesByOIDResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Intereses = new List<CasaInteresRequest>()
                };
            }
        }

        /// <summary>
        /// Actualiza un interés existente en Cosmos DB
        /// </summary>
        /// <param name="interesId">ID del interés a actualizar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <param name="interesActualizado">Datos actualizados del interés</param>
        /// <returns>Resultado de la operación de actualización</returns>
        public async Task<UpdateInteresResult> UpdateInteresAsync(string interesId, string twinId, CasaInteresRequest interesActualizado)
        {
            if (string.IsNullOrEmpty(interesId))
            {
                return new UpdateInteresResult
                {
                    Success = false,
                    ErrorMessage = "Interes ID cannot be null or empty",
                    InteresActualizado = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new UpdateInteresResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    InteresActualizado = null
                };
            }

            if (interesActualizado == null)
            {
                return new UpdateInteresResult
                {
                    Success = false,
                    ErrorMessage = "Updated interes data cannot be null",
                    InteresActualizado = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Verificar que el interés existe
                string queryCheck = "SELECT * FROM c WHERE c.id = @interesId AND c.TwinID = @twinId";
                var queryDefinition = new QueryDefinition(queryCheck)
                    .WithParameter("@interesId", interesId)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<CasaInteresRequest> feed = container.GetItemQueryIterator<CasaInteresRequest>(queryDefinition);

                CasaInteresRequest interesExistente = null;
                DateTime? fechaCreado = null;

                while (feed.HasMoreResults)
                {
                    FeedResponse<CasaInteresRequest> response = await feed.ReadNextAsync();
                    if (response.Count > 0)
                    {
                        interesExistente = response.FirstOrDefault();
                        fechaCreado = interesExistente.FechaCreado;
                        break;
                    }
                }

                if (interesExistente == null)
                {
                    _logger.LogWarning("⚠️ Interés not found for update: {InteresId}", interesId);
                    return new UpdateInteresResult
                    {
                        Success = false,
                        ErrorMessage = $"Interés with ID '{interesId}' not found",
                        InteresActualizado = null
                    };
                }

                // Serializar y adicionar metadatos de actualización
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(interesActualizado));
                
                documentData["id"] = interesId;
                documentData["TwinID"] = twinId;
                documentData["type"] = "interes_propiedad";
                documentData["fechaCreado"] = fechaCreado ?? DateTime.UtcNow; // Mantener fecha original
                documentData["fechaActualizado"] = DateTime.UtcNow;

                var updateResponse = await container.UpsertItemAsync(documentData, new PartitionKey(twinId));

                _logger.LogInformation("✅ Interés updated successfully. Document ID: {DocumentId}, URL: {URL}", 
                    interesId, interesActualizado.UrlPropiedad);

                return new UpdateInteresResult
                {
                    Success = true,
                    InteresActualizado = interesActualizado,
                    RUConsumed = updateResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new UpdateInteresResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    InteresActualizado = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating interés");
                return new UpdateInteresResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    InteresActualizado = null
                };
            }
        }

        /// <summary>
        /// Elimina un interés de Cosmos DB
        /// </summary>
        /// <param name="interesId">ID del interés a eliminar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Resultado de la operación de eliminación</returns>
        public async Task<DeleteInteresResult> DeleteInteresAsync(string interesId, string twinId)
        {
            if (string.IsNullOrEmpty(interesId))
            {
                return new DeleteInteresResult
                {
                    Success = false,
                    ErrorMessage = "Interes ID cannot be null or empty"
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new DeleteInteresResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                var deleteResponse = await container.DeleteItemAsync<CasaInteresRequest>(interesId, new PartitionKey(twinId));

                _logger.LogInformation("✅ Interés deleted successfully. Document ID: {DocumentId}", interesId);

                return new DeleteInteresResult
                {
                    Success = true,
                    InteresId = interesId,
                    RUConsumed = deleteResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow,
                    Message = "Interés eliminado exitosamente"
                };
            }
            catch (CosmosException cosmosEx) when (cosmosEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                string errorMessage = $"Interés with ID '{interesId}' not found";
                _logger.LogWarning("⚠️ {ErrorMessage}", errorMessage);

                return new DeleteInteresResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    InteresId = interesId
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new DeleteInteresResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    InteresId = interesId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting interés");
                return new DeleteInteresResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    InteresId = interesId
                };
            }
        }

        public void Dispose()
        {
            _cosmosClient?.Dispose();
        }
    }

    #region Data Models

    /// <summary>
    /// DTO para interés en propiedad
    /// </summary>
    public class CasaInteresRequest
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("CustomerID")]
        public string CustomerID { get; set; } = string.Empty;

        [JsonProperty("microsoftOID")]
        public string MicrosoftOID { get; set; } = string.Empty;

        [JsonProperty("urlPropiedad")]
        public string UrlPropiedad { get; set; } = string.Empty;

        [JsonProperty("notas")]
        public string Notas { get; set; } = string.Empty;

        [JsonProperty("queTeGusto")]
        public string QueTeGusto { get; set; } = string.Empty;

        [JsonProperty("agendarVisita")]
        public bool AgendarVisita { get; set; } = false;

        [JsonProperty("fechaVisita")]
        public DateTime? FechaVisita { get; set; }

        [JsonProperty("estadoInteres")]
        public string EstadoInteres { get; set; } = "nuevo"; // nuevo, contactado, visitado, interesado, descartado

        [JsonProperty("fuente")]
        public string Fuente { get; set; } = string.Empty; // inmuebles24, vivanuncios, etc.


        [JsonProperty("prosdpectado")]
        public bool Prospectado { get; set; }

        [JsonProperty("dateProspectado")]
        public DateTime DateProspectado { get; set; } = DateTime.UtcNow;

        [JsonProperty("fechaCreado")]
        public DateTime FechaCreado { get; set; } = DateTime.UtcNow;

        [JsonProperty("fechaActualizado")]
        public DateTime FechaActualizado { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Resultado de guardar un interés
    /// </summary>
    public class SaveInteresResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string DocumentId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener múltiples intereses
    /// </summary>
    public class GetInteresesResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<CasaInteresRequest> Intereses { get; set; } = new();
        public int InteresCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener intereses por Microsoft OID
    /// </summary>
    public class GetInteresesByOIDResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<CasaInteresRequest> Intereses { get; set; } = new();
        public int InteresCount { get; set; }
        public string MicrosoftOID { get; set; } = "";
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de actualizar un interés
    /// </summary>
    public class UpdateInteresResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public CasaInteresRequest InteresActualizado { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de eliminar un interés
    /// </summary>
    public class DeleteInteresResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Message { get; set; } = "";
        public string InteresId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

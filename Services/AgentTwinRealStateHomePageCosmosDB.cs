using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TwinAgentsNetwork.Agents;

namespace TwinAgentsNetwork.Services
{
    /// <summary>
    /// Servicio para gestionar páginas home de agentes inmobiliarios en Azure Cosmos DB
    /// Database: twinmicasadb
    /// Container: realstatehomepage
    /// Partition Key: /TwinID
    /// </summary>
    public class AgentTwinRealStateHomePageCosmosDB
    {
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "realstatehomepage";
        private CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AgentTwinRealStateHomePageCosmosDB> _logger;

        public AgentTwinRealStateHomePageCosmosDB(ILogger<AgentTwinRealStateHomePageCosmosDB> logger, IConfiguration configuration = null)
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
                    
                    _logger.LogInformation("✅ Successfully connected to RealState HomePage Cosmos DB");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error connecting to Cosmos DB");
                    throw;
                }
            }
        }

        /// <summary>
        /// Guarda o actualiza una página home en Cosmos DB
        /// </summary>
        /// <param name="homePage">Datos de la página home a guardar</param>
        /// <returns>Resultado de la operación con ID del documento creado/actualizado</returns>
        public async Task<SaveRealStateHomePageResult> SaveRealStateHomePageAsync(RealStateHomePageResult homePage)
        {
            if (homePage == null)
            {
                return new SaveRealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = "HomePage data cannot be null",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(homePage.TwinID))
            {
                return new SaveRealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(homePage.MicrosoftOIDRSA))
            {
                return new SaveRealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = "MicrosoftOIDRSA cannot be null or empty",
                    DocumentId = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Generar ID único si no se proporciona
                string documentId = string.IsNullOrEmpty(homePage.id) ? Guid.NewGuid().ToString() : homePage.id;

                // Serializar y adicionar metadatos
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(homePage));
                
                documentData["id"] = documentId;
                documentData["TwinID"] = homePage.TwinID;
                documentData["MicrosoftOIDRSA"] = homePage.MicrosoftOIDRSA;
                documentData["type"] = "realstate_homepage";
                documentData["fechaGuardado"] = DateTime.UtcNow;
                
                if (string.IsNullOrEmpty(homePage.id))
                {
                    documentData["fechaCreacion"] = DateTime.UtcNow;
                }
                else
                {
                    documentData["fechaActualizacion"] = DateTime.UtcNow;
                }

                // Usar Upsert para crear o actualizar
                var response = await container.UpsertItemAsync(documentData, new PartitionKey(homePage.TwinID));

                _logger.LogInformation("✅ RealState HomePage saved successfully. Document ID: {DocumentId}, TwinID: {TwinID}", 
                    documentId, homePage.TwinID);

                return new SaveRealStateHomePageResult
                {
                    Success = true,
                    DocumentId = documentId,
                    RUConsumed = response.RequestCharge,
                    Timestamp = DateTime.UtcNow,
                    Message = string.IsNullOrEmpty(homePage.id) 
                        ? "HomePage creada exitosamente" 
                        : "HomePage actualizada exitosamente"
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new SaveRealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    DocumentId = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving RealState HomePage");
                return new SaveRealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = null
                };
            }
        }

        /// <summary>
        /// Obtiene una página home por TwinID y MicrosoftOIDRSA
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <param name="microsoftOIDRSA">Microsoft Object ID del Real State Agent</param>
        /// <returns>Datos de la página home encontrada</returns>
        public async Task<GetRealStateHomePageResult> GetRealStateHomePageAsync(string twinId, string microsoftOIDRSA)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetRealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    HomePage = null
                };
            }

            if (string.IsNullOrEmpty(microsoftOIDRSA))
            {
                return new GetRealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = "MicrosoftOIDRSA cannot be null or empty",
                    HomePage = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId AND c.MicrosoftOIDRSA = @microsoftOIDRSA ORDER BY c.fechaGuardado DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@microsoftOIDRSA", microsoftOIDRSA);

                using FeedIterator<RealStateHomePageResult> feed = container.GetItemQueryIterator<RealStateHomePageResult>(queryDefinition);

                RealStateHomePageResult homePage = null;
                double totalRU = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<RealStateHomePageResult> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    if (response.Count > 0)
                    {
                        homePage = response.FirstOrDefault();
                        break; // Get the most recent one
                    }
                }

                if (homePage == null)
                {
                    _logger.LogWarning("⚠️ RealState HomePage not found for TwinID: {TwinId}, MicrosoftOIDRSA: {MicrosoftOIDRSA}", 
                        twinId, microsoftOIDRSA);
                    return new GetRealStateHomePageResult
                    {
                        Success = false,
                        ErrorMessage = $"HomePage not found for TwinID '{twinId}' and MicrosoftOIDRSA '{microsoftOIDRSA}'",
                        HomePage = null
                    };
                }

                _logger.LogInformation("✅ Retrieved RealState HomePage: {DocumentId}. RU consumed: {RU:F2}", 
                    homePage.id, totalRU);

                return new GetRealStateHomePageResult
                {
                    Success = true,
                    HomePage = homePage,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetRealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    HomePage = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving RealState HomePage");
                return new GetRealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    HomePage = null
                };
            }
        }

        /// <summary>
        /// Obtiene todas las páginas home para un TwinID específico
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <returns>Lista de páginas home encontradas</returns>
        public async Task<GetRealStateHomePagesResult> GetRealStateHomePagesByTwinIdAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetRealStateHomePagesResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    HomePages = new List<RealStateHomePageResult>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.fechaGuardado DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<RealStateHomePageResult> feed = container.GetItemQueryIterator<RealStateHomePageResult>(queryDefinition);

                var homePages = new List<RealStateHomePageResult>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<RealStateHomePageResult> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var homePage in response)
                    {
                        homePages.Add(homePage);
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} RealState HomePages for TwinID: {TwinId}. RU consumed: {RU:F2}", 
                    count, twinId, totalRU);

                return new GetRealStateHomePagesResult
                {
                    Success = true,
                    HomePages = homePages,
                    HomePageCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetRealStateHomePagesResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    HomePages = new List<RealStateHomePageResult>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving RealState HomePages");
                return new GetRealStateHomePagesResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    HomePages = new List<RealStateHomePageResult>()
                };
            }
        }

        /// <summary>
        /// Obtiene una página home por ID de documento
        /// </summary>
        /// <param name="homePageId">ID del documento</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Datos de la página home encontrada</returns>
        public async Task<GetRealStateHomePageByIdResult> GetRealStateHomePageByIdAsync(string homePageId, string twinId)
        {
            if (string.IsNullOrEmpty(homePageId))
            {
                return new GetRealStateHomePageByIdResult
                {
                    Success = false,
                    ErrorMessage = "HomePage ID cannot be null or empty",
                    HomePage = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new GetRealStateHomePageByIdResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    HomePage = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                var response = await container.ReadItemAsync<RealStateHomePageResult>(homePageId, new PartitionKey(twinId));

                _logger.LogInformation("✅ Retrieved RealState HomePage by ID: {HomePageId}. RU consumed: {RU:F2}", 
                    homePageId, response.RequestCharge);

                return new GetRealStateHomePageByIdResult
                {
                    Success = true,
                    HomePage = response.Resource,
                    RUConsumed = response.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx) when (cosmosEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                string errorMessage = $"HomePage with ID '{homePageId}' not found";
                _logger.LogWarning("⚠️ {ErrorMessage}", errorMessage);

                return new GetRealStateHomePageByIdResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    HomePage = null
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetRealStateHomePageByIdResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    HomePage = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving RealState HomePage by ID");
                return new GetRealStateHomePageByIdResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    HomePage = null
                };
            }
        }

        /// <summary>
        /// Elimina una página home de Cosmos DB
        /// </summary>
        /// <param name="homePageId">ID del documento a eliminar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Resultado de la operación de eliminación</returns>
        public async Task<DeleteRealStateHomePageResult> DeleteRealStateHomePageAsync(string homePageId, string twinId)
        {
            if (string.IsNullOrEmpty(homePageId))
            {
                return new DeleteRealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = "HomePage ID cannot be null or empty"
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new DeleteRealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                var deleteResponse = await container.DeleteItemAsync<RealStateHomePageResult>(homePageId, new PartitionKey(twinId));

                _logger.LogInformation("✅ RealState HomePage deleted successfully. Document ID: {DocumentId}", homePageId);

                return new DeleteRealStateHomePageResult
                {
                    Success = true,
                    HomePageId = homePageId,
                    RUConsumed = deleteResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow,
                    Message = "HomePage eliminada exitosamente"
                };
            }
            catch (CosmosException cosmosEx) when (cosmosEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                string errorMessage = $"HomePage with ID '{homePageId}' not found";
                _logger.LogWarning("⚠️ {ErrorMessage}", errorMessage);

                return new DeleteRealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    HomePageId = homePageId
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new DeleteRealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    HomePageId = homePageId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting RealState HomePage");
                return new DeleteRealStateHomePageResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    HomePageId = homePageId
                };
            }
        }

        public void Dispose()
        {
            _cosmosClient?.Dispose();
        }
    }

    #region Result Models

    /// <summary>
    /// Resultado de guardar una página home
    /// </summary>
    public class SaveRealStateHomePageResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Message { get; set; } = "";
        public string DocumentId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener una página home
    /// </summary>
    public class GetRealStateHomePageResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public RealStateHomePageResult HomePage { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener múltiples páginas home
    /// </summary>
    public class GetRealStateHomePagesResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<RealStateHomePageResult> HomePages { get; set; } = new();
        public int HomePageCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener una página home por ID
    /// </summary>
    public class GetRealStateHomePageByIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public RealStateHomePageResult HomePage { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de eliminar una página home
    /// </summary>
    public class DeleteRealStateHomePageResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Message { get; set; } = "";
        public string HomePageId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

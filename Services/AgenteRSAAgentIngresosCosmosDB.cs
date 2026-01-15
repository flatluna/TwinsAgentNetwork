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
    /// Servicio para gestionar ingresos de agentes inmobiliarios (Real Estate Agents) en Azure Cosmos DB
    /// Database: twinmicasadb
    /// Container: twinagentecasasingresos
    /// Partition Key: /TwinID
    /// </summary>
    public class AgenteRSAAgentIngresosCosmosDB
    {
        private readonly ILogger<AgenteRSAAgentIngresosCosmosDB> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "twinagentecasasingresos";
        private CosmosClient _cosmosClient;

        public AgenteRSAAgentIngresosCosmosDB(ILogger<AgenteRSAAgentIngresosCosmosDB> logger, IConfiguration configuration = null)
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
                    
                    _logger.LogInformation("✅ Successfully connected to Agente Ingresos Cosmos DB");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error connecting to Cosmos DB");
                    throw;
                }
            }
        }

        /// <summary>
        /// Guarda un nuevo ingreso de agente en Cosmos DB
        /// </summary>
        /// <param name="ingreso">Datos del ingreso del agente</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Resultado de la operación con ID del documento creado</returns>
        public async Task<SaveIngresoAgenteResult> SaveIngresoAsync(IngresoAgente ingreso, string twinId)
        {
            if (ingreso == null)
            {
                return new SaveIngresoAgenteResult
                {
                    Success = false,
                    ErrorMessage = "Ingreso data cannot be null",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new SaveIngresoAgenteResult
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

                // Generar ID único para el documento
                ingreso.Id = Guid.NewGuid().ToString();
                ingreso.TwinID = twinId;
                ingreso.FechaRegistro = DateTime.UtcNow;

                // Serializar y adicionar metadatos
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(ingreso));
                documentData["type"] = "ingreso_agente";
                documentData["fechaGuardado"] = DateTime.UtcNow;

                var response = await container.CreateItemAsync(documentData, new PartitionKey(twinId));

                _logger.LogInformation("✅ Ingreso agente saved successfully. Document ID: {DocumentId}, Tipo: {TipoIngreso}, Total Neto: ${TotalNeto}", 
                    ingreso.Id, ingreso.TipoIngreso, ingreso.TotalNeto);

                return new SaveIngresoAgenteResult
                {
                    Success = true,
                    DocumentId = ingreso.Id,
                    TwinId = twinId,
                    RUConsumed = response.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new SaveIngresoAgenteResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    DocumentId = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving ingreso agente");
                return new SaveIngresoAgenteResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = null
                };
            }
        }

        /// <summary>
        /// Obtiene todos los ingresos para un TwinID específico
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <returns>Lista de ingresos encontrados</returns>
        public async Task<GetIngresosAgenteResult> GetIngresosByTwinIdAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetIngresosAgenteResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Ingresos = new List<IngresoAgente>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.Fecha DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<IngresoAgente> feed = container.GetItemQueryIterator<IngresoAgente>(queryDefinition);

                var ingresos = new List<IngresoAgente>();
                double totalRU = 0;
                int count = 0;
                decimal totalIngresos = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<IngresoAgente> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var ingreso in response)
                    {
                        ingresos.Add(ingreso);
                        totalIngresos += ingreso.TotalNeto;
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} ingresos for TwinID: {TwinId}. Total: ${Total}. RU consumed: {RU:F2}", 
                    count, twinId, totalIngresos, totalRU);

                return new GetIngresosAgenteResult
                {
                    Success = true,
                    TwinId = twinId,
                    Ingresos = ingresos,
                    IngresoCount = count,
                    TotalIngresos = totalIngresos,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetIngresosAgenteResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Ingresos = new List<IngresoAgente>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving ingresos");
                return new GetIngresosAgenteResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Ingresos = new List<IngresoAgente>()
                };
            }
        }

        /// <summary>
        /// Obtiene todos los ingresos para un TwinID y MicrosoftOID específicos
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <param name="microsoftOID">ID de Microsoft del agente</param>
        /// <returns>Lista de ingresos encontrados</returns>
        public async Task<GetIngresosAgenteResult> GetIngresosByTwinIdAndMicrosoftOIDAsync(string twinId, string microsoftOID)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetIngresosAgenteResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Ingresos = new List<IngresoAgente>()
                };
            }

            if (string.IsNullOrEmpty(microsoftOID))
            {
                return new GetIngresosAgenteResult
                {
                    Success = false,
                    ErrorMessage = "MicrosoftOID cannot be null or empty",
                    Ingresos = new List<IngresoAgente>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId AND c.MicrosoftOID = @microsoftOID ORDER BY c.Fecha DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@microsoftOID", microsoftOID);

                using FeedIterator<IngresoAgente> feed = container.GetItemQueryIterator<IngresoAgente>(queryDefinition);

                var ingresos = new List<IngresoAgente>();
                double totalRU = 0;
                int count = 0;
                decimal totalIngresos = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<IngresoAgente> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var ingreso in response)
                    {
                        ingresos.Add(ingreso);
                        totalIngresos += ingreso.TotalNeto;
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} ingresos for TwinID: {TwinId}, MicrosoftOID: {MicrosoftOID}. Total: ${Total}. RU consumed: {RU:F2}", 
                    count, twinId, microsoftOID, totalIngresos, totalRU);

                return new GetIngresosAgenteResult
                {
                    Success = true,
                    TwinId = twinId,
                    MicrosoftOID = microsoftOID,
                    Ingresos = ingresos,
                    IngresoCount = count,
                    TotalIngresos = totalIngresos,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetIngresosAgenteResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Ingresos = new List<IngresoAgente>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving ingresos");
                return new GetIngresosAgenteResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Ingresos = new List<IngresoAgente>()
                };
            }
        }

        /// <summary>
        /// Obtiene un ingreso específico por ID
        /// </summary>
        /// <param name="ingresoId">ID del ingreso</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Datos del ingreso encontrado</returns>
        public async Task<GetIngresoAgenteByIdResult> GetIngresoByIdAsync(string ingresoId, string twinId)
        {
            if (string.IsNullOrEmpty(ingresoId))
            {
                return new GetIngresoAgenteByIdResult
                {
                    Success = false,
                    ErrorMessage = "Ingreso ID cannot be null or empty",
                    Ingreso = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new GetIngresoAgenteByIdResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Ingreso = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.id = @ingresoId AND c.TwinID = @twinId";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@ingresoId", ingresoId)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<IngresoAgente> feed = container.GetItemQueryIterator<IngresoAgente>(queryDefinition);

                IngresoAgente ingreso = null;
                double totalRU = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<IngresoAgente> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    if (response.Count > 0)
                    {
                        ingreso = response.FirstOrDefault();
                        break;
                    }
                }

                if (ingreso == null)
                {
                    _logger.LogWarning("⚠️ Ingreso not found: {IngresoId}", ingresoId);
                    return new GetIngresoAgenteByIdResult
                    {
                        Success = false,
                        ErrorMessage = $"Ingreso with ID '{ingresoId}' not found",
                        Ingreso = null
                    };
                }

                _logger.LogInformation("✅ Retrieved ingreso: {IngresoId}, Tipo: {TipoIngreso}", ingresoId, ingreso.TipoIngreso);

                return new GetIngresoAgenteByIdResult
                {
                    Success = true,
                    Ingreso = ingreso,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetIngresoAgenteByIdResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Ingreso = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving ingreso");
                return new GetIngresoAgenteByIdResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Ingreso = null
                };
            }
        }

        /// <summary>
        /// Actualiza un ingreso existente en Cosmos DB
        /// </summary>
        /// <param name="ingresoId">ID del ingreso a actualizar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <param name="ingresoActualizado">Datos actualizados del ingreso</param>
        /// <returns>Resultado de la operación de actualización</returns>
        public async Task<UpdateIngresoAgenteResult> UpdateIngresoAsync(string ingresoId, string twinId, IngresoAgente ingresoActualizado)
        {
            if (string.IsNullOrEmpty(ingresoId))
            {
                return new UpdateIngresoAgenteResult
                {
                    Success = false,
                    ErrorMessage = "Ingreso ID cannot be null or empty",
                    IngresoActualizado = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new UpdateIngresoAgenteResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    IngresoActualizado = null
                };
            }

            if (ingresoActualizado == null)
            {
                return new UpdateIngresoAgenteResult
                {
                    Success = false,
                    ErrorMessage = "Updated ingreso data cannot be null",
                    IngresoActualizado = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Primero verificar que el ingreso existe
                string queryCheck = "SELECT * FROM c WHERE c.id = @ingresoId AND c.TwinID = @twinId";
                var queryDefinition = new QueryDefinition(queryCheck)
                    .WithParameter("@ingresoId", ingresoId)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<IngresoAgente> feed = container.GetItemQueryIterator<IngresoAgente>(queryDefinition);

                IngresoAgente ingresoExistente = null;
                while (feed.HasMoreResults)
                {
                    FeedResponse<IngresoAgente> response = await feed.ReadNextAsync();
                    if (response.Count > 0)
                    {
                        ingresoExistente = response.FirstOrDefault();
                        break;
                    }
                }

                if (ingresoExistente == null)
                {
                    _logger.LogWarning("⚠️ Ingreso not found for update: {IngresoId}", ingresoId);
                    return new UpdateIngresoAgenteResult
                    {
                        Success = false,
                        ErrorMessage = $"Ingreso with ID '{ingresoId}' not found",
                        IngresoActualizado = null
                    };
                }

                // Mantener valores inmutables
                ingresoActualizado.Id = ingresoId;
                ingresoActualizado.TwinID = twinId;
                ingresoActualizado.MicrosoftOID = ingresoExistente.MicrosoftOID; // Mantener el MicrosoftOID original
                ingresoActualizado.FechaRegistro = ingresoExistente.FechaRegistro; // Mantener fecha de registro original

                // Serializar y adicionar metadatos de actualización
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(ingresoActualizado));
                documentData["type"] = "ingreso_agente";
                documentData["fechaActualizacion"] = DateTime.UtcNow;

                var updateResponse = await container.UpsertItemAsync(documentData, new PartitionKey(twinId));

                _logger.LogInformation("✅ Ingreso updated successfully. Document ID: {DocumentId}, Tipo: {TipoIngreso}, Total Neto: ${TotalNeto}", 
                    ingresoId, ingresoActualizado.TipoIngreso, ingresoActualizado.TotalNeto);

                return new UpdateIngresoAgenteResult
                {
                    Success = true,
                    IngresoActualizado = ingresoActualizado,
                    RUConsumed = updateResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new UpdateIngresoAgenteResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    IngresoActualizado = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating ingreso");
                return new UpdateIngresoAgenteResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    IngresoActualizado = null
                };
            }
        }

        /// <summary>
        /// Elimina un ingreso de Cosmos DB
        /// </summary>
        /// <param name="ingresoId">ID del ingreso a eliminar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Resultado de la operación de eliminación</returns>
        public async Task<DeleteIngresoAgenteResult> DeleteIngresoAsync(string ingresoId, string twinId)
        {
            if (string.IsNullOrEmpty(ingresoId))
            {
                return new DeleteIngresoAgenteResult
                {
                    Success = false,
                    ErrorMessage = "Ingreso ID cannot be null or empty"
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new DeleteIngresoAgenteResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Primero obtener el ingreso para registrar información antes de eliminar
                string queryCheck = "SELECT * FROM c WHERE c.id = @ingresoId AND c.TwinID = @twinId";
                var queryDefinition = new QueryDefinition(queryCheck)
                    .WithParameter("@ingresoId", ingresoId)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<IngresoAgente> feed = container.GetItemQueryIterator<IngresoAgente>(queryDefinition);

                IngresoAgente ingresoExistente = null;
                while (feed.HasMoreResults)
                {
                    FeedResponse<IngresoAgente> response = await feed.ReadNextAsync();
                    if (response.Count > 0)
                    {
                        ingresoExistente = response.FirstOrDefault();
                        break;
                    }
                }

                if (ingresoExistente == null)
                {
                    string errorMessage = $"Ingreso with ID '{ingresoId}' not found";
                    _logger.LogWarning("⚠️ {ErrorMessage}", errorMessage);
                    
                    return new DeleteIngresoAgenteResult
                    {
                        Success = false,
                        ErrorMessage = errorMessage,
                        IngresoId = ingresoId
                    };
                }

                // Eliminar el ingreso
                var deleteResponse = await container.DeleteItemAsync<IngresoAgente>(ingresoId, new PartitionKey(twinId));

                _logger.LogInformation("✅ Ingreso deleted successfully. Document ID: {DocumentId}, TwinID: {TwinId}, Total Neto eliminado: ${TotalNeto}", 
                    ingresoId, twinId, ingresoExistente.TotalNeto);

                return new DeleteIngresoAgenteResult
                {
                    Success = true,
                    IngresoId = ingresoId,
                    RUConsumed = deleteResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow,
                    Message = $"Ingreso '{ingresoExistente.TipoIngreso}' eliminado exitosamente. Total Neto: ${ingresoExistente.TotalNeto:N2}"
                };
            }
            catch (CosmosException cosmosEx) when (cosmosEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                string errorMessage = $"Ingreso with ID '{ingresoId}' not found";
                _logger.LogWarning("⚠️ {ErrorMessage}", errorMessage);

                return new DeleteIngresoAgenteResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    IngresoId = ingresoId
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new DeleteIngresoAgenteResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    IngresoId = ingresoId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting ingreso");
                return new DeleteIngresoAgenteResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    IngresoId = ingresoId
                };
            }
        }

        /// <summary>
        /// Libera recursos del cliente de Cosmos DB
        /// </summary>
        public void Dispose()
        {
            _cosmosClient?.Dispose();
        }
    }

    #region Data Models

    /// <summary>
    /// Modelo para representar un ingreso de agente inmobiliario
    /// </summary>
    public class IngresoAgente
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("TwinID")]
        public string TwinID { get; set; } = string.Empty;

        [JsonProperty("microsoftOID")]
        public string MicrosoftOID { get; set; } = string.Empty;

        [JsonProperty("tipoIngreso")]
        public string TipoIngreso { get; set; } = string.Empty;

        [JsonProperty("descripcion")]
        public string Descripcion { get; set; } = string.Empty;

        [JsonProperty("propiedad")]
        public string Propiedad { get; set; } = string.Empty;

        [JsonProperty("cliente")]
        public string Cliente { get; set; } = string.Empty;

        [JsonProperty("montoBase")]
        public decimal MontoBase { get; set; }

        [JsonProperty("porcentajeComision")]
        public decimal PorcentajeComision { get; set; }

        [JsonProperty("montoComision")]
        public decimal MontoComision { get; set; }

        [JsonProperty("bonos")]
        public decimal Bonos { get; set; }

        [JsonProperty("deducciones")]
        public decimal Deducciones { get; set; }

        [JsonProperty("totalNeto")]
        public decimal TotalNeto { get; set; }

        [JsonProperty("fecha")]
        public DateTime Fecha { get; set; }

        [JsonProperty("notas")]
        public string Notas { get; set; } = string.Empty;

        [JsonProperty("fechaRegistro")]
        public DateTime FechaRegistro { get; set; }
    }

    /// <summary>
    /// Resultado de guardar un ingreso de agente
    /// </summary>
    public class SaveIngresoAgenteResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string DocumentId { get; set; }
        public string TwinId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener múltiples ingresos de agente
    /// </summary>
    public class GetIngresosAgenteResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string TwinId { get; set; } = "";
        public string MicrosoftOID { get; set; } = "";
        public List<IngresoAgente> Ingresos { get; set; } = new();
        public int IngresoCount { get; set; }
        public decimal TotalIngresos { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener un ingreso de agente por ID
    /// </summary>
    public class GetIngresoAgenteByIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public IngresoAgente Ingreso { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de actualizar un ingreso de agente
    /// </summary>
    public class UpdateIngresoAgenteResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public IngresoAgente IngresoActualizado { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de eliminar un ingreso de agente
    /// </summary>
    public class DeleteIngresoAgenteResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Message { get; set; } = "";
        public string IngresoId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

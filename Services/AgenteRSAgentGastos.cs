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
    /// Servicio para gestionar gastos de agentes inmobiliarios (Real Estate Agents) en Azure Cosmos DB
    /// Database: twinmicasadb
    /// Container: twinagentecasasgastos
    /// Partition Key: /TwinID
    /// </summary>
    public class AgenteRSAgentGastos
    {
        private readonly ILogger<AgenteRSAgentGastos> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "twinagentecasasgastos";
        private CosmosClient _cosmosClient;

        public AgenteRSAgentGastos(ILogger<AgenteRSAgentGastos> logger, IConfiguration configuration = null)
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
                    
                    _logger.LogInformation("✅ Successfully connected to Agente Gastos Cosmos DB");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error connecting to Cosmos DB");
                    throw;
                }
            }
        }

        /// <summary>
        /// Guarda un nuevo gasto de agente en Cosmos DB
        /// </summary>
        /// <param name="gasto">Datos del gasto del agente</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Resultado de la operación con ID del documento creado</returns>
        public async Task<SaveGastoAgenteResult> SaveGastoAsync(GastoAgente gasto, string twinId)
        {
            if (gasto == null)
            {
                return new SaveGastoAgenteResult
                {
                    Success = false,
                    ErrorMessage = "Gasto data cannot be null",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new SaveGastoAgenteResult
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
                gasto.Id = Guid.NewGuid().ToString();
                gasto.TwinID = twinId;
                gasto.FechaRegistro = DateTime.UtcNow;

                // Calcular total si no está establecido
                if (gasto.Total == 0)
                {
                    decimal iva = gasto.Cantidad * gasto.CostoUnitario * gasto.IVA / 100;
                    decimal otrosImpuestos = gasto.Cantidad * gasto.CostoUnitario * gasto.OtrosImpuestos / 100;
                    gasto.Total = (gasto.Cantidad * gasto.CostoUnitario) + iva + otrosImpuestos;
                }

                // Serializar y adicionar metadatos
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(gasto));
                documentData["type"] = "gasto_agente";
                documentData["fechaGuardado"] = DateTime.UtcNow;

                var response = await container.CreateItemAsync(documentData, new PartitionKey(twinId));

                _logger.LogInformation("✅ Gasto agente saved successfully. Document ID: {DocumentId}, Tipo: {TipoGasto}, Total: ${Total}", 
                    gasto.Id, gasto.TipoGasto, gasto.Total);

                return new SaveGastoAgenteResult
                {
                    Success = true,
                    DocumentId = gasto.Id,
                    TwinId = twinId,
                    RUConsumed = response.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new SaveGastoAgenteResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    DocumentId = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving gasto agente");
                return new SaveGastoAgenteResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = null
                };
            }
        }

        /// <summary>
        /// Obtiene todos los gastos para un TwinID específico
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <returns>Lista de gastos encontrados</returns>
        public async Task<GetGastosAgenteResult> GetGastosByTwinIdAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetGastosAgenteResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Gastos = new List<GastoAgente>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.Fecha DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<GastoAgente> feed = container.GetItemQueryIterator<GastoAgente>(queryDefinition);

                var gastos = new List<GastoAgente>();
                double totalRU = 0;
                int count = 0;
                decimal totalGastos = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<GastoAgente> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var gasto in response)
                    {
                        gastos.Add(gasto);
                        totalGastos += gasto.Total;
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} gastos for TwinID: {TwinId}. Total: ${Total}. RU consumed: {RU:F2}", 
                    count, twinId, totalGastos, totalRU);

                return new GetGastosAgenteResult
                {
                    Success = true,
                    TwinId = twinId,
                    Gastos = gastos,
                    GastoCount = count,
                    TotalGastos = totalGastos,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetGastosAgenteResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Gastos = new List<GastoAgente>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving gastos");
                return new GetGastosAgenteResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Gastos = new List<GastoAgente>()
                };
            }
        }

        /// <summary>
        /// Obtiene todos los gastos para un TwinID y MicrosoftOID específicos
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <param name="microsoftOID">ID de Microsoft del agente</param>
        /// <returns>Lista de gastos encontrados</returns>
        public async Task<GetGastosAgenteResult> GetGastosByTwinIdAndMicrosoftOIDAsync(string twinId, string microsoftOID)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetGastosAgenteResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Gastos = new List<GastoAgente>()
                };
            }

            if (string.IsNullOrEmpty(microsoftOID))
            {
                return new GetGastosAgenteResult
                {
                    Success = false,
                    ErrorMessage = "MicrosoftOID cannot be null or empty",
                    Gastos = new List<GastoAgente>()
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

                using FeedIterator<GastoAgente> feed = container.GetItemQueryIterator<GastoAgente>(queryDefinition);

                var gastos = new List<GastoAgente>();
                double totalRU = 0;
                int count = 0;
                decimal totalGastos = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<GastoAgente> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var gasto in response)
                    {
                        gastos.Add(gasto);
                        totalGastos += gasto.Total;
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} gastos for TwinID: {TwinId}, MicrosoftOID: {MicrosoftOID}. Total: ${Total}. RU consumed: {RU:F2}", 
                    count, twinId, microsoftOID, totalGastos, totalRU);

                return new GetGastosAgenteResult
                {
                    Success = true,
                    TwinId = twinId,
                    MicrosoftOID = microsoftOID,
                    Gastos = gastos,
                    GastoCount = count,
                    TotalGastos = totalGastos,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetGastosAgenteResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Gastos = new List<GastoAgente>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving gastos");
                return new GetGastosAgenteResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Gastos = new List<GastoAgente>()
                };
            }
        }

        /// <summary>
        /// Obtiene un gasto específico por ID
        /// </summary>
        /// <param name="gastoId">ID del gasto</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Datos del gasto encontrado</returns>
        public async Task<GetGastoAgenteByIdResult> GetGastoByIdAsync(string gastoId, string twinId)
        {
            if (string.IsNullOrEmpty(gastoId))
            {
                return new GetGastoAgenteByIdResult
                {
                    Success = false,
                    ErrorMessage = "Gasto ID cannot be null or empty",
                    Gasto = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new GetGastoAgenteByIdResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Gasto = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.id = @gastoId AND c.TwinID = @twinId";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@gastoId", gastoId)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<GastoAgente> feed = container.GetItemQueryIterator<GastoAgente>(queryDefinition);

                GastoAgente gasto = null;
                double totalRU = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<GastoAgente> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    if (response.Count > 0)
                    {
                        gasto = response.FirstOrDefault();
                        break;
                    }
                }

                if (gasto == null)
                {
                    _logger.LogWarning("⚠️ Gasto not found: {GastoId}", gastoId);
                    return new GetGastoAgenteByIdResult
                    {
                        Success = false,
                        ErrorMessage = $"Gasto with ID '{gastoId}' not found",
                        Gasto = null
                    };
                }

                _logger.LogInformation("✅ Retrieved gasto: {GastoId}, Tipo: {TipoGasto}", gastoId, gasto.TipoGasto);

                return new GetGastoAgenteByIdResult
                {
                    Success = true,
                    Gasto = gasto,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetGastoAgenteByIdResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Gasto = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving gasto");
                return new GetGastoAgenteByIdResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Gasto = null
                };
            }
        }

        /// <summary>
        /// Actualiza un gasto existente en Cosmos DB
        /// </summary>
        /// <param name="gastoId">ID del gasto a actualizar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <param name="gastoActualizado">Datos actualizados del gasto</param>
        /// <returns>Resultado de la operación de actualización</returns>
        public async Task<UpdateGastoAgenteResult> UpdateGastoAsync(string gastoId, string twinId, GastoAgente gastoActualizado)
        {
            if (string.IsNullOrEmpty(gastoId))
            {
                return new UpdateGastoAgenteResult
                {
                    Success = false,
                    ErrorMessage = "Gasto ID cannot be null or empty",
                    GastoActualizado = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new UpdateGastoAgenteResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    GastoActualizado = null
                };
            }

            if (gastoActualizado == null)
            {
                return new UpdateGastoAgenteResult
                {
                    Success = false,
                    ErrorMessage = "Updated gasto data cannot be null",
                    GastoActualizado = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Primero verificar que el gasto existe
                string queryCheck = "SELECT * FROM c WHERE c.id = @gastoId AND c.TwinID = @twinId";
                var queryDefinition = new QueryDefinition(queryCheck)
                    .WithParameter("@gastoId", gastoId)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<GastoAgente> feed = container.GetItemQueryIterator<GastoAgente>(queryDefinition);

                GastoAgente gastoExistente = null;
                while (feed.HasMoreResults)
                {
                    FeedResponse<GastoAgente> response = await feed.ReadNextAsync();
                    if (response.Count > 0)
                    {
                        gastoExistente = response.FirstOrDefault();
                        break;
                    }
                }

                if (gastoExistente == null)
                {
                    _logger.LogWarning("⚠️ Gasto not found for update: {GastoId}", gastoId);
                    return new UpdateGastoAgenteResult
                    {
                        Success = false,
                        ErrorMessage = $"Gasto with ID '{gastoId}' not found",
                        GastoActualizado = null
                    };
                }

                // Mantener valores inmutables
                gastoActualizado.Id = gastoId;
                gastoActualizado.TwinID = twinId;
                gastoActualizado.MicrosoftOID = gastoExistente.MicrosoftOID; // Mantener el MicrosoftOID original
                gastoActualizado.FechaRegistro = gastoExistente.FechaRegistro; // Mantener fecha de registro original

            
                // Serializar y adicionar metadatos de actualización
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(gastoActualizado));
                documentData["type"] = "gasto_agente";
                documentData["fechaActualizacion"] = DateTime.UtcNow;

                var updateResponse = await container.UpsertItemAsync(documentData, new PartitionKey(twinId));

                _logger.LogInformation("✅ Gasto updated successfully. Document ID: {DocumentId}, Tipo: {TipoGasto}, Total: ${Total}", 
                    gastoId, gastoActualizado.TipoGasto, gastoActualizado.Total);

                return new UpdateGastoAgenteResult
                {
                    Success = true,
                    GastoActualizado = gastoActualizado,
                    RUConsumed = updateResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new UpdateGastoAgenteResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    GastoActualizado = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating gasto");
                return new UpdateGastoAgenteResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    GastoActualizado = null
                };
            }
        }

        /// <summary>
        /// Elimina un gasto de Cosmos DB
        /// </summary>
        /// <param name="gastoId">ID del gasto a eliminar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Resultado de la operación de eliminación</returns>
        public async Task<DeleteGastoAgenteResult> DeleteGastoAsync(string gastoId, string twinId)
        {
            if (string.IsNullOrEmpty(gastoId))
            {
                return new DeleteGastoAgenteResult
                {
                    Success = false,
                    ErrorMessage = "Gasto ID cannot be null or empty"
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new DeleteGastoAgenteResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Primero obtener el gasto para registrar información antes de eliminar
                string queryCheck = "SELECT * FROM c WHERE c.id = @gastoId AND c.TwinID = @twinId";
                var queryDefinition = new QueryDefinition(queryCheck)
                    .WithParameter("@gastoId", gastoId)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<GastoAgente> feed = container.GetItemQueryIterator<GastoAgente>(queryDefinition);

                GastoAgente gastoExistente = null;
                while (feed.HasMoreResults)
                {
                    FeedResponse<GastoAgente> response = await feed.ReadNextAsync();
                    if (response.Count > 0)
                    {
                        gastoExistente = response.FirstOrDefault();
                        break;
                    }
                }

                if (gastoExistente == null)
                {
                    string errorMessage = $"Gasto with ID '{gastoId}' not found";
                    _logger.LogWarning("⚠️ {ErrorMessage}", errorMessage);
                    
                    return new DeleteGastoAgenteResult
                    {
                        Success = false,
                        ErrorMessage = errorMessage,
                        GastoId = gastoId
                    };
                }

                // Eliminar el gasto
                var deleteResponse = await container.DeleteItemAsync<GastoAgente>(gastoId, new PartitionKey(twinId));

                _logger.LogInformation("✅ Gasto deleted successfully. Document ID: {DocumentId}, TwinID: {TwinId}, Total eliminado: ${Total}", 
                    gastoId, twinId, gastoExistente.Total);

                return new DeleteGastoAgenteResult
                {
                    Success = true,
                    GastoId = gastoId,
                    RUConsumed = deleteResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow,
                    Message = $"Gasto '{gastoExistente.TipoGasto}' eliminado exitosamente. Total: ${gastoExistente.Total:N2}"
                };
            }
            catch (CosmosException cosmosEx) when (cosmosEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                string errorMessage = $"Gasto with ID '{gastoId}' not found";
                _logger.LogWarning("⚠️ {ErrorMessage}", errorMessage);

                return new DeleteGastoAgenteResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    GastoId = gastoId
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new DeleteGastoAgenteResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    GastoId = gastoId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting gasto");
                return new DeleteGastoAgenteResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    GastoId = gastoId
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
    /// Modelo para representar un gasto de agente inmobiliario
    /// </summary>
    public class GastoAgente
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("TwinID")]
        public string TwinID { get; set; } = string.Empty;

        [JsonProperty("microsoftOID")]
        public string MicrosoftOID { get; set; } = string.Empty;

        [JsonProperty("tipoGasto")]
        public string TipoGasto { get; set; } = string.Empty;

        [JsonProperty("descripcion")]
        public string Descripcion { get; set; } = string.Empty;

        [JsonProperty("unidad")]
        public string Unidad { get; set; } = string.Empty;

        [JsonProperty("cantidad")]
        public decimal Cantidad { get; set; }

        [JsonProperty("costoUnitario")]
        public decimal CostoUnitario { get; set; }

        [JsonProperty("iva")]
        public decimal IVA { get; set; }

        [JsonProperty("otrosImpuestos")]
        public decimal OtrosImpuestos { get; set; }

        [JsonProperty("fecha")]
        public DateTime Fecha { get; set; }

        [JsonProperty("notas")]
        public string Notas { get; set; } = string.Empty;

        [JsonProperty("total")]
        public decimal Total { get; set; }

        [JsonProperty("fechaRegistro")]
        public DateTime FechaRegistro { get; set; }
    }

    /// <summary>
    /// Resultado de guardar un gasto de agente
    /// </summary>
    public class SaveGastoAgenteResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string DocumentId { get; set; }
        public string TwinId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener múltiples gastos de agente
    /// </summary>
    public class GetGastosAgenteResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string TwinId { get; set; } = "";
        public string MicrosoftOID { get; set; } = "";
        public List<GastoAgente> Gastos { get; set; } = new();
        public int GastoCount { get; set; }
        public decimal TotalGastos { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener un gasto de agente por ID
    /// </summary>
    public class GetGastoAgenteByIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public GastoAgente Gasto { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de actualizar un gasto de agente
    /// </summary>
    public class UpdateGastoAgenteResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public GastoAgente GastoActualizado { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de eliminar un gasto de agente
    /// </summary>
    public class DeleteGastoAgenteResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Message { get; set; } = "";
        public string GastoId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace TwinAgentsNetwork.Services
{
    /// <summary>
    /// Servicio para gestionar inversiones/gastos de clientes en Azure Cosmos DB
    /// Database: twinmicasadb
    /// Container: twinclienteinversiones
    /// </summary>
    public class ClienteInversionesCosmosDB
    {
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "twinclienteinversiones";
        private CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ClienteInversionesCosmosDB> _logger;

        public ClienteInversionesCosmosDB(ILogger<ClienteInversionesCosmosDB> logger, IConfiguration configuration = null)
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
                    
                    _logger.LogInformation("✅ Successfully connected to Cliente Inversiones Cosmos DB");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error connecting to Cosmos DB");
                    throw;
                }
            }
        }

        /// <summary>
        /// Guarda un gasto/inversión en Cosmos DB
        /// </summary>
        /// <param name="gasto">Datos del gasto a guardar</param>
        /// <param name="twinID">ID del twin/agente</param>
        /// <param name="customerID">ID del cliente</param>
        /// <returns>Resultado de la operación con ID del documento creado</returns>
        public async Task<SaveGastoResult> SaveGastoAsync(GastoInversion gasto, string twinID, string customerID)
        {
            if (gasto == null)
            {
                return new SaveGastoResult
                {
                    Success = false,
                    ErrorMessage = "Gasto data cannot be null",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(twinID))
            {
                return new SaveGastoResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(customerID))
            {
                return new SaveGastoResult
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
                gasto.Id = Guid.NewGuid().ToString();
                gasto.TwinID = twinID;
                gasto.CustomerID = customerID;
                gasto.FechaRegistro = DateTime.UtcNow;
                
                // Calcular total si no está establecido
                if (gasto.Total == 0)
                {
                    gasto.Total = gasto.Cantidad * gasto.CostoUnitario;
                }

                // Serializar y adicionar metadatos
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(gasto));
                documentData["type"] = "gasto_inversion";
                documentData["fechaGuardado"] = DateTime.UtcNow;

                var response = await container.CreateItemAsync(documentData, new PartitionKey(twinID));

                _logger.LogInformation("✅ Gasto saved successfully. Document ID: {DocumentId}, Tipo: {TipoGasto}, Total: {Total}", 
                    gasto.Id, gasto.TipoGasto, gasto.Total);

                return new SaveGastoResult
                {
                    Success = true,
                    DocumentId = gasto.Id,
                    RUConsumed = response.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new SaveGastoResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    DocumentId = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving gasto");
                return new SaveGastoResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = null
                };
            }
        }

        /// <summary>
        /// Obtiene todos los gastos para un TwinID y CustomerID específicos
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <param name="customerId">ID del cliente</param>
        /// <returns>Lista de gastos encontrados</returns>
        public async Task<GetGastosResult> GetGastosByTwinIdAndCustomerIdAsync(string twinId, string customerId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetGastosResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Gastos = new List<GastoInversion>()
                };
            }

            if (string.IsNullOrEmpty(customerId))
            {
                return new GetGastosResult
                {
                    Success = false,
                    ErrorMessage = "CustomerID cannot be null or empty",
                    Gastos = new List<GastoInversion>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId AND c.CustomerID = @customerId ORDER BY c.Fecha DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@customerId", customerId);

                using FeedIterator<GastoInversion> feed = container.GetItemQueryIterator<GastoInversion>(queryDefinition);

                var gastos = new List<GastoInversion>();
                double totalRU = 0;
                int count = 0;
                decimal totalGastos = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<GastoInversion> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var gasto in response)
                    {
                        gastos.Add(gasto);
                        totalGastos += gasto.Total;
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} gastos for TwinID: {TwinId}, CustomerID: {CustomerId}. Total: ${Total}. RU consumed: {RU:F2}", 
                    count, twinId, customerId, totalGastos, totalRU);

                return new GetGastosResult
                {
                    Success = true,
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

                return new GetGastosResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Gastos = new List<GastoInversion>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving gastos");
                return new GetGastosResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Gastos = new List<GastoInversion>()
                };
            }
        }

        /// <summary>
        /// Obtiene un gasto específico por ID
        /// </summary>
        /// <param name="gastoId">ID del gasto</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Datos del gasto encontrado</returns>
        public async Task<GetGastoByIdResult> GetGastoByIdAsync(string gastoId, string twinId)
        {
            if (string.IsNullOrEmpty(gastoId))
            {
                return new GetGastoByIdResult
                {
                    Success = false,
                    ErrorMessage = "Gasto ID cannot be null or empty",
                    Gasto = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new GetGastoByIdResult
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

                using FeedIterator<GastoInversion> feed = container.GetItemQueryIterator<GastoInversion>(queryDefinition);

                GastoInversion gasto = null;
                double totalRU = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<GastoInversion> response = await feed.ReadNextAsync();
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
                    return new GetGastoByIdResult
                    {
                        Success = false,
                        ErrorMessage = $"Gasto with ID '{gastoId}' not found",
                        Gasto = null
                    };
                }

                _logger.LogInformation("✅ Retrieved gasto: {GastoId}, Tipo: {TipoGasto}", gastoId, gasto.TipoGasto);

                return new GetGastoByIdResult
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

                return new GetGastoByIdResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Gasto = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving gasto");
                return new GetGastoByIdResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Gasto = null
                };
            }
        }

        /// <summary>
        /// Actualiza un gasto/inversión existente en Cosmos DB
        /// </summary>
        /// <param name="gastoId">ID del gasto a actualizar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <param name="gastoActualizado">Datos actualizados del gasto</param>
        /// <returns>Resultado de la operación de actualización</returns>
        public async Task<UpdateGastoResult> UpdateGastoAsync(string gastoId, string twinId, GastoInversion gastoActualizado)
        {
            if (string.IsNullOrEmpty(gastoId))
            {
                return new UpdateGastoResult
                {
                    Success = false,
                    ErrorMessage = "Gasto ID cannot be null or empty",
                    GastoActualizado = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new UpdateGastoResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    GastoActualizado = null
                };
            }

            if (gastoActualizado == null)
            {
                return new UpdateGastoResult
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

                using FeedIterator<GastoInversion> feed = container.GetItemQueryIterator<GastoInversion>(queryDefinition);

                GastoInversion gastoExistente = null;
                while (feed.HasMoreResults)
                {
                    FeedResponse<GastoInversion> response = await feed.ReadNextAsync();
                    if (response.Count > 0)
                    {
                        gastoExistente = response.FirstOrDefault();
                        break;
                    }
                }

                if (gastoExistente == null)
                {
                    _logger.LogWarning("⚠️ Gasto not found for update: {GastoId}", gastoId);
                    return new UpdateGastoResult
                    {
                        Success = false,
                        ErrorMessage = $"Gasto with ID '{gastoId}' not found",
                        GastoActualizado = null
                    };
                }

                // Mantener valores inmutables
                gastoActualizado.Id = gastoId;
                gastoActualizado.TwinID = twinId;
                gastoActualizado.CustomerID = gastoExistente.CustomerID; // Mantener el CustomerID original
                gastoActualizado.FechaRegistro = gastoExistente.FechaRegistro; // Mantener fecha de registro original

                // Recalcular total si los valores cambiaron
                if (gastoActualizado.Total == 0 || gastoActualizado.Cantidad != gastoExistente.Cantidad || 
                    gastoActualizado.CostoUnitario != gastoExistente.CostoUnitario)
                {
                    gastoActualizado.Total = gastoActualizado.Cantidad * gastoActualizado.CostoUnitario;
                }

                // Serializar y adicionar metadatos de actualización
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(gastoActualizado));
                documentData["type"] = "gasto_inversion";
                documentData["fechaActualizacion"] = DateTime.UtcNow;

                var updateResponse = await container.UpsertItemAsync(documentData, new PartitionKey(twinId));

                _logger.LogInformation("✅ Gasto updated successfully. Document ID: {DocumentId}, Tipo: {TipoGasto}, Total: {Total}", 
                    gastoId, gastoActualizado.TipoGasto, gastoActualizado.Total);

                return new UpdateGastoResult
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

                return new UpdateGastoResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    GastoActualizado = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating gasto");
                return new UpdateGastoResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    GastoActualizado = null
                };
            }
        }

        /// <summary>
        /// Elimina un gasto/inversión de Cosmos DB
        /// </summary>
        /// <param name="gastoId">ID del gasto a eliminar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Resultado de la operación de eliminación</returns>
        public async Task<DeleteGastoResult> DeleteGastoAsync(string gastoId, string twinId)
        {
            if (string.IsNullOrEmpty(gastoId))
            {
                return new DeleteGastoResult
                {
                    Success = false,
                    ErrorMessage = "Gasto ID cannot be null or empty"
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new DeleteGastoResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                var deleteResponse = await container.DeleteItemAsync<GastoInversion>(gastoId, new PartitionKey(twinId));

                _logger.LogInformation("✅ Gasto deleted successfully. Document ID: {DocumentId}", gastoId);

                return new DeleteGastoResult
                {
                    Success = true,
                    GastoId = gastoId,
                    RUConsumed = deleteResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx) when (cosmosEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                string errorMessage = $"Gasto with ID '{gastoId}' not found";
                _logger.LogWarning("⚠️ {ErrorMessage}", errorMessage);

                return new DeleteGastoResult
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

                return new DeleteGastoResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    GastoId = gastoId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting gasto");
                return new DeleteGastoResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    GastoId = gastoId
                };
            }
        }

        /// <summary>
        /// Elimina un gasto/inversión de Cosmos DB validando TwinID, CustomerID y GastoID
        /// </summary>
        /// <param name="gastoId">ID del gasto a eliminar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <param name="customerId">ID del cliente para validación adicional</param>
        /// <returns>Resultado de la operación de eliminación</returns>
        public async Task<DeleteGastoResult> DeleteGastoByTwinCustomerAsync(string gastoId, string twinId, string customerId)
        {
            if (string.IsNullOrEmpty(gastoId))
            {
                return new DeleteGastoResult
                {
                    Success = false,
                    ErrorMessage = "Gasto ID cannot be null or empty"
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new DeleteGastoResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty"
                };
            }

            if (string.IsNullOrEmpty(customerId))
            {
                return new DeleteGastoResult
                {
                    Success = false,
                    ErrorMessage = "CustomerID cannot be null or empty"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Primero verificar que el gasto existe y pertenece al cliente correcto
                string queryCheck = "SELECT * FROM c WHERE c.id = @gastoId AND c.TwinID = @twinId AND c.CustomerID = @customerId";
                var queryDefinition = new QueryDefinition(queryCheck)
                    .WithParameter("@gastoId", gastoId)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@customerId", customerId);

                using FeedIterator<GastoInversion> feed = container.GetItemQueryIterator<GastoInversion>(queryDefinition);

                GastoInversion gastoExistente = null;
                while (feed.HasMoreResults)
                {
                    FeedResponse<GastoInversion> response = await feed.ReadNextAsync();
                    if (response.Count > 0)
                    {
                        gastoExistente = response.FirstOrDefault();
                        break;
                    }
                }

                if (gastoExistente == null)
                {
                    string errorMessage = $"Gasto with ID '{gastoId}' not found for TwinID '{twinId}' and CustomerID '{customerId}'";
                    _logger.LogWarning("⚠️ {ErrorMessage}", errorMessage);
                    
                    return new DeleteGastoResult
                    {
                        Success = false,
                        ErrorMessage = errorMessage,
                        GastoId = gastoId
                    };
                }

                // Eliminar el gasto
                var deleteResponse = await container.DeleteItemAsync<GastoInversion>(gastoId, new PartitionKey(twinId));

                _logger.LogInformation("✅ Gasto deleted successfully. Document ID: {DocumentId}, TwinID: {TwinId}, CustomerID: {CustomerId}, Total eliminado: ${Total}", 
                    gastoId, twinId, customerId, gastoExistente.Total);

                return new DeleteGastoResult
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

                return new DeleteGastoResult
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

                return new DeleteGastoResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    GastoId = gastoId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting gasto");
                return new DeleteGastoResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    GastoId = gastoId
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
    /// Modelo para representar un gasto/inversión de cliente
    /// </summary>
    public class GastoInversion
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("TwinID")]
        public string TwinID { get; set; } = string.Empty;


        [JsonProperty("microsoftOID")]
        public string MicrosoftOID { get; set; } = string.Empty;

        [JsonProperty("CustomerID")]
        public string CustomerID { get; set; } = string.Empty;

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
    /// Resultado de guardar un gasto
    /// </summary>
    public class SaveGastoResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string DocumentId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener múltiples gastos
    /// </summary>
    public class GetGastosResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<GastoInversion> Gastos { get; set; } = new();
        public int GastoCount { get; set; }
        public decimal TotalGastos { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener un gasto por ID
    /// </summary>
    public class GetGastoByIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public GastoInversion Gasto { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de actualizar un gasto
    /// </summary>
    public class UpdateGastoResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public GastoInversion GastoActualizado { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de eliminar un gasto
    /// </summary>
    public class DeleteGastoResult
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

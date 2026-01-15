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
    /// Servicio para gestionar clientes compradores en Azure Cosmos DB
    /// Database: twinmicasadb
    /// Container: twinclientecomprador
    /// </summary>
    public class AentCustomerBuyerCosmosDB
    {
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "twinclientecomprador";
        private CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AentCustomerBuyerCosmosDB> _logger;

        public AentCustomerBuyerCosmosDB(ILogger<AentCustomerBuyerCosmosDB> logger, IConfiguration configuration = null)
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
                    
                    _logger.LogInformation("✅ Successfully connected to Cliente Comprador Cosmos DB");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error connecting to Cosmos DB");
                    throw;
                }
            }
        }

        /// <summary>
        /// Guarda un comprador en Cosmos DB
        /// </summary>
        /// <param name="comprador">Datos del comprador a guardar</param>
        /// <param name="twinID">ID del twin/agente</param>
        /// <returns>Resultado de la operación con ID del documento creado</returns>
        public async Task<SaveCompradorResult> SaveCompradorAsync(CompradorRequest comprador, string twinID)
        {
            if (comprador == null)
            {
                return new SaveCompradorResult
                {
                    Success = false,
                    ErrorMessage = "Comprador data cannot be null",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(twinID))
            {
                return new SaveCompradorResult
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
                string documentId = Guid.NewGuid().ToString();

                // Serializar y adicionar metadatos
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(comprador));
                
                documentData["id"] = documentId;
                documentData["TwinID"] = twinID;
                documentData["type"] = "cliente_comprador";
                documentData["tipoCliente"] = "comprador"; // Asegurar que esté establecido
                documentData["fechaGuardado"] = DateTime.UtcNow;

                var response = await container.CreateItemAsync(documentData, new PartitionKey(twinID));

                _logger.LogInformation("✅ Comprador saved successfully. Document ID: {DocumentId}, Nombre: {Nombre} {Apellido}", 
                    documentId, comprador.Nombre, comprador.Apellido);

                return new SaveCompradorResult
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

                return new SaveCompradorResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    DocumentId = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving comprador");
                return new SaveCompradorResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = null
                };
            }
        }

        /// <summary>
        /// Obtiene todos los compradores para un TwinID específico
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <returns>Lista de compradores encontrados</returns>
        public async Task<GetCompradoresResult> GetCompradoresByTwinIdAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetCompradoresResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Compradores = new List<CompradorRequest>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.agenteInmobiiarioTwinID = @twinId ORDER BY c.fechaRegistro DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<CompradorRequest> feed = container.GetItemQueryIterator<CompradorRequest>(queryDefinition);

                var compradores = new List<CompradorRequest>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<CompradorRequest> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var comprador in response)
                    {
                        compradores.Add(comprador);
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} compradores for TwinID: {TwinId}. RU consumed: {RU:F2}", 
                    count, twinId, totalRU);

                return new GetCompradoresResult
                {
                    Success = true,
                    Compradores = compradores,
                    CompradorCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetCompradoresResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Compradores = new List<CompradorRequest>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving compradores");
                return new GetCompradoresResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Compradores = new List<CompradorRequest>()
                };
            }
        }

        /// <summary>
        /// Obtiene un comprador específico por ID
        /// </summary>
        /// <param name="compradorId">ID del comprador</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Datos del comprador encontrado</returns>
        public async Task<GetCompradorByIdResult> GetCompradorByIdAsync(string compradorId, string twinId)
        {
            if (string.IsNullOrEmpty(compradorId))
            {
                return new GetCompradorByIdResult
                {
                    Success = false,
                    ErrorMessage = "Comprador ID cannot be null or empty",
                    Comprador = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new GetCompradorByIdResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Comprador = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.id = @compradorId AND c.TwinID = @twinId";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@compradorId", compradorId)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<CompradorRequest> feed = container.GetItemQueryIterator<CompradorRequest>(queryDefinition);

                CompradorRequest comprador = null;
                double totalRU = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<CompradorRequest> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    if (response.Count > 0)
                    {
                        comprador = response.FirstOrDefault();
                        break;
                    }
                }

                if (comprador == null)
                {
                    _logger.LogWarning("⚠️ Comprador not found: {CompradorId}", compradorId);
                    return new GetCompradorByIdResult
                    {
                        Success = false,
                        ErrorMessage = $"Comprador with ID '{compradorId}' not found",
                        Comprador = null
                    };
                }

                _logger.LogInformation("✅ Retrieved comprador: {CompradorId}, Nombre: {Nombre} {Apellido}", 
                    compradorId, comprador.Nombre, comprador.Apellido);

                return new GetCompradorByIdResult
                {
                    Success = true,
                    Comprador = comprador,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetCompradorByIdResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Comprador = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving comprador");
                return new GetCompradorByIdResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Comprador = null
                };
            }
        }

        /// <summary>
        /// Obtiene un comprador específico por Microsoft OID
        /// </summary>
        /// <param name="microsoftOID">Microsoft Object ID del usuario</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Datos del comprador encontrado</returns>
        public async Task<GetCompradorByOIDResult> GetCompradorByMicrosoftOIDAsync(string microsoftOID, string twinId)
        {
            if (string.IsNullOrEmpty(microsoftOID))
            {
                return new GetCompradorByOIDResult
                {
                    Success = false,
                    ErrorMessage = "Microsoft OID cannot be null or empty",
                    Comprador = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new GetCompradorByOIDResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Comprador = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.microsoftOID = @microsoftOID AND c.TwinID = @twinId";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@microsoftOID", microsoftOID)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<CompradorRequest> feed = container.GetItemQueryIterator<CompradorRequest>(queryDefinition);

                CompradorRequest comprador = null;
                double totalRU = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<CompradorRequest> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    if (response.Count > 0)
                    {
                        comprador = response.FirstOrDefault();
                        break;
                    }
                }

                if (comprador == null)
                {
                    _logger.LogWarning("⚠️ Comprador not found with Microsoft OID: {MicrosoftOID}", microsoftOID);
                    return new GetCompradorByOIDResult
                    {
                        Success = false,
                        ErrorMessage = $"Comprador with Microsoft OID '{microsoftOID}' not found",
                        Comprador = null
                    };
                }

                _logger.LogInformation("✅ Retrieved comprador by Microsoft OID: {MicrosoftOID}, Nombre: {Nombre} {Apellido}", 
                    microsoftOID, comprador.Nombre, comprador.Apellido);

                return new GetCompradorByOIDResult
                {
                    Success = true,
                    Comprador = comprador,
                    MicrosoftOID = microsoftOID,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetCompradorByOIDResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Comprador = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving comprador by Microsoft OID");
                return new GetCompradorByOIDResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Comprador = null
                };
            }
        }

        /// <summary>
        /// Actualiza un comprador existente en Cosmos DB
        /// </summary>
        /// <param name="compradorId">ID del comprador a actualizar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <param name="compradorActualizado">Datos actualizados del comprador</param>
        /// <returns>Resultado de la operación de actualización</returns>
        public async Task<UpdateCompradorResult> UpdateCompradorAsync(string compradorId, string twinId, CompradorRequest compradorActualizado)
        {
            if (string.IsNullOrEmpty(compradorId))
            {
                return new UpdateCompradorResult
                {
                    Success = false,
                    ErrorMessage = "Comprador ID cannot be null or empty",
                    CompradorActualizado = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new UpdateCompradorResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    CompradorActualizado = null
                };
            }

            if (compradorActualizado == null)
            {
                return new UpdateCompradorResult
                {
                    Success = false,
                    ErrorMessage = "Updated comprador data cannot be null",
                    CompradorActualizado = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Verificar que el comprador existe
                string queryCheck = "SELECT * FROM c WHERE c.id = @compradorId AND c.TwinID = @twinId";
                var queryDefinition = new QueryDefinition(queryCheck)
                    .WithParameter("@compradorId", compradorId)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<CompradorRequest> feed = container.GetItemQueryIterator<CompradorRequest>(queryDefinition);

                CompradorRequest compradorExistente = null;
                while (feed.HasMoreResults)
                {
                    FeedResponse<CompradorRequest> response = await feed.ReadNextAsync();
                    if (response.Count > 0)
                    {
                        compradorExistente = response.FirstOrDefault();
                        break;
                    }
                }

                if (compradorExistente == null)
                {
                    _logger.LogWarning("⚠️ Comprador not found for update: {CompradorId}", compradorId);
                    return new UpdateCompradorResult
                    {
                        Success = false,
                        ErrorMessage = $"Comprador with ID '{compradorId}' not found",
                        CompradorActualizado = null
                    };
                }

                // Serializar y adicionar metadatos de actualización
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(compradorActualizado));
                
                documentData["id"] = compradorId;
                documentData["TwinID"] = twinId;
                documentData["type"] = "cliente_comprador";
                documentData["tipoCliente"] = "comprador";
                documentData["fechaRegistro"] = compradorExistente.FechaRegistro; // Mantener fecha original
                documentData["fechaActualizacion"] = DateTime.UtcNow;

                var updateResponse = await container.UpsertItemAsync(documentData, new PartitionKey(twinId));

                _logger.LogInformation("✅ Comprador updated successfully. Document ID: {DocumentId}, Nombre: {Nombre} {Apellido}", 
                    compradorId, compradorActualizado.Nombre, compradorActualizado.Apellido);

                return new UpdateCompradorResult
                {
                    Success = true,
                    CompradorActualizado = compradorActualizado,
                    RUConsumed = updateResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new UpdateCompradorResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    CompradorActualizado = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating comprador");
                return new UpdateCompradorResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    CompradorActualizado = null
                };
            }
        }

        /// <summary>
        /// Elimina un comprador de Cosmos DB
        /// </summary>
        /// <param name="compradorId">ID del comprador a eliminar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Resultado de la operación de eliminación</returns>
        public async Task<DeleteCompradorResult> DeleteCompradorAsync(string compradorId, string twinId)
        {
            if (string.IsNullOrEmpty(compradorId))
            {
                return new DeleteCompradorResult
                {
                    Success = false,
                    ErrorMessage = "Comprador ID cannot be null or empty"
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new DeleteCompradorResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                var deleteResponse = await container.DeleteItemAsync<CompradorRequest>(compradorId, new PartitionKey(twinId));

                _logger.LogInformation("✅ Comprador deleted successfully. Document ID: {DocumentId}", compradorId);

                return new DeleteCompradorResult
                {
                    Success = true,
                    CompradorId = compradorId,
                    RUConsumed = deleteResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow,
                    Message = "Comprador eliminado exitosamente"
                };
            }
            catch (CosmosException cosmosEx) when (cosmosEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                string errorMessage = $"Comprador with ID '{compradorId}' not found";
                _logger.LogWarning("⚠️ {ErrorMessage}", errorMessage);

                return new DeleteCompradorResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    CompradorId = compradorId
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new DeleteCompradorResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    CompradorId = compradorId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting comprador");
                return new DeleteCompradorResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    CompradorId = compradorId
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
    /// DTO principal para comprador
    /// </summary>
    public class CompradorRequest
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("microsoftOID")]
        public string MicrosoftOID { get; set; } = string.Empty;

        [JsonProperty("agneteInmobiliarioid")]
        public string AgenteInmobiiarioId { get; set; } = string.Empty;

        [JsonProperty("agenteInmobiiarioTwinID ")]
        public string AgenteInmobiiarioTwinID { get; set; } = string.Empty;

        [JsonProperty("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [JsonProperty("apellido")]
        public string Apellido { get; set; } = string.Empty;

        [JsonProperty("email")]
        public string Email { get; set; } = string.Empty;

        [JsonProperty("telefono")]
        public string Telefono { get; set; } = string.Empty;

        [JsonProperty("telefonoSecundario")]
        public string TelefonoSecundario { get; set; } = string.Empty;

        [JsonProperty("tipoCliente")]
        public string TipoCliente { get; set; } = "comprador";

        [JsonProperty("presupuesto")]
        public PresupuestoPago Presupuesto { get; set; } = new();

        [JsonProperty("ubicacion")]
        public UbicacionDeseada Ubicacion { get; set; } = new();

        [JsonProperty("preferencias")]
        public PreferenciasPropiedad Preferencias { get; set; } = new();

        [JsonProperty("motivacion")]
        public string Motivacion { get; set; } = string.Empty;

        [JsonProperty("tiempoCompra")]
        public string TiempoCompra { get; set; } = string.Empty;

        [JsonProperty("notas")]
        public string Notas { get; set; } = string.Empty;

        [JsonProperty("fechaRegistro")]
        public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Sección: presupuesto y pago
    /// </summary>
    public class PresupuestoPago
    {
        [JsonProperty("presupuestoMaximo")]
        public double? PresupuestoMaximo { get; set; }

        [JsonProperty("moneda")]
        public string Moneda { get; set; } = "MXN";

        [JsonProperty("formaPago")]
        public string FormaPago { get; set; } = string.Empty;

        [JsonProperty("montoCredito")]
        public double? MontoCredito { get; set; }

        [JsonProperty("enganche")]
        public double? Enganche { get; set; }
    }

    /// <summary>
    /// Sección: ubicación deseada
    /// </summary>
    public class UbicacionDeseada
    {
        [JsonProperty("ciudad")]
        public string Ciudad { get; set; } = string.Empty;

        [JsonProperty("estado")]
        public string Estado { get; set; } = string.Empty;

        [JsonProperty("colonia")]
        public string Colonia { get; set; } = string.Empty;

        [JsonProperty("latitud")]
        public double? Latitud { get; set; }

        [JsonProperty("longitud")]
        public double? Longitud { get; set; }

        [JsonProperty("direccionLibre")]
        public string DireccionLibre { get; set; } = string.Empty;

        [JsonProperty("zonasPreferidas")]
        public List<string> ZonasPreferidas { get; set; } = new();
    }

    /// <summary>
    /// Sección: preferencias de propiedad
    /// </summary>
    public class PreferenciasPropiedad
    {
        [JsonProperty("tipoPropiedad")]
        public string TipoPropiedad { get; set; } = string.Empty;

        [JsonProperty("tipoOperacion")]
        public string TipoOperacion { get; set; } = "compra";

        [JsonProperty("recamaras")]
        public int? Recamaras { get; set; }

        [JsonProperty("banos")]
        public int? Banos { get; set; }

        [JsonProperty("estacionamientos")]
        public int? Estacionamientos { get; set; }

        [JsonProperty("metrosConstruidos")]
        public double? MetrosConstruidos { get; set; }

        [JsonProperty("metrosTerreno")]
        public double? MetrosTerreno { get; set; }

        [JsonProperty("jardin")]
        public bool? Jardin { get; set; }

        [JsonProperty("petFriendly")]
        public bool? PetFriendly { get; set; }

        [JsonProperty("elevador")]
        public bool? Elevador { get; set; }

        [JsonProperty("seguridad")]
        public bool? Seguridad { get; set; }

        [JsonProperty("amenidades")]
        public List<string> Amenidades { get; set; } = new();

        [JsonProperty("disponibilidad")]
        public string Disponibilidad { get; set; } = string.Empty;
    }

    /// <summary>
    /// Resultado de guardar un comprador
    /// </summary>
    public class SaveCompradorResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string DocumentId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener múltiples compradores
    /// </summary>
    public class GetCompradoresResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<CompradorRequest> Compradores { get; set; } = new();
        public int CompradorCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener un comprador por ID
    /// </summary>
    public class GetCompradorByIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public CompradorRequest Comprador { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener un comprador por Microsoft OID
    /// </summary>
    public class GetCompradorByOIDResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public CompradorRequest Comprador { get; set; }
        public string MicrosoftOID { get; set; } = "";
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de actualizar un comprador
    /// </summary>
    public class UpdateCompradorResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public CompradorRequest CompradorActualizado { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de eliminar un comprador
    /// </summary>
    public class DeleteCompradorResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Message { get; set; } = "";
        public string CompradorId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

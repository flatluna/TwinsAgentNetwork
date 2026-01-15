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
    /// Servicio para gestionar ventas de casas desde prospección hasta venta final en Azure Cosmos DB
    /// Database: twinmicasadb
    /// Container: twinagentecasasventas
    /// Partition Key: /TwinID
    /// </summary>
    public class AgentMisCasasVentasCosmosDB
    {
        private readonly ILogger<AgentMisCasasVentasCosmosDB> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "twinmiscasasventas";
        private CosmosClient _cosmosClient;

        public AgentMisCasasVentasCosmosDB(ILogger<AgentMisCasasVentasCosmosDB> logger, IConfiguration configuration = null)
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
                    
                    _logger.LogInformation("✅ Successfully connected to Casas Ventas Cosmos DB");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error connecting to Cosmos DB");
                    throw;
                }
            }
        }

        /// <summary>
        /// Guarda una nueva venta/prospecto de casa en Cosmos DB
        /// </summary>
        /// <param name="venta">Datos de la venta de casa</param>
        /// <param name="twinId">ID del twin (partition key - agente vendedor)</param>
        /// <returns>Resultado de la operación con ID del documento creado</returns>
        public async Task<SaveCasaVentaResult> SaveCasaVentaAsync(CasaVenta venta, string twinId)
        {
            if (venta == null)
            {
                return new SaveCasaVentaResult
                {
                    Success = false,
                    ErrorMessage = "Venta data cannot be null",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new SaveCasaVentaResult
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

                // ✅ VALIDACIÓN: Verificar que la propiedad no esté ya asignada al cliente
                _logger.LogInformation("🔍 Verificando si la propiedad {PropiedadId} ya está asignada al cliente {ClienteId}", 
                    venta.PropiedadId, venta.ClienteCompradorId);

                string checkQuery = "SELECT * FROM c WHERE c.TwinID = @twinId AND c.propiedadId = @propiedadId AND c.clienteCompradorId = @clienteCompradorId";
                
                var checkQueryDefinition = new QueryDefinition(checkQuery)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@propiedadId", venta.PropiedadId)
                    .WithParameter("@clienteCompradorId", venta.ClienteCompradorId);

                using (FeedIterator<CasaVenta> checkFeed = container.GetItemQueryIterator<CasaVenta>(checkQueryDefinition))
                {
                    bool existeAsignacion = false;
                    
                    while (checkFeed.HasMoreResults)
                    {
                        FeedResponse<CasaVenta> checkResponse = await checkFeed.ReadNextAsync();
                        if (checkResponse.Count > 0)
                        {
                            existeAsignacion = true;
                            var ventaExistente = checkResponse.FirstOrDefault();
                            
                            _logger.LogWarning("⚠️ La propiedad {PropiedadId} ya está asignada al cliente {ClienteId}. Estado actual: {Estado}", 
                                venta.PropiedadId, venta.ClienteCompradorId, ventaExistente?.EstadoActual);
                            
                            return new SaveCasaVentaResult
                            {
                                Success = false,
                                ErrorMessage = $"La propiedad '{venta.PropiedadId}' ya está asignada al cliente '{venta.ClienteCompradorId}'. Estado actual: {ventaExistente?.EstadoActual}",
                                DocumentId = null
                            };
                        }
                    }
                }

                _logger.LogInformation("✅ Validación exitosa: la propiedad no está duplicada para este cliente");

                // Generar ID único para el documento
                venta.Id = Guid.NewGuid().ToString();
                venta.TwinID = twinId;
                venta.FechaRegistro = DateTime.UtcNow;

                // Inicializar fecha del estado actual
                venta.FechaProspectada = DateTime.UtcNow;

                // Serializar y adicionar metadatos
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(venta));
                documentData["type"] = "casa_venta";
                documentData["fechaGuardado"] = DateTime.UtcNow;

                var response = await container.CreateItemAsync(documentData, new PartitionKey(twinId));

                _logger.LogInformation("✅ Casa venta saved successfully. Document ID: {DocumentId}, Propiedad: {PropiedadId}, Estado: {Estado}", 
                    venta.Id, venta.PropiedadId, venta.EstadoActual);

                return new SaveCasaVentaResult
                {
                    Success = true,
                    DocumentId = venta.Id,
                    TwinId = twinId,
                    RUConsumed = response.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new SaveCasaVentaResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    DocumentId = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving casa venta");
                return new SaveCasaVentaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = null
                };
            }
        }

        /// <summary>
        /// Obtiene todas las ventas para un TwinID específico
        /// </summary>
        /// <param name="twinId">ID del twin (agente vendedor)</param>
        /// <returns>Lista de ventas encontradas</returns>
        public async Task<GetCasasVentaResult> GetVentasByTwinIdAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetCasasVentaResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Ventas = new List<CasaVenta>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.FechaRegistro DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<CasaVenta> feed = container.GetItemQueryIterator<CasaVenta>(queryDefinition);

                var ventas = new List<CasaVenta>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<CasaVenta> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var venta in response)
                    {
                        ventas.Add(venta);
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} ventas for TwinID: {TwinId}. RU consumed: {RU:F2}", 
                    count, twinId, totalRU);

                return new GetCasasVentaResult
                {
                    Success = true,
                    TwinId = twinId,
                    Ventas = ventas,
                    VentaCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetCasasVentaResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Ventas = new List<CasaVenta>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving ventas");
                return new GetCasasVentaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Ventas = new List<CasaVenta>()
                };
            }
        }

        /// <summary>
        /// Obtiene ventas por TwinID y MicrosoftOID
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <param name="microsoftOID">ID de Microsoft del agente</param>
        /// <returns>Lista de ventas encontradas</returns>
        public async Task<GetCasasVentaResult> GetVentasByTwinIdAndMicrosoftOIDAsync(string twinId, string microsoftOID)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetCasasVentaResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Ventas = new List<CasaVenta>()
                };
            }

            if (string.IsNullOrEmpty(microsoftOID))
            {
                return new GetCasasVentaResult
                {
                    Success = false,
                    ErrorMessage = "MicrosoftOID cannot be null or empty",
                    Ventas = new List<CasaVenta>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId AND c.microsoftOID = @microsoftOID ORDER BY c.fechaRegistro DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@microsoftOID", microsoftOID);

                using FeedIterator<CasaVenta> feed = container.GetItemQueryIterator<CasaVenta>(queryDefinition);

                var ventas = new List<CasaVenta>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<CasaVenta> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var venta in response)
                    {
                        ventas.Add(venta);
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} ventas for TwinID: {TwinId}, MicrosoftOID: {MicrosoftOID}. RU consumed: {RU:F2}", 
                    count, twinId, microsoftOID, totalRU);

                return new GetCasasVentaResult
                {
                    Success = true,
                    TwinId = twinId,
                    MicrosoftOID = microsoftOID,
                    Ventas = ventas,
                    VentaCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetCasasVentaResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Ventas = new List<CasaVenta>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving ventas");
                return new GetCasasVentaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Ventas = new List<CasaVenta>()
                };
            }
        }

        /// <summary>
        /// Obtiene ventas por TwinID y ClienteCompradorId
        /// </summary>
        /// <param name="twinId">ID del twin (agente vendedor)</param>
        /// <param name="clienteCompradorId">ID del cliente comprador</param>
        /// <returns>Lista de ventas encontradas para el cliente específico</returns>
        public async Task<GetCasasVentaResult> GetVentasByTwinIdAndClienteCompradorAsync(string twinId, string clienteCompradorId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetCasasVentaResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Ventas = new List<CasaVenta>()
                };
            }

            if (string.IsNullOrEmpty(clienteCompradorId))
            {
                return new GetCasasVentaResult
                {
                    Success = false,
                    ErrorMessage = "ClienteCompradorId cannot be null or empty",
                    Ventas = new List<CasaVenta>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId AND c.clienteCompradorId = @clienteCompradorId ORDER BY c.fechaRegistro DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@clienteCompradorId", clienteCompradorId);

                using FeedIterator<CasaVenta> feed = container.GetItemQueryIterator<CasaVenta>(queryDefinition);

                var ventas = new List<CasaVenta>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<CasaVenta> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var venta in response)
                    {
                        ventas.Add(venta);
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} ventas for TwinID: {TwinId}, ClienteCompradorId: {ClienteCompradorId}. RU consumed: {RU:F2}", 
                    count, twinId, clienteCompradorId, totalRU);

                return new GetCasasVentaResult
                {
                    Success = true,
                    TwinId = twinId,
                    Ventas = ventas,
                    VentaCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetCasasVentaResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Ventas = new List<CasaVenta>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving ventas by cliente comprador");
                return new GetCasasVentaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Ventas = new List<CasaVenta>()
                };
            }
        }

        /// <summary>
        /// Obtiene ventas por estado
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <param name="estado">Estado de la venta</param>
        /// <returns>Lista de ventas filtradas por estado</returns>
        public async Task<GetCasasVentaResult> GetVentasByEstadoAsync(string twinId, EstadoVenta estado)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetCasasVentaResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Ventas = new List<CasaVenta>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId AND c.EstadoActual = @estado ORDER BY c.FechaRegistro DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@estado", estado.ToString());

                using FeedIterator<CasaVenta> feed = container.GetItemQueryIterator<CasaVenta>(queryDefinition);

                var ventas = new List<CasaVenta>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<CasaVenta> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var venta in response)
                    {
                        ventas.Add(venta);
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} ventas in estado {Estado} for TwinID: {TwinId}. RU consumed: {RU:F2}", 
                    count, estado, twinId, totalRU);

                return new GetCasasVentaResult
                {
                    Success = true,
                    TwinId = twinId,
                    Ventas = ventas,
                    VentaCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetCasasVentaResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Ventas = new List<CasaVenta>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving ventas by estado");
                return new GetCasasVentaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Ventas = new List<CasaVenta>()
                };
            }
        }

        /// <summary>
        /// Obtiene una venta específica por ID
        /// </summary>
        /// <param name="ventaId">ID de la venta</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Datos de la venta encontrada</returns>
        public async Task<GetCasaVentaByIdResult> GetVentaByIdAsync(string ventaId, string twinId)
        {
            if (string.IsNullOrEmpty(ventaId))
            {
                return new GetCasaVentaByIdResult
                {
                    Success = false,
                    ErrorMessage = "Venta ID cannot be null or empty",
                    Venta = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new GetCasaVentaByIdResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Venta = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.id = @ventaId AND c.TwinID = @twinId";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@ventaId", ventaId)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<CasaVenta> feed = container.GetItemQueryIterator<CasaVenta>(queryDefinition);

                CasaVenta venta = null;
                double totalRU = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<CasaVenta> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    if (response.Count > 0)
                    {
                        venta = response.FirstOrDefault();
                        break;
                    }
                }

                if (venta == null)
                {
                    _logger.LogWarning("⚠️ Venta not found: {VentaId}", ventaId);
                    return new GetCasaVentaByIdResult
                    {
                        Success = false,
                        ErrorMessage = $"Venta with ID '{ventaId}' not found",
                        Venta = null
                    };
                }

                _logger.LogInformation("✅ Retrieved venta: {VentaId}, Propiedad: {PropiedadId}, Estado: {Estado}", 
                    ventaId, venta.PropiedadId, venta.EstadoActual);

                return new GetCasaVentaByIdResult
                {
                    Success = true,
                    Venta = venta,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetCasaVentaByIdResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Venta = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving venta");
                return new GetCasaVentaByIdResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Venta = null
                };
            }
        }

        /// <summary>
        /// Actualiza el estado de una venta (con tracking de fechas por estado)
        /// </summary>
        /// <param name="ventaId">ID de la venta a actualizar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <param name="nuevoEstado">Nuevo estado de la venta</param>
        /// <returns>Resultado de la operación de actualización</returns>
        public async Task<UpdateCasaVentaEstadoResult> UpdateEstadoAgendarVentaAsync(string ventaId, string twinId, 
            bool quiereAgendar, string CusotmerComents)
        {
            if (string.IsNullOrEmpty(ventaId))
            {
                return new UpdateCasaVentaEstadoResult
                {
                    Success = false,
                    ErrorMessage = "Venta ID cannot be null or empty",
                    VentaActualizada = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new UpdateCasaVentaEstadoResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    VentaActualizada = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Primero obtener la venta existente
                var getResult = await GetVentaByIdAsync(ventaId, twinId);
                
                if (!getResult.Success || getResult.Venta == null)
                {
                    _logger.LogWarning("⚠️ Venta not found for update: {VentaId}", ventaId);
                    return new UpdateCasaVentaEstadoResult
                    {
                        Success = false,
                        ErrorMessage = $"Venta with ID '{ventaId}' not found",
                        VentaActualizada = null
                    };
                }

                var venta = getResult.Venta;
                var now = DateTime.UtcNow;

                // Actualizar campos
                venta.ClienteQuiereComprar = quiereAgendar;
                venta.NotasCliente = CusotmerComents ?? string.Empty;

                _logger.LogInformation("🔄 Actualizando venta {VentaId}: ClienteQuiereComprar={QuiereAgendar}", 
                    ventaId, quiereAgendar);

                // Serializar y adicionar metadatos de actualización
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(venta));
                documentData["type"] = "casa_venta";
                documentData["clienteQuiereComprar"] = quiereAgendar;
                documentData["notasCliente"] = CusotmerComents ?? string.Empty;
                documentData["fechaActualizacion"] = now;

                var updateResponse = await container.UpsertItemAsync(documentData, new PartitionKey(twinId));

                _logger.LogInformation("✅ Venta actualizada exitosamente. Document ID: {DocumentId}, Propiedad: {PropiedadId}", 
                    ventaId, venta.PropiedadId);

                return new UpdateCasaVentaEstadoResult
                {
                    Success = true,
                    VentaActualizada = venta,
                    RUConsumed = updateResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new UpdateCasaVentaEstadoResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    VentaActualizada = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating venta estado");
                return new UpdateCasaVentaEstadoResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    VentaActualizada = null
                };
            }
        }

        /// <summary>
        /// Actualiza el estado de una venta (con tracking de fechas por estado)
        /// </summary>
        /// <param name="ventaId">ID de la venta a actualizar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <param name="nuevoEstado">Nuevo estado de la venta</param>
        /// <returns>Resultado de la operación de actualización</returns>
        public async Task<UpdateCasaVentaEstadoResult> UpdateEstadoVentaAsync(string ventaId, string twinId, EstadoVenta nuevoEstado)
        {
            if (string.IsNullOrEmpty(ventaId))
            {
                return new UpdateCasaVentaEstadoResult
                {
                    Success = false,
                    ErrorMessage = "Venta ID cannot be null or empty",
                    VentaActualizada = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new UpdateCasaVentaEstadoResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    VentaActualizada = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Primero obtener la venta existente
                var getResult = await GetVentaByIdAsync(ventaId, twinId);
                
                if (!getResult.Success || getResult.Venta == null)
                {
                    _logger.LogWarning("⚠️ Venta not found for update: {VentaId}", ventaId);
                    return new UpdateCasaVentaEstadoResult
                    {
                        Success = false,
                        ErrorMessage = $"Venta with ID '{ventaId}' not found",
                        VentaActualizada = null
                    };
                }

                var venta = getResult.Venta;
                var now = DateTime.UtcNow;

                // Actualizar estado y fecha correspondiente
                venta.EstadoActual = nuevoEstado.ToString();

                // Actualizar la fecha según el estado
                switch (nuevoEstado)
                {
                    case EstadoVenta.Prospectada:
                        venta.FechaProspectada = now;
                        break;
                    case EstadoVenta.Aceptada:
                        venta.FechaAceptada = now;
                        break;
                    case EstadoVenta.Agendada:
                        venta.FechaAgendada = now;
                        break;
                    case EstadoVenta.Visitada:
                        venta.FechaVisitada = now;
                        break;
                    case EstadoVenta.Vendida:
                        venta.FechaVendida = now;
                        break;
                    case EstadoVenta.Pagada:
                        venta.FechaPagada = now;
                        break;
                }

                _logger.LogInformation("🔄 Actualizando venta {VentaId}: Estado={Estado}", ventaId, nuevoEstado);

                // Serializar y adicionar metadatos de actualización
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(venta));
                documentData["type"] = "casa_venta";
                documentData["fechaActualizacion"] = now;

                var updateResponse = await container.UpsertItemAsync(documentData, new PartitionKey(twinId));

                _logger.LogInformation("✅ Venta actualizada exitosamente. Document ID: {DocumentId}, Estado: {Estado}", 
                    ventaId, nuevoEstado);

                return new UpdateCasaVentaEstadoResult
                {
                    Success = true,
                    VentaActualizada = venta,
                    RUConsumed = updateResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new UpdateCasaVentaEstadoResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    VentaActualizada = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating venta estado");
                return new UpdateCasaVentaEstadoResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    VentaActualizada = null
                };
            }
        }

        /// <summary>
        /// Actualiza una venta completa
        /// </summary>
        /// <param name="ventaId">ID de la venta a actualizar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <param name="ventaActualizada">Datos actualizados de la venta</param>
        /// <returns>Resultado de la operación de actualización</returns>
        public async Task<UpdateCasaVentaResult> UpdateVentaAsync(string ventaId, string twinId, CasaVenta ventaActualizada)
        {
            if (string.IsNullOrEmpty(ventaId))
            {
                return new UpdateCasaVentaResult
                {
                    Success = false,
                    ErrorMessage = "Venta ID cannot be null or empty",
                    VentaActualizada = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new UpdateCasaVentaResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    VentaActualizada = null
                };
            }

            if (ventaActualizada == null)
            {
                return new UpdateCasaVentaResult
                {
                    Success = false,
                    ErrorMessage = "Updated venta data cannot be null",
                    VentaActualizada = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Verificar que la venta existe
                var getResult = await GetVentaByIdAsync(ventaId, twinId);
                
                if (!getResult.Success || getResult.Venta == null)
                {
                    _logger.LogWarning("⚠️ Venta not found for update: {VentaId}", ventaId);
                    return new UpdateCasaVentaResult
                    {
                        Success = false,
                        ErrorMessage = $"Venta with ID '{ventaId}' not found",
                        VentaActualizada = null
                    };
                }

                var ventaExistente = getResult.Venta;

                // Mantener valores inmutables
                ventaActualizada.Id = ventaId;
                ventaActualizada.TwinID = twinId;
                ventaActualizada.FechaRegistro = ventaExistente.FechaRegistro; // Mantener fecha de registro original

                // Serializar y adicionar metadatos de actualización
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(ventaActualizada));
                documentData["type"] = "casa_venta";
                documentData["fechaActualizacion"] = DateTime.UtcNow;

                var updateResponse = await container.UpsertItemAsync(documentData, new PartitionKey(twinId));

                _logger.LogInformation("✅ Venta updated successfully. Document ID: {DocumentId}, Propiedad: {PropiedadId}", 
                    ventaId, ventaActualizada.PropiedadId);

                return new UpdateCasaVentaResult
                {
                    Success = true,
                    VentaActualizada = ventaActualizada,
                    RUConsumed = updateResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new UpdateCasaVentaResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    VentaActualizada = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating venta");
                return new UpdateCasaVentaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    VentaActualizada = null
                };
            }
        }

        /// <summary>
        /// Elimina una venta de Cosmos DB
        /// </summary>
        /// <param name="ventaId">ID de la venta a eliminar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Resultado de la operación de eliminación</returns>
        public async Task<DeleteCasaVentaResult> DeleteVentaAsync(string ventaId, string twinId)
        {
            if (string.IsNullOrEmpty(ventaId))
            {
                return new DeleteCasaVentaResult
                {
                    Success = false,
                    ErrorMessage = "Venta ID cannot be null or empty"
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new DeleteCasaVentaResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Primero obtener la venta para registrar información antes de eliminar
                var getResult = await GetVentaByIdAsync(ventaId, twinId);
                
                if (!getResult.Success || getResult.Venta == null)
                {
                    string errorMessage = $"Venta with ID '{ventaId}' not found";
                    _logger.LogWarning("⚠️ {ErrorMessage}", errorMessage);
                    
                    return new DeleteCasaVentaResult
                    {
                        Success = false,
                        ErrorMessage = errorMessage,
                        VentaId = ventaId
                    };
                }

                var venta = getResult.Venta;

                // Eliminar la venta
                var deleteResponse = await container.DeleteItemAsync<CasaVenta>(ventaId, new PartitionKey(twinId));

                _logger.LogInformation("✅ Venta deleted successfully. Document ID: {DocumentId}, TwinID: {TwinId}, Propiedad: {PropiedadId}", 
                    ventaId, twinId, venta.PropiedadId);

                return new DeleteCasaVentaResult
                {
                    Success = true,
                    VentaId = ventaId,
                    RUConsumed = deleteResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow,
                    Message = $"Venta de propiedad '{venta.PropiedadId}' eliminada exitosamente"
                };
            }
            catch (CosmosException cosmosEx) when (cosmosEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                string errorMessage = $"Venta with ID '{ventaId}' not found";
                _logger.LogWarning("⚠️ {ErrorMessage}", errorMessage);

                return new DeleteCasaVentaResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    VentaId = ventaId
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new DeleteCasaVentaResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    VentaId = ventaId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting venta");
                return new DeleteCasaVentaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    VentaId = ventaId
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
    /// Estados posibles de una venta de casa
    /// </summary>
    public enum EstadoVenta
    {
        Prospectada,
        Aceptada,
        Agendada,
        Visitada,
        Vendida,
        Pagada
    }

    /// <summary>
    /// Modelo para representar una venta de casa con tracking de estados
    /// </summary>
    public class CasaVenta
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("TwinID")]
        public string TwinID { get; set; } = string.Empty;

        [JsonProperty("microsoftOID")]
        public string MicrosoftOID { get; set; } = string.Empty;

        [JsonProperty("clienteCompradorId")]
        public string ClienteCompradorId { get; set; } = string.Empty;

        [JsonProperty("clienteDuenoId")]
        public string ClienteDuenoId { get; set; } = string.Empty;

        [JsonProperty("propiedadId")]
        public string PropiedadId { get; set; } = string.Empty;

        [JsonProperty("estadoActual")]
        public string EstadoActual { get; set; } = string.Empty;

        [JsonProperty("clienteQuiereComprar")]
        public bool ClienteQuiereComprar { get; set; } = false;

        // Fechas por cada estado
        [JsonProperty("fechaProspectada")]
        public DateTime FechaProspectada { get; set; }

        [JsonProperty("fechaAceptada")]
        public DateTime FechaAceptada { get; set; }

        [JsonProperty("fechaAgendada")]
        public DateTime FechaAgendada { get; set; }

        [JsonProperty("fechaVisitada")]
        public DateTime FechaVisitada { get; set; }

        [JsonProperty("fechaVendida")]
        public DateTime FechaVendida { get; set; }

        [JsonProperty("fechaPagada")]
        public DateTime FechaPagada { get; set; }

        [JsonProperty("fechaRegistro")]
        public DateTime FechaRegistro { get; set; }

        [JsonProperty("notas")]
        public string Notas { get; set; } = string.Empty;

        [JsonProperty("notasCliente")]
        public string NotasCliente { get; set; } = string.Empty;
    }

    /// <summary>
    /// Resultado de guardar una venta
    /// </summary>
    public class SaveCasaVentaResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string DocumentId { get; set; }
        public string TwinId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener múltiples ventas
    /// </summary>
    public class GetCasasVentaResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string TwinId { get; set; } = "";
        public string MicrosoftOID { get; set; } = "";
        public List<CasaVenta> Ventas { get; set; } = new();
        public int VentaCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener una venta por ID
    /// </summary>
    public class GetCasaVentaByIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public CasaVenta Venta { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de actualizar estado de venta
    /// </summary>
    public class UpdateCasaVentaEstadoResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public CasaVenta VentaActualizada { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de actualizar una venta
    /// </summary>
    public class UpdateCasaVentaResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public CasaVenta VentaActualizada { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de eliminar una venta
    /// </summary>
    public class DeleteCasaVentaResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Message { get; set; } = "";
        public string VentaId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

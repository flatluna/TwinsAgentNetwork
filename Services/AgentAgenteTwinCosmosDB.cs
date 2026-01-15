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
    /// Servicio para gestionar agentes inmobiliarios (Real Estate Agents) en Azure Cosmos DB
    /// Database: twinmicasadb
    /// Container: twinagenteinmobiliario
    /// Partition Key: /TwinID
    /// </summary>
    public class AgentAgenteTwinCosmosDB
    {
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "twinagenteinmobiliario";
        private CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AgentAgenteTwinCosmosDB> _logger;

        public AgentAgenteTwinCosmosDB(ILogger<AgentAgenteTwinCosmosDB> logger, IConfiguration configuration = null)
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
                    
                    _logger.LogInformation("✅ Successfully connected to Agente Inmobiliario Cosmos DB");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error connecting to Cosmos DB");
                    throw;
                }
            }
        }

        /// <summary>
        /// Guarda o actualiza un agente inmobiliario en Cosmos DB
        /// </summary>
        /// <param name="agente">Datos del agente inmobiliario a guardar</param>
        /// <param name="twinID">ID del twin (propietario del agente)</param>
        /// <param name="agenteId">ID del agente (opcional - se genera si no se proporciona)</param>
        /// <returns>Resultado de la operación con ID del documento creado/actualizado</returns>
        public async Task<SaveAgenteInmobiliarioResult> SaveAgenteInmobiliarioAsync(
            AgenteInmobiliarioRequest agente, 
            string twinID, 
            string agenteId = null)
        {
            if (agente == null)
            {
                return new SaveAgenteInmobiliarioResult
                {
                    Success = false,
                    ErrorMessage = "Agente data cannot be null",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(twinID))
            {
                return new SaveAgenteInmobiliarioResult
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

                // Generar ID único si no se proporciona
                string documentId = string.IsNullOrEmpty(agenteId) ? Guid.NewGuid().ToString() : agenteId;

                // Serializar y adicionar metadatos
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(agente));
                
                documentData["id"] = documentId;
                documentData["TwinID"] = twinID;
                documentData["type"] = "agente_inmobiliario";
                documentData["fechaGuardado"] = DateTime.UtcNow;
                
                if (string.IsNullOrEmpty(agenteId))
                {
                    documentData["fechaRegistro"] = DateTime.UtcNow;
                }
                else
                {
                    documentData["fechaActualizacion"] = DateTime.UtcNow;
                }

                // Usar Upsert para crear o actualizar
                var response = await container.UpsertItemAsync(documentData, new PartitionKey(twinID));

                _logger.LogInformation("✅ Agente inmobiliario saved successfully. Document ID: {DocumentId}, Nombre: {Nombre}", 
                    documentId, agente.NombreEquipoAgente);

                return new SaveAgenteInmobiliarioResult
                {
                    Success = true,
                    DocumentId = documentId,
                    RUConsumed = response.RequestCharge,
                    Timestamp = DateTime.UtcNow,
                    Message = string.IsNullOrEmpty(agenteId) 
                        ? "Agente inmobiliario creado exitosamente" 
                        : "Agente inmobiliario actualizado exitosamente"
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new SaveAgenteInmobiliarioResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    DocumentId = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving agente inmobiliario");
                return new SaveAgenteInmobiliarioResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = null
                };
            }
        }

        /// <summary>
        /// Obtiene todos los agentes inmobiliarios para un TwinID específico
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <returns>Lista de agentes inmobiliarios encontrados</returns>
        public async Task<GetAgentesInmobiliariosResult> GetAgentesInmobiliariosByTwinIdAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetAgentesInmobiliariosResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Agentes = new List<AgenteInmobiliarioRequest>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.fechaRegistro DESC";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<AgenteInmobiliarioRequest> feed = container.GetItemQueryIterator<AgenteInmobiliarioRequest>(queryDefinition);

                var agentes = new List<AgenteInmobiliarioRequest>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<AgenteInmobiliarioRequest> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var agente in response)
                    {
                        agentes.Add(agente);
                        count++;
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} agentes inmobiliarios for TwinID: {TwinId}. RU consumed: {RU:F2}", 
                    count, twinId, totalRU);

                return new GetAgentesInmobiliariosResult
                {
                    Success = true,
                    Agentes = agentes,
                    AgenteCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetAgentesInmobiliariosResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Agentes = new List<AgenteInmobiliarioRequest>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving agentes inmobiliarios");
                return new GetAgentesInmobiliariosResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Agentes = new List<AgenteInmobiliarioRequest>()
                };
            }
        }

        /// <summary>
        /// Obtiene un agente inmobiliario específico por ID
        /// </summary>
        /// <param name="agenteId">ID del agente</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Datos del agente inmobiliario encontrado</returns>
        public async Task<GetAgenteInmobiliarioByIdResult> GetAgenteInmobiliarioByIdAsync(string agenteId, string twinId)
        {
            if (string.IsNullOrEmpty(agenteId))
            {
                return new GetAgenteInmobiliarioByIdResult
                {
                    Success = false,
                    ErrorMessage = "Agente ID cannot be null or empty",
                    Agente = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new GetAgenteInmobiliarioByIdResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Agente = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.id = @agenteId AND c.TwinID = @twinId";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@agenteId", agenteId)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<AgenteInmobiliarioRequest> feed = container.GetItemQueryIterator<AgenteInmobiliarioRequest>(queryDefinition);

                AgenteInmobiliarioRequest agente = null;
                double totalRU = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<AgenteInmobiliarioRequest> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    if (response.Count > 0)
                    {
                        agente = response.FirstOrDefault();
                        break;
                    }
                }

                if (agente == null)
                {
                    _logger.LogWarning("⚠️ Agente inmobiliario not found: {AgenteId}", agenteId);
                    return new GetAgenteInmobiliarioByIdResult
                    {
                        Success = false,
                        ErrorMessage = $"Agente inmobiliario with ID '{agenteId}' not found",
                        Agente = null
                    };
                }

                // Generar SAS URL para el logo desde Data Lake
                if (_configuration != null)
                {
                    try
                    {
                        _logger.LogInformation("📸 Generating SAS URL for Logo at path: Design/Images/Logo.png");
                        
                        // Crear DataLakeClient factory
                        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                        var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                        var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                        
                        // Path del logo: Design/Images/Logo.png
                        const string logoPath = "Design/Images/Logo.png";
                        
                        // Generar SAS URL con 24 horas de expiración
                        var logoSasUrl = await dataLakeClient.GenerateSasUrlAsync(logoPath, TimeSpan.FromHours(24));
                        
                        if (!string.IsNullOrEmpty(logoSasUrl))
                        {
                            // Limpiar la lista y agregar el logo URL
                            agente.LogoURL = logoSasUrl ;
                            _logger.LogInformation("✅ SAS URL generated successfully for logo");
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to generate SAS URL for logo - file may not exist");
                            agente.LogoURL = "";
                        }
                    }
                    catch (Exception sasEx)
                    {
                        _logger.LogWarning(sasEx, "⚠️ Error generating SAS URL for logo: {Message}", sasEx.Message);
                        // Continue without SAS URL - don't fail the entire operation
                        agente.LogoURL = "";
                    }
                }

                _logger.LogInformation("✅ Retrieved agente inmobiliario: {AgenteId}, Nombre: {Nombre}", 
                    agenteId, agente.NombreEquipoAgente);

                return new GetAgenteInmobiliarioByIdResult
                {
                    Success = true,
                    Agente = agente,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetAgenteInmobiliarioByIdResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Agente = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving agente inmobiliario");
                return new GetAgenteInmobiliarioByIdResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Agente = null
                };
            }
        }

        /// <summary>
        /// Obtiene un agente inmobiliario por MicrosoftOID
        /// </summary>
        /// <param name="microsoftOID">Microsoft Object ID del agente</param>
        /// <returns>Datos del agente inmobiliario encontrado</returns>
        public async Task<GetAgenteInmobiliarioByMicrosoftOIDResult> GetAgenteInmobiliarioByMicrosoftOIDAsync(string microsoftOID)
        {
            if (string.IsNullOrEmpty(microsoftOID))
            {
                return new GetAgenteInmobiliarioByMicrosoftOIDResult
                {
                    Success = false,
                    ErrorMessage = "MicrosoftOID cannot be null or empty",
                    Agente = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query para obtener agente solo por MicrosoftOID (sin TwinID)
                string query = "SELECT * FROM c WHERE c.microsoftOID = @microsoftOID";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@microsoftOID", microsoftOID);

                using FeedIterator<AgenteInmobiliarioRequest> feed = container.GetItemQueryIterator<AgenteInmobiliarioRequest>(queryDefinition);

                AgenteInmobiliarioRequest agente = null;
                double totalRU = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<AgenteInmobiliarioRequest> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    if (response.Count > 0)
                    {
                        agente = response.FirstOrDefault();
                        break; // Solo debería haber un agente con este MicrosoftOID
                    }
                }

                if (agente == null)
                {
                    _logger.LogWarning("⚠️ Agente inmobiliario not found for MicrosoftOID: {MicrosoftOID}", microsoftOID);
                    return new GetAgenteInmobiliarioByMicrosoftOIDResult
                    {
                        Success = false,
                        ErrorMessage = $"Agente inmobiliario with MicrosoftOID '{microsoftOID}' not found",
                        Agente = null
                    };
                }

                // Obtener el TwinID del agente encontrado para generar el SAS URL
                string twinId = agente.Id; // Usar el Id del documento que debería contener información del twin
                
                // Intentar extraer TwinID del contexto si está disponible
                // Buscar en la base de datos usando una query cross-partition
                try
                {
                    // Generar SAS URL para el logo desde Data Lake usando el TwinID del agente
                    if (_configuration != null && !string.IsNullOrEmpty(agente.Id))
                    {
                        // El TwinID debe estar en la partitionKey o podemos buscarlo
                        // Para Cosmos DB, necesitamos hacer una query para obtener el TwinID
                        string twinIdQuery = "SELECT c.TwinID FROM c WHERE c.microsoftOID = @microsoftOID";
                        var twinIdQueryDef = new QueryDefinition(twinIdQuery)
                            .WithParameter("@microsoftOID", microsoftOID);

                        using var twinIdFeed = container.GetItemQueryIterator<dynamic>(twinIdQueryDef);
                        if (twinIdFeed.HasMoreResults)
                        {
                            var twinIdResponse = await twinIdFeed.ReadNextAsync();
                            if (twinIdResponse.Count > 0)
                            {
                                var firstItem = twinIdResponse.FirstOrDefault();
                                twinId = firstItem?.TwinID?.ToString();
                            }
                        }

                        if (!string.IsNullOrEmpty(twinId))
                        {
                            try
                            {
                                _logger.LogInformation("📸 Generating SAS URL for Logo at path: Design/Images/Logo.png for TwinID: {TwinId}", twinId);
                                
                                // Crear DataLakeClient factory
                                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                                var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                                
                                // Path del logo: Design/Images/Logo.png
                                const string logoPath = "Design/Images/Logo.png";
                                
                                // Generar SAS URL con 24 horas de expiración
                                var logoSasUrl = await dataLakeClient.GenerateSasUrlAsync(logoPath, TimeSpan.FromHours(24));
                                
                                if (!string.IsNullOrEmpty(logoSasUrl))
                                {
                                    agente.LogoURL = logoSasUrl;
                                    _logger.LogInformation("✅ SAS URL generated successfully for logo");
                                }
                                else
                                {
                                    _logger.LogWarning("⚠️ Failed to generate SAS URL for logo - file may not exist");
                                    agente.LogoURL = "";
                                }
                            }
                            catch (Exception sasEx)
                            {
                                _logger.LogWarning(sasEx, "⚠️ Error generating SAS URL for logo: {Message}", sasEx.Message);
                                // Continue without SAS URL - don't fail the entire operation
                                agente.LogoURL = "";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Error processing TwinID or generating SAS URL: {Message}", ex.Message);
                    // Continue without SAS URL
                }

                _logger.LogInformation("✅ Retrieved agente inmobiliario for MicrosoftOID: {MicrosoftOID}, Nombre: {Nombre}. RU consumed: {RU:F2}", 
                    microsoftOID, agente.NombreEquipoAgente, totalRU);

                return new GetAgenteInmobiliarioByMicrosoftOIDResult
                {
                    Success = true,
                    Agente = agente,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetAgenteInmobiliarioByMicrosoftOIDResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Agente = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving agente inmobiliario by MicrosoftOID");
                return new GetAgenteInmobiliarioByMicrosoftOIDResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Agente = null
                };
            }
        }

        /// <summary>
        /// Obtiene un agente inmobiliario solo por el ID del documento
        /// </summary>
        /// <param name="agenteId">ID del documento en Cosmos DB</param>
        /// <returns>Datos del agente inmobiliario encontrado</returns>
        public async Task<GetAgenteInmobiliarioByDocumentIdResult> GetAgenteInmobiliarioByDocumentIdAsync(string agenteId)
        {
            if (string.IsNullOrEmpty(agenteId))
            {
                return new GetAgenteInmobiliarioByDocumentIdResult
                {
                    Success = false,
                    ErrorMessage = "Agente ID cannot be null or empty",
                    Agente = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query cross-partition - busca solo por el ID del documento
                string query = "SELECT * FROM c WHERE c.id = @agenteId";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@agenteId", agenteId);

                using FeedIterator<AgenteInmobiliarioRequest> feed = container.GetItemQueryIterator<AgenteInmobiliarioRequest>(queryDefinition);

                AgenteInmobiliarioRequest agente = null;
                double totalRU = 0;
                string twinId = null;

                while (feed.HasMoreResults)
                {
                    FeedResponse<AgenteInmobiliarioRequest> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    if (response.Count > 0)
                    {
                        agente = response.FirstOrDefault();
                        break;
                    }
                }

                if (agente == null)
                {
                    _logger.LogWarning("⚠️ Agente inmobiliario not found for Document ID: {AgenteId}", agenteId);
                    return new GetAgenteInmobiliarioByDocumentIdResult
                    {
                        Success = false,
                        ErrorMessage = $"Agente inmobiliario with ID '{agenteId}' not found",
                        Agente = null
                    };
                }

                // Obtener el TwinID del agente encontrado para generar el SAS URL
                try
                {
                    if (_configuration != null && !string.IsNullOrEmpty(agente.Id))
                    {
                        // Buscar el TwinID en el documento usando otra query
                        string twinIdQuery = "SELECT c.TwinID FROM c WHERE c.id = @agenteId";
                        var twinIdQueryDef = new QueryDefinition(twinIdQuery)
                            .WithParameter("@agenteId", agenteId);

                        using var twinIdFeed = container.GetItemQueryIterator<dynamic>(twinIdQueryDef);
                        if (twinIdFeed.HasMoreResults)
                        {
                            var twinIdResponse = await twinIdFeed.ReadNextAsync();
                            if (twinIdResponse.Count > 0)
                            {
                                var firstItem = twinIdResponse.FirstOrDefault();
                                twinId = firstItem?.TwinID?.ToString();
                            }
                        }

                        // Generar SAS URL para el logo si tenemos el TwinID
                        if (!string.IsNullOrEmpty(twinId))
                        {
                            try
                            {
                                _logger.LogInformation("📸 Generating SAS URL for Logo at path: Design/Images/Logo.png for TwinID: {TwinId}", twinId);
                                
                                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                                var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                                
                                const string logoPath = "Design/Images/Logo.png";
                                var logoSasUrl = await dataLakeClient.GenerateSasUrlAsync(logoPath, TimeSpan.FromHours(24));
                                
                                if (!string.IsNullOrEmpty(logoSasUrl))
                                {
                                    agente.LogoURL = logoSasUrl;
                                    _logger.LogInformation("✅ SAS URL generated successfully for logo");
                                }
                                else
                                {
                                    _logger.LogWarning("⚠️ Failed to generate SAS URL for logo - file may not exist");
                                    agente.LogoURL = "";
                                }
                            }
                            catch (Exception sasEx)
                            {
                                _logger.LogWarning(sasEx, "⚠️ Error generating SAS URL for logo: {Message}", sasEx.Message);
                                agente.LogoURL = "";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Error processing TwinID or generating SAS URL: {Message}", ex.Message);
                }

                _logger.LogInformation("✅ Retrieved agente inmobiliario by Document ID: {AgenteId}, Nombre: {Nombre}. RU consumed: {RU:F2}", 
                    agenteId, agente.NombreEquipoAgente, totalRU);

                return new GetAgenteInmobiliarioByDocumentIdResult
                {
                    Success = true,
                    Agente = agente,
                    TwinId = twinId,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetAgenteInmobiliarioByDocumentIdResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Agente = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving agente inmobiliario by Document ID");
                return new GetAgenteInmobiliarioByDocumentIdResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Agente = null
                };
            }
        }

        /// <summary>
        /// Actualiza un agente inmobiliario existente en Cosmos DB
        /// </summary>
        /// <param name="agenteId">ID del agente a actualizar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <param name="agenteActualizado">Datos actualizados del agente</param>
        /// <returns>Resultado de la operación de actualización</returns>
        public async Task<UpdateAgenteInmobiliarioResult> UpdateAgenteInmobiliarioAsync(
            string agenteId, 
            string twinId, 
            AgenteInmobiliarioRequest agenteActualizado)
        {
            // Reutilizar SaveAgenteInmobiliarioAsync con el ID existente
            var saveResult = await SaveAgenteInmobiliarioAsync(agenteActualizado, twinId, agenteId);

            return new UpdateAgenteInmobiliarioResult
            {
                Success = saveResult.Success,
                ErrorMessage = saveResult.ErrorMessage,
                AgenteActualizado = saveResult.Success ? agenteActualizado : null,
                RUConsumed = saveResult.RUConsumed,
                Timestamp = saveResult.Timestamp
            };
        }

        /// <summary>
        /// Elimina un agente inmobiliario de Cosmos DB
        /// </summary>
        /// <param name="agenteId">ID del agente a eliminar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Resultado de la operación de eliminación</returns>
        public async Task<DeleteAgenteInmobiliarioResult> DeleteAgenteInmobiliarioAsync(string agenteId, string twinId)
        {
            if (string.IsNullOrEmpty(agenteId))
            {
                return new DeleteAgenteInmobiliarioResult
                {
                    Success = false,
                    ErrorMessage = "Agente ID cannot be null or empty"
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new DeleteAgenteInmobiliarioResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                var deleteResponse = await container.DeleteItemAsync<AgenteInmobiliarioRequest>(agenteId, new PartitionKey(twinId));

                _logger.LogInformation("✅ Agente inmobiliario deleted successfully. Document ID: {DocumentId}", agenteId);

                return new DeleteAgenteInmobiliarioResult
                {
                    Success = true,
                    AgenteId = agenteId,
                    RUConsumed = deleteResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow,
                    Message = "Agente inmobiliario eliminado exitosamente"
                };
            }
            catch (CosmosException cosmosEx) when (cosmosEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                string errorMessage = $"Agente inmobiliario with ID '{agenteId}' not found";
                _logger.LogWarning("⚠️ {ErrorMessage}", errorMessage);

                return new DeleteAgenteInmobiliarioResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    AgenteId = agenteId
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new DeleteAgenteInmobiliarioResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    AgenteId = agenteId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting agente inmobiliario");
                return new DeleteAgenteInmobiliarioResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    AgenteId = agenteId
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
    /// DTO principal para agente inmobiliario (Real Estate Agent)
    /// </summary>
    public class AgenteInmobiliarioRequest
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty; 

        [JsonProperty("microsoftOID")] 
        public string MicrosoftOID { get; set; } = string.Empty;

        [JsonProperty("nombreEquipoAgente")]
        public string NombreEquipoAgente { get; set; } = string.Empty;

        [JsonProperty("empresaBroker")]
        public string EmpresaBroker { get; set; } = string.Empty;

        [JsonProperty("direccionZonaPrincipal")]
        public string DireccionZonaPrincipal { get; set; } = string.Empty;

        [JsonProperty("descripcionDetallada")]
        public string DescripcionDetallada { get; set; } = string.Empty;

        [JsonProperty("highlights")]
        public HighlightsAgente Highlights { get; set; } = new();

        [JsonProperty("mensajeCortoClientes")]
        public string MensajeCortoClientes { get; set; } = string.Empty;

        // Top-level contact fields (direct access)
        [JsonProperty("direccionFisicaOficina")]
        public string DireccionFisicaOficina { get; set; } = string.Empty;

        [JsonProperty("telefono")]
        public string Telefono { get; set; } = string.Empty;

        [JsonProperty("email")]
        public string Email { get; set; } = string.Empty;

        [JsonProperty("contacto")]
        public ContactoAgente Contacto { get; set; } = new();

        [JsonProperty("redes")]
        public RedesSociales Redes { get; set; } = new();

        [JsonProperty("especialidades")]
        public List<string> Especialidades { get; set; } = new();

        [JsonProperty("idiomas")]
        public List<string> Idiomas { get; set; } = new();


        [JsonProperty("logoURL")]
        public string  LogoURL { get; set; } 

        [JsonProperty("licencias")]
        public List<LicenciaAgente> Licencias { get; set; } = new();

        [JsonProperty("premiosReconocimientos")]
        public List<string> PremiosReconocimientos { get; set; } = new();

        [JsonProperty("activo")]
        public bool Activo { get; set; } = true;

        [JsonProperty("fechaRegistro")]
        public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

        [JsonProperty("fechaActualizacion")]
        public DateTime? FechaActualizacion { get; set; }
    }

    /// <summary>
    /// Highlights del agente (ventas, experiencia, premios)
    /// </summary>
    public class HighlightsAgente
    {
        [JsonProperty("ventasRecientes")]
        public int? VentasRecientes { get; set; }

        [JsonProperty("ticketMediano")]
        public string TicketMediano { get; set; } = string.Empty;

        [JsonProperty("rangoPreciosMinimo")]
        public string RangoPreciosMinimo { get; set; } = string.Empty;

        [JsonProperty("rangoPreciosMaximo")]
        public string RangoPreciosMaximo { get; set; } = string.Empty;

        [JsonProperty("anosExperiencia")]
        public int? AnosExperiencia { get; set; }

        [JsonProperty("reconocimientos")]
        public string Reconocimientos { get; set; } = string.Empty;

        [JsonProperty("calificacionPromedio")]
        public double? CalificacionPromedio { get; set; }

        [JsonProperty("numeroReviews")]
        public int? NumeroReviews { get; set; }
    }

    /// <summary>
    /// Información de contacto del agente
    /// </summary>
    public class ContactoAgente
    {
        [JsonProperty("telefono")]
        public string Telefono { get; set; } = string.Empty;


        [JsonProperty("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [JsonProperty("apelidoPaterno")]
        public string ApellidoPaterno { get; set; } = string.Empty;

        [JsonProperty("apelidoMaterno")]
        public string ApellidoMaterno { get; set; } = string.Empty;


        [JsonProperty("telefonoSecundario")]
        public string TelefonoSecundario { get; set; } = string.Empty;

        [JsonProperty("email")]
        public string Email { get; set; } = string.Empty;

        [JsonProperty("emailSecundario")]
        public string EmailSecundario { get; set; } = string.Empty;

        [JsonProperty("sitioWeb")]
        public string SitioWeb { get; set; } = string.Empty;

        [JsonProperty("direccionOficina")]
        public string DireccionOficina { get; set; } = string.Empty;

        [JsonProperty("horarioAtencion")]
        public string HorarioAtencion { get; set; } = string.Empty;
    }

    /// <summary>
    /// Redes sociales del agente
    /// </summary>
    public class RedesSociales
    {
        [JsonProperty("facebook")]
        public string Facebook { get; set; } = string.Empty;

        [JsonProperty("instagram")]
        public string Instagram { get; set; } = string.Empty;

        [JsonProperty("twitter")]
        public string Twitter { get; set; } = string.Empty;

        [JsonProperty("linkedin")]
        public string LinkedIN { get; set; } = string.Empty;

        [JsonProperty("youtube")]
        public string YouTube { get; set; } = string.Empty;

        [JsonProperty("tiktok")]
        public string TikTok { get; set; } = string.Empty;
    }

    /// <summary>
    /// Licencia del agente inmobiliario
    /// </summary>
    public class LicenciaAgente
    {
        [JsonProperty("numeroLicencia")]
        public string NumeroLicencia { get; set; } = string.Empty;

        [JsonProperty("estado")]
        public string Estado { get; set; } = string.Empty;

        [JsonProperty("fechaEmision")]
        public DateTime? FechaEmision { get; set; }

        [JsonProperty("fechaVencimiento")]
        public DateTime? FechaVencimiento { get; set; }

        [JsonProperty("activa")]
        public bool Activa { get; set; } = true;
    }

    /// <summary>
    /// Resultado de guardar un agente inmobiliario
    /// </summary>
    public class SaveAgenteInmobiliarioResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Message { get; set; } = "";
        public string DocumentId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener múltiples agentes inmobiliarios
    /// </summary>
    public class GetAgentesInmobiliariosResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<AgenteInmobiliarioRequest> Agentes { get; set; } = new();
        public int AgenteCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener un agente inmobiliario por ID
    /// </summary>
    public class GetAgenteInmobiliarioByIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public AgenteInmobiliarioRequest Agente { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener un agente inmobiliario por TwinID y MicrosoftOID
    /// </summary>
    public class GetAgenteInmobiliarioByTwinIdAndMicrosoftOIDResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public AgenteInmobiliarioRequest Agente { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener un agente inmobiliario por MicrosoftOID
    /// </summary>
    public class GetAgenteInmobiliarioByMicrosoftOIDResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public AgenteInmobiliarioRequest Agente { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de obtener un agente inmobiliario solo por el ID del documento
    /// </summary>
    public class GetAgenteInmobiliarioByDocumentIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public AgenteInmobiliarioRequest Agente { get; set; }
        public string TwinId { get; set; } = "";
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de actualizar un agente inmobiliario
    /// </summary>
    public class UpdateAgenteInmobiliarioResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public AgenteInmobiliarioRequest AgenteActualizado { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Resultado de eliminar un agente inmobiliario
    /// </summary>
    public class DeleteAgenteInmobiliarioResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Message { get; set; } = "";
        public string AgenteId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

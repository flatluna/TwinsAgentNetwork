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
    /// Servicio para gestionar conversaciones Hablemos en Azure Cosmos DB
    /// Database: twinmicasadb
    /// Container: twinhablemos
    /// Partition Key: /PairId (combinación de los dos TwinIDs)
    /// </summary>
    public class AgentHablemosCosmosDB
    {
        private readonly ILogger<AgentHablemosCosmosDB> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "twinhablemos";
        private CosmosClient _cosmosClient;

        public AgentHablemosCosmosDB(ILogger<AgentHablemosCosmosDB> logger, IConfiguration configuration)
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

                    _logger.LogInformation("✅ Successfully connected to Hablemos Cosmos DB");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error connecting to Cosmos DB");
                    throw;
                }
            }
        }

        /// <summary>
        /// Guarda un mensaje en la conversación
        /// Partition Key: /TwinID (dueño del app)
        /// Document ID: PairId (combinación de los dos usuarios)
        /// </summary>
        public async Task<GuardarMensajeResult> GuardarMensajeAsync(HablemosMessage mensaje)
        {
            if (mensaje == null)
            {
                return new GuardarMensajeResult
                {
                    Success = false,
                    ErrorMessage = "El mensaje no puede ser null"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                // Generar PairId (será el ID del documento)
                string pairId = AgentHablemos.GeneratePairId(
                    mensaje.ClientePrimeroID,
                    mensaje.ClienteSegundoID
                );

                _logger.LogInformation("💾 Guardando mensaje en conversación: {PairId}, TwinID: {TwinID}", 
                    pairId, mensaje.TwinID);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Intentar obtener la conversación existente por ID y Partition Key (TwinID)
                HablemosConversation conversation = null;
                try
                {
                    var response = await container.ReadItemAsync<HablemosConversation>(
                        pairId,                              // id del documento
                        new PartitionKey(mensaje.TwinID)     // Partition Key = TwinID del dueño
                    );
                    conversation = response.Resource;
                    _logger.LogInformation("📖 Conversación existente encontrada");
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Crear nueva conversación
                    conversation = new HablemosConversation
                    {
                        Id = pairId,                        // id = PairId
                        PairId = pairId,                    // Para referencia
                        ClientePrimeroID = mensaje.ClientePrimeroID, 
                        ClienteSegundoID = mensaje.ClienteSegundoID, 
                        TwinID = mensaje.TwinID,            // Partition Key
                        DuenoAppMicrosoftOID = mensaje.DuenoAppMicrosoftOID,
                        Mensajes = new List<HablemosMessage>(), 
                        CreatedAt = DateTime.UtcNow,
                        LastActivityAt = DateTime.UtcNow
                    };
                    _logger.LogInformation("🆕 Creando nueva conversación");
                }

                // Agregar el mensaje
                mensaje.IsDelivered = true;
                mensaje.DeliveredAt = DateTime.UtcNow;
                conversation.Mensajes.Add(mensaje);
                conversation.LastActivityAt = DateTime.UtcNow;

                // Guardar en Cosmos DB usando TwinID como Partition Key
                var upsertResponse = await container.UpsertItemAsync(
                    conversation,
                    new PartitionKey(mensaje.TwinID)
                );

                _logger.LogInformation("✅ Mensaje guardado exitosamente. RU consumed: {RU}",
                    upsertResponse.RequestCharge);

                return new GuardarMensajeResult
                {
                    Success = true,
                    MessageId = mensaje.MessageId,
                    PairId = pairId,
                    RUConsumed = upsertResponse.RequestCharge
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error guardando mensaje");
                return new GuardarMensajeResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Obtiene mensajes de una conversación filtrados por fecha
        /// Lee por ID (PairId) y Partition Key (TwinID)
        /// </summary>
        public async Task<ObtenerMensajesResult> ObtenerMensajesAsync(
            string clientePrimeroID,
            string clienteSegundoID,
            string twinID,
            DateTime fechaInicio,
            DateTime fechaFin)
        {
            if (string.IsNullOrEmpty(clientePrimeroID) || string.IsNullOrEmpty(clienteSegundoID))
            {
                return new ObtenerMensajesResult
                {
                    Success = false,
                    ErrorMessage = "Los IDs de ambos clientes son requeridos"
                };
            }

            if (string.IsNullOrEmpty(twinID))
            {
                return new ObtenerMensajesResult
                {
                    Success = false,
                    ErrorMessage = "TwinID es requerido"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                string pairId = AgentHablemos.GeneratePairId(clientePrimeroID, clienteSegundoID);

                _logger.LogInformation("📖 Obteniendo mensajes. PairId: {PairId}, TwinID: {TwinID}", 
                    pairId, twinID);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Leer por ID y Partition Key
                var response = await container.ReadItemAsync<HablemosConversation>(
                    pairId,                          // id del documento
                    new PartitionKey(twinID)         // Partition Key
                );

                var conversation = response.Resource;

                // Filtrar mensajes por fecha
                var mensajesFiltrados = conversation.Mensajes
                    .Where(m => m.FechaCreado >= fechaInicio && m.FechaCreado <= fechaFin)
                    .OrderBy(m => m.FechaCreado)
                    .ToList();

                _logger.LogInformation("✅ {Count} mensajes encontrados. RU consumed: {RU}",
                    mensajesFiltrados.Count, response.RequestCharge);

                return new ObtenerMensajesResult
                {
                    Success = true,
                    Mensajes = mensajesFiltrados,
                    PairId = pairId,
                    TotalMensajes = mensajesFiltrados.Count,
                    RUConsumed = response.RequestCharge
                };
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Conversación no encontrada");
                return new ObtenerMensajesResult
                {
                    Success = true,
                    Mensajes = new List<HablemosMessage>(),
                    TotalMensajes = 0,
                    Message = "No hay conversación entre estos usuarios"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo mensajes");
                return new ObtenerMensajesResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Obtiene todas las conversaciones de un usuario
        /// Busca por ambas combinaciones posibles del PairId (agenteId_clienteId y clienteId_agenteId)
        /// </summary>
        public async Task<ObtenerConversacionesResult> ObtenerConversacionesUsuarioAsync(
            string clienteID, 
            string agenteID,
            string twinID)
        {
            if (string.IsNullOrEmpty(clienteID))
            {
                return new ObtenerConversacionesResult
                {
                    Success = false,
                    ErrorMessage = "ClienteID es requerido"
                };
            }

            if (string.IsNullOrEmpty(agenteID))
            {
                return new ObtenerConversacionesResult
                {
                    Success = false,
                    ErrorMessage = "AgenteID es requerido"
                };
            }

            if (string.IsNullOrEmpty(twinID))
            {
                return new ObtenerConversacionesResult
                {
                    Success = false,
                    ErrorMessage = "TwinID es requerido"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("📖 Obteniendo conversaciones. ClienteID: {ClienteID}, AgenteID: {AgenteID}, TwinID: {TwinID}", 
                    clienteID, agenteID, twinID);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Generar ambas combinaciones posibles del PairId
                string pairId1 = $"{agenteID}_{clienteID}";  // agenteId_clienteId
                string pairId2 = $"{clienteID}_{agenteID}";  // clienteId_agenteId

                _logger.LogInformation("🔍 Buscando con PairIds: {PairId1} o {PairId2}", pairId1, pairId2);

                var conversaciones = new List<HablemosConversation>();
                double totalRU = 0;

                // Intentar buscar con la primera combinación (agenteId_clienteId)
                try
                {
                    var response1 = await container.ReadItemAsync<HablemosConversation>(
                        pairId1,
                        new PartitionKey(twinID)
                    );
                    conversaciones.Add(response1.Resource);
                    totalRU += response1.RequestCharge;
                    _logger.LogInformation("✅ Conversación encontrada con PairId: {PairId}", pairId1);
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("ℹ️ No se encontró conversación con PairId: {PairId}", pairId1);
                }

                // Intentar buscar con la segunda combinación (clienteId_agenteId)
                try
                {
                    var response2 = await container.ReadItemAsync<HablemosConversation>(
                        pairId2,
                        new PartitionKey(twinID)
                    );
                    conversaciones.Add(response2.Resource);
                    totalRU += response2.RequestCharge;
                    _logger.LogInformation("✅ Conversación encontrada con PairId: {PairId}", pairId2);
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("ℹ️ No se encontró conversación con PairId: {PairId}", pairId2);
                }

                // Ordenar por última actividad
                conversaciones = conversaciones
                    .OrderByDescending(c => c.LastActivityAt)
                    .ToList();

                _logger.LogInformation("✅ {Count} conversación(es) encontrada(s). RU consumed: {RU}",
                    conversaciones.Count, totalRU);

                return new ObtenerConversacionesResult
                {
                    Success = true,
                    Conversaciones = conversaciones,
                    TotalConversaciones = conversaciones.Count,
                    RUConsumed = totalRU
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo conversaciones");
                return new ObtenerConversacionesResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Marca mensajes como leídos
        /// Lee por ID (PairId) y Partition Key (TwinID)
        /// </summary>
        public async Task<MarcarLeidosResult> MarcarMensajesComoLeidosAsync(
            string pairId,
            string twinID,
            List<string> messageIds,
            string leidoPor)
        {
            if (string.IsNullOrEmpty(pairId) || messageIds == null || messageIds.Count == 0)
            {
                return new MarcarLeidosResult
                {
                    Success = false,
                    ErrorMessage = "PairId y MessageIds son requeridos"
                };
            }

            if (string.IsNullOrEmpty(twinID))
            {
                return new MarcarLeidosResult
                {
                    Success = false,
                    ErrorMessage = "TwinID es requerido"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("✅ Marcando {Count} mensajes como leídos. PairId: {PairId}, TwinID: {TwinID}",
                    messageIds.Count, pairId, twinID);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Obtener la conversación por ID y Partition Key
                var response = await container.ReadItemAsync<HablemosConversation>(
                    pairId,                          // id del documento
                    new PartitionKey(twinID)         // Partition Key
                );

                var conversation = response.Resource;
                int marcados = 0;

                // Marcar mensajes como leídos
                foreach (var messageId in messageIds)
                {
                    var mensaje = conversation.Mensajes.FirstOrDefault(m => m.MessageId == messageId);
                    if (mensaje != null && mensaje.ParaQuien == leidoPor && !mensaje.IsRead)
                    {
                        mensaje.IsRead = true;
                        mensaje.ReadAt = DateTime.UtcNow;
                        marcados++;
                    }
                }

                if (marcados > 0)
                {
                    // Actualizar en Cosmos DB
                    var updateResponse = await container.UpsertItemAsync(
                        conversation,
                        new PartitionKey(twinID)
                    );

                    _logger.LogInformation("✅ {Count} mensajes marcados como leídos. RU consumed: {RU}",
                        marcados, updateResponse.RequestCharge);

                    return new MarcarLeidosResult
                    {
                        Success = true,
                        MarcadosCount = marcados,
                        RUConsumed = updateResponse.RequestCharge
                    };
                }

                return new MarcarLeidosResult
                {
                    Success = true,
                    MarcadosCount = 0,
                    Message = "No hay mensajes para marcar"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error marcando mensajes como leídos");
                return new MarcarLeidosResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Obtiene todos los mensajes de una conversación por ID directo
        /// Busca por ambas combinaciones posibles del PairId (agenteId_clienteId y clienteId_agenteId)
        /// No requiere TwinID porque busca el documento directamente
        /// </summary>
        public async Task<ObtenerMensajesResult> ObtenerMensajesPorIdAsync(
            string clienteID,
            string agenteInmueblesID)
        {
            if (string.IsNullOrEmpty(clienteID))
            {
                return new ObtenerMensajesResult
                {
                    Success = false,
                    ErrorMessage = "ClienteID es requerido"
                };
            }

            if (string.IsNullOrEmpty(agenteInmueblesID))
            {
                return new ObtenerMensajesResult
                {
                    Success = false,
                    ErrorMessage = "AgenteInmueblesID es requerido"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("📖 Obteniendo mensajes. ClienteID: {ClienteID}, AgenteID: {AgenteID}", 
                    clienteID, agenteInmueblesID);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Generar ambas combinaciones posibles del PairId
                string pairId1 = $"{agenteInmueblesID}_{clienteID}";  // agenteId_clienteId
                string pairId2 = $"{clienteID}_{agenteInmueblesID}";  // clienteId_agenteId

                _logger.LogInformation("🔍 Buscando mensajes con PairIds: {PairId1} o {PairId2}", pairId1, pairId2);

                HablemosConversation? conversation = null;
                double totalRU = 0;
                string pairIdEncontrado = string.Empty;

                // Intentar buscar con la primera combinación (agenteId_clienteId)
                try
                {
                    // Buscar en todas las particiones usando query
                    var queryDefinition1 = new QueryDefinition("SELECT * FROM c WHERE c.id = @pairId")
                        .WithParameter("@pairId", pairId1);

                    var iterator1 = container.GetItemQueryIterator<HablemosConversation>(queryDefinition1);

                    while (iterator1.HasMoreResults)
                    {
                        var response = await iterator1.ReadNextAsync();
                        totalRU += response.RequestCharge;
                        
                        if (response.Count > 0)
                        {
                            conversation = response.FirstOrDefault();
                            pairIdEncontrado = pairId1;
                            _logger.LogInformation("✅ Conversación encontrada con PairId: {PairId}", pairId1);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Error buscando con PairId: {PairId}", pairId1);
                }

                // Si no se encontró, intentar con la segunda combinación (clienteId_agenteId)
                if (conversation == null)
                {
                    try
                    {
                        var queryDefinition2 = new QueryDefinition("SELECT * FROM c WHERE c.id = @pairId")
                            .WithParameter("@pairId", pairId2);

                        var iterator2 = container.GetItemQueryIterator<HablemosConversation>(queryDefinition2);

                        while (iterator2.HasMoreResults)
                        {
                            var response = await iterator2.ReadNextAsync();
                            totalRU += response.RequestCharge;
                            
                            if (response.Count > 0)
                            {
                                conversation = response.FirstOrDefault();
                                pairIdEncontrado = pairId2;
                                _logger.LogInformation("✅ Conversación encontrada con PairId: {PairId}", pairId2);
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error buscando con PairId: {PairId}", pairId2);
                    }
                }

                // Si no se encontró ninguna conversación
                if (conversation == null)
                {
                    _logger.LogWarning("⚠️ No se encontró conversación entre {ClienteID} y {AgenteID}", 
                        clienteID, agenteInmueblesID);
                    
                    return new ObtenerMensajesResult
                    {
                        Success = true,
                        Mensajes = new List<HablemosMessage>(),
                        PairId = string.Empty,
                        TotalMensajes = 0,
                        RUConsumed = totalRU,
                        Message = "No hay conversación entre estos usuarios"
                    };
                }

                // Retornar todos los mensajes ordenados por fecha
                var mensajesOrdenados = conversation.Mensajes
                    .OrderBy(m => m.FechaCreado)
                    .ToList();

                _logger.LogInformation("✅ {Count} mensajes encontrados. RU consumed: {RU}",
                    mensajesOrdenados.Count, totalRU);

                return new ObtenerMensajesResult
                {
                    Success = true,
                    Mensajes = mensajesOrdenados,
                    PairId = pairIdEncontrado,
                    TotalMensajes = mensajesOrdenados.Count,
                    RUConsumed = totalRU
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo mensajes");
                return new ObtenerMensajesResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Obtiene mensajes de una conversación por ID directo CON FILTRO DE FECHAS
        /// Busca por ambas combinaciones posibles del PairId (agenteId_clienteId y clienteId_agenteId)
        /// No requiere TwinID porque busca el documento directamente
        /// </summary>
        public async Task<ObtenerMensajesResult> ObtenerMensajesPorIdConFechasAsync(
            string clienteID,
            string agenteInmueblesID,
            DateTime fromDate,
            DateTime toDate)
        {
            if (string.IsNullOrEmpty(clienteID))
            {
                return new ObtenerMensajesResult
                {
                    Success = false,
                    ErrorMessage = "ClienteID es requerido"
                };
            }

            if (string.IsNullOrEmpty(agenteInmueblesID))
            {
                return new ObtenerMensajesResult
                {
                    Success = false,
                    ErrorMessage = "AgenteInmueblesID es requerido"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("📖 Obteniendo mensajes con filtro de fechas. ClienteID: {ClienteID}, AgenteID: {AgenteID}, From: {FromDate}, To: {ToDate}", 
                    clienteID, agenteInmueblesID, fromDate, toDate);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Generar ambas combinaciones posibles del PairId
                string pairId1 = $"{agenteInmueblesID}_{clienteID}";  // agenteId_clienteId
                string pairId2 = $"{clienteID}_{agenteInmueblesID}";  // clienteId_agenteId

                _logger.LogInformation("🔍 Buscando mensajes con PairIds: {PairId1} o {PairId2}", pairId1, pairId2);

                HablemosConversation? conversation = null;
                double totalRU = 0;
                string pairIdEncontrado = string.Empty;

                // Intentar buscar con la primera combinación (agenteId_clienteId)
                try
                {
                    // Buscar en todas las particiones usando query
                    var queryDefinition1 = new QueryDefinition("SELECT * FROM c WHERE c.id = @pairId")
                        .WithParameter("@pairId", pairId1);

                    var iterator1 = container.GetItemQueryIterator<HablemosConversation>(queryDefinition1);

                    while (iterator1.HasMoreResults)
                    {
                        var response = await iterator1.ReadNextAsync();
                        totalRU += response.RequestCharge;
                        
                        if (response.Count > 0)
                        {
                            conversation = response.FirstOrDefault();
                            pairIdEncontrado = pairId1;
                            _logger.LogInformation("✅ Conversación encontrada con PairId: {PairId}", pairId1);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Error buscando con PairId: {PairId}", pairId1);
                }

                // Si no se encontró, intentar con la segunda combinación (clienteId_agenteId)
                if (conversation == null)
                {
                    try
                    {
                        var queryDefinition2 = new QueryDefinition("SELECT * FROM c WHERE c.id = @pairId")
                            .WithParameter("@pairId", pairId2);

                        var iterator2 = container.GetItemQueryIterator<HablemosConversation>(queryDefinition2);

                        while (iterator2.HasMoreResults)
                        {
                            var response = await iterator2.ReadNextAsync();
                            totalRU += response.RequestCharge;
                            
                            if (response.Count > 0)
                            {
                                conversation = response.FirstOrDefault();
                                pairIdEncontrado = pairId2;
                                _logger.LogInformation("✅ Conversación encontrada con PairId: {PairId}", pairId2);
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error buscando con PairId: {PairId}", pairId2);
                    }
                }

                // Si no se encontró ninguna conversación
                if (conversation == null)
                {
                    _logger.LogWarning("⚠️ No se encontró conversación entre {ClienteID} y {AgenteID}", 
                        clienteID, agenteInmueblesID);
                    
                    return new ObtenerMensajesResult
                    {
                        Success = true,
                        Mensajes = new List<HablemosMessage>(),
                        PairId = string.Empty,
                        TotalMensajes = 0,
                        RUConsumed = totalRU,
                        Message = "No hay conversación entre estos usuarios"
                    };
                }

                // Filtrar mensajes por rango de fechas y ordenar
                var mensajesFiltrados = conversation.Mensajes
                    .Where(m => m.FechaCreado >= fromDate && m.FechaCreado <= toDate)
                    .OrderBy(m => m.FechaCreado)
                    .ToList();

                _logger.LogInformation("✅ {Count} mensajes encontrados en el rango de fechas. RU consumed: {RU}",
                    mensajesFiltrados.Count, totalRU);

                return new ObtenerMensajesResult
                {
                    Success = true,
                    Mensajes = mensajesFiltrados,
                    PairId = pairIdEncontrado,
                    TotalMensajes = mensajesFiltrados.Count,
                    RUConsumed = totalRU
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo mensajes con filtro de fechas");
                return new ObtenerMensajesResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Edita un mensaje específico en una conversación
        /// Busca el mensaje por messageId y actualiza su contenido
        /// </summary>
        public async Task<EditarMensajeResult> EditarMensajeAsync(
            string clienteID,
            string agenteInmueblesID,
            string messageId,
            string nuevoMensaje)
        {
            if (string.IsNullOrEmpty(clienteID))
            {
                return new EditarMensajeResult
                {
                    Success = false,
                    ErrorMessage = "ClienteID es requerido"
                };
            }

            if (string.IsNullOrEmpty(agenteInmueblesID))
            {
                return new EditarMensajeResult
                {
                    Success = false,
                    ErrorMessage = "AgenteInmueblesID es requerido"
                };
            }

            if (string.IsNullOrEmpty(messageId))
            {
                return new EditarMensajeResult
                {
                    Success = false,
                    ErrorMessage = "MessageId es requerido"
                };
            }

            if (string.IsNullOrEmpty(nuevoMensaje))
            {
                return new EditarMensajeResult
                {
                    Success = false,
                    ErrorMessage = "El nuevo mensaje no puede estar vacío"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("✏️ Editando mensaje. ClienteID: {ClienteID}, AgenteID: {AgenteID}, MessageId: {MessageId}", 
                    clienteID, agenteInmueblesID, messageId);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Generar ambas combinaciones posibles del PairId
                string pairId1 = $"{agenteInmueblesID}_{clienteID}";  // agenteId_clienteId
                string pairId2 = $"{clienteID}_{agenteInmueblesID}";  // clienteId_agenteId

                _logger.LogInformation("🔍 Buscando conversación con PairIds: {PairId1} o {PairId2}", pairId1, pairId2);

                HablemosConversation? conversation = null;
                double totalRU = 0;
                string pairIdEncontrado = string.Empty;

                // Intentar buscar con la primera combinación (agenteId_clienteId)
                try
                {
                    var queryDefinition1 = new QueryDefinition("SELECT * FROM c WHERE c.id = @pairId")
                        .WithParameter("@pairId", pairId1);

                    var iterator1 = container.GetItemQueryIterator<HablemosConversation>(queryDefinition1);

                    while (iterator1.HasMoreResults)
                    {
                        var response = await iterator1.ReadNextAsync();
                        totalRU += response.RequestCharge;
                        
                        if (response.Count > 0)
                        {
                            conversation = response.FirstOrDefault();
                            pairIdEncontrado = pairId1;
                            _logger.LogInformation("✅ Conversación encontrada con PairId: {PairId}", pairId1);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Error buscando con PairId: {PairId}", pairId1);
                }

                // Si no se encontró, intentar con la segunda combinación (clienteId_agenteId)
                if (conversation == null)
                {
                    try
                    {
                        var queryDefinition2 = new QueryDefinition("SELECT * FROM c WHERE c.id = @pairId")
                            .WithParameter("@pairId", pairId2);

                        var iterator2 = container.GetItemQueryIterator<HablemosConversation>(queryDefinition2);

                        while (iterator2.HasMoreResults)
                        {
                            var response = await iterator2.ReadNextAsync();
                            totalRU += response.RequestCharge;
                            
                            if (response.Count > 0)
                            {
                                conversation = response.FirstOrDefault();
                                pairIdEncontrado = pairId2;
                                _logger.LogInformation("✅ Conversación encontrada con PairId: {PairId}", pairId2);
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error buscando con PairId: {PairId}", pairId2);
                    }
                }

                // Si no se encontró la conversación
                if (conversation == null)
                {
                    _logger.LogWarning("⚠️ No se encontró conversación entre {ClienteID} y {AgenteID}", 
                        clienteID, agenteInmueblesID);
                    
                    return new EditarMensajeResult
                    {
                        Success = false,
                        ErrorMessage = "No se encontró la conversación entre estos usuarios"
                    };
                }

                // Buscar el mensaje específico por MessageId
                var mensajeAEditar = conversation.Mensajes.FirstOrDefault(m => m.MessageId == messageId);

                if (mensajeAEditar == null)
                {
                    _logger.LogWarning("⚠️ Mensaje no encontrado. MessageId: {MessageId}", messageId);
                    
                    return new EditarMensajeResult
                    {
                        Success = false,
                        ErrorMessage = $"No se encontró el mensaje con ID: {messageId}"
                    };
                }

                // Guardar el mensaje original para retornarlo
                string mensajeOriginal = mensajeAEditar.Mensaje;

                // Editar el mensaje
                mensajeAEditar.Mensaje = nuevoMensaje;
                
                // Agregar metadata de edición (opcional, si quieres trackear esto)
                // Nota: necesitarías agregar estas propiedades a HablemosMessage si quieres usarlas
                // mensajeAEditar.IsEdited = true;
                // mensajeAEditar.EditedAt = DateTime.UtcNow;
                // mensajeAEditar.OriginalMessage = mensajeOriginal;

                // Actualizar LastActivityAt de la conversación
                conversation.LastActivityAt = DateTime.UtcNow;

                // Guardar en Cosmos DB
                var upsertResponse = await container.UpsertItemAsync(
                    conversation,
                    new PartitionKey(conversation.TwinID)
                );

                totalRU += upsertResponse.RequestCharge;

                _logger.LogInformation("✅ Mensaje editado exitosamente. MessageId: {MessageId}, RU consumed: {RU}",
                    messageId, totalRU);

                return new EditarMensajeResult
                {
                    Success = true,
                    MessageId = messageId,
                    PairId = pairIdEncontrado,
                    MensajeOriginal = mensajeOriginal,
                    MensajeEditado = nuevoMensaje,
                    RUConsumed = totalRU
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error editando mensaje");
                return new EditarMensajeResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Borra un mensaje específico de una conversación en Cosmos DB
        /// Busca automáticamente con ambas combinaciones posibles del PairId
        /// NO requiere TwinID - búsqueda cross-partition
        /// </summary>
        /// <param name="clienteID">ID del cliente</param>
        /// <param name="agenteInmueblesID">ID del agente inmobiliario</param>
        /// <param name="messageId">ID único del mensaje a borrar</param>
        /// <returns>Resultado de la operación con detalles del mensaje borrado</returns>
        public async Task<BorrarMensajeResult> BorrarMensajeAsync(
            string clienteID,
            string agenteInmueblesID,
            string messageId)
        {
            try
            {
                await InitializeCosmosClientAsync();

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Generar ambas combinaciones posibles del PairId
                string pairId1 = $"{agenteInmueblesID}_{clienteID}";  // agenteId_clienteId
                string pairId2 = $"{clienteID}_{agenteInmueblesID}";  // clienteId_agenteId

                _logger.LogInformation("🔍 Buscando conversación para borrar mensaje. PairId1: {PairId1}, PairId2: {PairId2}, MessageId: {MessageId}",
                    pairId1, pairId2, messageId);

                HablemosConversation? conversation = null;
                double totalRU = 0;
                string pairIdEncontrado = string.Empty;

                // Intentar buscar con la primera combinación (agenteId_clienteId)
                try
                {
                    var queryDefinition1 = new QueryDefinition("SELECT * FROM c WHERE c.id = @pairId")
                        .WithParameter("@pairId", pairId1);

                    var iterator1 = container.GetItemQueryIterator<HablemosConversation>(queryDefinition1);

                    while (iterator1.HasMoreResults)
                    {
                        var response = await iterator1.ReadNextAsync();
                        totalRU += response.RequestCharge;
                        
                        if (response.Count > 0)
                        {
                            conversation = response.FirstOrDefault();
                            pairIdEncontrado = pairId1;
                            _logger.LogInformation("✅ Conversación encontrada con PairId: {PairId}", pairId1);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Error buscando con PairId: {PairId}", pairId1);
                }

                // Si no se encontró, intentar con la segunda combinación (clienteId_agenteId)
                if (conversation == null)
                {
                    try
                    {
                        var queryDefinition2 = new QueryDefinition("SELECT * FROM c WHERE c.id = @pairId")
                            .WithParameter("@pairId", pairId2);

                        var iterator2 = container.GetItemQueryIterator<HablemosConversation>(queryDefinition2);

                        while (iterator2.HasMoreResults)
                        {
                            var response = await iterator2.ReadNextAsync();
                            totalRU += response.RequestCharge;
                            
                            if (response.Count > 0)
                            {
                                conversation = response.FirstOrDefault();
                                pairIdEncontrado = pairId2;
                                _logger.LogInformation("✅ Conversación encontrada con PairId: {PairId}", pairId2);
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error buscando con PairId: {PairId}", pairId2);
                    }
                }

                // Si no se encontró la conversación
                if (conversation == null)
                {
                    _logger.LogWarning("⚠️ No se encontró conversación entre {ClienteID} y {AgenteID}", 
                        clienteID, agenteInmueblesID);
                    
                    return new BorrarMensajeResult
                    {
                        Success = false,
                        ErrorMessage = "No se encontró la conversación entre estos usuarios"
                    };
                }

                // Buscar el mensaje específico por MessageId
                var mensajeABorrar = conversation.Mensajes.FirstOrDefault(m => m.MessageId == messageId);

                if (mensajeABorrar == null)
                {
                    _logger.LogWarning("⚠️ Mensaje no encontrado. MessageId: {MessageId}", messageId);
                    
                    return new BorrarMensajeResult
                    {
                        Success = false,
                        ErrorMessage = $"No se encontró el mensaje con ID: {messageId}"
                    };
                }

                // Guardar datos del mensaje antes de borrarlo
                string mensajeBorrado = mensajeABorrar.Mensaje;
                string deQuien = mensajeABorrar.DeQuien;
                string paraQuien = mensajeABorrar.ParaQuien;
                DateTime fechaCreado = mensajeABorrar.FechaCreado;

                // Borrar el mensaje de la lista
                conversation.Mensajes.Remove(mensajeABorrar);
                
                // Actualizar LastActivityAt
                conversation.LastActivityAt = DateTime.UtcNow;

                _logger.LogInformation("🗑️ Borrando mensaje. MessageId: {MessageId}, De: {DeQuien}, Para: {ParaQuien}",
                    messageId, deQuien, paraQuien);

                // Guardar la conversación actualizada (sin el mensaje)
                var updateResponse = await container.UpsertItemAsync(
                    conversation
                 
                );

                totalRU += updateResponse.RequestCharge;

                _logger.LogInformation("✅ Mensaje borrado exitosamente. RU consumed: {RU}", totalRU);

                return new BorrarMensajeResult
                {
                    Success = true,
                    MessageId = messageId,
                    PairId = pairIdEncontrado,
                    MensajeBorrado = mensajeBorrado,
                    DeQuien = deQuien,
                    ParaQuien = paraQuien,
                    FechaBorrado = DateTime.UtcNow,
                    FechaOriginal = fechaCreado,
                    RUConsumed = totalRU,
                    MensajesRestantes = conversation.Mensajes.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error borrando mensaje");
                return new BorrarMensajeResult
                {
                    Success = false,
                    ErrorMessage = $"Error borrando mensaje: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Obtiene una conversación específica por su ID (PairId)
        /// Usa query SELECT para buscar sin necesidad de Partition Key
        /// </summary>
        public async Task<HablemosConversation?> ObtenerConversacionPorIdAsync(string documentId)
        {
            if (string.IsNullOrEmpty(documentId))
            {
                _logger.LogWarning("⚠️ DocumentId vacío");
                return null;
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("🔍 Buscando conversación con ID: {DocumentId}", documentId);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Usar query SELECT para buscar por id sin necesidad de Partition Key
                var queryDefinition = new QueryDefinition("SELECT * FROM c WHERE c.id = @documentId")
                    .WithParameter("@documentId", documentId);

                var iterator = container.GetItemQueryIterator<HablemosConversation>(queryDefinition);

                HablemosConversation? conversation = null;
                double totalRU = 0;

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    if (response.Count > 0)
                    {
                        conversation = response.FirstOrDefault();
                        _logger.LogInformation("✅ Conversación encontrada. RU consumed: {RU}", totalRU);
                        break;
                    }
                }

                if (conversation == null)
                {
                    _logger.LogInformation("ℹ️ Conversación no encontrada con ID: {DocumentId}", documentId);
                }

                return conversation;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error buscando conversación");
                return null;
            }
        }

        /// <summary>
        /// Guarda o actualiza una conversación completa (UpsertItem)
        /// </summary>
        public async Task<GuardarMensajeResult> GuardarConversacionAsync(
            HablemosConversation conversation, 
            string twinID)
        {
            if (conversation == null)
            {
                return new GuardarMensajeResult
                {
                    Success = false,
                    ErrorMessage = "La conversación no puede ser null"
                };
            }

            if (string.IsNullOrEmpty(twinID))
            {
                return new GuardarMensajeResult
                {
                    Success = false,
                    ErrorMessage = "TwinID es requerido"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("💾 Guardando conversación: {DocumentId}, TwinID: {TwinID}, Mensajes: {Count}", 
                    conversation.Id, twinID, conversation.Mensajes.Count);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Upsert (inserta si no existe, actualiza si existe)
                var upsertResponse = await container.UpsertItemAsync(
                    conversation,
                    new PartitionKey(twinID)
                );

                _logger.LogInformation("✅ Conversación guardada exitosamente. RU consumed: {RU}",
                    upsertResponse.RequestCharge);

                return new GuardarMensajeResult
                {
                    Success = true,
                    MessageId = conversation.Mensajes.LastOrDefault()?.MessageId ?? string.Empty,
                    PairId = conversation.Id,
                    RUConsumed = upsertResponse.RequestCharge
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error guardando conversación");
                return new GuardarMensajeResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public void Dispose()
        {
            _cosmosClient?.Dispose();
        }
    }

    #region Result Models

    public class GuardarMensajeResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? MessageId { get; set; }
        public string? PairId { get; set; }
        public double RUConsumed { get; set; }
    }

    public class ObtenerMensajesResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public List<HablemosMessage> Mensajes { get; set; } = new();
        public string PairId { get; set; } = string.Empty;
        public int TotalMensajes { get; set; }
        public double RUConsumed { get; set; }
    }

    public class ObtenerConversacionesResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<HablemosConversation> Conversaciones { get; set; } = new();
        public int TotalConversaciones { get; set; }
        public double RUConsumed { get; set; }
    }

    public class MarcarLeidosResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public int MarcadosCount { get; set; }
        public double RUConsumed { get; set; }
    }

    public class EditarMensajeResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string PairId { get; set; } = string.Empty;
        public string MensajeOriginal { get; set; } = string.Empty;
        public string MensajeEditado { get; set; } = string.Empty;
        public double RUConsumed { get; set; }
    }

    public class BorrarMensajeResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string PairId { get; set; } = string.Empty;
        public string MensajeBorrado { get; set; } = string.Empty;
        public string DeQuien { get; set; } = string.Empty;
        public string ParaQuien { get; set; } = string.Empty;
        public DateTime FechaBorrado { get; set; }
        public DateTime FechaOriginal { get; set; }
        public double RUConsumed { get; set; }
        public int MensajesRestantes { get; set; }
    }

    #endregion
}

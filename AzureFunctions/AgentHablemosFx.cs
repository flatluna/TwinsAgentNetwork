using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Functions para sistema de mensajería simple Hablemos (1-a-1 sin IA)
    /// Endpoints REST para enviar/recibir mensajes, marcar como leído y obtener conversaciones
    /// </summary>
    public class AgentHablemosFx
    {
        private readonly ILogger<AgentHablemosFx> _logger;
        private readonly IConfiguration _configuration;

        public AgentHablemosFx(
            ILogger<AgentHablemosFx> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        #region Enviar Mensaje

        [Function("EnviarMensajeHablemosOptions")]
        public async Task<HttpResponseData> HandleEnviarMensajeOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "hablemos/mensaje/enviar")] HttpRequestData req)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for hablemos/mensaje/enviar");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Envía un mensaje entre dos usuarios y lo guarda automáticamente en Cosmos DB
        /// </summary>
        [Function("EnviarMensajeHablemos")]
        public async Task<HttpResponseData> EnviarMensaje(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hablemos/mensaje/enviar")] HttpRequestData req)
        {
            _logger.LogInformation("💬 EnviarMensajeHablemos function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "Request body is required" });
                    return badResponse;
                }

                var request = JsonConvert.DeserializeObject<EnviarMensajeRequest>(requestBody);

                if (request == null)
                {
                    _logger.LogError("❌ Invalid request format");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "Invalid request format" });
                    return badResponse;
                }

                _logger.LogInformation("💬 Sending message from {From} to {To}", request.DeQuien, request.ParaQuien);

                // Crear agente
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var agentLogger = loggerFactory.CreateLogger<AgentHablemos>();
                var agent = new AgentHablemos(agentLogger, _configuration);

                // Enviar mensaje (se guarda automáticamente en Cosmos DB)
                var result = await agent.EnviarMensajeAsync(request);

                if (!result.Success)
                {
                    _logger.LogError("❌ Error sending message: {Error}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogInformation("✅ Message sent successfully. MessageId: {MessageId}, PairId: {PairId}",
                    result.Mensaje?.MessageId, result.PairId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Mensaje enviado exitosamente",
                    messageId = result.Mensaje?.MessageId,
                    pairId = result.PairId,
                    sentAt = result.Mensaje?.FechaCreado,
                    ruConsumed = result.RUConsumed,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Unexpected error in EnviarMensajeHablemos");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while sending the message",
                    details = ex.Message,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                });
                return errorResponse;
            }
        }

        #endregion

        #region Obtener Mensajes

        [Function("ObtenerMensajesHablemosOptions")]
        public async Task<HttpResponseData> HandleObtenerMensajesOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "hablemos/mensajes/{clientePrimeroID}/{clienteSegundoID}/{twinID}/{periodo}")] HttpRequestData req,
            string clientePrimeroID,
            string clienteSegundoID,
            string twinID,
            string periodo)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for hablemos/mensajes/{Client1}/{Client2}/{TwinID}/{Periodo}",
                clientePrimeroID, clienteSegundoID, twinID, periodo);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Obtiene mensajes entre dos usuarios filtrados por período (dia, semana, mes)
        /// Requiere TwinID del dueño del app
        /// </summary>
        [Function("ObtenerMensajesHablemos")]
        public async Task<HttpResponseData> ObtenerMensajes(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hablemos/mensajes/{clientePrimeroID}/{clienteSegundoID}/{twinID}/{periodo}")] HttpRequestData req,
            string clientePrimeroID,
            string clienteSegundoID,
            string twinID,
            string periodo = "dia")
        {
            _logger.LogInformation("📖 ObtenerMensajesHablemos function triggered for {Client1} - {Client2}, TwinID: {TwinID}, Period: {Periodo}",
                clientePrimeroID, clienteSegundoID, twinID, periodo);

            try
            {
                if (string.IsNullOrEmpty(clientePrimeroID) || string.IsNullOrEmpty(clienteSegundoID) || string.IsNullOrEmpty(twinID))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "ClientePrimeroID, ClienteSegundoID and TwinID are required" });
                    return badResponse;
                }

                // Crear agente
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var agentLogger = loggerFactory.CreateLogger<AgentHablemos>();
                var agent = new AgentHablemos(agentLogger, _configuration);

                // Obtener mensajes desde Cosmos DB
                var result = await agent.GetMensajesAsync(
                    clientePrimeroID,
                    clienteSegundoID,
                    twinID,
                    periodo
                );

                if (!result.Success)
                {
                    _logger.LogError("❌ Error getting messages: {Error}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                _logger.LogInformation("✅ Retrieved {Count} messages", result.TotalMensajes);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    clientePrimeroID = clientePrimeroID,
                    clienteSegundoID = clienteSegundoID,
                    twinID = twinID,
                    periodo = result.Periodo,
                    fechaInicio = result.FechaInicio,
                    fechaFin = result.FechaFin,
                    totalMensajes = result.TotalMensajes,
                    mensajes = result.Mensajes,
                    ruConsumed = result.RUConsumed,
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in ObtenerMensajesHablemos");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while retrieving messages"
                });
                return errorResponse;
            }
        }

        #endregion

        #region Obtener Mensajes Por ID (Simple)

        [Function("ObtenerMensajesPorIdOptions")]
        public async Task<HttpResponseData> HandleObtenerMensajesPorIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "hablemos/mensajes-simple/{clienteID}/{agenteInmueblesID}")] HttpRequestData req,
            string clienteID,
            string agenteInmueblesID)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for hablemos/mensajes-simple/{ClienteID}/{AgenteID}", 
                clienteID, agenteInmueblesID);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Obtiene TODOS los mensajes entre un cliente y un agente inmobiliario
        /// No requiere TwinID ni fechas - búsqueda directa por ID
        /// Busca automáticamente con ambas combinaciones posibles del PairId
        /// </summary>
        [Function("ObtenerMensajesPorId")]
        public async Task<HttpResponseData> ObtenerMensajesPorId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hablemos/mensajes-simple/{clienteID}/{agenteInmueblesID}")] HttpRequestData req,
            string clienteID,
            string agenteInmueblesID)
        {
            _logger.LogInformation("📖 ObtenerMensajesPorId function triggered. ClienteID: {ClienteID}, AgenteID: {AgenteID}",
                clienteID, agenteInmueblesID);

            try
            {
                if (string.IsNullOrEmpty(clienteID) || string.IsNullOrEmpty(agenteInmueblesID))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "ClienteID and AgenteInmueblesID are required" });
                    return badResponse;
                }

                // Crear servicio Cosmos DB directamente
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<TwinAgentsNetwork.Services.AgentHablemosCosmosDB>();
                var cosmosService = new TwinAgentsNetwork.Services.AgentHablemosCosmosDB(cosmosLogger, _configuration);

                // Obtener todos los mensajes (sin filtro de fechas ni TwinID)
                var result = await cosmosService.ObtenerMensajesPorIdAsync(
                    clienteID,
                    agenteInmueblesID
                );

                if (!result.Success)
                {
                    _logger.LogError("❌ Error getting messages: {Error}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                _logger.LogInformation("✅ Retrieved {Count} messages. PairId: {PairId}", 
                    result.TotalMensajes, result.PairId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    clienteID = clienteID,
                    agenteInmueblesID = agenteInmueblesID,
                    pairId = result.PairId,
                    totalMensajes = result.TotalMensajes,
                    mensajes = result.Mensajes,
                    ruConsumed = result.RUConsumed,
                    timestamp = DateTime.UtcNow,
                    note = "Retorna TODOS los mensajes de la conversación (sin filtro de fechas)"
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in ObtenerMensajesPorId");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while retrieving messages",
                    details = ex.Message
                });
                return errorResponse;
            }
        }

        #endregion

        #region Obtener Mensajes Por ID Con Fechas

        [Function("ObtenerMensajesPorIdConFechasOptions")]
        public async Task<HttpResponseData> HandleObtenerMensajesPorIdConFechasOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "hablemos/mensajes-filtro/{clienteID}/{agenteInmueblesID}/{fromDate}/{toDate}")] HttpRequestData req,
            string clienteID,
            string agenteInmueblesID,
            string fromDate,
            string toDate)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for hablemos/mensajes-filtro/{ClienteID}/{AgenteID}/{FromDate}/{ToDate}", 
                clienteID, agenteInmueblesID, fromDate, toDate);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Obtiene mensajes entre un cliente y un agente inmobiliario CON FILTRO DE FECHAS
        /// No requiere TwinID - búsqueda directa por ID con rango de fechas
        /// Busca automáticamente con ambas combinaciones posibles del PairId
        /// Formato de fechas: yyyy-MM-dd o yyyy-MM-ddTHH:mm:ss
        /// </summary>
        [Function("ObtenerMensajesPorIdConFechas")]
        public async Task<HttpResponseData> ObtenerMensajesPorIdConFechas(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hablemos/mensajes-filtro/{clienteID}/{agenteInmueblesID}/{fromDate}/{toDate}")] HttpRequestData req,
            string clienteID,
            string agenteInmueblesID,
            string fromDate,
            string toDate)
        {
            _logger.LogInformation("📖 ObtenerMensajesPorIdConFechas function triggered. ClienteID: {ClienteID}, AgenteID: {AgenteID}, From: {FromDate}, To: {ToDate}",
                clienteID, agenteInmueblesID, fromDate, toDate);

            try
            {
                if (string.IsNullOrEmpty(clienteID) || string.IsNullOrEmpty(agenteInmueblesID))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "ClienteID and AgenteInmueblesID are required" });
                    return badResponse;
                }

                // Parsear fechas
                DateTime parsedFromDate;
                DateTime parsedToDate;

                try
                {
                    parsedFromDate = DateTime.Parse(fromDate);
                    parsedToDate = DateTime.Parse(toDate);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error parsing dates");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new 
                    { 
                        success = false, 
                        error = "Invalid date format. Use yyyy-MM-dd or yyyy-MM-ddTHH:mm:ss",
                        details = ex.Message
                    });
                    return badResponse;
                }

                // Validar que fromDate sea anterior a toDate
                if (parsedFromDate > parsedToDate)
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "FromDate must be earlier than ToDate" });
                    return badResponse;
                }

                // Crear servicio Cosmos DB directamente
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<TwinAgentsNetwork.Services.AgentHablemosCosmosDB>();
                var cosmosService = new TwinAgentsNetwork.Services.AgentHablemosCosmosDB(cosmosLogger, _configuration);

                // Obtener mensajes filtrados por fecha
                var result = await cosmosService.ObtenerMensajesPorIdConFechasAsync(
                    clienteID,
                    agenteInmueblesID,
                    parsedFromDate,
                    parsedToDate
                );

                if (!result.Success)
                {
                    _logger.LogError("❌ Error getting messages: {Error}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                _logger.LogInformation("✅ Retrieved {Count} messages in date range. PairId: {PairId}", 
                    result.TotalMensajes, result.PairId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    clienteID = clienteID,
                    agenteInmueblesID = agenteInmueblesID,
                    pairId = result.PairId,
                    fromDate = parsedFromDate,
                    toDate = parsedToDate,
                    totalMensajes = result.TotalMensajes,
                    mensajes = result.Mensajes,
                    ruConsumed = result.RUConsumed,
                    timestamp = DateTime.UtcNow,
                    note = "Mensajes filtrados por rango de fechas"
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in ObtenerMensajesPorIdConFechas");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while retrieving messages",
                    details = ex.Message
                });
                return errorResponse;
            }
        }

        #endregion

        #region Marcar Como Leído

        [Function("MarcarComoLeidoHablemosOptions")]
        public async Task<HttpResponseData> HandleMarcarComoLeidoOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "hablemos/mensajes/marcar-leido")] HttpRequestData req)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for hablemos/mensajes/marcar-leido");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Marca mensajes específicos como leídos
        /// </summary>
        [Function("MarcarComoLeidoHablemos")]
        public async Task<HttpResponseData> MarcarComoLeido(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hablemos/mensajes/marcar-leido")] HttpRequestData req)
        {
            _logger.LogInformation("✅ MarcarComoLeidoHablemos function triggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var request = JsonConvert.DeserializeObject<MarcarLeidoRequest>(requestBody);

                if (request == null || string.IsNullOrEmpty(request.ClientePrimeroID) ||
                    string.IsNullOrEmpty(request.ClienteSegundoID) ||
                    string.IsNullOrEmpty(request.TwinID) ||
                    string.IsNullOrEmpty(request.LeidoPor) ||
                    request.MessageIds == null || !request.MessageIds.Any())
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "ClientePrimeroID, ClienteSegundoID, TwinID, LeidoPor and MessageIds are required"
                    });
                    return badResponse;
                }

                // Crear agente
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var agentLogger = loggerFactory.CreateLogger<AgentHablemos>();
                var agent = new AgentHablemos(agentLogger, _configuration);

                // Marcar como leído
                var result = await agent.MarcarComoLeidoAsync(
                    request.ClientePrimeroID,
                    request.ClienteSegundoID,
                    request.TwinID,
                    request.MessageIds,
                    request.LeidoPor
                );

                if (!result.Success)
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                _logger.LogInformation("✅ Marked {Count} messages as read", result.MarcadosCount);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = $"{result.MarcadosCount} mensajes marcados como leídos",
                    marcadosCount = result.MarcadosCount,
                    ruConsumed = result.RUConsumed,
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in MarcarComoLeidoHablemos");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while marking messages as read"
                });
                return errorResponse;
            }
        }

        #endregion

        #region Obtener Conversaciones de Usuario

        [Function("ObtenerConversacionesUsuarioOptions")]
        public async Task<HttpResponseData> HandleObtenerConversacionesOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "hablemos/conversaciones/{clienteID}/{agenteID}/{twinID}")] HttpRequestData req,
            string clienteID,
            string agenteID,
            string twinID)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for hablemos/conversaciones/{ClienteID}/{AgenteID}/{TwinID}", 
                clienteID, agenteID, twinID);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Obtiene todas las conversaciones activas de un usuario
        /// Busca por ambas combinaciones posibles: agenteId_clienteId y clienteId_agenteId
        /// </summary>
        [Function("ObtenerConversacionesUsuario")]
        public async Task<HttpResponseData> ObtenerConversacionesUsuario(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hablemos/conversaciones/{clienteID}/{agenteID}/{twinID}")] HttpRequestData req,
            string clienteID,
            string agenteID,
            string twinID)
        {
            _logger.LogInformation("💬 ObtenerConversacionesUsuario function triggered. ClienteID: {ClienteID}, AgenteID: {AgenteID}, TwinID: {TwinID}", 
                clienteID, agenteID, twinID);

            try
            {
                if (string.IsNullOrEmpty(clienteID) || string.IsNullOrEmpty(agenteID) || string.IsNullOrEmpty(twinID))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "ClienteID, AgenteID and TwinID are required" });
                    return badResponse;
                }

                // Crear agente
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var agentLogger = loggerFactory.CreateLogger<AgentHablemos>();
                var agent = new AgentHablemos(agentLogger, _configuration);

                // Obtener conversaciones
                var result = await agent.ObtenerConversacionesUsuarioAsync(clienteID, agenteID, twinID);

                if (!result.Success)
                {
                    _logger.LogError("❌ Error getting conversations: {Error}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                _logger.LogInformation("✅ Retrieved {Count} conversations for ClienteID: {ClienteID}",
                    result.TotalConversaciones, clienteID);

                // Enriquecer conversaciones con info adicional
                var conversacionesEnriquecidas = result.Conversaciones.Select(conv => new
                {
                    pairId = conv.PairId,
                    clientePrimeroID = conv.ClientePrimeroID,
                    clienteSegundoID = conv.ClienteSegundoID,
                    twinID = conv.TwinID,
                    
                    // Determinar el "otro usuario"
                    otroUsuarioID = conv.ClientePrimeroID == clienteID
                        ? conv.ClienteSegundoID
                        : conv.ClientePrimeroID,
                    
                    totalMensajes = conv.Mensajes.Count,
                    mensajesNoLeidos = conv.Mensajes.Count(m => m.ParaQuien == clienteID && !m.IsRead),
                    ultimoMensaje = conv.Mensajes.OrderByDescending(m => m.FechaCreado).FirstOrDefault(),
                    lastActivityAt = conv.LastActivityAt,
                    createdAt = conv.CreatedAt
                }).ToList();

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    clienteID = clienteID,
                    agenteID = agenteID,
                    twinID = twinID,
                    totalConversaciones = result.TotalConversaciones,
                    conversaciones = conversacionesEnriquecidas,
                    ruConsumed = result.RUConsumed,
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in ObtenerConversacionesUsuario");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while retrieving conversations"
                });
                return errorResponse;
            }
        }

        #endregion

        #region Editar Mensaje

        [Function("EditarMensajeHablemosOptions")]
        public async Task<HttpResponseData> HandleEditarMensajeOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "hablemos/mensaje/editar")] HttpRequestData req)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for hablemos/mensaje/editar");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("EditarMensajeHablemos")]
        public async Task<HttpResponseData> EditarMensaje(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "hablemos/mensaje/editar")] HttpRequestData req)
        {
            _logger.LogInformation("✏️ EditarMensajeHablemos function triggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var request = JsonConvert.DeserializeObject<EditarMensajeRequest>(requestBody);

                if (request == null || string.IsNullOrEmpty(request.ClienteID) ||
                    string.IsNullOrEmpty(request.AgenteInmueblesID) ||
                    string.IsNullOrEmpty(request.MessageId) ||
                    string.IsNullOrEmpty(request.NuevoMensaje))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "ClienteID, AgenteInmueblesID, MessageId and NuevoMensaje are required" });
                    return badResponse;
                }

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<TwinAgentsNetwork.Services.AgentHablemosCosmosDB>();
                var cosmosService = new TwinAgentsNetwork.Services.AgentHablemosCosmosDB(cosmosLogger, _configuration);

                var result = await cosmosService.EditarMensajeAsync(
                    request.ClienteID,
                    request.AgenteInmueblesID,
                    request.MessageId,
                    request.NuevoMensaje
                );

                if (!result.Success)
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    messageId = result.MessageId,
                    pairId = result.PairId,
                    mensajeOriginal = result.MensajeOriginal,
                    mensajeEditado = result.MensajeEditado,
                    ruConsumed = result.RUConsumed
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in EditarMensajeHablemos");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new { success = false, error = ex.Message });
                return errorResponse;
            }
        }

        #endregion

        #region Borrar Mensaje

        [Function("BorrarMensajeHablemosOptions")]
        public async Task<HttpResponseData> HandleBorrarMensajeOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "hablemos/mensaje/borrar")] HttpRequestData req)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for hablemos/mensaje/borrar");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Borra un mensaje específico de la conversación
        /// No requiere TwinID - búsqueda automática con ambas combinaciones del PairId
        /// </summary>
        [Function("BorrarMensajeHablemos")]
        public async Task<HttpResponseData> BorrarMensaje(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "hablemos/mensaje/borrar")] HttpRequestData req)
        {
            _logger.LogInformation("🗑️ BorrarMensajeHablemos function triggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var request = JsonConvert.DeserializeObject<BorrarMensajeRequest>(requestBody);

                if (request == null || string.IsNullOrEmpty(request.ClienteID) ||
                    string.IsNullOrEmpty(request.AgenteInmueblesID) ||
                    string.IsNullOrEmpty(request.MessageId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "ClienteID, AgenteInmueblesID and MessageId are required" });
                    return badResponse;
                }

                _logger.LogInformation("🗑️ Deleting message. ClienteID: {ClienteID}, AgenteID: {AgenteID}, MessageId: {MessageId}",
                    request.ClienteID, request.AgenteInmueblesID, request.MessageId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<TwinAgentsNetwork.Services.AgentHablemosCosmosDB>();
                var cosmosService = new TwinAgentsNetwork.Services.AgentHablemosCosmosDB(cosmosLogger, _configuration);

                var result = await cosmosService.BorrarMensajeAsync(
                    request.ClienteID,
                    request.AgenteInmueblesID,
                    request.MessageId
                );

                if (!result.Success)
                {
                    _logger.LogError("❌ Error deleting message: {Error}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                _logger.LogInformation("✅ Message deleted successfully. MessageId: {MessageId}, MensajesRestantes: {Restantes}",
                    result.MessageId, result.MensajesRestantes);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    messageId = result.MessageId,
                    pairId = result.PairId,
                    mensajeBorrado = result.MensajeBorrado,
                    deQuien = result.DeQuien,
                    paraQuien = result.ParaQuien,
                    fechaBorrado = result.FechaBorrado,
                    fechaOriginal = result.FechaOriginal,
                    mensajesRestantes = result.MensajesRestantes,
                    ruConsumed = result.RUConsumed,
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in BorrarMensajeHablemos");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new { success = false, error = ex.Message });
                return errorResponse;
            }
        }

        #endregion

        #region Mejorar Texto con IA

        [Function("MejorarTextoHablemosOptions")]
        public async Task<HttpResponseData> HandleMejorarTextoOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "hablemos/mejorar-texto")] HttpRequestData req)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for hablemos/mejorar-texto");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Mejora un texto usando IA según el estilo especificado
        /// Estilos: conciso, sumario, formal, casual, profesional, creativo
        /// </summary>
        [Function("MejorarTextoHablemos")]
        public async Task<HttpResponseData> MejorarTexto(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "hablemos/mejorar-texto")] HttpRequestData req)
        {
            _logger.LogInformation("✏️ MejorarTextoHablemos function triggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var request = JsonConvert.DeserializeObject<MejorarTextoRequest>(requestBody);

                if (request == null || string.IsNullOrEmpty(request.Texto))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "Texto is required" });
                    return badResponse;
                }

                // Default style
                string estilo = string.IsNullOrEmpty(request.Estilo) ? "conciso" : request.Estilo;

                _logger.LogInformation("✏️ Improving text with style: {Style}", estilo);

                // Crear agente
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var agentLogger = loggerFactory.CreateLogger<AgentHablemos>();
                var agent = new AgentHablemos(agentLogger, _configuration);

                // Mejorar texto
                var result = await agent.MejorarTextoConEstiloAsync(request.Texto, estilo);

                if (!result.Success)
                {
                    _logger.LogError("❌ Error improving text: {Error}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                _logger.LogInformation("✅ Text improved successfully. Original: {OrigLen} chars, Improved: {ImpLen} chars",
                    result.CaracteresOriginales, result.CaracteresMejorados);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    textoOriginal = result.TextoOriginal,
                    textoMejorado = result.TextoMejorado,
                    estiloAplicado = result.EstiloAplicado,
                    caracteresOriginales = result.CaracteresOriginales,
                    caracteresMejorados = result.CaracteresMejorados,
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in MejorarTextoHablemos");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new { success = false, error = ex.Message });
                return errorResponse;
            }
        }

        #endregion

        #region CORS Helper

        private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
            var origin = originHeader.Key != null ? originHeader.Value?.FirstOrDefault() : null;

            var allowedOrigins = new[] { "http://localhost:5173", "http://localhost:3000", "http://127.0.0.1:5173", "http://127.0.0.1:3000" };

            if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
            {
                response.Headers.Add("Access-Control-Allow-Origin", origin);
            }
            else
            {
                response.Headers.Add("Access-Control-Allow-Origin", "*");
            }

            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent");
            response.Headers.Add("Access-Control-Max-Age", "3600");
        }

        #endregion
    }

    #region Request Models

    public class MarcarLeidoRequest
    {
        [JsonProperty("clientePrimeroID")]
        public string ClientePrimeroID { get; set; } = string.Empty;

        [JsonProperty("clienteSegundoID")]
        public string ClienteSegundoID { get; set; } = string.Empty;

        [JsonProperty("twinID")]
        public string TwinID { get; set; } = string.Empty;

        [JsonProperty("messageIds")]
        public List<string> MessageIds { get; set; } = new();

        [JsonProperty("leidoPor")]
        public string LeidoPor { get; set; } = string.Empty;
    }

    public class EditarMensajeRequest
    {
        [JsonProperty("clienteID")]
        public string ClienteID { get; set; } = string.Empty;

        [JsonProperty("agenteInmueblesID")]
        public string AgenteInmueblesID { get; set; } = string.Empty;

        [JsonProperty("messageId")]
        public string MessageId { get; set; } = string.Empty;

        [JsonProperty("nuevoMensaje")]
        public string NuevoMensaje { get; set; } = string.Empty;
    }

    public class BorrarMensajeRequest
    {
        [JsonProperty("clienteID")]
        public string ClienteID { get; set; } = string.Empty;

        [JsonProperty("agenteInmueblesID")]
        public string AgenteInmueblesID { get; set; } = string.Empty;

        [JsonProperty("messageId")]
        public string MessageId { get; set; } = string.Empty;
    }

    public class MejorarTextoRequest
    {
        [JsonProperty("texto")]
        public string Texto { get; set; } = string.Empty;

        [JsonProperty("estilo")]
        public string Estilo { get; set; } = "conciso"; // conciso, sumario, formal, casual, profesional, creativo
    }

    #endregion
}

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Functions para gestionar agentes inmobiliarios (Real Estate Agents)
    /// Proporciona endpoints CRUD completos para la gestión de agentes
    /// </summary>
    public class AgentTwinAgenteFx
    {
        private readonly ILogger<AgentTwinAgenteFx> _logger;
        private readonly IConfiguration _configuration;

        public AgentTwinAgenteFx(ILogger<AgentTwinAgenteFx> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        #region Save/Create Agent Functions

        [Function("SaveAgenteInmobiliarioOptions")]
        public async Task<HttpResponseData> HandleSaveAgenteInmobiliarioOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "save-agente-inmobiliario/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for save-agente-inmobiliario/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("SaveAgenteInmobiliario")]
        public async Task<HttpResponseData> SaveAgenteInmobiliario(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "save-agente-inmobiliario/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🏢 SaveAgenteInmobiliario function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveAgenteInmobiliarioFxResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Saving agente inmobiliario for Twin ID: {TwinId}", twinId);

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var agenteRequest = JsonSerializer.Deserialize<AgenteInmobiliarioRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (agenteRequest == null)
                {
                    _logger.LogError("❌ Failed to parse agente inmobiliario request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveAgenteInmobiliarioFxResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid agente inmobiliario request data format"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(agenteRequest.NombreEquipoAgente))
                {
                    _logger.LogError("❌ Nombre del equipo/agente is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveAgenteInmobiliarioFxResponse
                    {
                        Success = false,
                        ErrorMessage = "Nombre del equipo/agente is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📋 Agente details: Nombre={Nombre}, Empresa={Empresa}", 
                    agenteRequest.NombreEquipoAgente, agenteRequest.EmpresaBroker);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentAgenteTwinCosmosDB>();
                var agenteCosmosDB = new AgentAgenteTwinCosmosDB(cosmosLogger, _configuration);
                twinId = agenteRequest.MicrosoftOID;
                var saveResult = await agenteCosmosDB.SaveAgenteInmobiliarioAsync(agenteRequest, twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                if (!saveResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to save agente inmobiliario: {Error}", saveResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveAgenteInmobiliarioFxResponse
                    {
                        Success = false,
                        ErrorMessage = saveResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Agente inmobiliario saved successfully. Document ID: {DocumentId}", saveResult.DocumentId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new SaveAgenteInmobiliarioFxResponse
                {
                    Success = true,
                    TwinId = twinId,
                    DocumentId = saveResult.DocumentId,
                    Agente = agenteRequest,
                    RUConsumed = saveResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = saveResult.Message,
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error saving agente inmobiliario after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveAgenteInmobiliarioFxResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al guardar el agente inmobiliario"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Get Agents Functions

        [Function("GetAgentesInmobiliariosOptions")]
        public async Task<HttpResponseData> HandleGetAgentesInmobiliariosOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-agentes-inmobiliarios/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-agentes-inmobiliarios/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetAgentesInmobiliarios")]
        public async Task<HttpResponseData> GetAgentesInmobiliarios(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-agentes-inmobiliarios/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("📋 GetAgentesInmobiliarios function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetAgentesInmobiliariosFxResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting agentes inmobiliarios for Twin ID: {TwinId}", twinId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentAgenteTwinCosmosDB>();
                var agenteCosmosDB = new AgentAgenteTwinCosmosDB(cosmosLogger, _configuration);

                var result = await agenteCosmosDB.GetAgentesInmobiliariosByTwinIdAsync(twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetAgentesInmobiliariosFxResponse
                {
                    Success = result.Success,
                    TwinId = twinId,
                    Agentes = result.Agentes,
                    AgenteCount = result.AgenteCount,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = result.Success 
                        ? $"Se encontraron {result.AgenteCount} agentes inmobiliarios" 
                        : "Error al obtener agentes inmobiliarios",
                    ErrorMessage = result.Success ? null : result.ErrorMessage
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved {Count} agentes inmobiliarios", result.AgenteCount);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting agentes inmobiliarios after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetAgentesInmobiliariosFxResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener agentes inmobiliarios"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Get Agent by ID Functions

        [Function("GetAgenteInmobiliarioByIdOptions")]
        public async Task<HttpResponseData> HandleGetAgenteInmobiliarioByIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-agente-inmobiliario-by-id/{twinId}/{agenteId}")] HttpRequestData req,
            string twinId,
            string agenteId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-agente-inmobiliario-by-id/{TwinId}/{AgenteId}", twinId, agenteId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetAgenteInmobiliarioById")]
        public async Task<HttpResponseData> GetAgenteInmobiliarioById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-agente-inmobiliario-by-id/{twinId}/{agenteId}")] HttpRequestData req,
            string twinId,
            string agenteId)
        {
            _logger.LogInformation("🔍 GetAgenteInmobiliarioById function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetAgenteInmobiliarioByIdFxResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(agenteId))
                {
                    _logger.LogError("❌ Agente ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetAgenteInmobiliarioByIdFxResponse
                    {
                        Success = false,
                        ErrorMessage = "Agente ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting agente inmobiliario ID: {AgenteId} for Twin ID: {TwinId}", agenteId, twinId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentAgenteTwinCosmosDB>();
                var agenteCosmosDB = new AgentAgenteTwinCosmosDB(cosmosLogger, _configuration);

                var result = await agenteCosmosDB.GetAgenteInmobiliarioByIdAsync(agenteId, twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                if (!result.Success)
                {
                    _logger.LogWarning("⚠️ Agente inmobiliario not found: {AgenteId}", agenteId);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new GetAgenteInmobiliarioByIdFxResponse
                    {
                        Success = false,
                        ErrorMessage = result.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return notFoundResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetAgenteInmobiliarioByIdFxResponse
                {
                    Success = true,
                    TwinId = twinId,
                    AgenteId = agenteId,
                    Agente = result.Agente,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Agente inmobiliario encontrado exitosamente"
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved agente inmobiliario: {AgenteId}", agenteId);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting agente inmobiliario by ID after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetAgenteInmobiliarioByIdFxResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener agente inmobiliario"
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// OPTIONS handler para GetAgenteInmobiliarioByMicrosoftOID
        /// </summary>
        [Function("GetAgenteInmobiliarioByMicrosoftOIDOptions")]
        public async Task<HttpResponseData> HandleGetAgenteInmobiliarioByMicrosoftOIDOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-agente-inmobiliario/microsoft-oid/{microsoftOID}")] HttpRequestData req,
            string microsoftOID)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-agente-inmobiliario/microsoft-oid/{MicrosoftOID}", microsoftOID);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Obtiene un agente inmobiliario por MicrosoftOID
        /// GET /api/get-agente-inmobiliario/microsoft-oid/{microsoftOID}
        /// </summary>
        [Function("GetAgenteInmobiliarioByMicrosoftOID")]
        public async Task<HttpResponseData> GetAgenteInmobiliarioByMicrosoftOID(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-agente-inmobiliario/microsoft-oid/{microsoftOID}")] HttpRequestData req,
            string microsoftOID)
        {
            _logger.LogInformation("🔍 GetAgenteInmobiliarioByMicrosoftOID function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(microsoftOID))
                {
                    _logger.LogError("❌ MicrosoftOID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetAgenteInmobiliarioByMicrosoftOIDFxResponse
                    {
                        Success = false,
                        ErrorMessage = "MicrosoftOID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting agente inmobiliario for MicrosoftOID: {MicrosoftOID}", microsoftOID);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentAgenteTwinCosmosDB>();
                var agenteCosmosDB = new AgentAgenteTwinCosmosDB(cosmosLogger, _configuration);

                var result = await agenteCosmosDB.GetAgenteInmobiliarioByMicrosoftOIDAsync(microsoftOID);

                var processingTime = DateTime.UtcNow - startTime;

                if (!result.Success)
                {
                    _logger.LogWarning("⚠️ Agente inmobiliario not found for MicrosoftOID: {MicrosoftOID}", microsoftOID);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new GetAgenteInmobiliarioByMicrosoftOIDFxResponse
                    {
                        Success = false,
                        ErrorMessage = result.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return notFoundResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetAgenteInmobiliarioByMicrosoftOIDFxResponse
                {
                    Success = true,
                    MicrosoftOID = microsoftOID,
                    Agente = result.Agente,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Agente inmobiliario encontrado exitosamente"
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved agente inmobiliario for MicrosoftOID: {MicrosoftOID}", microsoftOID);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting agente inmobiliario by MicrosoftOID after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetAgenteInmobiliarioByMicrosoftOIDFxResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener agente inmobiliario"
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// OPTIONS handler para GetAgenteInmobiliarioByDocumentId
        /// </summary>
        [Function("GetAgenteInmobiliarioByDocumentIdOptions")]
        public async Task<HttpResponseData> HandleGetAgenteInmobiliarioByDocumentIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-agente-inmobiliario/document-id/{agenteId}")] HttpRequestData req,
            string agenteId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-agente-inmobiliario/document-id/{AgenteId}", agenteId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Obtiene un agente inmobiliario solo por el ID del documento (sin necesidad de TwinID)
        /// GET /api/get-agente-inmobiliario/document-id/{agenteId}
        /// </summary>
        [Function("GetAgenteInmobiliarioByDocumentId")]
        public async Task<HttpResponseData> GetAgenteInmobiliarioByDocumentId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-agente-inmobiliario/document-id/{agenteId}")] HttpRequestData req,
            string agenteId)
        {
            _logger.LogInformation("🔍 GetAgenteInmobiliarioByDocumentId function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(agenteId))
                {
                    _logger.LogError("❌ Agente ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetAgenteInmobiliarioByDocumentIdFxResponse
                    {
                        Success = false,
                        ErrorMessage = "Agente ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting agente inmobiliario by Document ID: {AgenteId}", agenteId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentAgenteTwinCosmosDB>();
                var agenteCosmosDB = new AgentAgenteTwinCosmosDB(cosmosLogger, _configuration);

                var result = await agenteCosmosDB.GetAgenteInmobiliarioByDocumentIdAsync(agenteId);

                var processingTime = DateTime.UtcNow - startTime;

                if (!result.Success)
                {
                    _logger.LogWarning("⚠️ Agente inmobiliario not found for Document ID: {AgenteId}", agenteId);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new GetAgenteInmobiliarioByDocumentIdFxResponse
                    {
                        Success = false,
                        ErrorMessage = result.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return notFoundResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetAgenteInmobiliarioByDocumentIdFxResponse
                {
                    Success = true,
                    AgenteId = agenteId,
                    TwinId = result.TwinId,
                    Agente = result.Agente,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Agente inmobiliario encontrado exitosamente"
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved agente inmobiliario by Document ID: {AgenteId}, TwinID detectado: {TwinId}", agenteId, result.TwinId);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting agente inmobiliario by Document ID after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetAgenteInmobiliarioByDocumentIdFxResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener agente inmobiliario"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Update Agent Functions

        [Function("UpdateAgenteInmobiliarioOptions")]
        public async Task<HttpResponseData> HandleUpdateAgenteInmobiliarioOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "update-agente-inmobiliario/{twinId}/{agenteId}")] HttpRequestData req,
            string twinId,
            string agenteId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for update-agente-inmobiliario/{TwinId}/{AgenteId}", twinId, agenteId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("UpdateAgenteInmobiliario")]
        public async Task<HttpResponseData> UpdateAgenteInmobiliario(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "update-agente-inmobiliario/{twinId}/{agenteId}")] HttpRequestData req,
            string twinId,
            string agenteId)
        {
            _logger.LogInformation("✏️ UpdateAgenteInmobiliario function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateAgenteInmobiliarioFxResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(agenteId))
                {
                    _logger.LogError("❌ Agente ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateAgenteInmobiliarioFxResponse
                    {
                        Success = false,
                        ErrorMessage = "Agente ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Updating agente inmobiliario ID: {AgenteId} for Twin ID: {TwinId}", agenteId, twinId);

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var agenteRequest = JsonSerializer.Deserialize<AgenteInmobiliarioRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (agenteRequest == null)
                {
                    _logger.LogError("❌ Failed to parse agente inmobiliario request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateAgenteInmobiliarioFxResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid agente inmobiliario request data format"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📋 Update details: Nombre={Nombre}, Empresa={Empresa}", 
                    agenteRequest.NombreEquipoAgente, agenteRequest.EmpresaBroker);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentAgenteTwinCosmosDB>();
                var agenteCosmosDB = new AgentAgenteTwinCosmosDB(cosmosLogger, _configuration);

                var updateResult = await agenteCosmosDB.UpdateAgenteInmobiliarioAsync(agenteId, twinId.ToLowerInvariant(), agenteRequest);

                var processingTime = DateTime.UtcNow - startTime;

                if (!updateResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to update agente inmobiliario: {Error}", updateResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateAgenteInmobiliarioFxResponse
                    {
                        Success = false,
                        ErrorMessage = updateResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Agente inmobiliario updated successfully. Agente ID: {AgenteId}", agenteId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new UpdateAgenteInmobiliarioFxResponse
                {
                    Success = true,
                    AgenteId = agenteId,
                    TwinId = twinId,
                    AgenteActualizado = updateResult.AgenteActualizado,
                    RUConsumed = updateResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Agente inmobiliario actualizado exitosamente",
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error updating agente inmobiliario after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateAgenteInmobiliarioFxResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al actualizar el agente inmobiliario"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Delete Agent Functions

        [Function("DeleteAgenteInmobiliarioOptions")]
        public async Task<HttpResponseData> HandleDeleteAgenteInmobiliarioOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "delete-agente-inmobiliario/{twinId}/{agenteId}")] HttpRequestData req,
            string twinId,
            string agenteId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for delete-agente-inmobiliario/{TwinId}/{AgenteId}", twinId, agenteId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("DeleteAgenteInmobiliario")]
        public async Task<HttpResponseData> DeleteAgenteInmobiliario(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "delete-agente-inmobiliario/{twinId}/{agenteId}")] HttpRequestData req,
            string twinId,
            string agenteId)
        {
            _logger.LogInformation("🗑️ DeleteAgenteInmobiliario function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteAgenteInmobiliarioFxResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(agenteId))
                {
                    _logger.LogError("❌ Agente ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteAgenteInmobiliarioFxResponse
                    {
                        Success = false,
                        ErrorMessage = "Agente ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Deleting agente inmobiliario ID: {AgenteId} for Twin ID: {TwinId}", agenteId, twinId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentAgenteTwinCosmosDB>();
                var agenteCosmosDB = new AgentAgenteTwinCosmosDB(cosmosLogger, _configuration);

                var deleteResult = await agenteCosmosDB.DeleteAgenteInmobiliarioAsync(agenteId, twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                if (!deleteResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to delete agente inmobiliario: {Error}", deleteResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteAgenteInmobiliarioFxResponse
                    {
                        Success = false,
                        ErrorMessage = deleteResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Agente inmobiliario deleted successfully. Agente ID: {AgenteId}", agenteId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new DeleteAgenteInmobiliarioFxResponse
                {
                    Success = true,
                    AgenteId = agenteId,
                    TwinId = twinId,
                    RUConsumed = deleteResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = deleteResult.Message ?? "Agente inmobiliario eliminado exitosamente",
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error deleting agente inmobiliario after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteAgenteInmobiliarioFxResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al eliminar el agente inmobiliario"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Helper Methods

        private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
            var origin = originHeader.Key != null ? originHeader.Value?.FirstOrDefault() : null;

            var allowedOrigins = new[] { 
                "http://localhost:5173", 
                "http://localhost:5174",  // ✨ Agregado para el nuevo puerto
                "http://localhost:3000", 
                "http://127.0.0.1:5173", 
                "http://127.0.0.1:5174",  // ✨ Agregado para el nuevo puerto
                "http://127.0.0.1:3000" 
            };

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

    #region Response Models

    public class SaveAgenteInmobiliarioFxResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public AgenteInmobiliarioRequest? Agente { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class GetAgentesInmobiliariosFxResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public System.Collections.Generic.List<AgenteInmobiliarioRequest> Agentes { get; set; } = new();
        public int AgenteCount { get; set; }
        public double RUConsumed { get; set; }
    }

    public class GetAgenteInmobiliarioByIdFxResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string AgenteId { get; set; } = string.Empty;
        public AgenteInmobiliarioRequest? Agente { get; set; }
        public double RUConsumed { get; set; }
    }

    public class GetAgenteInmobiliarioByMicrosoftOIDFxResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string MicrosoftOID { get; set; } = string.Empty;
        public AgenteInmobiliarioRequest? Agente { get; set; }
        public double RUConsumed { get; set; }
    }

    public class GetAgenteInmobiliarioByDocumentIdFxResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string AgenteId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public AgenteInmobiliarioRequest? Agente { get; set; }
        public double RUConsumed { get; set; }
    }

    public class UpdateAgenteInmobiliarioFxResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string AgenteId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public AgenteInmobiliarioRequest? AgenteActualizado { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class DeleteAgenteInmobiliarioFxResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string AgenteId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

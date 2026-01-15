using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Functions para gestionar ventas de casas desde prospección hasta venta final
    /// </summary>
    public class AgentMisCasasVentasFx
    {
        private readonly ILogger<AgentMisCasasVentasFx> _logger;
        private readonly IConfiguration _configuration;

        public AgentMisCasasVentasFx(ILogger<AgentMisCasasVentasFx> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        #region Save Casa Venta Functions

        [Function("SaveCasaVentaOptions")]
        public async Task<HttpResponseData> HandleSaveCasaVentaOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "save-casa-venta/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for save-casa-venta/{TwinId}", twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("SaveCasaVenta")]
        public async Task<HttpResponseData> SaveCasaVenta(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "save-casa-venta/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🏠 SaveCasaVenta function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveCasaVentaResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Saving casa venta for Twin ID: {TwinId}", twinId);

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var ventaRequest = JsonSerializer.Deserialize<SaveCasaVentaRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (ventaRequest == null)
                {
                    _logger.LogError("❌ Failed to parse venta request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveCasaVentaResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid venta request data format"
                    }));
                    return badResponse;
                }

                // Validar campos requeridos
                if (string.IsNullOrEmpty(ventaRequest.ClienteCompradorId))
                {
                    _logger.LogError("❌ ClienteCompradorId is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveCasaVentaResponse
                    {
                        Success = false,
                        ErrorMessage = "ClienteCompradorId is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(ventaRequest.PropiedadId))
                {
                    _logger.LogError("❌ PropiedadId is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveCasaVentaResponse
                    {
                        Success = false,
                        ErrorMessage = "PropiedadId is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(ventaRequest.MicrosoftOID))
                {
                    _logger.LogError("❌ MicrosoftOID is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveCasaVentaResponse
                    {
                        Success = false,
                        ErrorMessage = "MicrosoftOID is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📋 Venta details: Propiedad={PropiedadId}, Cliente={ClienteCompradorId}, Estado={Estado}", 
                    ventaRequest.PropiedadId, ventaRequest.ClienteCompradorId, ventaRequest.EstadoActual);

                // Crear objeto CasaVenta
                var venta = new CasaVenta
                {
                    ClienteCompradorId = ventaRequest.ClienteCompradorId,
                    ClienteDuenoId = ventaRequest.ClienteDuenoId ?? string.Empty,
                    PropiedadId = ventaRequest.PropiedadId,
                    MicrosoftOID = ventaRequest.MicrosoftOID,
                    EstadoActual = "Prospectada",
                    FechaProspectada = ventaRequest.FechaProspectada,
                    Notas = ventaRequest.Notas ?? string.Empty
                };

                // Guardar en Cosmos DB
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentMisCasasVentasCosmosDB>();
                var ventasCosmosDB = new AgentMisCasasVentasCosmosDB(cosmosLogger, _configuration);

                var saveResult = await ventasCosmosDB.SaveCasaVentaAsync(venta, twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                if (!saveResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to save venta: {Error}", saveResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveCasaVentaResponse
                    {
                        Success = false,
                        ErrorMessage = saveResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Casa venta saved successfully. Document ID: {DocumentId}", saveResult.DocumentId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new SaveCasaVentaResponse
                {
                    Success = true,
                    TwinId = twinId,
                    DocumentId = saveResult.DocumentId,
                    Venta = venta,
                    RUConsumed = saveResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Venta de casa guardada exitosamente",
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
                _logger.LogError(ex, "❌ Error saving casa venta after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveCasaVentaResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al guardar la venta de casa"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Get Ventas Functions

        [Function("GetVentasByTwinOptions")]
        public async Task<HttpResponseData> HandleGetVentasByTwinOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-ventas-casas/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-ventas-casas/{TwinId}", twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetVentasByTwin")]
        public async Task<HttpResponseData> GetVentasByTwin(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-ventas-casas/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("📋 GetVentasByTwin function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentasResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting ventas for Twin ID: {TwinId}", twinId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentMisCasasVentasCosmosDB>();
                var ventasCosmosDB = new AgentMisCasasVentasCosmosDB(cosmosLogger, _configuration);

                var result = await ventasCosmosDB.GetVentasByTwinIdAsync(twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetVentasResponse
                {
                    Success = result.Success,
                    TwinId = twinId,
                    Ventas = result.Ventas,
                    VentaCount = result.VentaCount,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = result.Success 
                        ? $"Se encontraron {result.VentaCount} ventas" 
                        : "Error al obtener ventas",
                    ErrorMessage = result.Success ? null : result.ErrorMessage
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved {Count} ventas", result.VentaCount);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting ventas after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentasResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener ventas"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Get Ventas By MicrosoftOID Functions

        [Function("GetVentasByMicrosoftOIDOptions")]
        public async Task<HttpResponseData> HandleGetVentasByMicrosoftOIDOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-ventas-casas/{twinId}/microsoft/{microsoftOID}")] HttpRequestData req,
            string twinId,
            string microsoftOID)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-ventas-casas/{TwinId}/microsoft/{MicrosoftOID}", twinId, microsoftOID);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetVentasByMicrosoftOID")]
        public async Task<HttpResponseData> GetVentasByMicrosoftOID(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-ventas-casas/{twinId}/microsoft/{microsoftOID}")] HttpRequestData req,
            string twinId,
            string microsoftOID)
        {
            _logger.LogInformation("📋 GetVentasByMicrosoftOID function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentasResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(microsoftOID))
                {
                    _logger.LogError("❌ MicrosoftOID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentasResponse
                    {
                        Success = false,
                        ErrorMessage = "MicrosoftOID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting ventas for Twin ID: {TwinId}, MicrosoftOID: {MicrosoftOID}", twinId, microsoftOID);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentMisCasasVentasCosmosDB>();
                var ventasCosmosDB = new AgentMisCasasVentasCosmosDB(cosmosLogger, _configuration);

                var result = await ventasCosmosDB.GetVentasByTwinIdAndMicrosoftOIDAsync(twinId.ToLowerInvariant(), microsoftOID);

                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetVentasResponse
                {
                    Success = result.Success,
                    TwinId = twinId,
                    MicrosoftOID = microsoftOID,
                    Ventas = result.Ventas,
                    VentaCount = result.VentaCount,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = result.Success 
                        ? $"Se encontraron {result.VentaCount} ventas" 
                        : "Error al obtener ventas",
                    ErrorMessage = result.Success ? null : result.ErrorMessage
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved {Count} ventas for MicrosoftOID", result.VentaCount);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting ventas after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentasResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener ventas"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Get Ventas By ClienteComprador Functions

        [Function("GetVentasByClienteCompradorOptions")]
        public async Task<HttpResponseData> HandleGetVentasByClienteCompradorOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-ventas-casas/{twinId}/cliente/{clienteCompradorId}")] HttpRequestData req,
            string twinId,
            string clienteCompradorId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-ventas-casas/{TwinId}/cliente/{ClienteCompradorId}", twinId, clienteCompradorId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetVentasByClienteComprador")]
        public async Task<HttpResponseData> GetVentasByClienteComprador(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-ventas-casas/{twinId}/cliente/{clienteCompradorId}")] HttpRequestData req,
            string twinId,
            string clienteCompradorId)
        {
            _logger.LogInformation("📋 GetVentasByClienteComprador function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentasResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(clienteCompradorId))
                {
                    _logger.LogError("❌ ClienteCompradorId parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentasResponse
                    {
                        Success = false,
                        ErrorMessage = "ClienteCompradorId parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting ventas for Twin ID: {TwinId}, ClienteCompradorId: {ClienteCompradorId}", twinId, clienteCompradorId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentMisCasasVentasCosmosDB>();
                var ventasCosmosDB = new AgentMisCasasVentasCosmosDB(cosmosLogger, _configuration);

                var result = await ventasCosmosDB.GetVentasByTwinIdAndClienteCompradorAsync(twinId.ToLowerInvariant(), clienteCompradorId);

                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetVentasResponse
                {
                    Success = result.Success,
                    TwinId = twinId,
                    Ventas = result.Ventas,
                    VentaCount = result.VentaCount,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = result.Success 
                        ? $"Se encontraron {result.VentaCount} ventas para el cliente" 
                        : "Error al obtener ventas del cliente",
                    ErrorMessage = result.Success ? null : result.ErrorMessage
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved {Count} ventas for ClienteCompradorId", result.VentaCount);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting ventas after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentasResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener ventas del cliente"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Get Ventas By Estado Functions

        [Function("GetVentasByEstadoOptions")]
        public async Task<HttpResponseData> HandleGetVentasByEstadoOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-ventas-casas/{twinId}/estado/{estado}")] HttpRequestData req,
            string twinId,
            string estado)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-ventas-casas/{TwinId}/estado/{Estado}", twinId, estado);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetVentasByEstado")]
        public async Task<HttpResponseData> GetVentasByEstado(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-ventas-casas/{twinId}/estado/{estado}")] HttpRequestData req,
            string twinId,
            string estado)
        {
            _logger.LogInformation("📋 GetVentasByEstado function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentasResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(estado))
                {
                    _logger.LogError("❌ Estado parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentasResponse
                    {
                        Success = false,
                        ErrorMessage = "Estado parameter is required"
                    }));
                    return badResponse;
                }

                // Parse estado string to enum
                if (!Enum.TryParse<EstadoVenta>(estado, true, out var estadoVenta))
                {
                    _logger.LogError("❌ Invalid estado value: {Estado}", estado);
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentasResponse
                    {
                        Success = false,
                        ErrorMessage = $"Invalid estado value. Valid values: Prospectada, Aceptada, Agendada, Visitada, Vendida, Pagada"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting ventas for Twin ID: {TwinId}, Estado: {Estado}", twinId, estadoVenta);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentMisCasasVentasCosmosDB>();
                var ventasCosmosDB = new AgentMisCasasVentasCosmosDB(cosmosLogger, _configuration);

                var result = await ventasCosmosDB.GetVentasByEstadoAsync(twinId.ToLowerInvariant(), estadoVenta);

                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetVentasResponse
                {
                    Success = result.Success,
                    TwinId = twinId,
                    Ventas = result.Ventas,
                    VentaCount = result.VentaCount,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = result.Success 
                        ? $"Se encontraron {result.VentaCount} ventas en estado {estadoVenta}" 
                        : "Error al obtener ventas por estado",
                    ErrorMessage = result.Success ? null : result.ErrorMessage
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved {Count} ventas in estado {Estado}", result.VentaCount, estadoVenta);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting ventas by estado after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentasResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener ventas por estado"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Get Venta By ID Functions

        [Function("GetVentaByIdOptions")]
        public async Task<HttpResponseData> HandleGetVentaByIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-venta-casa/{twinId}/{ventaId}")] HttpRequestData req,
            string twinId,
            string ventaId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-venta-casa/{TwinId}/{VentaId}", twinId, ventaId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetVentaById")]
        public async Task<HttpResponseData> GetVentaById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-venta-casa/{twinId}/{ventaId}")] HttpRequestData req,
            string twinId,
            string ventaId)
        {
            _logger.LogInformation("📋 GetVentaById function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentaByIdResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(ventaId))
                {
                    _logger.LogError("❌ Venta ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentaByIdResponse
                    {
                        Success = false,
                        ErrorMessage = "Venta ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting venta ID: {VentaId} for Twin ID: {TwinId}", ventaId, twinId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentMisCasasVentasCosmosDB>();
                var ventasCosmosDB = new AgentMisCasasVentasCosmosDB(cosmosLogger, _configuration);

                var result = await ventasCosmosDB.GetVentaByIdAsync(ventaId, twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                if (!result.Success)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentaByIdResponse
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

                var responseData = new GetVentaByIdResponse
                {
                    Success = true,
                    Venta = result.Venta,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Venta encontrada exitosamente",
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved venta successfully");

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting venta after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetVentaByIdResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener la venta"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Update Estado Venta Functions

        [Function("UpdateEstadoVentaOptions")]
        public async Task<HttpResponseData> HandleUpdateEstadoVentaOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "update-estado-venta/{twinId}/{ventaId}/{nuevoEstado}")] HttpRequestData req,
            string twinId,
            string ventaId,
            string nuevoEstado)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for update-estado-venta/{TwinId}/{VentaId}/{NuevoEstado}", 
                twinId, ventaId, nuevoEstado);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("UpdateEstadoVenta")]
        public async Task<HttpResponseData> UpdateEstadoVenta(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "update-estado-venta/{twinId}/{ventaId}/{nuevoEstado}")] HttpRequestData req,
            string twinId,
            string ventaId,
            string nuevoEstado)
        {
            _logger.LogInformation("✏️ UpdateEstadoVenta function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateEstadoVentaResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(ventaId))
                {
                    _logger.LogError("❌ Venta ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateEstadoVentaResponse
                    {
                        Success = false,
                        ErrorMessage = "Venta ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(nuevoEstado))
                {
                    _logger.LogError("❌ Nuevo Estado parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateEstadoVentaResponse
                    {
                        Success = false,
                        ErrorMessage = "Nuevo Estado parameter is required"
                    }));
                    return badResponse;
                }

                // Parse estado string to enum
                if (!Enum.TryParse<EstadoVenta>(nuevoEstado, true, out var estadoVenta))
                {
                    _logger.LogError("❌ Invalid estado value: {Estado}", nuevoEstado);
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateEstadoVentaResponse
                    {
                        Success = false,
                        ErrorMessage = $"Invalid estado value. Valid values: Prospectada, Aceptada, Agendada, Visitada, Vendida, Pagada"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Updating estado for venta ID: {VentaId} to {NuevoEstado}", ventaId, estadoVenta);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentMisCasasVentasCosmosDB>();
                var ventasCosmosDB = new AgentMisCasasVentasCosmosDB(cosmosLogger, _configuration);

                var updateResult = await ventasCosmosDB.UpdateEstadoVentaAsync(ventaId, twinId.ToLowerInvariant(), estadoVenta);

                var processingTime = DateTime.UtcNow - startTime;

                if (!updateResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to update estado: {Error}", updateResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateEstadoVentaResponse
                    {
                        Success = false,
                        ErrorMessage = updateResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Estado updated successfully to {Estado}", estadoVenta);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new UpdateEstadoVentaResponse
                {
                    Success = true,
                    VentaId = ventaId,
                    TwinId = twinId,
                    VentaActualizada = updateResult.VentaActualizada,
                    RUConsumed = updateResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = $"Estado actualizado exitosamente a {estadoVenta}",
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
                _logger.LogError(ex, "❌ Error updating estado after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateEstadoVentaResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al actualizar el estado"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Update Venta Complete Functions

        [Function("UpdateVentaCompleteOptions")]
        public async Task<HttpResponseData> HandleUpdateVentaCompleteOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "update-venta-casa/{twinId}/{ventaId}")] HttpRequestData req,
            string twinId,
            string ventaId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for update-venta-casa/{TwinId}/{VentaId}", twinId, ventaId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("UpdateVentaComplete")]
        public async Task<HttpResponseData> UpdateVentaComplete(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "update-venta-casa/{twinId}/{ventaId}")] HttpRequestData req,
            string twinId,
            string ventaId)
        {
            _logger.LogInformation("✏️ UpdateVentaComplete function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateVentaResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(ventaId))
                {
                    _logger.LogError("❌ Venta ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateVentaResponse
                    {
                        Success = false,
                        ErrorMessage = "Venta ID parameter is required"
                    }));
                    return badResponse;
                }

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var updateRequest = JsonSerializer.Deserialize<UpdateVentaRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (updateRequest == null)
                {
                    _logger.LogError("❌ Failed to parse update request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateVentaResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid update request data format"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📋 Update details: Estado={Estado}, Propiedad={PropiedadId}", 
                    updateRequest.EstadoActual, updateRequest.PropiedadId);

                // Crear objeto CasaVenta actualizado
                var ventaActualizada = new CasaVenta
                {
                    ClienteCompradorId = updateRequest.ClienteCompradorId ?? string.Empty,
                    ClienteDuenoId = updateRequest.ClienteDuenoId ?? string.Empty,
                    PropiedadId = updateRequest.PropiedadId ?? string.Empty,
                    MicrosoftOID = updateRequest.MicrosoftOID ?? string.Empty,
                    EstadoActual = updateRequest.EstadoActual,
                    FechaProspectada = updateRequest.FechaProspectada,
                    FechaAceptada = updateRequest.FechaAceptada,
                    FechaAgendada = updateRequest.FechaAgendada,
                    FechaVisitada = updateRequest.FechaVisitada,
                    FechaVendida = updateRequest.FechaVendida,
                    FechaPagada = updateRequest.FechaPagada,
                    Notas = updateRequest.Notas ?? string.Empty
                };

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentMisCasasVentasCosmosDB>();
                var ventasCosmosDB = new AgentMisCasasVentasCosmosDB(cosmosLogger, _configuration);

                var updateResult = await ventasCosmosDB.UpdateVentaAsync(ventaId, twinId.ToLowerInvariant(), ventaActualizada);

                var processingTime = DateTime.UtcNow - startTime;

                if (!updateResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to update venta: {Error}", updateResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateVentaResponse
                    {
                        Success = false,
                        ErrorMessage = updateResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Venta updated successfully");

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new UpdateVentaResponse
                {
                    Success = true,
                    VentaId = ventaId,
                    TwinId = twinId,
                    VentaActualizada = updateResult.VentaActualizada,
                    RUConsumed = updateResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Venta actualizada exitosamente",
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
                _logger.LogError(ex, "❌ Error updating venta after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateVentaResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al actualizar la venta"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Delete Venta Functions

        [Function("DeleteVentaOptions")]
        public async Task<HttpResponseData> HandleDeleteVentaOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "delete-venta-casa/{twinId}/{ventaId}")] HttpRequestData req,
            string twinId,
            string ventaId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for delete-venta-casa/{TwinId}/{VentaId}", twinId, ventaId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("DeleteVenta")]
        public async Task<HttpResponseData> DeleteVenta(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "delete-venta-casa/{twinId}/{ventaId}")] HttpRequestData req,
            string twinId,
            string ventaId)
        {
            _logger.LogInformation("🗑️ DeleteVenta function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteVentaResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(ventaId))
                {
                    _logger.LogError("❌ Venta ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteVentaResponse
                    {
                        Success = false,
                        ErrorMessage = "Venta ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Deleting venta ID: {VentaId} for Twin ID: {TwinId}", ventaId, twinId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentMisCasasVentasCosmosDB>();
                var ventasCosmosDB = new AgentMisCasasVentasCosmosDB(cosmosLogger, _configuration);

                var deleteResult = await ventasCosmosDB.DeleteVentaAsync(ventaId, twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                if (!deleteResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to delete venta: {Error}", deleteResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteVentaResponse
                    {
                        Success = false,
                        ErrorMessage = deleteResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Venta deleted successfully");

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new DeleteVentaResponse
                {
                    Success = true,
                    VentaId = ventaId,
                    TwinId = twinId,
                    RUConsumed = deleteResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = deleteResult.Message ?? "Venta eliminada exitosamente",
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
                _logger.LogError(ex, "❌ Error deleting venta after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteVentaResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al eliminar la venta"
                }));

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

    #region Request/Response Models

    /// <summary>
    /// Request para guardar una venta de casa
    /// </summary>
    public class SaveCasaVentaRequest
    {
        public string ClienteCompradorId { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string ClienteDuenoId { get; set; } = string.Empty;
        public string PropiedadId { get; set; } = string.Empty;
        public string MicrosoftOID { get; set; } = string.Empty;
        public string EstadoActual { get; set; } = "Prospectada";
        public DateTime FechaProspectada { get; set; }
        public string Notas { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response de guardar una venta de casa
    /// </summary>
    public class SaveCasaVentaResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public CasaVenta? Venta { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Response de obtener ventas
    /// </summary>
    public class GetVentasResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string MicrosoftOID { get; set; } = string.Empty;
        public List<CasaVenta> Ventas { get; set; } = new();
        public int VentaCount { get; set; }
        public double RUConsumed { get; set; }
    }

    /// <summary>
    /// Response de obtener una venta por ID
    /// </summary>
    public class GetVentaByIdResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public CasaVenta? Venta { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Response de actualizar estado de venta
    /// </summary>
    public class UpdateEstadoVentaResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string VentaId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public CasaVenta? VentaActualizada { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Request para actualizar una venta completa
    /// </summary>
    public class UpdateVentaRequest
    {
        public string ClienteCompradorId { get; set; } = string.Empty;
        public string ClienteDuenoId { get; set; } = string.Empty;
        public string PropiedadId { get; set; } = string.Empty;
        public string MicrosoftOID { get; set; } = string.Empty;
        public string EstadoActual { get; set; } = "Prospectada";
        public DateTime FechaProspectada { get; set; }
        public DateTime FechaAceptada { get; set; }
        public DateTime FechaAgendada { get; set; }
        public DateTime FechaVisitada { get; set; }
        public DateTime FechaVendida { get; set; }
        public DateTime FechaPagada { get; set; }
        public string Notas { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response de actualizar una venta
    /// </summary>
    public class UpdateVentaResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string VentaId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public CasaVenta? VentaActualizada { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Response de eliminar una venta
    /// </summary>
    public class DeleteVentaResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string VentaId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

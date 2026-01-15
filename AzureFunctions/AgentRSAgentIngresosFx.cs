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
    /// Azure Functions para gestionar ingresos de agentes inmobiliarios (Real Estate Agents)
    /// </summary>
    public class AgentRSAgentIngresosFx
    {
        private readonly ILogger<AgentRSAgentIngresosFx> _logger;
        private readonly IConfiguration _configuration;

        public AgentRSAgentIngresosFx(ILogger<AgentRSAgentIngresosFx> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        #region Save Ingreso Agente Functions

        [Function("SaveIngresoAgenteOptions")]
        public async Task<HttpResponseData> HandleSaveIngresoAgenteOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "save-ingreso-agente/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for save-ingreso-agente/{TwinId}", twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("SaveIngresoAgente")]
        public async Task<HttpResponseData> SaveIngresoAgente(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "save-ingreso-agente/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("💵 SaveIngresoAgente function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveIngresoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Saving ingreso for Agent Twin ID: {TwinId}", twinId);

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var ingresoRequest = JsonSerializer.Deserialize<SaveIngresoAgenteRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (ingresoRequest == null)
                {
                    _logger.LogError("❌ Failed to parse ingreso request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveIngresoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid ingreso request data format"
                    }));
                    return badResponse;
                }

                // Validar campos requeridos
                if (string.IsNullOrEmpty(ingresoRequest.TipoIngreso))
                {
                    _logger.LogError("❌ TipoIngreso is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveIngresoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "TipoIngreso is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(ingresoRequest.MicrosoftOID))
                {
                    _logger.LogError("❌ MicrosoftOID is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveIngresoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "MicrosoftOID is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📋 Ingreso details: Tipo={TipoIngreso}, Cliente={Cliente}, MontoBase={MontoBase}, TotalNeto={TotalNeto}", 
                    ingresoRequest.TipoIngreso, ingresoRequest.Cliente, ingresoRequest.MontoBase, ingresoRequest.TotalNeto);

                // Crear objeto IngresoAgente
                var ingreso = new IngresoAgente
                {
                    TipoIngreso = ingresoRequest.TipoIngreso,
                    Descripcion = ingresoRequest.Descripcion ?? string.Empty,
                    Propiedad = ingresoRequest.Propiedad ?? string.Empty,
                    Cliente = ingresoRequest.Cliente ?? string.Empty,
                    MontoBase = ingresoRequest.MontoBase,
                    PorcentajeComision = ingresoRequest.PorcentajeComision,
                    MontoComision = ingresoRequest.MontoComision,
                    Bonos = ingresoRequest.Bonos,
                    Deducciones = ingresoRequest.Deducciones,
                    TotalNeto = ingresoRequest.TotalNeto,
                    MicrosoftOID = ingresoRequest.MicrosoftOID,
                    Fecha = ingresoRequest.Fecha,
                    Notas = ingresoRequest.Notas ?? string.Empty
                };

                // Guardar en Cosmos DB
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgenteRSAAgentIngresosCosmosDB>();
                var ingresosCosmosDB = new AgenteRSAAgentIngresosCosmosDB(cosmosLogger, _configuration);

                var saveResult = await ingresosCosmosDB.SaveIngresoAsync(ingreso, twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                if (!saveResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to save ingreso: {Error}", saveResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveIngresoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = saveResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Ingreso agente saved successfully. Document ID: {DocumentId}", saveResult.DocumentId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new SaveIngresoAgenteResponse
                {
                    Success = true,
                    TwinId = twinId,
                    DocumentId = saveResult.DocumentId,
                    Ingreso = ingreso,
                    RUConsumed = saveResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Ingreso de agente guardado exitosamente",
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
                _logger.LogError(ex, "❌ Error saving ingreso agente after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveIngresoAgenteResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al guardar el ingreso del agente"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Get Ingresos Agente Functions

        [Function("GetIngresosAgenteOptions")]
        public async Task<HttpResponseData> HandleGetIngresosAgenteOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-ingresos-agente/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-ingresos-agente/{TwinId}", twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetIngresosAgente")]
        public async Task<HttpResponseData> GetIngresosAgente(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-ingresos-agente/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("📋 GetIngresosAgente function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetIngresosAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting ingresos for Agent Twin ID: {TwinId}", twinId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgenteRSAAgentIngresosCosmosDB>();
                var ingresosCosmosDB = new AgenteRSAAgentIngresosCosmosDB(cosmosLogger, _configuration);

                var result = await ingresosCosmosDB.GetIngresosByTwinIdAsync(twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetIngresosAgenteResponse
                {
                    Success = result.Success,
                    TwinId = twinId,
                    Ingresos = result.Ingresos,
                    IngresoCount = result.IngresoCount,
                    TotalIngresos = result.TotalIngresos,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = result.Success 
                        ? $"Se encontraron {result.IngresoCount} ingresos. Total: ${result.TotalIngresos:N2}" 
                        : "Error al obtener ingresos",
                    ErrorMessage = result.Success ? null : result.ErrorMessage
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved {Count} ingresos. Total: ${Total:N2}", result.IngresoCount, result.TotalIngresos);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting ingresos after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetIngresosAgenteResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener ingresos del agente"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Get Ingresos Agente By MicrosoftOID Functions

        [Function("GetIngresosAgenteByMicrosoftOIDOptions")]
        public async Task<HttpResponseData> HandleGetIngresosAgenteByMicrosoftOIDOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-ingresos-agente/{twinId}/microsoft/{microsoftOID}")] HttpRequestData req,
            string twinId,
            string microsoftOID)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-ingresos-agente/{TwinId}/microsoft/{MicrosoftOID}", twinId, microsoftOID);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetIngresosAgenteByMicrosoftOID")]
        public async Task<HttpResponseData> GetIngresosAgenteByMicrosoftOID(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-ingresos-agente/{twinId}/microsoft/{microsoftOID}")] HttpRequestData req,
            string twinId,
            string microsoftOID)
        {
            _logger.LogInformation("📋 GetIngresosAgenteByMicrosoftOID function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetIngresosAgenteResponse
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
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetIngresosAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "MicrosoftOID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting ingresos for Agent Twin ID: {TwinId}, MicrosoftOID: {MicrosoftOID}", twinId, microsoftOID);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgenteRSAAgentIngresosCosmosDB>();
                var ingresosCosmosDB = new AgenteRSAAgentIngresosCosmosDB(cosmosLogger, _configuration);

                var result = await ingresosCosmosDB.GetIngresosByTwinIdAndMicrosoftOIDAsync(twinId.ToLowerInvariant(), microsoftOID);

                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetIngresosAgenteResponse
                {
                    Success = result.Success,
                    TwinId = twinId,
                    MicrosoftOID = microsoftOID,
                    Ingresos = result.Ingresos,
                    IngresoCount = result.IngresoCount,
                    TotalIngresos = result.TotalIngresos,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = result.Success 
                        ? $"Se encontraron {result.IngresoCount} ingresos. Total: ${result.TotalIngresos:N2}" 
                        : "Error al obtener ingresos",
                    ErrorMessage = result.Success ? null : result.ErrorMessage
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved {Count} ingresos for MicrosoftOID. Total: ${Total:N2}", result.IngresoCount, result.TotalIngresos);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting ingresos after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetIngresosAgenteResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener ingresos del agente"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Get Ingreso Agente By ID Functions

        [Function("GetIngresoAgenteByIdOptions")]
        public async Task<HttpResponseData> HandleGetIngresoAgenteByIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-ingreso-agente/{twinId}/{ingresoId}")] HttpRequestData req,
            string twinId,
            string ingresoId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-ingreso-agente/{TwinId}/{IngresoId}", twinId, ingresoId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetIngresoAgenteById")]
        public async Task<HttpResponseData> GetIngresoAgenteById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-ingreso-agente/{twinId}/{ingresoId}")] HttpRequestData req,
            string twinId,
            string ingresoId)
        {
            _logger.LogInformation("📋 GetIngresoAgenteById function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetIngresoAgenteByIdResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(ingresoId))
                {
                    _logger.LogError("❌ Ingreso ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetIngresoAgenteByIdResponse
                    {
                        Success = false,
                        ErrorMessage = "Ingreso ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting ingreso ID: {IngresoId} for Twin ID: {TwinId}", ingresoId, twinId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgenteRSAAgentIngresosCosmosDB>();
                var ingresosCosmosDB = new AgenteRSAAgentIngresosCosmosDB(cosmosLogger, _configuration);

                var result = await ingresosCosmosDB.GetIngresoByIdAsync(ingresoId, twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                if (!result.Success)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new GetIngresoAgenteByIdResponse
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

                var responseData = new GetIngresoAgenteByIdResponse
                {
                    Success = true,
                    Ingreso = result.Ingreso,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Ingreso encontrado exitosamente",
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved ingreso successfully");

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting ingreso after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetIngresoAgenteByIdResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener el ingreso"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Update Ingreso Agente Functions

        [Function("UpdateIngresoAgenteOptions")]
        public async Task<HttpResponseData> HandleUpdateIngresoAgenteOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "update-ingreso-agente/{twinId}/{ingresoId}")] HttpRequestData req,
            string twinId,
            string ingresoId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for update-ingreso-agente/{TwinId}/{IngresoId}", twinId, ingresoId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("UpdateIngresoAgente")]
        public async Task<HttpResponseData> UpdateIngresoAgente(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "update-ingreso-agente/{twinId}/{ingresoId}")] HttpRequestData req,
            string twinId,
            string ingresoId)
        {
            _logger.LogInformation("✏️ UpdateIngresoAgente function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateIngresoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(ingresoId))
                {
                    _logger.LogError("❌ Ingreso ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateIngresoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Ingreso ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Updating ingreso ID: {IngresoId} for Twin ID: {TwinId}", ingresoId, twinId);

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var updateRequest = JsonSerializer.Deserialize<UpdateIngresoAgenteRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (updateRequest == null)
                {
                    _logger.LogError("❌ Failed to parse update request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateIngresoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid update request data format"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📋 Update details: Tipo={TipoIngreso}, Cliente={Cliente}, TotalNeto={TotalNeto}", 
                    updateRequest.TipoIngreso, updateRequest.Cliente, updateRequest.TotalNeto);

                // Crear objeto IngresoAgente actualizado
                var ingresoActualizado = new IngresoAgente
                {
                    TipoIngreso = updateRequest.TipoIngreso ?? string.Empty,
                    Descripcion = updateRequest.Descripcion ?? string.Empty,
                    Propiedad = updateRequest.Propiedad ?? string.Empty,
                    Cliente = updateRequest.Cliente ?? string.Empty,
                    MontoBase = updateRequest.MontoBase,
                    PorcentajeComision = updateRequest.PorcentajeComision,
                    MontoComision = updateRequest.MontoComision,
                    Bonos = updateRequest.Bonos,
                    Deducciones = updateRequest.Deducciones,
                    TotalNeto = updateRequest.TotalNeto,
                    Fecha = updateRequest.Fecha,
                    Notas = updateRequest.Notas ?? string.Empty
                };

                // Actualizar en Cosmos DB
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgenteRSAAgentIngresosCosmosDB>();
                var ingresosCosmosDB = new AgenteRSAAgentIngresosCosmosDB(cosmosLogger, _configuration);

                var updateResult = await ingresosCosmosDB.UpdateIngresoAsync(ingresoId, twinId.ToLowerInvariant(), ingresoActualizado);

                var processingTime = DateTime.UtcNow - startTime;

                if (!updateResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to update ingreso: {Error}", updateResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateIngresoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = updateResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Ingreso updated successfully. Ingreso ID: {IngresoId}", ingresoId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new UpdateIngresoAgenteResponse
                {
                    Success = true,
                    IngresoId = ingresoId,
                    TwinId = twinId,
                    IngresoActualizado = updateResult.IngresoActualizado,
                    RUConsumed = updateResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Ingreso actualizado exitosamente",
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
                _logger.LogError(ex, "❌ Error updating ingreso after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateIngresoAgenteResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al actualizar el ingreso"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Delete Ingreso Agente Functions

        [Function("DeleteIngresoAgenteOptions")]
        public async Task<HttpResponseData> HandleDeleteIngresoAgenteOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "delete-ingreso-agente/{twinId}/{ingresoId}")] HttpRequestData req,
            string twinId,
            string ingresoId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for delete-ingreso-agente/{TwinId}/{IngresoId}", twinId, ingresoId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("DeleteIngresoAgente")]
        public async Task<HttpResponseData> DeleteIngresoAgente(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "delete-ingreso-agente/{twinId}/{ingresoId}")] HttpRequestData req,
            string twinId,
            string ingresoId)
        {
            _logger.LogInformation("🗑️ DeleteIngresoAgente function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteIngresoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(ingresoId))
                {
                    _logger.LogError("❌ Ingreso ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteIngresoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Ingreso ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Deleting ingreso ID: {IngresoId} for Twin ID: {TwinId}", ingresoId, twinId);

                // Eliminar de Cosmos DB
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgenteRSAAgentIngresosCosmosDB>();
                var ingresosCosmosDB = new AgenteRSAAgentIngresosCosmosDB(cosmosLogger, _configuration);

                var deleteResult = await ingresosCosmosDB.DeleteIngresoAsync(ingresoId, twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                if (!deleteResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to delete ingreso: {Error}", deleteResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteIngresoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = deleteResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Ingreso deleted successfully. Ingreso ID: {IngresoId}", ingresoId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new DeleteIngresoAgenteResponse
                {
                    Success = true,
                    IngresoId = ingresoId,
                    TwinId = twinId,
                    RUConsumed = deleteResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = deleteResult.Message ?? "Ingreso eliminado exitosamente",
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
                _logger.LogError(ex, "❌ Error deleting ingreso after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteIngresoAgenteResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al eliminar el ingreso"
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
    /// Request para guardar un ingreso de agente
    /// </summary>
    public class SaveIngresoAgenteRequest
    {
        public string TipoIngreso { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Propiedad { get; set; } = string.Empty;
        public string Cliente { get; set; } = string.Empty;
        public decimal MontoBase { get; set; }
        public decimal PorcentajeComision { get; set; }
        public decimal MontoComision { get; set; }
        public decimal Bonos { get; set; }
        public decimal Deducciones { get; set; }
        public decimal TotalNeto { get; set; }
        public string MicrosoftOID { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public string Notas { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response de guardar un ingreso de agente
    /// </summary>
    public class SaveIngresoAgenteResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public IngresoAgente? Ingreso { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Response de obtener ingresos de agente
    /// </summary>
    public class GetIngresosAgenteResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string MicrosoftOID { get; set; } = string.Empty;
        public List<IngresoAgente> Ingresos { get; set; } = new();
        public int IngresoCount { get; set; }
        public decimal TotalIngresos { get; set; }
        public double RUConsumed { get; set; }
    }

    /// <summary>
    /// Response de obtener un ingreso de agente por ID
    /// </summary>
    public class GetIngresoAgenteByIdResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public IngresoAgente? Ingreso { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Request para actualizar un ingreso de agente
    /// </summary>
    public class UpdateIngresoAgenteRequest
    {
        public string TipoIngreso { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Propiedad { get; set; } = string.Empty;
        public string Cliente { get; set; } = string.Empty;
        public decimal MontoBase { get; set; }
        public decimal PorcentajeComision { get; set; }
        public decimal MontoComision { get; set; }
        public decimal Bonos { get; set; }
        public decimal Deducciones { get; set; }
        public decimal TotalNeto { get; set; }
        public DateTime Fecha { get; set; }
        public string Notas { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response de actualizar un ingreso de agente
    /// </summary>
    public class UpdateIngresoAgenteResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string IngresoId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public IngresoAgente? IngresoActualizado { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Response de eliminar un ingreso de agente
    /// </summary>
    public class DeleteIngresoAgenteResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string IngresoId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

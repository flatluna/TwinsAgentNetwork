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
    /// Azure Functions para gestionar gastos de agentes inmobiliarios (Real Estate Agents)
    /// </summary>
    public class AgenteRSAgenteFX
    {
        private readonly ILogger<AgenteRSAgenteFX> _logger;
        private readonly IConfiguration _configuration;

        public AgenteRSAgenteFX(ILogger<AgenteRSAgenteFX> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        #region Save Gasto Agente Functions

        [Function("SaveGastoAgenteOptions")]
        public async Task<HttpResponseData> HandleSaveGastoAgenteOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "save-gasto-agente/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for save-gasto-agente/{TwinId}", twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("SaveGastoAgente")]
        public async Task<HttpResponseData> SaveGastoAgente(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "save-gasto-agente/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("💰 SaveGastoAgente function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveGastoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Saving gasto for Agent Twin ID: {TwinId}", twinId);

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var gastoRequest = JsonSerializer.Deserialize<SaveGastoAgenteRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (gastoRequest == null)
                {
                    _logger.LogError("❌ Failed to parse gasto request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveGastoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid gasto request data format"
                    }));
                    return badResponse;
                }

                // Validar campos requeridos
                if (string.IsNullOrEmpty(gastoRequest.TipoGasto))
                {
                    _logger.LogError("❌ TipoGasto is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveGastoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "TipoGasto is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📋 Gasto details: Tipo={TipoGasto}, Cantidad={Cantidad}, CostoUnitario={CostoUnitario}, IVA={IVA}", 
                    gastoRequest.TipoGasto, gastoRequest.Cantidad, gastoRequest.CostoUnitario, gastoRequest.IVA);

                // Crear objeto GastoAgente
                var gasto = new GastoAgente
                {
                    TipoGasto = gastoRequest.TipoGasto,
                    Descripcion = gastoRequest.Descripcion ?? string.Empty,
                    Unidad = gastoRequest.Unidad ?? string.Empty,
                    Cantidad = gastoRequest.Cantidad,
                    CostoUnitario = gastoRequest.CostoUnitario,
                    IVA = gastoRequest.IVA,
                    OtrosImpuestos = gastoRequest.OtrosImpuestos,
                    MicrosoftOID = gastoRequest.MicrosoftOID ?? string.Empty,
                    Fecha = gastoRequest.Fecha,
                    TwinID = twinId,
                    Notas = gastoRequest.Notas ?? string.Empty
                };

                // Guardar en Cosmos DB
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgenteRSAgentGastos>();
                var gastosCosmosDB = new AgenteRSAgentGastos(cosmosLogger, _configuration);

                var saveResult = await gastosCosmosDB.SaveGastoAsync(gasto, twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                if (!saveResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to save gasto: {Error}", saveResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveGastoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = saveResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Gasto agente saved successfully. Document ID: {DocumentId}", saveResult.DocumentId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new SaveGastoAgenteResponse
                {
                    Success = true,
                    TwinId = twinId,
                    DocumentId = saveResult.DocumentId,
                    Gasto = gasto,
                    RUConsumed = saveResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Gasto de agente guardado exitosamente",
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
                _logger.LogError(ex, "❌ Error saving gasto agente after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveGastoAgenteResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al guardar el gasto del agente"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Get Gastos Agente Functions

        [Function("GetGastosAgenteOptions")]
        public async Task<HttpResponseData> HandleGetGastosAgenteOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-gastos-agente/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-gastos-agente/{TwinId}", twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetGastosAgente")]
        public async Task<HttpResponseData> GetGastosAgente(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-gastos-agente/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("📋 GetGastosAgente function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetGastosAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting gastos for Agent Twin ID: {TwinId}", twinId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgenteRSAgentGastos>();
                var gastosCosmosDB = new AgenteRSAgentGastos(cosmosLogger, _configuration);

                var result = await gastosCosmosDB.GetGastosByTwinIdAsync(twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetGastosAgenteResponse
                {
                    Success = result.Success,
                    TwinId = twinId,
                    Gastos = result.Gastos,
                    GastoCount = result.GastoCount,
                    TotalGastos = result.TotalGastos,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = result.Success 
                        ? $"Se encontraron {result.GastoCount} gastos. Total: ${result.TotalGastos:N2}" 
                        : "Error al obtener gastos",
                    ErrorMessage = result.Success ? null : result.ErrorMessage
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved {Count} gastos. Total: ${Total:N2}", result.GastoCount, result.TotalGastos);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting gastos after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetGastosAgenteResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener gastos del agente"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Get Gastos Agente By MicrosoftOID Functions

        [Function("GetGastosAgenteByMicrosoftOIDOptions")]
        public async Task<HttpResponseData> HandleGetGastosAgenteByMicrosoftOIDOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-gastos-agente/{twinId}/microsoft/{microsoftOID}")] HttpRequestData req,
            string twinId,
            string microsoftOID)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-gastos-agente/{TwinId}/microsoft/{MicrosoftOID}", twinId, microsoftOID);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetGastosAgenteByMicrosoftOID")]
        public async Task<HttpResponseData> GetGastosAgenteByMicrosoftOID(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-gastos-agente/{twinId}/microsoft/{microsoftOID}")] HttpRequestData req,
            string twinId,
            string microsoftOID)
        {
            _logger.LogInformation("📋 GetGastosAgenteByMicrosoftOID function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetGastosAgenteResponse
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
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetGastosAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "MicrosoftOID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting gastos for Agent Twin ID: {TwinId}, MicrosoftOID: {MicrosoftOID}", twinId, microsoftOID);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgenteRSAgentGastos>();
                var gastosCosmosDB = new AgenteRSAgentGastos(cosmosLogger, _configuration);

                var result = await gastosCosmosDB.GetGastosByTwinIdAndMicrosoftOIDAsync(twinId.ToLowerInvariant(), microsoftOID);

                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetGastosAgenteResponse
                {
                    Success = result.Success,
                    TwinId = twinId,
                    MicrosoftOID = microsoftOID,
                    Gastos = result.Gastos,
                    GastoCount = result.GastoCount,
                    TotalGastos = result.TotalGastos,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = result.Success 
                        ? $"Se encontraron {result.GastoCount} gastos. Total: ${result.TotalGastos:N2}" 
                        : "Error al obtener gastos",
                    ErrorMessage = result.Success ? null : result.ErrorMessage
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved {Count} gastos for MicrosoftOID. Total: ${Total:N2}", result.GastoCount, result.TotalGastos);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting gastos after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetGastosAgenteResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener gastos del agente"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Get Gasto Agente By ID Functions

        [Function("GetGastoAgenteByIdOptions")]
        public async Task<HttpResponseData> HandleGetGastoAgenteByIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-gasto-agente/{twinId}/{gastoId}")] HttpRequestData req,
            string twinId,
            string gastoId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-gasto-agente/{TwinId}/{GastoId}", twinId, gastoId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetGastoAgenteById")]
        public async Task<HttpResponseData> GetGastoAgenteById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-gasto-agente/{twinId}/{gastoId}")] HttpRequestData req,
            string twinId,
            string gastoId)
        {
            _logger.LogInformation("📋 GetGastoAgenteById function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetGastoAgenteByIdResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(gastoId))
                {
                    _logger.LogError("❌ Gasto ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetGastoAgenteByIdResponse
                    {
                        Success = false,
                        ErrorMessage = "Gasto ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting gasto ID: {GastoId} for Twin ID: {TwinId}", gastoId, twinId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgenteRSAgentGastos>();
                var gastosCosmosDB = new AgenteRSAgentGastos(cosmosLogger, _configuration);

                var result = await gastosCosmosDB.GetGastoByIdAsync(gastoId, twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                if (!result.Success)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new GetGastoAgenteByIdResponse
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

                var responseData = new GetGastoAgenteByIdResponse
                {
                    Success = true,
                    Gasto = result.Gasto,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Gasto encontrado exitosamente",
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved gasto successfully");

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting gasto after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetGastoAgenteByIdResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener el gasto"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Update Gasto Agente Functions

        [Function("UpdateGastoAgenteOptions")]
        public async Task<HttpResponseData> HandleUpdateGastoAgenteOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "update-gasto-agente/{twinId}/{gastoId}")] HttpRequestData req,
            string twinId,
            string gastoId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for update-gasto-agente/{TwinId}/{GastoId}", twinId, gastoId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("UpdateGastoAgente")]
        public async Task<HttpResponseData> UpdateGastoAgente(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "update-gasto-agente/{twinId}/{gastoId}")] HttpRequestData req,
            string twinId,
            string gastoId)
        {
            _logger.LogInformation("✏️ UpdateGastoAgente function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateGastoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(gastoId))
                {
                    _logger.LogError("❌ Gasto ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateGastoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Gasto ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Updating gasto ID: {GastoId} for Twin ID: {TwinId}", gastoId, twinId);

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var updateRequest = JsonSerializer.Deserialize<UpdateGastoAgenteRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (updateRequest == null)
                {
                    _logger.LogError("❌ Failed to parse update request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateGastoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid update request data format"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📋 Update details: Tipo={TipoGasto}, Cantidad={Cantidad}, CostoUnitario={CostoUnitario}", 
                    updateRequest.TipoGasto, updateRequest.Cantidad, updateRequest.CostoUnitario);

                // Crear objeto GastoAgente actualizado
                var gastoActualizado = new GastoAgente
                {
                    TipoGasto = updateRequest.TipoGasto ?? string.Empty,
                    Descripcion = updateRequest.Descripcion ?? string.Empty,
                    Unidad = updateRequest.Unidad ?? string.Empty,
                    Cantidad = updateRequest.Cantidad,
                    CostoUnitario = updateRequest.CostoUnitario,
                    IVA = updateRequest.IVA,
                    OtrosImpuestos = updateRequest.OtrosImpuestos,
                    Fecha = updateRequest.Fecha,
                    Notas = updateRequest.Notas ?? string.Empty,
                    Total = updateRequest.Total
                };

                // Actualizar en Cosmos DB
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgenteRSAgentGastos>();
                var gastosCosmosDB = new AgenteRSAgentGastos(cosmosLogger, _configuration);

                var updateResult = await gastosCosmosDB.UpdateGastoAsync(gastoId, twinId.ToLowerInvariant(), gastoActualizado);

                var processingTime = DateTime.UtcNow - startTime;

                if (!updateResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to update gasto: {Error}", updateResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateGastoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = updateResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Gasto updated successfully. Gasto ID: {GastoId}", gastoId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new UpdateGastoAgenteResponse
                {
                    Success = true,
                    GastoId = gastoId,
                    TwinId = twinId,
                    GastoActualizado = updateResult.GastoActualizado,
                    RUConsumed = updateResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Gasto actualizado exitosamente",
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
                _logger.LogError(ex, "❌ Error updating gasto after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateGastoAgenteResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al actualizar el gasto"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Delete Gasto Agente Functions

        [Function("DeleteGastoAgenteOptions")]
        public async Task<HttpResponseData> HandleDeleteGastoAgenteOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "delete-gasto-agente/{twinId}/{gastoId}")] HttpRequestData req,
            string twinId,
            string gastoId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for delete-gasto-agente/{TwinId}/{GastoId}", twinId, gastoId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("DeleteGastoAgente")]
        public async Task<HttpResponseData> DeleteGastoAgente(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "delete-gasto-agente/{twinId}/{gastoId}")] HttpRequestData req,
            string twinId,
            string gastoId)
        {
            _logger.LogInformation("🗑️ DeleteGastoAgente function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteGastoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(gastoId))
                {
                    _logger.LogError("❌ Gasto ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteGastoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = "Gasto ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Deleting gasto ID: {GastoId} for Twin ID: {TwinId}", gastoId, twinId);

                // Eliminar de Cosmos DB
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgenteRSAgentGastos>();
                var gastosCosmosDB = new AgenteRSAgentGastos(cosmosLogger, _configuration);

                var deleteResult = await gastosCosmosDB.DeleteGastoAsync(gastoId, twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                if (!deleteResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to delete gasto: {Error}", deleteResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteGastoAgenteResponse
                    {
                        Success = false,
                        ErrorMessage = deleteResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Gasto deleted successfully. Gasto ID: {GastoId}", gastoId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new DeleteGastoAgenteResponse
                {
                    Success = true,
                    GastoId = gastoId,
                    TwinId = twinId,
                    RUConsumed = deleteResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = deleteResult.Message ?? "Gasto eliminado exitosamente",
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
                _logger.LogError(ex, "❌ Error deleting gasto after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteGastoAgenteResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al eliminar el gasto"
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
    /// Request para guardar un gasto de agente
    /// </summary>
    public class SaveGastoAgenteRequest
    {
        public string TipoGasto { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Unidad { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public decimal CostoUnitario { get; set; }
        public decimal IVA { get; set; }
        public decimal OtrosImpuestos { get; set; }
        public string MicrosoftOID { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public string Notas { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response de guardar un gasto de agente
    /// </summary>
    public class SaveGastoAgenteResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public GastoAgente? Gasto { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Response de obtener gastos de agente
    /// </summary>
    public class GetGastosAgenteResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string MicrosoftOID { get; set; } = string.Empty;
        public List<GastoAgente> Gastos { get; set; } = new();
        public int GastoCount { get; set; }
        public decimal TotalGastos { get; set; }
        public double RUConsumed { get; set; }
    }

    /// <summary>
    /// Response de obtener un gasto de agente por ID
    /// </summary>
    public class GetGastoAgenteByIdResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public GastoAgente? Gasto { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Request para actualizar un gasto de agente
    /// </summary>
    public class UpdateGastoAgenteRequest
    {
        public string TipoGasto { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Unidad { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public decimal CostoUnitario { get; set; }

        public string MicrosoftOID { get; set; } = string.Empty;

        public decimal Total { get; set; }
        public decimal IVA { get; set; }
        public decimal OtrosImpuestos { get; set; }
        public DateTime Fecha { get; set; }
        public string Notas { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response de actualizar un gasto de agente
    /// </summary>
    public class UpdateGastoAgenteResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string GastoId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public GastoAgente? GastoActualizado { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Response de eliminar un gasto de agente
    /// </summary>
    public class DeleteGastoAgenteResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string GastoId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

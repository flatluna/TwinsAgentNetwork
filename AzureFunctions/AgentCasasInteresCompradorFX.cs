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
    /// Azure Functions para gestionar intereses de compradores en propiedades
    /// Proporciona endpoints CRUD completos para la gestión de propiedades de interés
    /// </summary>
    public class AgentCasasInteresCompradorFX
    {
        private readonly ILogger<AgentCasasInteresCompradorFX> _logger;
        private readonly IConfiguration _configuration;

        public AgentCasasInteresCompradorFX(ILogger<AgentCasasInteresCompradorFX> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        #region Save Interes Functions

        [Function("SaveInteresOptions")]
        public async Task<HttpResponseData> HandleSaveInteresOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "save-interes-casa/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for save-interes-casa/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("SaveInteresCasa")]
        public async Task<HttpResponseData> SaveInteresCasa(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "save-interes-casa/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🏠 SaveInteresCasa function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveInteresResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Saving interés casa for Twin ID: {TwinId}", twinId);

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var interesRequest = JsonSerializer.Deserialize<CasaInteresRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (interesRequest == null)
                {
                    _logger.LogError("❌ Failed to parse interés request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveInteresResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid interés request data format"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(interesRequest.CustomerID))
                {
                    _logger.LogError("❌ CustomerID is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveInteresResponse
                    {
                        Success = false,
                        ErrorMessage = "CustomerID is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(interesRequest.UrlPropiedad))
                {
                    _logger.LogError("❌ UrlPropiedad is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveInteresResponse
                    {
                        Success = false,
                        ErrorMessage = "UrlPropiedad is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📋 Interés details: CustomerID={CustomerID}, URL={URL}, Estado={Estado}", 
                    interesRequest.CustomerID, interesRequest.UrlPropiedad, interesRequest.EstadoInteres);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentCasaCompraInteresCosmosDB>();
                var interesCosmosDB = new AgentCasaCompraInteresCosmosDB(cosmosLogger, _configuration);

                var saveResult = await interesCosmosDB.SaveInteresAsync(interesRequest, twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                if (!saveResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to save interés: {Error}", saveResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveInteresResponse
                    {
                        Success = false,
                        ErrorMessage = saveResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Interés saved successfully. Document ID: {DocumentId}", saveResult.DocumentId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new SaveInteresResponse
                {
                    Success = true,
                    TwinId = twinId,
                    DocumentId = saveResult.DocumentId,
                    Interes = interesRequest,
                    RUConsumed = saveResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Interés en propiedad guardado exitosamente",
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
                _logger.LogError(ex, "❌ Error saving interés after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveInteresResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al guardar el interés"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Get Intereses Functions

        [Function("GetInteresesByTwinOptions")]
        public async Task<HttpResponseData> HandleGetInteresesByTwinOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-intereses-casas/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-intereses-casas/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetInteresesByTwin")]
        public async Task<HttpResponseData> GetInteresesByTwin(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-intereses-casas/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("📋 GetInteresesByTwin function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetInteresesResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting intereses for Twin ID: {TwinId}", twinId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentCasaCompraInteresCosmosDB>();
                var interesCosmosDB = new AgentCasaCompraInteresCosmosDB(cosmosLogger, _configuration);

                var result = await interesCosmosDB.GetInteresesByTwinIdAsync(twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetInteresesResponse
                {
                    Success = result.Success,
                    TwinId = twinId,
                    Intereses = result.Intereses,
                    InteresCount = result.InteresCount,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = result.Success 
                        ? $"Se encontraron {result.InteresCount} intereses en propiedades" 
                        : "Error al obtener intereses",
                    ErrorMessage = result.Success ? null : result.ErrorMessage
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved {Count} intereses", result.InteresCount);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting intereses after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetInteresesResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener intereses"
                }));

                return errorResponse;
            }
        }

        [Function("GetInteresesByCustomerOptions")]
        public async Task<HttpResponseData> HandleGetInteresesByCustomerOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-intereses-casas/{twinId}/{customerId}")] HttpRequestData req,
            string twinId,
            string customerId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-intereses-casas/{TwinId}/{CustomerId}", twinId, customerId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetInteresesByCustomer")]
        public async Task<HttpResponseData> GetInteresesByCustomer(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-intereses-casas/{twinId}/{customerId}")] HttpRequestData req,
            string twinId,
            string customerId)
        {
            _logger.LogInformation("🔍 GetInteresesByCustomer function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetInteresesResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(customerId))
                {
                    _logger.LogError("❌ Customer ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetInteresesResponse
                    {
                        Success = false,
                        ErrorMessage = "Customer ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting intereses for Twin ID: {TwinId} and Customer ID: {CustomerId}", twinId, customerId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentCasaCompraInteresCosmosDB>();
                var interesCosmosDB = new AgentCasaCompraInteresCosmosDB(cosmosLogger, _configuration);

                var result = await interesCosmosDB.GetInteresesByTwinIdAndCustomerIdAsync(twinId.ToLowerInvariant(), customerId);

                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetInteresesResponse
                {
                    Success = result.Success,
                    TwinId = twinId,
                    CustomerId = customerId,
                    Intereses = result.Intereses,
                    InteresCount = result.InteresCount,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = result.Success 
                        ? $"Se encontraron {result.InteresCount} intereses para el cliente" 
                        : "Error al obtener intereses del cliente",
                    ErrorMessage = result.Success ? null : result.ErrorMessage
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved {Count} intereses for customer {CustomerId}", result.InteresCount, customerId);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting intereses for customer after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetInteresesResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener intereses del cliente"
                }));

                return errorResponse;
            }
        }

        [Function("GetInteresesByOIDOptions")]
        public async Task<HttpResponseData> HandleGetInteresesByOIDOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-intereses-casas-by-oid/{twinId}/{microsoftOID}")] HttpRequestData req,
            string twinId,
            string microsoftOID)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for get-intereses-casas-by-oid/{TwinId}/{MicrosoftOID}", twinId, microsoftOID);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetInteresesByOID")]
        public async Task<HttpResponseData> GetInteresesByOID(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-intereses-casas-by-oid/{twinId}/{microsoftOID}")] HttpRequestData req,
            string twinId,
            string microsoftOID)
        {
            _logger.LogInformation("🔐 GetInteresesByOID function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetInteresesByOIDResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(microsoftOID))
                {
                    _logger.LogError("❌ Microsoft OID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetInteresesByOIDResponse
                    {
                        Success = false,
                        ErrorMessage = "Microsoft OID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Getting intereses by Microsoft OID: {MicrosoftOID} for Twin ID: {TwinId}", microsoftOID, twinId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentCasaCompraInteresCosmosDB>();
                var interesCosmosDB = new AgentCasaCompraInteresCosmosDB(cosmosLogger, _configuration);

                var result = await interesCosmosDB.GetInteresesByMicrosoftOIDAsync(twinId.ToLowerInvariant(), microsoftOID);

                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GetInteresesByOIDResponse
                {
                    Success = result.Success,
                    TwinId = twinId,
                    MicrosoftOID = microsoftOID,
                    Intereses = result.Intereses,
                    InteresCount = result.InteresCount,
                    RUConsumed = result.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = result.Success 
                        ? $"Se encontraron {result.InteresCount} intereses para el usuario" 
                        : "Error al obtener intereses del usuario",
                    ErrorMessage = result.Success ? null : result.ErrorMessage
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Retrieved {Count} intereses for Microsoft OID {MicrosoftOID}", result.InteresCount, microsoftOID);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error getting intereses by Microsoft OID after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetInteresesByOIDResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al obtener intereses por Microsoft OID"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Update Interes Functions

        [Function("UpdateInteresOptions")]
        public async Task<HttpResponseData> HandleUpdateInteresOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "update-interes-casa/{twinId}/{interesId}")] HttpRequestData req,
            string twinId,
            string interesId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for update-interes-casa/{TwinId}/{InteresId}", twinId, interesId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("UpdateInteresCasa")]
        public async Task<HttpResponseData> UpdateInteresCasa(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "update-interes-casa/{twinId}/{interesId}")] HttpRequestData req,
            string twinId,
            string interesId)
        {
            _logger.LogInformation("✏️ UpdateInteresCasa function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateInteresResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(interesId))
                {
                    _logger.LogError("❌ Interés ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateInteresResponse
                    {
                        Success = false,
                        ErrorMessage = "Interés ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Updating interés ID: {InteresId} for Twin ID: {TwinId}", interesId, twinId);

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var interesRequest = JsonSerializer.Deserialize<CasaInteresRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (interesRequest == null)
                {
                    _logger.LogError("❌ Failed to parse interés request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateInteresResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid interés request data format"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📋 Update details: Estado={Estado}, AgendarVisita={AgendarVisita}", 
                    interesRequest.EstadoInteres, interesRequest.AgendarVisita);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentCasaCompraInteresCosmosDB>();
                var interesCosmosDB = new AgentCasaCompraInteresCosmosDB(cosmosLogger, _configuration);

                var updateResult = await interesCosmosDB.UpdateInteresAsync(interesId, twinId.ToLowerInvariant(), interesRequest);

                var processingTime = DateTime.UtcNow - startTime;

                if (!updateResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to update interés: {Error}", updateResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateInteresResponse
                    {
                        Success = false,
                        ErrorMessage = updateResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Interés updated successfully. Interés ID: {InteresId}", interesId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new UpdateInteresResponse
                {
                    Success = true,
                    InteresId = interesId,
                    TwinId = twinId,
                    InteresActualizado = updateResult.InteresActualizado,
                    RUConsumed = updateResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Interés actualizado exitosamente",
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
                _logger.LogError(ex, "❌ Error updating interés after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateInteresResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al actualizar el interés"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Delete Interes Functions

        [Function("DeleteInteresOptions")]
        public async Task<HttpResponseData> HandleDeleteInteresOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "delete-interes-casa/{twinId}/{interesId}")] HttpRequestData req,
            string twinId,
            string interesId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for delete-interes-casa/{TwinId}/{InteresId}", twinId, interesId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("DeleteInteresCasa")]
        public async Task<HttpResponseData> DeleteInteresCasa(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "delete-interes-casa/{twinId}/{interesId}")] HttpRequestData req,
            string twinId,
            string interesId)
        {
            _logger.LogInformation("🗑️ DeleteInteresCasa function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteInteresResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(interesId))
                {
                    _logger.LogError("❌ Interés ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteInteresResponse
                    {
                        Success = false,
                        ErrorMessage = "Interés ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("📂 Deleting interés ID: {InteresId} for Twin ID: {TwinId}", interesId, twinId);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var cosmosLogger = loggerFactory.CreateLogger<AgentCasaCompraInteresCosmosDB>();
                var interesCosmosDB = new AgentCasaCompraInteresCosmosDB(cosmosLogger, _configuration);

                var deleteResult = await interesCosmosDB.DeleteInteresAsync(interesId, twinId.ToLowerInvariant());

                var processingTime = DateTime.UtcNow - startTime;

                if (!deleteResult.Success)
                {
                    _logger.LogWarning("⚠️ Failed to delete interés: {Error}", deleteResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteInteresResponse
                    {
                        Success = false,
                        ErrorMessage = deleteResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Interés deleted successfully. Interés ID: {InteresId}", interesId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new DeleteInteresResponse
                {
                    Success = true,
                    InteresId = interesId,
                    TwinId = twinId,
                    RUConsumed = deleteResult.RUConsumed,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = deleteResult.Message ?? "Interés eliminado exitosamente",
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
                _logger.LogError(ex, "❌ Error deleting interés after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteInteresResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al eliminar el interés"
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

    #region Response Models

    public class SaveInteresResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public CasaInteresRequest? Interes { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class GetInteresesResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string? CustomerId { get; set; }
        public System.Collections.Generic.List<CasaInteresRequest> Intereses { get; set; } = new();
        public int InteresCount { get; set; }
        public double RUConsumed { get; set; }
    }

    public class UpdateInteresResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string InteresId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public CasaInteresRequest? InteresActualizado { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class DeleteInteresResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string InteresId { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class GetInteresesByOIDResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string MicrosoftOID { get; set; } = string.Empty;
        public System.Collections.Generic.List<CasaInteresRequest> Intereses { get; set; } = new();
        public int InteresCount { get; set; }
        public double RUConsumed { get; set; }
    }

    #endregion
}

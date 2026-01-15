using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.AzureFunctions;

public class AgentCustomerCompradorFX
{
    private readonly ILogger<AgentCustomerCompradorFX> _logger;
    private readonly IConfiguration _configuration;

    public AgentCustomerCompradorFX(ILogger<AgentCustomerCompradorFX> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [Function("SaveCompradorOptions")]
    public async Task<HttpResponseData> HandleSaveCompradorOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "save-comprador/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for save-comprador/{TwinId}", twinId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("SaveComprador")]
    public async Task<HttpResponseData> SaveComprador(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "save-comprador/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("👤 SaveComprador function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveCompradorResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Saving comprador for Twin ID: {TwinId}", twinId);

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

            var compradorRequest = JsonSerializer.Deserialize<CompradorRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (compradorRequest == null)
            {
                _logger.LogError("❌ Failed to parse comprador request data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveCompradorResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid comprador request data format"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(compradorRequest.Nombre))
            {
                _logger.LogError("❌ Nombre is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveCompradorResponse
                {
                    Success = false,
                    ErrorMessage = "Nombre is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📋 Comprador details: Nombre={Nombre}, Apellido={Apellido}, Email={Email}", 
                compradorRequest.Nombre, compradorRequest.Apellido, compradorRequest.Email);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<AentCustomerBuyerCosmosDB>();
            var compradorCosmosDB = new AentCustomerBuyerCosmosDB(cosmosLogger, _configuration);

            var saveResult = await compradorCosmosDB.SaveCompradorAsync(compradorRequest, twinId.ToLowerInvariant());

            var processingTime = DateTime.UtcNow - startTime;

            if (!saveResult.Success)
            {
                _logger.LogWarning("⚠️ Failed to save comprador: {Error}", saveResult.ErrorMessage);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveCompradorResponse
                {
                    Success = false,
                    ErrorMessage = saveResult.ErrorMessage,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return errorResponse;
            }

            _logger.LogInformation("✅ Comprador saved successfully. Document ID: {DocumentId}", saveResult.DocumentId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new SaveCompradorResponse
            {
                Success = true,
                TwinId = twinId,
                DocumentId = saveResult.DocumentId,
                Comprador = compradorRequest,
                RUConsumed = saveResult.RUConsumed,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Comprador guardado exitosamente",
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
            _logger.LogError(ex, "❌ Error saving comprador after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveCompradorResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error al guardar el comprador"
            }));

            return errorResponse;
        }
    }

    [Function("GetCompradoresOptions")]
    public async Task<HttpResponseData> HandleGetCompradoresOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-compradores/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for get-compradores/{TwinId}", twinId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("GetCompradores")]
    public async Task<HttpResponseData> GetCompradores(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-compradores/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("📋 GetCompradores function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetCompradoresResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Getting compradores for Twin ID: {TwinId}", twinId);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<AentCustomerBuyerCosmosDB>();
            var compradorCosmosDB = new AentCustomerBuyerCosmosDB(cosmosLogger, _configuration);

            var result = await compradorCosmosDB.GetCompradoresByTwinIdAsync(twinId.ToLowerInvariant());

            var processingTime = DateTime.UtcNow - startTime;

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new GetCompradoresResponse
            {
                Success = result.Success,
                TwinId = twinId,
                Compradores = result.Compradores,
                CompradorCount = result.CompradorCount,
                RUConsumed = result.RUConsumed,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = result.Success 
                    ? $"Se encontraron {result.CompradorCount} compradores" 
                    : "Error al obtener compradores",
                ErrorMessage = result.Success ? null : result.ErrorMessage
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            _logger.LogInformation("✅ Retrieved {Count} compradores", result.CompradorCount);

            return response;
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "❌ Error getting compradores after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetCompradoresResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error al obtener compradores"
            }));

            return errorResponse;
        }
    }

    [Function("GetCompradorByIdOptions")]
    public async Task<HttpResponseData> HandleGetCompradorByIdOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-comprador/{twinId}/{compradorId}")] HttpRequestData req,
        string twinId,
        string compradorId)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for get-comprador/{TwinId}/{CompradorId}", twinId, compradorId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("GetCompradorById")]
    public async Task<HttpResponseData> GetCompradorById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-comprador/{twinId}/{compradorId}")] HttpRequestData req,
        string twinId,
        string compradorId)
    {
        _logger.LogInformation("🔍 GetCompradorById function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetCompradorByIdResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(compradorId))
            {
                _logger.LogError("❌ Comprador ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetCompradorByIdResponse
                {
                    Success = false,
                    ErrorMessage = "Comprador ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Getting comprador ID: {CompradorId} for Twin ID: {TwinId}", compradorId, twinId);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<AentCustomerBuyerCosmosDB>();
            var compradorCosmosDB = new AentCustomerBuyerCosmosDB(cosmosLogger, _configuration);

            var result = await compradorCosmosDB.GetCompradorByIdAsync(compradorId, twinId.ToLowerInvariant());

            var processingTime = DateTime.UtcNow - startTime;

            if (!result.Success)
            {
                _logger.LogWarning("⚠️ Comprador not found: {CompradorId}", compradorId);
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                AddCorsHeaders(notFoundResponse, req);
                await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new GetCompradorByIdResponse
                {
                    Success = false,
                    ErrorMessage = result.ErrorMessage,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Comprador no encontrado"
                }));
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new GetCompradorByIdResponse
            {
                Success = true,
                TwinId = twinId,
                CompradorId = compradorId,
                Comprador = result.Comprador,
                RUConsumed = result.RUConsumed,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Comprador encontrado exitosamente",
                Timestamp = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            _logger.LogInformation("✅ Retrieved comprador: {CompradorId}, Nombre: {Nombre} {Apellido}", 
                compradorId, result.Comprador?.Nombre, result.Comprador?.Apellido);

            return response;
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "❌ Error getting comprador by ID after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetCompradorByIdResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error al obtener el comprador"
            }));

            return errorResponse;
        }
    }

    [Function("GetCompradorByOIDOptions")]
    public async Task<HttpResponseData> HandleGetCompradorByOIDOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-comprador-by-oid/{twinId}/{microsoftOID}")] HttpRequestData req,
        string twinId,
        string microsoftOID)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for get-comprador-by-oid/{TwinId}/{MicrosoftOID}", twinId, microsoftOID);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("GetCompradorByOID")]
    public async Task<HttpResponseData> GetCompradorByOID(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-comprador-by-oid/{twinId}/{microsoftOID}")] HttpRequestData req,
        string twinId,
        string microsoftOID)
    {
        _logger.LogInformation("🔐 GetCompradorByOID function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetCompradorByOIDResponse
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
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetCompradorByOIDResponse
                {
                    Success = false,
                    ErrorMessage = "Microsoft OID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Getting comprador by Microsoft OID: {MicrosoftOID} for Twin ID: {TwinId}", microsoftOID, twinId);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<AentCustomerBuyerCosmosDB>();
            var compradorCosmosDB = new AentCustomerBuyerCosmosDB(cosmosLogger, _configuration);

            var result = await compradorCosmosDB.GetCompradorByMicrosoftOIDAsync(microsoftOID, twinId.ToLowerInvariant());

            var processingTime = DateTime.UtcNow - startTime;

            if (!result.Success)
            {
                _logger.LogWarning("⚠️ Comprador not found with Microsoft OID: {MicrosoftOID}", microsoftOID);
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                AddCorsHeaders(notFoundResponse, req);
                await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new GetCompradorByOIDResponse
                {
                    Success = false,
                    ErrorMessage = result.ErrorMessage,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Comprador no encontrado con el Microsoft OID proporcionado"
                }));
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new GetCompradorByOIDResponse
            {
                Success = true,
                TwinId = twinId,
                MicrosoftOID = microsoftOID,
                Comprador = result.Comprador,
                RUConsumed = result.RUConsumed,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Comprador encontrado exitosamente por Microsoft OID",
                Timestamp = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            _logger.LogInformation("✅ Retrieved comprador by Microsoft OID: {MicrosoftOID}, Nombre: {Nombre} {Apellido}", 
                microsoftOID, result.Comprador?.Nombre, result.Comprador?.Apellido);

            return response;
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "❌ Error getting comprador by Microsoft OID after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetCompradorByOIDResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error al obtener el comprador por Microsoft OID"
            }));

            return errorResponse;
        }
    }

    [Function("UpdateCompradorOptions")]
    public async Task<HttpResponseData> HandleUpdateCompradorOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "update-comprador/{twinId}/{compradorId}")] HttpRequestData req,
        string twinId,
        string compradorId)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for update-comprador/{TwinId}/{CompradorId}", twinId, compradorId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("UpdateComprador")]
    public async Task<HttpResponseData> UpdateComprador(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "update-comprador/{twinId}/{compradorId}")] HttpRequestData req,
        string twinId,
        string compradorId)
    {
        _logger.LogInformation("✏️ UpdateComprador function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateCompradorResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(compradorId))
            {
                _logger.LogError("❌ Comprador ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateCompradorResponse
                {
                    Success = false,
                    ErrorMessage = "Comprador ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Updating comprador ID: {CompradorId} for Twin ID: {TwinId}", compradorId, twinId);

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

            var compradorRequest = JsonSerializer.Deserialize<CompradorRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (compradorRequest == null)
            {
                _logger.LogError("❌ Failed to parse comprador request data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateCompradorResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid comprador request data format"
                }));
                return badResponse;
            }

            _logger.LogInformation("📋 Update details: Nombre={Nombre}, Apellido={Apellido}, Email={Email}", 
                compradorRequest.Nombre, compradorRequest.Apellido, compradorRequest.Email);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<AentCustomerBuyerCosmosDB>();
            var compradorCosmosDB = new AentCustomerBuyerCosmosDB(cosmosLogger, _configuration);

            var updateResult = await compradorCosmosDB.UpdateCompradorAsync(compradorId, twinId.ToLowerInvariant(), compradorRequest);

            var processingTime = DateTime.UtcNow - startTime;

            if (!updateResult.Success)
            {
                _logger.LogWarning("⚠️ Failed to update comprador: {Error}", updateResult.ErrorMessage);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateCompradorResponse
                {
                    Success = false,
                    ErrorMessage = updateResult.ErrorMessage,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return errorResponse;
            }

            _logger.LogInformation("✅ Comprador updated successfully. Comprador ID: {CompradorId}", compradorId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new UpdateCompradorResponse
            {
                Success = true,
                CompradorId = compradorId,
                TwinId = twinId,
                CompradorActualizado = updateResult.CompradorActualizado,
                RUConsumed = updateResult.RUConsumed,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Comprador actualizado exitosamente",
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
            _logger.LogError(ex, "❌ Error updating comprador after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateCompradorResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error al actualizar el comprador"
            }));

            return errorResponse;
        }
    }

    [Function("DeleteCompradorOptions")]
    public async Task<HttpResponseData> HandleDeleteCompradorOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "delete-comprador/{twinId}/{compradorId}")] HttpRequestData req,
        string twinId,
        string compradorId)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for delete-comprador/{TwinId}/{CompradorId}", twinId, compradorId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("DeleteComprador")]
    public async Task<HttpResponseData> DeleteComprador(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "delete-comprador/{twinId}/{compradorId}")] HttpRequestData req,
        string twinId,
        string compradorId)
    {
        _logger.LogInformation("🗑️ DeleteComprador function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteCompradorResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(compradorId))
            {
                _logger.LogError("❌ Comprador ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteCompradorResponse
                {
                    Success = false,
                    ErrorMessage = "Comprador ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Deleting comprador ID: {CompradorId} for Twin ID: {TwinId}", compradorId, twinId);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<AentCustomerBuyerCosmosDB>();
            var compradorCosmosDB = new AentCustomerBuyerCosmosDB(cosmosLogger, _configuration);

            var deleteResult = await compradorCosmosDB.DeleteCompradorAsync(compradorId, twinId.ToLowerInvariant());

            var processingTime = DateTime.UtcNow - startTime;

            if (!deleteResult.Success)
            {
                _logger.LogWarning("⚠️ Failed to delete comprador: {Error}", deleteResult.ErrorMessage);
                var errorResponse = req.CreateResponse(HttpStatusCode.NotFound);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteCompradorResponse
                {
                    Success = false,
                    ErrorMessage = deleteResult.ErrorMessage,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return errorResponse;
            }

            _logger.LogInformation("✅ Comprador deleted successfully. Comprador ID: {CompradorId}", compradorId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new DeleteCompradorResponse
            {
                Success = true,
                CompradorId = compradorId,
                TwinId = twinId,
                RUConsumed = deleteResult.RUConsumed,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = deleteResult.Message ?? "Comprador eliminado exitosamente",
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
            _logger.LogError(ex, "❌ Error deleting comprador after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteCompradorResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error al eliminar el comprador"
            }));

            return errorResponse;
        }
    }

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
}

public class SaveCompradorResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public string TwinId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty; 

    public string AgenteInmobiiarioId { get; set; } = string.Empty;
     
    public string AgenteInmobiiarioTwinID { get; set; } = string.Empty;
    public CompradorRequest? Comprador { get; set; }
    public double RUConsumed { get; set; }
    public DateTime Timestamp { get; set; }
}

public class GetCompradoresResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public string TwinId { get; set; } = string.Empty;
    public List<CompradorRequest> Compradores { get; set; } = new();
    public int CompradorCount { get; set; }
    public double RUConsumed { get; set; }
}

public class UpdateCompradorResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public string CompradorId { get; set; } = string.Empty;


    public string AgenteInmobiiarioId { get; set; } = string.Empty;


    public string AgenteInmobiiarioTwinID { get; set; } = string.Empty;
    public string TwinId { get; set; } = string.Empty;
    public CompradorRequest? CompradorActualizado { get; set; }
    public double RUConsumed { get; set; }
    public DateTime Timestamp { get; set; }
}

public class DeleteCompradorResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public string CompradorId { get; set; } = string.Empty;
    public string TwinId { get; set; } = string.Empty;
    public double RUConsumed { get; set; }
    public DateTime Timestamp { get; set; }
}

public class GetCompradorByIdResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public string TwinId { get; set; } = string.Empty;
    public string CompradorId { get; set; } = string.Empty;
    public CompradorRequest? Comprador { get; set; }
    public double RUConsumed { get; set; }
    public DateTime Timestamp { get; set; }
}

public class GetCompradorByOIDResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public string TwinId { get; set; } = string.Empty;
    public string MicrosoftOID { get; set; } = string.Empty;
    public CompradorRequest? Comprador { get; set; }
    public double RUConsumed { get; set; }
    public DateTime Timestamp { get; set; }
}

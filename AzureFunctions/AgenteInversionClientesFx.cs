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

namespace TwinAgentsNetwork.AzureFunctions;

/// <summary>
/// Azure Functions para gestionar inversiones/gastos de clientes inmobiliarios
/// </summary>
public class AgenteInversionClientesFx
{
    private readonly ILogger<AgenteInversionClientesFx> _logger;
    private readonly IConfiguration _configuration;

    public AgenteInversionClientesFx(ILogger<AgenteInversionClientesFx> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    #region Save Gasto Functions

    [Function("SaveGastoClienteOptions")]
    public async Task<HttpResponseData> HandleSaveGastoClienteOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "save-gasto-cliente/{twinId}/{customerId}")] HttpRequestData req,
        string twinId,
        string customerId)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for save-gasto-cliente/{TwinId}/{CustomerId}", twinId, customerId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("SaveGastoCliente")]
    public async Task<HttpResponseData> SaveGastoCliente(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "save-gasto-cliente/{twinId}/{customerId}")] HttpRequestData req,
        string twinId,
        string customerId)
    {
        _logger.LogInformation("💰 SaveGastoCliente function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveGastoResponse
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
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveGastoResponse
                {
                    Success = false,
                    ErrorMessage = "Customer ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Saving gasto for Twin ID: {TwinId}, Customer ID: {CustomerId}", twinId, customerId);

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

            var gastoRequest = JsonSerializer.Deserialize<SaveGastoRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (gastoRequest == null)
            {
                _logger.LogError("❌ Failed to parse gasto request data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveGastoResponse
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
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveGastoResponse
                {
                    Success = false,
                    ErrorMessage = "TipoGasto is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📋 Gasto details: Tipo={TipoGasto}, Cantidad={Cantidad}, CostoUnitario={CostoUnitario}", 
                gastoRequest.TipoGasto, gastoRequest.Cantidad, gastoRequest.CostoUnitario);

            // Crear objeto GastoInversion
            var gasto = new GastoInversion
            {
                TipoGasto = gastoRequest.TipoGasto,
                Descripcion = gastoRequest.Descripcion ?? string.Empty,
                Unidad = gastoRequest.Unidad ?? string.Empty,
                Cantidad = gastoRequest.Cantidad,
                CostoUnitario = gastoRequest.CostoUnitario,
                MicrosoftOID = gastoRequest.MicrosoftOID ?? string.Empty,
                Fecha = gastoRequest.Fecha,
                Notas = gastoRequest.Notas ?? string.Empty,
                Total = gastoRequest.Total > 0 ? gastoRequest.Total : gastoRequest.Cantidad * gastoRequest.CostoUnitario
            };

            // Guardar en Cosmos DB
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<ClienteInversionesCosmosDB>();
            var inversionesCosmosDB = new ClienteInversionesCosmosDB(cosmosLogger, _configuration);

            var saveResult = await inversionesCosmosDB.SaveGastoAsync(gasto, twinId.ToLowerInvariant(), customerId);

            var processingTime = DateTime.UtcNow - startTime;

            if (!saveResult.Success)
            {
                _logger.LogWarning("⚠️ Failed to save gasto: {Error}", saveResult.ErrorMessage);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveGastoResponse
                {
                    Success = false,
                    ErrorMessage = saveResult.ErrorMessage,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return errorResponse;
            }

            _logger.LogInformation("✅ Gasto saved successfully. Document ID: {DocumentId}", saveResult.DocumentId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new SaveGastoResponse
            {
                Success = true,
                TwinId = twinId,
                CustomerId = customerId,
                DocumentId = saveResult.DocumentId,
                Gasto = gasto,
                RUConsumed = saveResult.RUConsumed,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Gasto guardado exitosamente",
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
            _logger.LogError(ex, "❌ Error saving gasto after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new SaveGastoResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error al guardar el gasto"
            }));

            return errorResponse;
        }
    }

    #endregion

    #region Get Gastos Functions

    [Function("GetGastosClienteOptions")]
    public async Task<HttpResponseData> HandleGetGastosClienteOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-gastos-cliente/{twinId}/{customerId}")] HttpRequestData req,
        string twinId,
        string customerId)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for get-gastos-cliente/{TwinId}/{CustomerId}", twinId, customerId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("GetGastosCliente")]
    public async Task<HttpResponseData> GetGastosCliente(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-gastos-cliente/{twinId}/{customerId}")] HttpRequestData req,
        string twinId,
        string customerId)
    {
        _logger.LogInformation("📋 GetGastosCliente function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetGastosResponse
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
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetGastosResponse
                {
                    Success = false,
                    ErrorMessage = "Customer ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Getting gastos for Twin ID: {TwinId}, Customer ID: {CustomerId}", twinId, customerId);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<ClienteInversionesCosmosDB>();
            var inversionesCosmosDB = new ClienteInversionesCosmosDB(cosmosLogger, _configuration);

            var result = await inversionesCosmosDB.GetGastosByTwinIdAndCustomerIdAsync(twinId.ToLowerInvariant(), customerId);

            var processingTime = DateTime.UtcNow - startTime;

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new GetGastosResponse
            {
                Success = result.Success,
                TwinId = twinId,
                CustomerId = customerId,
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
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetGastosResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error al obtener gastos del cliente"
            }));

            return errorResponse;
        }
    }

    #endregion

    #region Update Gasto Functions

    [Function("UpdateGastoClienteOptions")]
    public async Task<HttpResponseData> HandleUpdateGastoClienteOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "update-gasto-cliente/{twinId}/{gastoId}")] HttpRequestData req,
        string twinId,
        string gastoId)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for update-gasto-cliente/{TwinId}/{GastoId}", twinId, gastoId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("UpdateGastoCliente")]
    public async Task<HttpResponseData> UpdateGastoCliente(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "update-gasto-cliente/{twinId}/{gastoId}")] HttpRequestData req,
        string twinId,
        string gastoId)
    {
        _logger.LogInformation("✏️ UpdateGastoCliente function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateGastoResponse
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
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateGastoResponse
                {
                    Success = false,
                    ErrorMessage = "Gasto ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Updating gasto ID: {GastoId} for Twin ID: {TwinId}", gastoId, twinId);

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

            var updateRequest = JsonSerializer.Deserialize<UpdateGastoRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (updateRequest == null)
            {
                _logger.LogError("❌ Failed to parse update request data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateGastoResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid update request data format"
                }));
                return badResponse;
            }

            _logger.LogInformation("📋 Update details: Tipo={TipoGasto}, Cantidad={Cantidad}, CostoUnitario={CostoUnitario}", 
                updateRequest.TipoGasto, updateRequest.Cantidad, updateRequest.CostoUnitario);

            // Crear objeto GastoInversion actualizado
            var gastoActualizado = new GastoInversion
            {
                TipoGasto = updateRequest.TipoGasto ?? string.Empty,
                Descripcion = updateRequest.Descripcion ?? string.Empty,
                Unidad = updateRequest.Unidad ?? string.Empty,
                Cantidad = updateRequest.Cantidad,
                CostoUnitario = updateRequest.CostoUnitario,
                Fecha = updateRequest.Fecha,
                Notas = updateRequest.Notas ?? string.Empty,
                Total = updateRequest.Total
            };

            // Actualizar en Cosmos DB
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<ClienteInversionesCosmosDB>();
            var inversionesCosmosDB = new ClienteInversionesCosmosDB(cosmosLogger, _configuration);

            var updateResult = await inversionesCosmosDB.UpdateGastoAsync(gastoId, twinId.ToLowerInvariant(), gastoActualizado);

            var processingTime = DateTime.UtcNow - startTime;

            if (!updateResult.Success)
            {
                _logger.LogWarning("⚠️ Failed to update gasto: {Error}", updateResult.ErrorMessage);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateGastoResponse
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

            var responseData = new UpdateGastoResponse
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
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UpdateGastoResponse
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

    #region Delete Gasto Functions

    [Function("DeleteGastoClienteOptions")]
    public async Task<HttpResponseData> HandleDeleteGastoClienteOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "delete-gasto-cliente/{twinId}/{customerId}/{gastoId}")] HttpRequestData req,
        string twinId,
        string customerId,
        string gastoId)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for delete-gasto-cliente/{TwinId}/{CustomerId}/{GastoId}", twinId, customerId, gastoId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("DeleteGastoCliente")]
    public async Task<HttpResponseData> DeleteGastoCliente(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "delete-gasto-cliente/{twinId}/{customerId}/{gastoId}")] HttpRequestData req,
        string twinId,
        string customerId,
        string gastoId)
    {
        _logger.LogInformation("🗑️ DeleteGastoCliente function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteGastoResponse
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
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteGastoResponse
                {
                    Success = false,
                    ErrorMessage = "Customer ID parameter is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(gastoId))
            {
                _logger.LogError("❌ Gasto ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteGastoResponse
                {
                    Success = false,
                    ErrorMessage = "Gasto ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Deleting gasto ID: {GastoId} for Twin ID: {TwinId}, Customer ID: {CustomerId}", 
                gastoId, twinId, customerId);

            // Eliminar de Cosmos DB con validación completa
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<ClienteInversionesCosmosDB>();
            var inversionesCosmosDB = new ClienteInversionesCosmosDB(cosmosLogger, _configuration);

            var deleteResult = await inversionesCosmosDB.DeleteGastoByTwinCustomerAsync(gastoId, twinId.ToLowerInvariant(), customerId);

            var processingTime = DateTime.UtcNow - startTime;

            if (!deleteResult.Success)
            {
                _logger.LogWarning("⚠️ Failed to delete gasto: {Error}", deleteResult.ErrorMessage);
                var errorResponse = req.CreateResponse(HttpStatusCode.NotFound);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteGastoResponse
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

            var responseData = new DeleteGastoResponse
            {
                Success = true,
                GastoId = gastoId,
                TwinId = twinId,
                CustomerId = customerId,
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
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteGastoResponse
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
/// Request para guardar un gasto
/// </summary>
public class SaveGastoRequest
{
    public string TipoGasto { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;

    public string TwinID { get; set; } = string.Empty;

    public string MicrosoftOID { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public DateTime Fecha { get; set; }
    public string Notas { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

/// <summary>
/// Response de guardar un gasto
/// </summary>
public class SaveGastoResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public string TwinId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public GastoInversion? Gasto { get; set; }
    public double RUConsumed { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Response de obtener gastos
/// </summary>
public class GetGastosResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public string TwinId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public List<GastoInversion> Gastos { get; set; } = new();
    public int GastoCount { get; set; }
    public decimal TotalGastos { get; set; }
    public double RUConsumed { get; set; }
}

/// <summary>
/// Request para actualizar un gasto
/// </summary>
public class UpdateGastoRequest
{
    public string TipoGasto { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public DateTime Fecha { get; set; }
    public string Notas { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

/// <summary>
/// Response de actualizar un gasto
/// </summary>
public class UpdateGastoResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public string GastoId { get; set; } = string.Empty;
    public string TwinId { get; set; } = string.Empty;
    public GastoInversion? GastoActualizado { get; set; }
    public double RUConsumed { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Response de eliminar un gasto
/// </summary>
public class DeleteGastoResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public string GastoId { get; set; } = string.Empty;
    public string TwinId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public double RUConsumed { get; set; }
    public DateTime Timestamp { get; set; }
}

#endregion

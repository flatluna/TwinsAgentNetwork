using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinAgentsNetwork.Agents;
using TwinFx.Services;

namespace TwinAgentsNetwork.AzureFunctions;

public class AgentFacturasClientesFx
{
    private readonly ILogger<AgentFacturasClientesFx> _logger;
    private readonly IConfiguration _configuration;

    public AgentFacturasClientesFx(ILogger<AgentFacturasClientesFx> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [Function("UploadFacturaClienteOptions")]
    public async Task<HttpResponseData> HandleUploadFacturaClienteOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "upload-factura-cliente/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for upload-factura-cliente/{TwinId}", twinId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("UploadFacturaCliente")]
    public async Task<HttpResponseData> UploadFacturaCliente(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "upload-factura-cliente/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("🧾 UploadFacturaCliente function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadFacturaClienteResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Processing factura upload for Twin ID: {TwinId}", twinId);

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

            var uploadRequest = JsonSerializer.Deserialize<UploadFacturaClienteRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (uploadRequest == null)
            {
                _logger.LogError("❌ Failed to parse upload request data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadFacturaClienteResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid upload request data format"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(uploadRequest.FileName))
            {
                _logger.LogError("❌ File name is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadFacturaClienteResponse
                {
                    Success = false,
                    ErrorMessage = "File name is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(uploadRequest.FileContent))
            {
                _logger.LogError("❌ File content is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadFacturaClienteResponse
                {
                    Success = false,
                    ErrorMessage = "File content is required"
                }));
                return badResponse;
            }

            var customerID = uploadRequest.CustomerID ?? string.Empty;
            var language = uploadRequest.Language ?? "es";

            _logger.LogInformation("📋 Upload details: FileName={FileName}, CustomerID={CustomerID}, Language={Language}", 
                uploadRequest.FileName, customerID, language);

            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

            byte[] fileBytes;
            try
            {
                fileBytes = Convert.FromBase64String(uploadRequest.FileContent);
                _logger.LogInformation("💾 File size: {Size} bytes", fileBytes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to decode base64 file content");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadFacturaClienteResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid base64 file content"
                }));
                return badResponse;
            }

            var filePath = Path.Combine(uploadRequest.FilePath, uploadRequest.FileName).Replace("\\", "/");

            _logger.LogInformation("📁 Final file path: {FilePath}", filePath);

            var mimeType = GetMimeType(uploadRequest.FileName);
            _logger.LogInformation("📄 MIME type: {MimeType}", mimeType);

            var directoryPath = Path.GetDirectoryName(filePath)?.Replace("\\", "/") ?? "";
            var fileName = Path.GetFileName(filePath);

            if (string.IsNullOrEmpty(fileName))
            {
                _logger.LogError("❌ Invalid file path - no filename found: {FilePath}", filePath);
                var invalidPathResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(invalidPathResponse, req);
                await invalidPathResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadFacturaClienteResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid file path - no filename found"
                }));
                return invalidPathResponse;
            }

            _logger.LogInformation("📂 Parsed path - Directory: '{Directory}', File: '{File}'", directoryPath, fileName);

            using var fileStream = new MemoryStream(fileBytes);
            var uploadSuccess = await dataLakeClient.UploadFileAsync(
                twinId.ToLowerInvariant(),
                directoryPath,
                fileName,
                fileStream,
                mimeType
            );

            if (!uploadSuccess)
            {
                _logger.LogError("❌ Failed to upload file to DataLake");
                var uploadErrorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(uploadErrorResponse, req);
                await uploadErrorResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadFacturaClienteResponse
                {
                    Success = false,
                    ErrorMessage = "Failed to upload file to storage"
                }));
                return uploadErrorResponse;
            }

            _logger.LogInformation("✅ File uploaded successfully: {FilePath}", filePath);

            _logger.LogInformation("🤖 Processing factura with AgentTwinFacturasClientes...");
            try
            {
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var agentLogger = loggerFactory.CreateLogger<AgentTwinFacturasClientes>();
                var facturasAgent = new AgentTwinFacturasClientes(agentLogger, _configuration);

                var facturaResult = await facturasAgent.ProcesaFacturaClientesAsync(
                    language,
                    twinId.ToLowerInvariant(),
                    directoryPath,
                    fileName,
                    customerID
                );

                if (!facturaResult.Success)
                {
                    _logger.LogWarning("⚠️ Factura processing failed: {Error}", facturaResult.ErrorMessage);
                }
                else
                {
                    _logger.LogInformation("✅ Factura processed successfully");
                    
                    var cosmosLogger = loggerFactory.CreateLogger<Services.AgentFacturasClientesCosmosDB>();
                    var facturasCosmosDB = new Services.AgentFacturasClientesCosmosDB(cosmosLogger, _configuration);
                    
                    var saveResult = await facturasCosmosDB.SaveFacturaAsync(facturaResult.FacturaData, twinId.ToLowerInvariant(), customerID);
                    
                    if (!saveResult.Success)
                    {
                        _logger.LogWarning("⚠️ Failed to save factura to Cosmos DB: {Error}", saveResult.ErrorMessage);
                    }
                    else
                    {
                        _logger.LogInformation("✅ Factura saved to Cosmos DB. Document ID: {DocumentId}", saveResult.DocumentId);
                    }
                }

                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));
                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new UploadFacturaClienteResponse
                {
                    Success = facturaResult.Success,
                    TwinId = twinId,
                    FileName = fileName,
                    FilePath = filePath,
                    ContainerName = twinId.ToLowerInvariant(),
                    FileSize = fileBytes.Length,
                    MimeType = mimeType,
                    Url = sasUrl,
                    UploadedAt = DateTime.UtcNow,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = facturaResult.Success 
                        ? "Factura procesada y extraída exitosamente" 
                        : $"Factura subida pero procesamiento falló: {facturaResult.ErrorMessage}",
                    TotalPages = facturaResult.TotalPages,
                    FacturaData = facturaResult.FacturaData,
                    ErrorMessage = facturaResult.Success ? null : facturaResult.ErrorMessage
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                return response;
            }
            catch (Exception aiEx)
            {
                _logger.LogError(aiEx, "❌ Error during factura processing");
                
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));
                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new UploadFacturaClienteResponse
                {
                    Success = false,
                    TwinId = twinId,
                    FileName = fileName,
                    FilePath = filePath,
                    ContainerName = twinId.ToLowerInvariant(),
                    FileSize = fileBytes.Length,
                    MimeType = mimeType,
                    Url = sasUrl,
                    UploadedAt = DateTime.UtcNow,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Factura subida pero el procesamiento falló",
                    ErrorMessage = aiEx.Message
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                return response;
            }
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "❌ Error uploading factura after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadFacturaClienteResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error durante el procesamiento de la factura"
            }));

            return errorResponse;
        }
    }

    [Function("GetFacturasByClienteOptions")]
    public async Task<HttpResponseData> HandleGetFacturasByClienteOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-facturas-cliente/{twinId}/{idCliente}")] HttpRequestData req,
        string twinId,
        string idCliente)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for get-facturas-cliente/{TwinId}/{IdCliente}", twinId, idCliente);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("GetFacturasByCliente")]
    public async Task<HttpResponseData> GetFacturasByCliente(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-facturas-cliente/{twinId}/{idCliente}")] HttpRequestData req,
        string twinId,
        string idCliente)
    {
        _logger.LogInformation("📋 GetFacturasByCliente function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetFacturasClienteResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(idCliente))
            {
                _logger.LogError("❌ Cliente ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetFacturasClienteResponse
                {
                    Success = false,
                    ErrorMessage = "Cliente ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Getting facturas for Twin ID: {TwinId}, Cliente ID: {IdCliente}", twinId, idCliente);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<Services.AgentFacturasClientesCosmosDB>();
            var facturasCosmosDB = new Services.AgentFacturasClientesCosmosDB(cosmosLogger, _configuration);

            var result = await facturasCosmosDB.GetFacturasByTwinIdAndIdClienteAsync(twinId.ToLowerInvariant(), idCliente);

            var processingTime = DateTime.UtcNow - startTime;

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new GetFacturasClienteResponse
            {
                Success = result.Success,
                TwinId = twinId,
                IdCliente = idCliente,
                Facturas = result.Facturas,
                FacturaCount = result.FacturaCount,
                RUConsumed = result.RUConsumed,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = result.Success 
                    ? $"Se encontraron {result.FacturaCount} facturas" 
                    : "Error al obtener facturas",
                ErrorMessage = result.Success ? null : result.ErrorMessage
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            _logger.LogInformation("✅ Retrieved {Count} facturas for TwinId={TwinId}, IdCliente={IdCliente}", 
                result.FacturaCount, twinId, idCliente);

            return response;
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "❌ Error getting facturas after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetFacturasClienteResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error al obtener facturas del cliente"
            }));

            return errorResponse;
        }
    }

    private static string GetMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            _ => "application/octet-stream"
        };
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

public class UploadFacturaClienteRequest
{
    public string FileName { get; set; } = string.Empty;
    public string CustomerID { get; set; } = string.Empty;
    public string FileContent { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string? Language { get; set; }
}

public class UploadFacturaClienteResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public string TwinId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public int TotalPages { get; set; }
    public FacturaClienteData? FacturaData { get; set; }
}

public class GetFacturasClienteResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public string TwinId { get; set; } = string.Empty;
    public string IdCliente { get; set; } = string.Empty;
    public List<FacturaClienteData> Facturas { get; set; } = new();
    public int FacturaCount { get; set; }
    public double RUConsumed { get; set; }
}

public class FacturaData
{
    public string Id { get; set; } = string.Empty;
    public string TwinId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; }
    public Dictionary<string, object> ExtraData { get; set; } = new Dictionary<string, object>();
}

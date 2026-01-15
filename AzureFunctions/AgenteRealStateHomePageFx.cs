using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinAgentsNetwork.Agents;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.AzureFunctions;

/// <summary>
/// Azure Function para generar páginas home profesionales para agentes inmobiliarios
/// </summary>
public class AgenteRealStateHomePageFx
{
    private readonly ILogger<AgenteRealStateHomePageFx> _logger;
    private readonly IConfiguration _configuration;

    public AgenteRealStateHomePageFx(ILogger<AgenteRealStateHomePageFx> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [Function("GenerateRealStateHomePageOptions")]
    public async Task<HttpResponseData> HandleGenerateHomePageOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "generate-homepage/{twinId}/{microsoftOID}")] HttpRequestData req,
        string twinId,
        string microsoftOID)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for generate-homepage/{TwinId}/{MicrosoftOID}", twinId, microsoftOID);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("GetRealStateHomePageOptions")]
    public async Task<HttpResponseData> HandleGetHomePageOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-homepage/{twinId}/{microsoftOID}")] HttpRequestData req,
        string twinId,
        string microsoftOID)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for get-homepage/{TwinId}/{MicrosoftOID}", twinId, microsoftOID);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("GenerateRealStateHomePage")]
    public async Task<HttpResponseData> GenerateRealStateHomePage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "generate-homepage/{twinId}/{microsoftOID}")] HttpRequestData req,
        string twinId,
        string microsoftOID)
    {
        _logger.LogInformation("🏠 GenerateRealStateHomePage function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateHomePageResponse
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
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateHomePageResponse
                {
                    Success = false,
                    ErrorMessage = "Microsoft OID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Generating home page for TwinID: {TwinId}, MicrosoftOID: {MicrosoftOID}", 
                twinId, microsoftOID);

            // Leer descripción adicional del body (opcional)
            string descripcionAgente = "";
            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (!string.IsNullOrEmpty(requestBody))
                {
                    var requestData = JsonSerializer.Deserialize<GenerateHomePageRequest>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    descripcionAgente = requestData?.DescripcionDiseno ?? "";
                    _logger.LogInformation("📝 Descripción adicional recibida: {Length} caracteres", descripcionAgente.Length);
                }
            }
            catch (Exception bodyEx)
            {
                _logger.LogWarning(bodyEx, "⚠️ No se pudo parsear el body, continuando sin descripción adicional");
            }

            // Crear instancia del agente
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var agentLogger = loggerFactory.CreateLogger<AgentTwinRealStateHomePage>();
            var agent = new AgentTwinRealStateHomePage(agentLogger, _configuration);

            // Generar la home page
            var result = await agent.GenerateHomePageAsync(twinId, microsoftOID, descripcionAgente);

            var processingTime = DateTime.UtcNow - startTime;

            if (!result.Success)
            {
                _logger.LogWarning("⚠️ Failed to generate home page: {Error}", result.ErrorMessage);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateHomePageResponse
                {
                    Success = false,
                    ErrorMessage = result.ErrorMessage,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return errorResponse;
            }

            _logger.LogInformation("✅ Home page generated successfully. Processing time: {ProcessingTime}ms", result.ProcessingTimeMs);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new GenerateHomePageResponse
            {
                Success = true,
                TwinID = result.TwinID,
                MicrosoftOIDRSA = result.MicrosoftOIDRSA,
                Id = result.id,
                HtmlCompleto = result.HtmlCompleto,
                DescripcionDiseno = result.DescripcionDiseno,
                ColoresUsados = result.ColoresUsados,
                SeccionesIncluidas = result.SeccionesIncluidas,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                ProcessingTimeMs = result.ProcessingTimeMs,
                Message = "Home page generada exitosamente",
                Timestamp = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            }));

            return response;
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "❌ Error generating home page after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateHomePageResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error al generar la home page"
            }));

            return errorResponse;
        }
    }

    [Function("GetRealStateHomePage")]
    public async Task<HttpResponseData> GetRealStateHomePage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-homepage/{twinId}/{microsoftOID}")] HttpRequestData req,
        string twinId,
        string microsoftOID)
    {
        _logger.LogInformation("📖 GetRealStateHomePage function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetHomePageResponse
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
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetHomePageResponse
                {
                    Success = false,
                    ErrorMessage = "Microsoft OID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Retrieving home page for TwinID: {TwinId}, MicrosoftOID: {MicrosoftOID}", 
                twinId, microsoftOID);

            // Crear instancia del servicio de Cosmos DB
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<AgentTwinRealStateHomePageCosmosDB>();
            var cosmosService = new AgentTwinRealStateHomePageCosmosDB(cosmosLogger, _configuration);

            // Obtener la home page
            var result = await cosmosService.GetRealStateHomePageAsync(twinId, microsoftOID);

            var processingTime = DateTime.UtcNow - startTime;

            if (!result.Success)
            {
                _logger.LogWarning("⚠️ HomePage not found for TwinID: {TwinId}, MicrosoftOID: {MicrosoftOID}", 
                    twinId, microsoftOID);
                
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                AddCorsHeaders(notFoundResponse, req);
                await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new GetHomePageResponse
                {
                    Success = false,
                    ErrorMessage = result.ErrorMessage,
                    Message = "HomePage no encontrada para este agente",
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return notFoundResponse;
            }

            _logger.LogInformation("✅ HomePage retrieved successfully. Document ID: {DocumentId}", result.HomePage.id);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new GetHomePageResponse
            {
                Success = true,
                TwinID = result.HomePage.TwinID,
                MicrosoftOIDRSA = result.HomePage.MicrosoftOIDRSA,
                Id = result.HomePage.id,
                HtmlCompleto = result.HomePage.HtmlCompleto,
                DescripcionDiseno = result.HomePage.DescripcionDiseno,
                ColoresUsados = result.HomePage.ColoresUsados,
                SeccionesIncluidas = result.HomePage.SeccionesIncluidas,
                ProcessingTimeMs = result.HomePage.ProcessingTimeMs,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                RUConsumed = result.RUConsumed,
                Message = "HomePage obtenida exitosamente",
                Timestamp = result.Timestamp
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            }));

            return response;
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "❌ Error retrieving home page after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetHomePageResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error al obtener la home page"
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

#region Request/Response Models

/// <summary>
/// Request para generar home page (body opcional)
/// </summary>
public class GenerateHomePageRequest
{
    public string DescripcionDiseno { get; set; } = "";
}

/// <summary>
/// Response de la generación de home page
/// </summary>
public class GenerateHomePageResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public double ProcessingTimeMs { get; set; }
    public string TwinID { get; set; } = "";
    public string MicrosoftOIDRSA { get; set; } = "";
    public string Id { get; set; } = "";
    public string HtmlCompleto { get; set; } = "";
    public string DescripcionDiseno { get; set; } = "";
    public List<string> ColoresUsados { get; set; } = new();
    public List<string> SeccionesIncluidas { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Response para obtener home page
/// </summary>
public class GetHomePageResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public double ProcessingTimeMs { get; set; }
    public double RUConsumed { get; set; }
    public string TwinID { get; set; } = "";
    public string MicrosoftOIDRSA { get; set; } = "";
    public string Id { get; set; } = "";
    public string HtmlCompleto { get; set; } = "";
    public string DescripcionDiseno { get; set; } = "";
    public List<string> ColoresUsados { get; set; } = new();
    public List<string> SeccionesIncluidas { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

#endregion

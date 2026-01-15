using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Newtonsoft.Json;
using TwinAgentsNetwork.Agents;

namespace TwinAgentsNetwork.AzureFunctions;

public class AgentTwinReqalStateAIFx
{
    private readonly ILogger<AgentTwinReqalStateAIFx> _logger;
    private readonly IConfiguration _configuration;

    public AgentTwinReqalStateAIFx(ILogger<AgentTwinReqalStateAIFx> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [Function("AnalyzarCasaCompradorOptions")]
    public async Task<HttpResponseData> HandleAnalyzarCasaCompradorOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "analizar-casa/{twinId}/{compradorId}")] HttpRequestData req,
        string twinId,
        string compradorId)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for analizar-casa/{TwinId}/{CompradorId}", twinId, compradorId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("AnalizarCasaComprador")]
    public async Task<HttpResponseData> AnalyzarCasaComprador(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "analizar-casa/{twinId}/{compradorId}")] HttpRequestData req,
        string twinId,
        string compradorId)
    {
        _logger.LogInformation("🏠 AnalyzarCasaComprador function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new AnalyzarCasaResponse
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
                await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new AnalyzarCasaResponse
                {
                    Success = false,
                    ErrorMessage = "Comprador ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Analyzing casa for Comprador ID: {CompradorId}, Twin ID: {TwinId}", compradorId, twinId);

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

            var casaRequest = System.Text.Json.JsonSerializer.Deserialize<AnalyzarCasaRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (casaRequest == null || string.IsNullOrEmpty(casaRequest.DatosTextoCasa))
            {
                _logger.LogError("❌ Datos de la casa are required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new AnalyzarCasaResponse
                {
                    Success = false,
                    ErrorMessage = "Datos de la casa son requeridos en el body del request"
                }));
                return badResponse;
            }

            _logger.LogInformation("📋 Casa data received: {Length} characters", casaRequest.DatosTextoCasa.Length);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var aiLogger = loggerFactory.CreateLogger<AgentTwinRealStateCasasAI>();
            var realStateAgent = new AgentTwinRealStateCasasAI(aiLogger, _configuration);

            var analysisResult = await realStateAgent.AnalizarCasaParaCompradorAsync(
                compradorId, 
                twinId.ToLowerInvariant(), 
                casaRequest.DatosTextoCasa);

            var processingTime = DateTime.UtcNow - startTime;

            if (!analysisResult.Success)
            {
                _logger.LogWarning("⚠️ Failed to analyze casa: {Error}", analysisResult.ErrorMessage);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new AnalyzarCasaResponse
                {
                    Success = false,
                    ErrorMessage = analysisResult.ErrorMessage,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return errorResponse;
            }

            _logger.LogInformation("✅ Casa analyzed successfully. Precio: ${Precio}, Ubicación: {Ciudad}", 
                analysisResult.DatosCasa.Precio, 
                analysisResult.DatosCasa.Ciudad);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new AnalyzarCasaResponse
            {
                Success = true,
                TwinId = twinId,
                CompradorId = compradorId,
                AnalisisResult = analysisResult,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = $"Análisis completado. Casa en {analysisResult.DatosCasa.Ciudad}, {analysisResult.DatosCasa.Estado}",
                Timestamp = DateTime.UtcNow
            };

            // Use Newtonsoft.Json to serialize because AnalisisCasaResult uses [JsonProperty] attributes
            var jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
                Formatting = Newtonsoft.Json.Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };

            await response.WriteStringAsync(JsonConvert.SerializeObject(responseData, jsonSettings));

            return response;
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "❌ Error analyzing casa after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new AnalyzarCasaResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error al analizar la casa"
            }));

            return errorResponse;
        }
    }

    [Function("EditarCasaCompradorOptions")]
    public async Task<HttpResponseData> HandleEditarCasaCompradorOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "editar-casa/{twinId}/{compradorId}")] HttpRequestData req,
        string twinId,
        string compradorId)
    {
        _logger.LogInformation("🔧 OPTIONS preflight request for editar-casa/{TwinId}/{CompradorId}", twinId, compradorId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("EditarCasaComprador")]
    public async Task<HttpResponseData> EditarCasaComprador(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "editar-casa/{twinId}/{compradorId}")] HttpRequestData req,
        string twinId,
        string compradorId)
    {
        _logger.LogInformation("✏️ EditarCasaComprador function triggered");
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("❌ Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new AnalyzarCasaResponse
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
                await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new AnalyzarCasaResponse
                {
                    Success = false,
                    ErrorMessage = "Comprador ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation("📂 Editing casa for Comprador ID: {CompradorId}, Twin ID: {TwinId}", compradorId, twinId);

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

            // Use Newtonsoft.Json for deserialization to match the JsonProperty attributes
            var editCasaRequest = JsonConvert.DeserializeObject<AnalyzarEditCasaRequest>(requestBody);

            if (editCasaRequest == null)
            {
                _logger.LogError("❌ Invalid edit casa request data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new AnalyzarCasaResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid edit casa request data format"
                }));
                return badResponse;
            }

            // Validate required fields
            if (editCasaRequest.Precio <= 0)
            {
                _logger.LogError("❌ Precio is required and must be greater than 0");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new AnalyzarCasaResponse
                {
                    Success = false,
                    ErrorMessage = "Precio is required and must be greater than 0"
                }));
                return badResponse;
            }

            _logger.LogInformation("📋 Casa edit data: Precio=${Precio}, Recámaras={Recamaras}, Ciudad={Ciudad}", 
                editCasaRequest.Precio, editCasaRequest.Recamaras, editCasaRequest.Ciudad);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var aiLogger = loggerFactory.CreateLogger<AgentTwinRealStateCasasAI>();
            var realStateAgent = new AgentTwinRealStateCasasAI(aiLogger, _configuration);

            var analysisResult = await realStateAgent.AnalizarEditCasaParaCompradorAsync(
                compradorId, 
                twinId.ToLowerInvariant(), 
                editCasaRequest);

            var processingTime = DateTime.UtcNow - startTime;

            if (!analysisResult.Success)
            {
                _logger.LogWarning("⚠️ Failed to edit/analyze casa: {Error}", analysisResult.ErrorMessage);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new AnalyzarCasaResponse
                {
                    Success = false,
                    ErrorMessage = analysisResult.ErrorMessage,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return errorResponse;
            }

            _logger.LogInformation("✅ Casa edited successfully. Precio: ${Precio}, Ubicación: {Ciudad}", 
                analysisResult.DatosCasa.Precio, 
                analysisResult.DatosCasa.Ciudad);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new AnalyzarCasaResponse
            {
                Success = true,
                TwinId = twinId,
                CompradorId = compradorId,
                AnalisisResult = analysisResult,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = $"Casa editada exitosamente. Casa en {analysisResult.DatosCasa.Ciudad}, {analysisResult.DatosCasa.Estado}",
                Timestamp = DateTime.UtcNow
            };

            // Use Newtonsoft.Json to serialize because AnalisisCasaResult uses [JsonProperty] attributes
            var jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
                Formatting = Newtonsoft.Json.Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };

            await response.WriteStringAsync(JsonConvert.SerializeObject(responseData, jsonSettings));

            return response;
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "❌ Error editing casa after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new AnalyzarCasaResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error al editar la casa"
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

public class AnalyzarCasaRequest
{
    public string DatosTextoCasa { get; set; } = string.Empty;
}


public class AnalyzarEditCasaRequest
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("TwinID")]
    public string TwinID { get; set; } = string.Empty;

    [JsonProperty("clienteID")]
    public string ClienteID { get; set; } = string.Empty;

    [JsonProperty("estatusCasa")]
    public string EstatusCasa { get; set; } = string.Empty;

    [JsonProperty("precio")]
    public double Precio { get; set; }

    [JsonProperty("moneda")]
    public string Moneda { get; set; } = "MXN";

    [JsonProperty("recamaras")]
    public int? Recamaras { get; set; }

    [JsonProperty("banos")]
    public int? Banos { get; set; }

    [JsonProperty("estacionamientos")]
    public int? Estacionamientos { get; set; }

    [JsonProperty("metrosConstruccion")]
    public double? MetrosConstruccion { get; set; }

    [JsonProperty("direccionCompleta")]
    public string DireccionCompleta { get; set; } = string.Empty;

    [JsonProperty("descripcionExtra")]
    public string DescripcionExtra { get; set; } = string.Empty;

    // Campos adicionales opcionales de DatosCasaExtraidos
    [JsonProperty("mantenimiento")]
    public double? Mantenimiento { get; set; }

    [JsonProperty("ciudad")]
    public string Ciudad { get; set; } = string.Empty;

    [JsonProperty("estado")]
    public string Estado { get; set; } = string.Empty;

    [JsonProperty("colonia")]
    public string Colonia { get; set; } = string.Empty;

    [JsonProperty("fraccionamiento")]
    public string Fraccionamiento { get; set; } = string.Empty;

    [JsonProperty("condominio")]
    public string Condominio { get; set; } = string.Empty;

    [JsonProperty("metrosLote")]
    public double? MetrosLote { get; set; }

    [JsonProperty("metrosCobertura")]
    public double? MetrosCobertura { get; set; }

    [JsonProperty("medioBanos")]
    public int? MedioBanos { get; set; }

    [JsonProperty("antiguedad")]
    public int? Antiguedad { get; set; }

    [JsonProperty("niveles")]
    public int? Niveles { get; set; }

    [JsonProperty("estadoConservacion")]
    public string EstadoConservacion { get; set; } = string.Empty;

    [JsonProperty("distribucion")]
    public string Distribucion { get; set; } = string.Empty;

    [JsonProperty("amenidadesCondominio")]
    public List<string> AmenidadesCondominio { get; set; } = new();

    [JsonProperty("amenidadesFraccionamiento")]
    public List<string> AmenidadesFraccionamiento { get; set; } = new();

    [JsonProperty("amenidadesCasa")]
    public List<string> AmenidadesCasa { get; set; } = new();

    [JsonProperty("cartaClienteHTML")]
    public string CartaClienteHTML { get; set; } = string.Empty;

    [JsonProperty("cercania")]
    public List<string> Cercania { get; set; } = new();

    [JsonProperty("coordenadas")]
    public Coordenadas Coordenadas { get; set; } = new();

    [JsonProperty("urlGoogleMaps")]
    public string UrlGoogleMaps { get; set; } = string.Empty;

    [JsonProperty("emailEditedHTML")]
    public string EmailEditedHTML { get; set; } = string.Empty;

    [JsonProperty("agendarCasa")]
    public Agenda AgendarCasa { get; set; } = new();


    [JsonProperty("prompt")]
    public string Prompt { get; set; } = string.Empty;


    [JsonProperty("casaURL")]
    public string CasaURL { get; set; } = string.Empty;
}

public class AnalyzarCasaResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
    public double ProcessingTimeSeconds { get; set; }
    public string TwinId { get; set; } = string.Empty;
    public string CompradorId { get; set; } = string.Empty;
    public AnalisisCasaResult? AnalisisResult { get; set; }
    public DateTime Timestamp { get; set; }
}

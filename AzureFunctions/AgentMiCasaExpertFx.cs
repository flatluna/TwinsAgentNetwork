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
using TwinAgentsNetwork.Agents;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Functions para el Agente Experto MiCasa
    /// Permite consultar al agente experto sobre propiedades y perfiles de compradores
    /// Soporta continuidad de conversación usando SerializedThreadJson
    /// </summary>
    public class AgentMiCasaExpertFx
    {
        private readonly ILogger<AgentMiCasaExpertFx> _logger;
        private readonly IConfiguration _configuration;

        public AgentMiCasaExpertFx(ILogger<AgentMiCasaExpertFx> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        #region Consultar Experto Functions

        [Function("ConsultarExpertoOptions")]
        public async Task<HttpResponseData> HandleConsultarExpertoOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "micasa-expert/consultar/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? OPTIONS preflight request for micasa-expert/consultar/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Consulta al agente experto MiCasa con información de propiedades y perfil del comprador
        /// Soporta continuidad de conversación pasando SerializedThreadJson
        /// </summary>
        /// <param name="req">HTTP request con el cuerpo de la solicitud</param>
        /// <param name="twinId">ID del Twin</param>
        /// <returns>Respuesta del agente experto con recomendaciones y SerializedThreadJson para continuar</returns>
        [Function("ConsultarExperto")]
        public async Task<HttpResponseData> ConsultarExperto(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "micasa-expert/consultar/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? ConsultarExperto function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                // Validar TwinId
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("? Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ConsultarExpertoResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                // Leer y deserializar el request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                
                if (string.IsNullOrEmpty(requestBody))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ConsultarExpertoResponse
                    {
                        Success = false,
                        ErrorMessage = "Request body is required"
                    }));
                    return badResponse;
                }

                var consultaRequest = JsonSerializer.Deserialize<ConsultarExpertoRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (consultaRequest == null)
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ConsultarExpertoResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid request format"
                    }));
                    return badResponse;
                }

                // Validar pregunta
                if (string.IsNullOrEmpty(consultaRequest.Pregunta))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ConsultarExpertoResponse
                    {
                        Success = false,
                        ErrorMessage = "Pregunta is required"
                    }));
                    return badResponse;
                }

                // Validar propiedades
                if (consultaRequest.PropiedadesIds == null || consultaRequest.PropiedadesIds.Count == 0)
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ConsultarExpertoResponse
                    {
                        Success = false,
                        ErrorMessage = "At least one property is required (PropiedadesIds)"
                    }));
                    return badResponse;
                }

                bool hasPriorConversation = !string.IsNullOrEmpty(consultaRequest.SerializedThreadJson);
                _logger.LogInformation("?? Processing expert consultation: Pregunta length={PreguntaLength}, Properties={PropertyCount}, CompradorOID={CompradorOID}, HasPriorConversation={HasPrior}",
                    consultaRequest.Pregunta.Length,
                    consultaRequest.PropiedadesIds.Count,
                    consultaRequest.CompradorMicrosoftOID ?? "N/A",
                    hasPriorConversation);

                // Crear el agente experto
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var agentLogger = loggerFactory.CreateLogger<AgentMiCasaExpert>();
                var agentExpert = new AgentMiCasaExpert(agentLogger, _configuration);

                // Preparar el request para el agente
                var agentRequest = new MiCasaExpertRequest
                {
                    TwinId = twinId,
                    CompradorMicrosoftOID = consultaRequest.CompradorMicrosoftOID,
                    PropiedadesIds = consultaRequest.PropiedadesIds.Select(p => new PropiedadIdentificador
                    {
                        ClienteId = p.ClienteId,
                        PropiedadId = p.PropiedadId
                    }).ToList(),
                    Pregunta = consultaRequest.Pregunta,
                    ContextoAdicional = consultaRequest.ContextoAdicional,
                    SerializedThreadJson = consultaRequest.SerializedThreadJson
                };

                // Ejecutar consulta al experto
                var result = await agentExpert.ConsultarExpertoAsync(agentRequest);

                var processingTime = DateTime.UtcNow - startTime;

                if (!result.Success)
                {
                    _logger.LogWarning("?? Expert consultation failed: {Error}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new ConsultarExpertoResponse
                    {
                        Success = false,
                        ErrorMessage = result.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("? Expert consultation completed successfully in {ProcessingTime}ms, IsNewConversation={IsNew}", 
                    result.ProcessingTimeMs, result.IsNewConversation);

                // Preparar respuesta exitosa
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                await response.WriteStringAsync(JsonSerializer.Serialize(new ConsultarExpertoResponse
                {
                    Success = true,
                    TwinId = twinId,
                    Respuesta = result.Respuesta,
                    PropiedadesRecomendadas = result.PropiedadesRecomendadas,
                    Observaciones = result.Observaciones,
                    SiguientesPasos = result.SiguientesPasos,
                    PropiedadesAnalizadas = result.PropiedadesAnalizadas,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    IsNewConversation = result.IsNewConversation,
                    SerializedThreadJson = result.SerializedThreadJson,
                    Message = result.IsNewConversation 
                        ? "New expert consultation started successfully" 
                        : "Conversation continued successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "? Error in ConsultarExperto function");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new ConsultarExpertoResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
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
    /// Request para consultar al agente experto
    /// </summary>
    public class ConsultarExpertoRequest
    {
        /// <summary>
        /// Microsoft OID del comprador (opcional - para obtener su perfil)
        /// </summary>
        public string? CompradorMicrosoftOID { get; set; }

        /// <summary>
        /// Lista de propiedades a analizar (ClienteId + PropiedadId)
        /// </summary>
        public List<PropiedadIdentificadorRequest> PropiedadesIds { get; set; } = new();

        /// <summary>
        /// Pregunta del usuario para el agente experto
        /// </summary>
        public string Pregunta { get; set; } = string.Empty;

        /// <summary>
        /// Contexto adicional para la consulta (opcional)
        /// </summary>
        public string? ContextoAdicional { get; set; }

        /// <summary>
        /// JSON serializado del thread de conversación anterior (opcional - para continuar conversación)
        /// Pasar el valor de SerializedThreadJson de la respuesta anterior para mantener contexto
        /// </summary>
        public string? SerializedThreadJson { get; set; }
    }

    /// <summary>
    /// Identificador de una propiedad para el request
    /// </summary>
    public class PropiedadIdentificadorRequest
    {
        /// <summary>
        /// ID del cliente vendedor (documento en Cosmos DB)
        /// </summary>
        public string ClienteId { get; set; } = string.Empty;

        /// <summary>
        /// ID de la propiedad dentro del array de propiedades del cliente
        /// </summary>
        public string PropiedadId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response de la consulta al agente experto
    /// </summary>
    public class ConsultarExpertoResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string? TwinId { get; set; }
        public int PropiedadesAnalizadas { get; set; }
        public string Respuesta { get; set; } = string.Empty;
        public List<string>? PropiedadesRecomendadas { get; set; }
        public List<string>? Observaciones { get; set; }
        public List<string>? SiguientesPasos { get; set; }

        /// <summary>
        /// Indica si es una nueva conversación o continuación de una existente
        /// </summary>
        public bool IsNewConversation { get; set; }

        /// <summary>
        /// JSON serializado del thread de conversación para continuar en futuras llamadas
        /// Guardar este valor y pasarlo en el siguiente request para mantener el contexto de la conversación
        /// </summary>
        public string? SerializedThreadJson { get; set; }
    }

    #endregion
}

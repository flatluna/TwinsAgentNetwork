using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TwinAgentsNetwork.Services;
using System.Linq;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Function para gestionar propiedades de MiCasa
    /// Provee endpoints para consultar propiedades individuales
    /// </summary>
    public class MiCasaPropertyFx
    {
        private readonly ILogger<MiCasaPropertyFx> _logger;
        private readonly IConfiguration _configuration;

        public MiCasaPropertyFx(ILogger<MiCasaPropertyFx> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Retrieves a specific property by client ID, TwinID, and property ID
        /// GET /api/micasa/properties/{clientId}/{twinId}/{propiedadId}
        /// </summary>
        [Function("GetPropertyByClientTwinAndId")]
        public async Task<HttpResponseData> GetPropertyByClientTwinAndId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "micasa/properties/{clientId}/{twinId}/{propiedadId}")] HttpRequestData req,
            string clientId,
            string twinId,
            string propiedadId)
        {
            _logger.LogInformation("?? GetPropertyByClientTwinAndId function triggered. ClientID: {ClientId}, TwinID: {TwinId}, PropertyID: {PropiedadId}", 
                clientId, twinId, propiedadId);

            try
            {
                // Validate parameters
                if (string.IsNullOrEmpty(clientId))
                {
                    _logger.LogError("? ClientID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "ClientID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("? TwinID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "TwinID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(propiedadId))
                {
                    _logger.LogError("? PropertyID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "PropertyID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("?? Retrieving property: ClientID={ClientId}, TwinID={TwinId}, PropertyID={PropiedadId}", 
                    clientId, twinId, propiedadId);

                // Call service to retrieve property with configuration for SAS URL generation
                var propiedadesService = new AgentMiCasaPropiedadesCosmosDB(_configuration);
                var result = await propiedadesService.GetPropertyByClientTwinAndIdAsync(clientId, twinId, propiedadId);

                if (!result.Success)
                {
                    _logger.LogWarning("?? Property not found or error occurred: {ErrorMessage}", result.ErrorMessage);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = result.ErrorMessage
                    }));
                    return notFoundResponse;
                }

                _logger.LogInformation("? Property retrieved successfully. RU consumed: {RUConsumed:F2}", result.RUConsumed);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    clientId = result.ClientId,
                    twinId = result.TwinId,
                    propertyId = propiedadId,
                    propiedad = result.Propiedad,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp,
                    message = "Property retrieved successfully"
                }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error in GetPropertyByClientTwinAndId: {Message}", ex.Message);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while retrieving the property"
                }));
                return errorResponse;
            }
        }

        /// <summary>
        /// CORS preflight for GetPropertyByClientTwinAndId
        /// </summary>
        [Function("GetPropertyByClientTwinAndIdOptions")]
        public async Task<HttpResponseData> GetPropertyByClientTwinAndIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "micasa/properties/{clientId}/{twinId}/{propiedadId}")] HttpRequestData req,
            string clientId,
            string twinId,
            string propiedadId)
        {
            _logger.LogInformation("? CORS preflight request handled for GetPropertyByClientTwinAndId");
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, req);
            return response;
        }

        /// <summary>
        /// Adds CORS headers to the response
        /// </summary>
        private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            // Get origin from request or use wildcard
            string origin = request.Headers.Contains("Origin") 
                ? request.Headers.GetValues("Origin").First() 
                : "*";

            response.Headers.Add("Access-Control-Allow-Origin", origin);
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS, PUT, DELETE");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Requested-With");
            response.Headers.Add("Access-Control-Max-Age", "3600");
            response.Headers.Add("Access-Control-Allow-Credentials", "true");
        }
    }
}
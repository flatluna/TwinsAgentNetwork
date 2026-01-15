using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinAgentsNetwork.Agents;

namespace TwinAgentsNetwork.AzureFunctions
{
    public class AgentMasterFx
    {
        private readonly ILogger<AgentMasterFx> _logger;
        private readonly TwinAgentMaster _twinAgentMaster;

        public AgentMasterFx(ILogger<AgentMasterFx> logger, TwinAgentMaster twinAgentMaster)
        {
            _logger = logger;
            _twinAgentMaster = twinAgentMaster;
        }

        [Function("DetectAgentIntention")]
        public async Task<HttpResponseData> DetectAgentIntention(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agent-master/detect-intention")] HttpRequestData req)
        {
            _logger.LogInformation("🎯 DetectAgentIntention function triggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                
                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Request body is required"
                    }));
                    return badResponse;
                }

                var requestData = JsonSerializer.Deserialize<DetectIntentionRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (requestData == null || string.IsNullOrEmpty(requestData.TwinId))
                {
                    _logger.LogError("❌ TwinId parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "TwinId parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(requestData.Message))
                {
                    _logger.LogError("❌ Message parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Message parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation($"🔍 Detecting agent intention for Twin ID: {requestData.TwinId}");
                _logger.LogInformation($"📝 Message: {requestData.Message}");
                _logger.LogInformation($"🤖 Agent Name: {requestData.NombreAgente ?? "null"}");
                _logger.LogInformation($"📊 Message Number: {requestData.NumeroMensaje}");

                int messageNumber = string.IsNullOrEmpty(requestData.NombreAgente) ? 0 : requestData.NumeroMensaje;

                var result = await _twinAgentMaster.DetectAgentIntentionAsync(
                    requestData.TwinId,
                    requestData.Message,
                    requestData.NombreAgente ?? "",
                    messageNumber);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error in DetectAgentIntention: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while detecting agent intention"
                }));
                return errorResponse;
            }
        }

        [Function("DetectAgentIntentionOptions")]
        public async Task<HttpResponseData> DetectAgentIntentionOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "agent-master/detect-intention")] HttpRequestData req)
        {
            _logger.LogInformation("✅ CORS preflight request handled");
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, req);
            return response;
        }

        private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        }
    }

    #region Request Models

    public class DetectIntentionRequest
    {
        public string TwinId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? NombreAgente { get; set; }
        public int NumeroMensaje { get; set; }
    }

    #endregion
}

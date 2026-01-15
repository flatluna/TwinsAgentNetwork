using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.Web;
using TwinAgentsNetwork.Agents;
using TwinAgentsNetwork.Models;

namespace TwinAgentsNetwork.AzureFunctions
{
    public class ContactsFx
    {
        private readonly ILogger<ContactsFx> _logger;
        private readonly AgentTwinContacts _agentTwinContacts;

        public ContactsFx(ILogger<ContactsFx> logger, AgentTwinContacts agentTwinContacts)
        {
            _logger = logger;
            _agentTwinContacts = agentTwinContacts;
        }

        /// <summary>
        /// Azure Function to enable users to ask questions about their twin's contact data from the UI
        /// </summary>
        /// <param name="req">HTTP request containing twinId, language, and question parameters</param>
        /// <returns>JSON formatted response with AI agent contact analysis</returns>
        [Function("AskTwinContactsQuestion")]
        public async Task<HttpResponseData> AskTwinContactsQuestion(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-contacts/ask")] HttpRequestData req)
        {
            _logger.LogInformation("📞 AskTwinContactsQuestion function triggered");

            try
            {
                // Read and parse the request body
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

                // Deserialize the request
                var requestData = JsonSerializer.Deserialize<TwinContactsQuestionRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Validate required parameters
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

                // Set defaults for optional parameters
                string language = !string.IsNullOrEmpty(requestData.Language) ? requestData.Language : "English";
                string question = !string.IsNullOrEmpty(requestData.Question) ? requestData.Question : 
                    "Tell me about the contacts and relationships.";

                _logger.LogInformation($"📞 Processing contacts question for Twin ID: {requestData.TwinId}, Language: {language}");

                // Call the AI agent to get the contacts analysis response
                var conversationResult = await _agentTwinContacts.AgentTwinContactsAnswer(requestData.TwinId, language, requestData.Question, requestData.SerializedThreadJson ?? null);

                _logger.LogInformation($"✅ Successfully generated contacts analysis for Twin ID: {requestData.TwinId}");

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(JsonSerializer.Serialize(conversationResult, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                return response;
            }
            catch (JsonException ex)
            {
                _logger.LogError($"❌ JSON parsing error: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid JSON format in request body"
                }));
                return errorResponse;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError($"❌ Argument validation error: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = ex.Message
                }));
                return errorResponse;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError($"❌ Security validation error: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = ex.Message
                }));
                return errorResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Unexpected error in AskTwinContactsQuestion: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An unexpected error occurred while analyzing contact data"
                }));
                return errorResponse;
            }
        }

        /// <summary>
        /// Alternative GET endpoint for simple contacts questions via query parameters
        /// </summary>
        [Function("AskTwinContactsQuestionGet")]
        public async Task<HttpResponseData> AskTwinContactsQuestionGet(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-contacts/ask/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"📞 AskTwinContactsQuestionGet function triggered for Twin ID: {twinId}");

            try
            {
                // Validate twinId
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                // Get query parameters
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                string language = query["language"] ?? "English";
                string question = query["question"] ?? "Tell me about the contacts and relationships.";

                _logger.LogInformation($"📞 Processing GET contacts question for Twin ID: {twinId}, Language: {language}");

                // Call the AI agent to get the contacts analysis response
                var conversationResult = await _agentTwinContacts.AgentTwinContactsAnswer(twinId, language, question);

                _logger.LogInformation($"✅ Successfully generated contacts analysis for Twin ID: {twinId}");

                // Create successful response (plain text for GET compatibility)
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                
                response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
                await response.WriteStringAsync(conversationResult.LastAssistantResponse);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Unexpected error in AskTwinContactsQuestionGet: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync($"Contacts analysis error: {ex.Message}");
                return errorResponse;
            }
        }

        /// <summary>
        /// Adds CORS headers to the response
        /// </summary>
        private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        }
    }

    /// <summary>
    /// Request model for twin contacts data questions
    /// </summary>
    public class TwinContactsQuestionRequest
    {
        public string TwinId { get; set; } = string.Empty;
        public string Language { get; set; } = "English";
        public string Question { get; set; } = string.Empty;
        public string SerializedThreadJson { get; set; } = string.Empty; // Thread serializado para continuar conversación
    }
}

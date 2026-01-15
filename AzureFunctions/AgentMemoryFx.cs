using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinAgentsNetwork.Agents;

namespace TwinAgentsNetwork.AzureFunctions
{
    public class AgentMemoryFx
    {
        private readonly ILogger<AgentMemoryFx> _logger;
        private readonly AgentTwinMyMemory _agentTwinMyMemory;

        public AgentMemoryFx(ILogger<AgentMemoryFx> logger, AgentTwinMyMemory agentTwinMyMemory)
        {
            _logger = logger;
            _agentTwinMyMemory = agentTwinMyMemory;
        }

        /// <summary>
        /// Azure Function to search MyMemory translation memory and generate AI-powered HTML responses
        /// </summary>
        /// <param name="req">HTTP request containing twinId, question, and language parameters</param>
        /// <returns>JSON formatted response with MyMemory search results and AI analysis HTML</returns>
        [Function("SearchMyMemoryQuestion")]
        public async Task<HttpResponseData> SearchMyMemoryQuestion(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-memory/ask")] HttpRequestData req)
        {
            _logger.LogInformation("🔍 SearchMyMemoryQuestion function triggered");

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
                var requestData = JsonSerializer.Deserialize<TwinMyMemoryQuestionRequest>(requestBody, new JsonSerializerOptions
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

                if (string.IsNullOrEmpty(requestData.Question))
                {
                    _logger.LogError("❌ Question parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Question parameter is required"
                    }));
                    return badResponse;
                }

                // Set defaults for optional parameters
                string language = !string.IsNullOrEmpty(requestData.Language) ? requestData.Language : "English";

                _logger.LogInformation($"🔍 Processing MyMemory question for Twin ID: {requestData.TwinId}, Language: {language}");

                // Call the AI agent to get the MyMemory search and analysis response
                var conversationResult = await _agentTwinMyMemory.SearchMyMemoryWithAIAsync(
                    requestData.TwinId, 
                    requestData.Question, 
                    language, 
                    requestData.SerializedThreadJson ?? null);

                _logger.LogInformation($"✅ Successfully generated MyMemory analysis for Twin ID: {requestData.TwinId}");

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = conversationResult.Success,
                    twinId = requestData.TwinId,
                    question = requestData.Question,
                    language = language,
                    myMemoryResults = conversationResult.MyMemoryResults,
                    aiAnalysisHtml = conversationResult.AIAnalysisHtml,
                    serializedThreadJson = conversationResult.SerializedThreadJson,
                    errorMessage = conversationResult.ErrorMessage,
                    processedAt = conversationResult.ProcessedTimestamp,
                    message = conversationResult.Success ? "MyMemory analysis completed successfully" : "Error processing MyMemory search"
                }, new JsonSerializerOptions
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
            catch (Exception ex)
            {
                _logger.LogError($"❌ Unexpected error in SearchMyMemoryQuestion: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An unexpected error occurred while processing MyMemory search"
                }));
                return errorResponse;
            }
        }

        /// <summary>
        /// Alternative GET endpoint for simple MyMemory questions via query parameters
        /// </summary>
        [Function("SearchMyMemoryQuestionGet")]
        public async Task<HttpResponseData> SearchMyMemoryQuestionGet(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-memory/ask/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"🔍 SearchMyMemoryQuestionGet function triggered for Twin ID: {twinId}");

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
                string question = query["q"] ?? query["question"] ?? "";
                string language = query["language"] ?? "English";

                if (string.IsNullOrEmpty(question))
                {
                    _logger.LogError("❌ Question parameter 'q' or 'question' is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Question parameter 'q' or 'question' is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation($"🔍 Processing GET MyMemory question for Twin ID: {twinId}, Language: {language}");

                // Call the AI agent to get the MyMemory analysis response
                var conversationResult = await _agentTwinMyMemory.SearchMyMemoryWithAIAsync(
                    twinId, 
                    question, 
                    language);

                _logger.LogInformation($"✅ Successfully generated MyMemory analysis for Twin ID: {twinId}");

                // Create successful response (returning HTML for GET compatibility)
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                
                response.Headers.Add("Content-Type", "text/html; charset=utf-8");
                await response.WriteStringAsync(conversationResult.AIAnalysisHtml);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Unexpected error in SearchMyMemoryQuestionGet: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync($"MyMemory analysis error: {ex.Message}");
                return errorResponse;
            }
        }

        /// <summary>
        /// Gets raw MyMemory translation memory suggestions without AI analysis
        /// </summary>
        [Function("GetMyMemorySuggestions")]
        public async Task<HttpResponseData> GetMyMemorySuggestions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-memory/suggestions/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"🔍 GetMyMemorySuggestions function triggered for Twin ID: {twinId}");

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
                string searchTerm = query["q"] ?? query["search"] ?? "";

                if (string.IsNullOrEmpty(searchTerm))
                {
                    _logger.LogError("❌ Search term parameter 'q' or 'search' is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Search term parameter 'q' or 'search' is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation($"🔍 Getting MyMemory suggestions for Twin ID: {twinId}, Search term: {searchTerm}");

                // Get translation memory suggestions
                var suggestions = await _agentTwinMyMemory.GetTranslationMemorySuggestionsAsync(twinId, searchTerm);

                _logger.LogInformation($"✅ Retrieved {suggestions.Count} MyMemory suggestions for Twin ID: {twinId}");

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    twinId = twinId,
                    searchTerm = searchTerm,
                    suggestions = suggestions,
                    count = suggestions.Count,
                    message = $"Found {suggestions.Count} translation memory suggestions for '{searchTerm}'"
                }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Unexpected error in GetMyMemorySuggestions: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An unexpected error occurred while retrieving suggestions"
                }));
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
    /// Request model for MyMemory question
    /// </summary>
    public class TwinMyMemoryQuestionRequest
    {
        public string TwinId { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public string Language { get; set; } = "English";
        public string SerializedThreadJson { get; set; } = string.Empty; // Thread serialized for conversation continuity
    }
}

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
    public class ShortMemoryFx
    {
        private readonly ILogger<ShortMemoryFx> _logger;
        private readonly AgentTwinShortMemory _agentTwinShortMemory;

        public ShortMemoryFx(ILogger<ShortMemoryFx> logger, AgentTwinShortMemory agentTwinShortMemory)
        {
            _logger = logger;
            _agentTwinShortMemory = agentTwinShortMemory;
        }

        /// <summary>
        /// Azure Function to search short memory documents using semantic search and generate AI-powered HTML responses
        /// </summary>
        /// <param name="req">HTTP request containing twinId, question, and language parameters</param>
        /// <returns>JSON formatted response with short memory search results and AI analysis HTML</returns>
        [Function("SearchShortMemoryQuestion")]
        public async Task<HttpResponseData> SearchShortMemoryQuestion(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-short-memory/ask")] HttpRequestData req)
        {
            _logger.LogInformation("?? SearchShortMemoryQuestion function triggered");

            try
            {
                // Read and parse the request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                
                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("? Request body is empty");
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
                var requestData = JsonSerializer.Deserialize<TwinShortMemoryQuestionRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Validate required parameters
                if (requestData == null || string.IsNullOrEmpty(requestData.TwinId))
                {
                    _logger.LogError("? TwinId parameter is required");
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
                    _logger.LogError("? Question parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Question parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation($"?? Searching short memory for Twin ID: {requestData.TwinId}");
                _logger.LogInformation($"? Question: {requestData.Question}");

                // Search short memory with AI analysis
                var result = await _agentTwinShortMemory.SearchShortMemoryWithAIAsync(
                    requestData.TwinId,
                    requestData.Question,
                    requestData.Language ?? "English",
                    requestData.SerializedThreadJson ?? null);

                if (!result.Success)
                {
                    _logger.LogError($"? Short memory search failed: {result.ErrorMessage}");
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        twinId = requestData.TwinId,
                        question = requestData.Question,
                        errorMessage = result.ErrorMessage
                    }));
                    return errorResponse;
                }

                _logger.LogInformation($"? Short memory analysis completed for Twin ID: {requestData.TwinId}");

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    twinId = requestData.TwinId,
                    question = requestData.Question,
                    language = requestData.Language ?? "English",
                    shortMemoryResults = result.ShortMemoryResults,
                    aiAnalysisHtml = result.AIAnalysisHtml,
                    serializedThreadJson = result.SerializedThreadJson,
                    processedAt = result.ProcessedTimestamp,
                    message = "Short memory analysis completed successfully"
                }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                return response;
            }
            catch (JsonException ex)
            {
                _logger.LogError($"? JSON parsing error: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Invalid JSON format in request body"
                }));
                return errorResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError($"? Unexpected error in SearchShortMemoryQuestion: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An unexpected error occurred while processing your request"
                }));
                return errorResponse;
            }
        }

        /// <summary>
        /// Azure Function GET endpoint for short memory search - returns HTML directly
        /// </summary>
        /// <param name="req">HTTP request with query parameters</param>
        /// <param name="twinId">Twin ID from URL</param>
        /// <returns>HTML response with AI analysis</returns>
        [Function("SearchShortMemoryQuestionGet")]
        public async Task<HttpResponseData> SearchShortMemoryQuestionGet(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-short-memory/ask/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"?? SearchShortMemoryQuestionGet function triggered for Twin ID: {twinId}");

            try
            {
                // Extract query parameters using ParseQueryString
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                string? questionParam = query["q"] ?? query["question"];
                string language = query["language"] ?? "English";

                // Validate parameters
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("? Twin ID is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(GenerateErrorHtml("Twin ID parameter is required", language));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(questionParam))
                {
                    _logger.LogError("? Question parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(GenerateErrorHtml("Question parameter is required (use ?q=your+question)", language));
                    return badResponse;
                }

                _logger.LogInformation($"? Question: {questionParam}");

                // Search short memory with AI analysis
                var result = await _agentTwinShortMemory.SearchShortMemoryWithAIAsync(
                    twinId,
                    questionParam,
                    language);

                if (!result.Success)
                {
                    _logger.LogError($"? Short memory search failed: {result.ErrorMessage}");
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(GenerateErrorHtml(result.ErrorMessage, language));
                    return errorResponse;
                }

                _logger.LogInformation($"? Short memory analysis completed for Twin ID: {twinId}");

                // Return HTML response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "text/html; charset=utf-8");
                await response.WriteStringAsync(result.AIAnalysisHtml);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"? Error in SearchShortMemoryQuestionGet: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(GenerateErrorHtml("An error occurred while processing your request", "English"));
                return errorResponse;
            }
        }

        /// <summary>
        /// Azure Function to get raw short memory suggestions without AI analysis
        /// </summary>
        /// <param name="req">HTTP request with query parameters</param>
        /// <param name="twinId">Twin ID from URL</param>
        /// <returns>JSON response with raw search results</returns>
        [Function("GetShortMemorySuggestions")]
        public async Task<HttpResponseData> GetShortMemorySuggestions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-short-memory/suggestions/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"?? GetShortMemorySuggestions function triggered for Twin ID: {twinId}");

            try
            {
                // Extract query parameters using ParseQueryString
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                string? searchTermParam = query["q"] ?? query["search"];
                string limitStr = query["limit"] ?? "10";

                if (!int.TryParse(limitStr, out int limit))
                {
                    limit = 10;
                }

                // Validate parameters
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("? Twin ID is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(searchTermParam))
                {
                    _logger.LogError("? Search term is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Search term parameter is required (use ?q=search+term)"
                    }));
                    return badResponse;
                }

                _logger.LogInformation($"?? Searching short memory with term: {searchTermParam}");

                // Perform simple search without AI analysis
                var result = await _agentTwinShortMemory.SimpleSearchShortMemoryAsync(twinId, searchTermParam, limit);

                _logger.LogInformation($"? Found {result.Documents.Count} short memory suggestions");

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    twinId = twinId,
                    searchTerm = searchTermParam,
                    documentCount = result.Documents.Count,
                    documents = result.Documents,
                    processedAt = DateTime.UtcNow
                }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"? Error in GetShortMemorySuggestions: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while retrieving suggestions"
                }));
                return errorResponse;
            }
        }

        /// <summary>
        /// Azure Function to search short memory documents using semantic search
        /// </summary>
        /// <param name="req">HTTP request with query parameters</param>
        /// <param name="twinId">Twin ID from URL</param>
        /// <returns>JSON response with raw search results</returns>
        [Function("SearchShortMemories")]
        public async Task<HttpResponseData> SearchShortMemories(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/short-memory/search")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"🔍 SearchShortMemories function triggered for Twin ID: {twinId}");

            try
            {
                // Extract query parameters using ParseQueryString
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                string? searchQuery = query["q"] ?? query["query"];
                string limitStr = query["limit"] ?? "10";

                if (!int.TryParse(limitStr, out int limit))
                {
                    limit = 10;
                }

                // Validate parameters
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(searchQuery))
                {
                    _logger.LogError("❌ Search query is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Search query parameter is required (use ?q=search+term)"
                    }));
                    return badResponse;
                }

                _logger.LogInformation($"🔍 Searching short memory with query: {searchQuery}");

                // Perform simple search without AI analysis
                var result = await _agentTwinShortMemory.SimpleSearchShortMemoryAsync(twinId, searchQuery, limit);

                _logger.LogInformation($"✅ Found {result.Documents.Count} documents");

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = result.Success,
                    documents = result.Documents,
                    responseStatus = result.ResponseStatus,
                    responseDetails = result.ResponseDetails,
                    processedAt = DateTime.UtcNow
                }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error in SearchShortMemories: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while searching short memory"
                }));
                return errorResponse;
            }
        }

        /// <summary>
        /// Azure Function to search short memory documents by date range
        /// </summary>
        [Function("SearchShortMemoriesByDateRange")]
        public async Task<HttpResponseData> SearchShortMemoriesByDateRange(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/short-memory/search-by-date")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("📅 SearchShortMemoriesByDateRange function triggered for Twin: {TwinId}", twinId);

            try
            {
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

                var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var fromDateStr = queryParams["fromDate"];
                var toDateStr = queryParams["toDate"];
                var topParam = queryParams["top"];
                var pageParam = queryParams["page"];

                if (string.IsNullOrEmpty(fromDateStr) || string.IsNullOrEmpty(toDateStr))
                {
                    _logger.LogError("❌ fromDate and toDate parameters are required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "fromDate and toDate parameters are required (ISO 8601 format)"
                    }));
                    return badResponse;
                }

                if (!DateTime.TryParse(fromDateStr, out var fromDate))
                {
                    _logger.LogError("❌ Invalid fromDate format");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Invalid fromDate format (use ISO 8601)"
                    }));
                    return badResponse;
                }

                if (!DateTime.TryParse(toDateStr, out var toDate))
                {
                    _logger.LogError("❌ Invalid toDate format");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Invalid toDate format (use ISO 8601)"
                    }));
                    return badResponse;
                }

                int top = 10;
                if (!string.IsNullOrEmpty(topParam) && !int.TryParse(topParam, out top))
                {
                    top = 10;
                }

                int page = 1;
                if (!string.IsNullOrEmpty(pageParam) && !int.TryParse(pageParam, out page))
                {
                    page = 1;
                }

                _logger.LogInformation($"🔍 Searching short memory by date range: {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}");

                var result = await _agentTwinShortMemory.SearchShortMemoriesByDateRangeAsync(twinId, fromDate, toDate, top, page);

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
                _logger.LogError($"❌ Error in SearchShortMemoriesByDateRange: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while searching short memories"
                }));
                return errorResponse;
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// Generates error HTML response
        /// </summary>
        private string GenerateErrorHtml(string errorMessage, string language)
        {
            string title = language.ToLower() switch
            {
                "spanish" => "Error en la Búsqueda de Memoria Corta",
                "french" => "Erreur de Recherche de Mémoire Courte",
                "german" => "Fehler bei der Suche nach Kurzzeitgedächtnis",
                _ => "Short Memory Search Error"
            };

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{title}</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            margin: 0;
            padding: 20px;
            min-height: 100vh;
        }}
        .error-container {{
            background-color: #ffe6e6;
            border: 3px solid #C73E1D;
            padding: 30px;
            border-radius: 12px;
            font-family: Arial, sans-serif;
            max-width: 600px;
            margin: 0 auto;
            box-shadow: 0 4px 15px rgba(0,0,0,0.2);
        }}
        .error-title {{
            color: #C73E1D;
            font-size: 28px;
            font-weight: bold;
            margin-bottom: 15px;
            display: flex;
            align-items: center;
            gap: 10px;
        }}
        .error-icon {{
            font-size: 32px;
        }}
        .error-message {{
            color: #333;
            font-size: 16px;
            line-height: 1.6;
        }}
    </style>
</head>
<body>
    <div class='error-container'>
        <div class='error-title'>
            <span class='error-icon'>??</span>
            {title}
        </div>
        <div class='error-message'>{errorMessage}</div>
    </div>
</body>
</html>";
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

        #endregion
    }

    #region Request Models

    /// <summary>
    /// Request model for short memory search with AI analysis
    /// </summary>
    public class TwinShortMemoryQuestionRequest
    {
        public string TwinId { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public string? Language { get; set; } = "English";
        public string? SerializedThreadJson { get; set; }
    }

    /// <summary>
    /// Request model for short memory search by date range
    /// </summary>
    public class SearchMemoryByDateRangeRequest
    {
        public string TwinId { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; } 
    }

    #endregion
}

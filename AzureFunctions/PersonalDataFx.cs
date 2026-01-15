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

namespace TwinAgentsNetwork.AzureFunctions;

public class PersonalDataFx
{
    private readonly ILogger<PersonalDataFx> _logger;
    private readonly AgentTwinPersonalData _agentTwinPersonalData;
    private readonly AgentTwinFamily _agentTwinFamily;

    public PersonalDataFx(ILogger<PersonalDataFx> logger, AgentTwinPersonalData agentTwinPersonalData, AgentTwinFamily agentTwinFamily)
    {
        _logger = logger;
        _agentTwinPersonalData = agentTwinPersonalData;
        _agentTwinFamily = agentTwinFamily;
    }

    [Function("PersonalDataFx")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }

    /// <summary>
    /// Azure Function to enable users to ask questions about their twin's personal data from the UI
    /// </summary>
    /// <param name="req">HTTP request containing twinId, language, and question parameters</param>
    /// <returns>HTML formatted response with AI agent analysis</returns>
    [Function("AskTwinPersonalQuestion")]
    public async Task<HttpResponseData> AskTwinPersonalQuestion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-personal/ask")] HttpRequestData req)
    {
        _logger.LogInformation("?? AskTwinPersonalQuestion function triggered");

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
            var requestData = JsonSerializer.Deserialize<TwinQuestionRequest>(requestBody, new JsonSerializerOptions
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

            // Set defaults for optional parameters
            string language = !string.IsNullOrEmpty(requestData.Language) ? requestData.Language : "English";
            string question = !string.IsNullOrEmpty(requestData.Question) ? requestData.Question : 
                "Please provide a comprehensive analysis of my personal data and characteristics.";

            _logger.LogInformation($"?? Processing question for Twin ID: {requestData.TwinId}, Language: {language}");

            // Call the AI agent to get the response
            var conversationResult = await _agentTwinPersonalData.AgentTwinPersonal(
                requestData.TwinId, 
                language, 
                question,
                requestData.SerializedThreadJson); // Pasar el thread serializado

            _logger.LogInformation($"? Successfully generated AI response for Twin ID: {requestData.TwinId}");

            // Create successful response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            
            // Devolver el objeto completo en lugar de solo HTML
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
        catch (ArgumentException ex)
        {
            _logger.LogError($"? Argument validation error: {ex.Message}");
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
            _logger.LogError($"? Unexpected error in AskTwinPersonalQuestion: {ex.Message}");
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
    /// Alternative GET endpoint for simple questions via query parameters
    /// </summary>
    [Function("AskTwinPersonalQuestionGet")]
    public async Task<HttpResponseData> AskTwinPersonalQuestionGet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-personal/ask/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"?? AskTwinPersonalQuestionGet function triggered for Twin ID: {twinId}");

        try
        {
            // Validate twinId
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("? Twin ID parameter is required");
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
            string question = query["question"] ?? "Please provide a comprehensive analysis of my personal data and characteristics.";

            _logger.LogInformation($"?? Processing GET question for Twin ID: {twinId}, Language: {language}");

            // Call the AI agent to get the response
            var conversationResult = await _agentTwinPersonalData.AgentTwinPersonal(twinId, language, question);

            _logger.LogInformation($"? Successfully generated AI response for Twin ID: {twinId}");

            // Create successful response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            
            // Para GET, mantener compatibilidad devolviendo solo el HTML de la última respuesta
            response.Headers.Add("Content-Type", "text/html; charset=utf-8");
            await response.WriteStringAsync(conversationResult.LastAssistantResponse);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"? Unexpected error in AskTwinPersonalQuestionGet: {ex.Message}");
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
    /// Azure Function to enable users to ask questions about their twin's family data from the UI
    /// </summary>
    /// <param name="req">HTTP request containing twinId, language, and question parameters</param>
    /// <returns>JSON formatted response with AI agent family analysis</returns>
    [Function("AskTwinFamilyQuestion")]
    public async Task<HttpResponseData> AskTwinFamilyQuestion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-family/ask")] HttpRequestData req)
    {
        _logger.LogInformation("?? AskTwinFamilyQuestion function triggered");

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
            var requestData = JsonSerializer.Deserialize<TwinFamilyQuestionRequest>(requestBody, new JsonSerializerOptions
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

            // Set defaults for optional parameters
            string language = !string.IsNullOrEmpty(requestData.Language) ? requestData.Language : "English";
            string question = !string.IsNullOrEmpty(requestData.Question) ? requestData.Question : 
                "Tell me about the family structure and relationships.";

            _logger.LogInformation($"??????????? Processing family question for Twin ID: {requestData.TwinId}, Language: {language}");

            // Call the AI agent to get the family analysis response
            var conversationResult = await _agentTwinFamily.AgentTwinFamilyAnswer(requestData.TwinId, language, requestData.Question, requestData.SerializedThreadJson ?? null);

            _logger.LogInformation($"? Successfully generated family analysis for Twin ID: {requestData.TwinId}");

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
        catch (ArgumentException ex)
        {
            _logger.LogError($"? Argument validation error: {ex.Message}");
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
            _logger.LogError($"? Security validation error: {ex.Message}");
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
            _logger.LogError($"? Unexpected error in AskTwinFamilyQuestion: {ex.Message}");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = "An unexpected error occurred while analyzing family data"
            }));
            return errorResponse;
        }
    }

    /// <summary>
    /// Alternative GET endpoint for simple family questions via query parameters
    /// </summary>
    [Function("AskTwinFamilyQuestionGet")]
    public async Task<HttpResponseData> AskTwinFamilyQuestionGet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-family/ask/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"??????????? AskTwinFamilyQuestionGet function triggered for Twin ID: {twinId}");

        try
        {
            // Validate twinId
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("? Twin ID parameter is required");
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
            string question = query["question"] ?? "Tell me about the family structure and relationships.";

            _logger.LogInformation($"??????????? Processing GET family question for Twin ID: {twinId}, Language: {language}");

            // Call the AI agent to get the family analysis response
            var conversationResult = await _agentTwinFamily.AgentTwinFamilyAnswer(twinId, language, question);

            _logger.LogInformation($"? Successfully generated family analysis for Twin ID: {twinId}");

            // Create successful response (plain text for GET compatibility)
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            await response.WriteStringAsync(conversationResult.LastAssistantResponse);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"? Unexpected error in AskTwinFamilyQuestionGet: {ex.Message}");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync($"Family analysis error: {ex.Message}");
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
/// Request model for twin personal data questions
/// </summary>
public class TwinQuestionRequest
{
    public string TwinId { get; set; } = string.Empty;
    public string Language { get; set; } = "English";
    public string Question { get; set; } = string.Empty;
    public string SerializedThreadJson { get; set; } = string.Empty; // Nuevo campo para thread existente
}

/// <summary>
/// Request model for twin family data questions
/// </summary>
public class TwinFamilyQuestionRequest
{
    public string TwinId { get; set; } = string.Empty;
    public string Language { get; set; } = "English";
    public string Question { get; set; } = string.Empty;
    public string SerializedThreadJson { get; set; } = string.Empty; // Thread serializado para continuar conversación
}
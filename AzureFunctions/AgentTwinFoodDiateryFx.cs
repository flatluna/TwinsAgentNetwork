using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;
using TwinAgentsLibrary.Models;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Function para obtener información nutricional de alimentos
    /// Utiliza AgentTwinFoodDietery para buscar datos nutricionales detallados
    /// </summary>
    public class AgentTwinFoodDiateryFx
    {
        private readonly ILogger<AgentTwinFoodDiateryFx> _logger;
        private readonly AgentTwinFoodDietery _agentTwinFoodDietery;

        public AgentTwinFoodDiateryFx(ILogger<AgentTwinFoodDiateryFx> logger, AgentTwinFoodDietery agentTwinFoodDietery)
        {
            _logger = logger;
            _agentTwinFoodDietery = agentTwinFoodDietery;
        }

        /// <summary>
        /// Azure Function to get detailed nutritional information for a food
        /// </summary>
        /// <param name="req">HTTP request containing food description and twinId</param>
        /// <returns>JSON response with detailed food nutrition information</returns>
        [Function("GetFoodNutritionInfo")]
        public async Task<HttpResponseData> GetFoodNutritionInfo(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-food/nutrition")] HttpRequestData req)
        {
            _logger.LogInformation("🍎 GetFoodNutritionInfo function triggered");

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
                var requestData = JsonSerializer.Deserialize<FoodNutritionRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Validate required parameters
                if (requestData == null || string.IsNullOrEmpty(requestData.FoodDescription))
                {
                    _logger.LogError("❌ Food description parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Food description parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(requestData.TwinId))
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

                _logger.LogInformation("🔍 Getting nutrition info for food: {FoodDescription}, Twin: {TwinId}",
                    requestData.FoodDescription, requestData.TwinId);

                // Call the agent method
                var foodDiaryEntry = await _agentTwinFoodDietery.GetFoodNutritionInfoAsync(
                    requestData.FoodDescription,
                    requestData.TwinId);

                if (foodDiaryEntry == null)
                {
                    _logger.LogError("❌ Failed to get nutrition information");
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Failed to retrieve nutrition information"
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Nutrition info retrieved successfully for: {FoodDescription}",
                    requestData.FoodDescription);

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    foodDiaryEntry = foodDiaryEntry,
                    processedAt = DateTime.UtcNow,
                    message = $"Nutrition information retrieved successfully for: {requestData.FoodDescription}"
                }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                return response;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "❌ JSON parsing error in GetFoodNutritionInfo");
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
                _logger.LogError(ex, "❌ Unexpected error in GetFoodNutritionInfo");
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
        /// Adds CORS headers to the response
        /// </summary>
        private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        }
    }

    #region Request Models

    /// <summary>
    /// Request model for food nutrition information
    /// </summary>
    public class FoodNutritionRequest
    {
        public string FoodDescription { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
    }

    #endregion
}

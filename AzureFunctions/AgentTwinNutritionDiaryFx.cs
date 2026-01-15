using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;
using TwinAgentsNetwork.Services;
using TwinAgentsLibrary.Models;
using Newtonsoft.Json;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Function para registrar alimentos en el diario de nutrición
    /// Utiliza AgentTwinFoodDietery para obtener información nutricional
    /// y AgentNutritionCosmosDB para guardar en la base de datos
    /// </summary>
    public class AgentTwinNutritionDiaryFx
    {
        private readonly ILogger<AgentTwinNutritionDiaryFx> _logger;
        private readonly AgentTwinFoodDietery _foodDieteryAgent;
        private readonly AgentNutritionCosmosDB _cosmosService;

        public AgentTwinNutritionDiaryFx(
            ILogger<AgentTwinNutritionDiaryFx> logger,
            AgentTwinFoodDietery foodDieteryAgent,
            AgentNutritionCosmosDB cosmosService)
        {
            _logger = logger;
            _foodDieteryAgent = foodDieteryAgent;
            _cosmosService = cosmosService;
        }

        /// <summary>
        /// Azure Function para registrar un alimento en el diario de nutrición
        /// Obtiene información nutricional completa y la guarda en Cosmos DB
        /// </summary>
        /// <param name="req">HTTP request con foodDescription y twinId</param>
   

        /// <summary>
        /// Azure Function para obtener el historial de alimentos de un Twin
        /// </summary>
        /// <param name="req">HTTP request con twinId en la query string</param>
        /// <returns>JSON con la lista de alimentos registrados</returns>
        [Function("GetFoodHistory")]
        public async Task<HttpResponseData> GetFoodHistory(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-nutrition/history/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("📊 GetFoodHistory function triggered for Twin: {TwinID}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ TwinID is required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "TwinID is required");
                }

                _logger.LogInformation("🔍 Fetching food history for Twin: {TwinID}", twinId);

                // Obtener todas las entradas del diario para este Twin
                var foodEntries = await _cosmosService.GetFoodDiaryEntriesByTwinAsync(twinId);

                _logger.LogInformation("✅ Retrieved {Count} food entries for Twin: {TwinID}", 
                    foodEntries.Count, twinId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinID = twinId,
                    count = foodEntries.Count,
                    entries = foodEntries
                });

                return response;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Validation error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in GetFoodHistory");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                    "An error occurred while retrieving food history");
            }
        }

        /// <summary>
        /// Azure Function para obtener una entrada específica del diario de alimentos
        /// </summary>
        [Function("GetFoodEntry")]
        public async Task<HttpResponseData> GetFoodEntry(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-nutrition/entry/{twinId}/{entryId}")] HttpRequestData req,
            string twinId,
            string entryId)
        {
            _logger.LogInformation("📖 GetFoodEntry function triggered. TwinID: {TwinID}, EntryID: {EntryID}", 
                twinId, entryId);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(entryId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "TwinID and EntryID are required");
                }

                var foodEntry = await _cosmosService.GetFoodDiaryEntryAsync(entryId, twinId);

                if (foodEntry == null)
                {
                    _logger.LogWarning("⚠️ Food entry not found. EntryID: {EntryID}", entryId);
                    return await CreateErrorResponse(req, HttpStatusCode.NotFound, 
                        "Food entry not found");
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    data = foodEntry
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in GetFoodEntry");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                    "An error occurred while retrieving the food entry");
            }
        }

        /// <summary>
        /// Azure Function para eliminar una entrada del diario de alimentos
        /// </summary>
        [Function("DeleteFoodEntry")]
        public async Task<HttpResponseData> DeleteFoodEntry(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twin-nutrition/entry/{twinId}/{entryId}")] HttpRequestData req,
            string twinId,
            string entryId)
        {
            _logger.LogInformation("🗑️ DeleteFoodEntry function triggered. TwinID: {TwinID}, EntryID: {EntryID}", 
                twinId, entryId);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(entryId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "TwinID and EntryID are required");
                }

                await _cosmosService.DeleteFoodDiaryEntryAsync(entryId, twinId);

                _logger.LogInformation("✅ Food entry deleted successfully. EntryID: {EntryID}", entryId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Food entry deleted successfully"
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in DeleteFoodEntry");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                    "An error occurred while deleting the food entry");
            }
        }

        /// <summary>
        /// Azure Function para guardar una entrada de alimento directamente en la base de datos
        /// </summary>
        /// <param name="req">HTTP request con FoodDiaryEntry en JSON</param>
        /// <returns>JSON con la entrada guardada en Cosmos DB</returns>
        [Function("SaveFoodEntry")]
        public async Task<HttpResponseData> SaveFoodEntry(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-nutrition/save-entry")] HttpRequestData req)
        {
            _logger.LogInformation("💾 SaveFoodEntry function triggered");

            try
            {
                // Leer el cuerpo de la solicitud
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required");
                }

                // Deserializar la solicitud como FoodDiaryEntry
                var foodDiaryEntry = JsonConvert.DeserializeObject<FoodDiaryEntry>(requestBody);

                // Validar parámetros requeridos
                if (foodDiaryEntry == null || string.IsNullOrEmpty(foodDiaryEntry.TwinID))
                {
                    _logger.LogError("❌ FoodDiaryEntry and TwinID are required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "FoodDiaryEntry and TwinID are required");
                }

                _logger.LogInformation("📋 Saving food entry: {DiaryFood} for Twin: {TwinID}", 
                    foodDiaryEntry.DiaryFood, foodDiaryEntry.TwinID);

                // Guardar directamente en Cosmos DB
                _logger.LogInformation("💾 Saving to Cosmos DB...");
                foodDiaryEntry.FoodStats.id = Guid.NewGuid().ToString();
                foodDiaryEntry.FoodStats.TwinID = foodDiaryEntry.TwinID;
                foodDiaryEntry.FoodStats.FoodName = foodDiaryEntry.FoodName;

                var foodExists = await _cosmosService.CheckFoodExistsAsync(
                    foodDiaryEntry.FoodName, foodDiaryEntry.TwinID);
                if (!foodExists)
                {
                    var savedFood = await _cosmosService.SaveFoodAlimentosAsync(foodDiaryEntry.FoodStats);
                }
              

                FoodDiaryCosmosEntry FoodCosmos = new FoodDiaryCosmosEntry();
              
                FoodCosmos.FoodName = foodDiaryEntry.FoodName;
                FoodCosmos.TwinID = foodDiaryEntry.TwinID;
                FoodCosmos.DiaryFood = foodDiaryEntry.DiaryFood;
                FoodCosmos.DateTimeConsumed = foodDiaryEntry.DateTimeConsumed;
                FoodCosmos.DateTimeCreated = DateTime.Now;
                FoodCosmos.Time = foodDiaryEntry.Time;
                FoodCosmos.FoodName = foodDiaryEntry.FoodName;
                FoodCosmos.Id = Guid.NewGuid().ToString(); 


                var savedEntry = await _cosmosService.SaveFoodDiaryEntryAsync(FoodCosmos);

                _logger.LogInformation("✅ Food entry saved successfully. ID: {Id}", savedEntry.Id);

                // Crear respuesta exitosa
                var successResponse = req.CreateResponse(HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Food entry saved successfully",
                    data = savedEntry
                });

                return successResponse;
            }
            catch (ArgumentNullException ex)
            {
                _logger.LogError(ex, "❌ Null argument error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "FoodDiaryEntry cannot be null");
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Validation error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ Entry already exists");
                return await CreateErrorResponse(req, HttpStatusCode.Conflict, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in SaveFoodEntry");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                    "An error occurred while saving the food entry");
            }
        }

        /// <summary>
        /// Azure Function para obtener alimentos consumidos en una fecha específica
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <param name="year">Año (ejemplo: 2025)</param>
        /// <param name="month">Mes (1-12)</param>
        /// <param name="day">Día (1-31)</param>
        /// <returns>JSON con la lista de alimentos consumidos en esa fecha</returns>
        [Function("GetFoodByDate")]
        public async Task<HttpResponseData> GetFoodByDate(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-nutrition/by-date/{twinId}/{year}/{month}/{day}")] HttpRequestData req,
            string twinId,
            int year,
            int month,
            int day)
        {
            _logger.LogInformation("📅 GetFoodByDate function triggered for Date: {Day}/{Month}/{Year}, TwinID: {TwinID}",
                day, month, year, twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "TwinID is required");
                }

                _logger.LogInformation("🔍 Fetching food entries for Twin: {TwinID} on {Date}",
                    twinId, $"{day}/{month}/{year}");

                // Obtener las entradas del diario para esa fecha
                var foodEntries = await _cosmosService.GetFoodDiaryEntriesByDateAndTimeAsync(
                    twinId, year, month, day);

                _logger.LogInformation("✅ Retrieved {Count} food entries for Twin: {TwinID} on {Date}",
                    foodEntries.Count, twinId, $"{day}/{month}/{year}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinID = twinId,
                    date = new { year, month, day },
                    count = foodEntries.Count,
                    entries = foodEntries
                });

                return response;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Validation error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in GetFoodByDate");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving food entries by date");
            }
        }

        /// <summary>
        /// Azure Function para obtener alimentos consumidos en un rango de fechas
        /// Utiliza GET con ruta: /twin-nutrition/by-date-range/{twinId}/{fromYear}/{fromMonth}/{fromDay}/{toYear}/{toMonth}/{toDay}
        /// </summary>
        /// <param name="req">HTTP request</param>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <param name="fromYear">Año de inicio (ejemplo: 2025)</param>
        /// <param name="fromMonth">Mes de inicio (1-12)</param>
        /// <param name="fromDay">Día de inicio (1-31)</param>
        /// <param name="toYear">Año de fin (ejemplo: 2025)</param>
        /// <param name="toMonth">Mes de fin (1-12)</param>
        /// <param name="toDay">Día de fin (1-31)</param>
        /// <returns>JSON con la lista de alimentos consumidos en el rango de fechas</returns>
        [Function("GetFoodByDateRange")]
        public async Task<HttpResponseData> GetFoodByDateRange(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-nutrition/by-date-range/{twinId}/{fromYear}/{fromMonth}/{fromDay}/{toYear}/{toMonth}/{toDay}")] HttpRequestData req,
            string twinId,
            int fromYear,
            int fromMonth,
            int fromDay,
            int toYear,
            int toMonth,
            int toDay)
        {
            _logger.LogInformation("📅 GetFoodByDateRange function triggered for Date Range: {FromDate} to {ToDate}, TwinID: {TwinID}",
                $"{fromDay}/{fromMonth}/{fromYear}", $"{toDay}/{toMonth}/{toYear}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "TwinID is required");
                }

                _logger.LogInformation("🔍 Fetching food entries for Twin: {TwinID} from {FromDate} to {ToDate}",
                    twinId, $"{fromDay}/{fromMonth}/{fromYear}", $"{toDay}/{toMonth}/{toYear}");

                // Obtener las entradas del diario para el rango de fechas
                var foodEntries = await _cosmosService.GetFoodDiaryEntriesByDateRangeAsync(
                    twinId, fromYear, fromMonth, fromDay, toYear, toMonth, toDay);

                _logger.LogInformation("✅ Retrieved {Count} food entries for Twin: {TwinID} from {FromDate} to {ToDate}",
                    foodEntries.Count, twinId, $"{fromDay}/{fromMonth}/{fromYear}", $"{toDay}/{toMonth}/{toYear}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinID = twinId,
                    dateRange = new 
                    { 
                        from = new { year = fromYear, month = fromMonth, day = fromDay },
                        to = new { year = toYear, month = toMonth, day = toDay }
                    },
                    count = foodEntries.Count,
                    entries = foodEntries
                });

                return response;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Validation error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in GetFoodByDateRange");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving food entries by date range");
            }
        }

        /// <summary>
        /// Azure Function para obtener alimentos consumidos en un rango de fechas usando POST con JSON
        /// </summary>
        /// <param name="req">HTTP request con JSON body conteniendo twinId, fromYear, fromMonth, fromDay, toYear, toMonth, toDay</param>
        /// <returns>JSON con la lista de alimentos filtrados por rango de fechas</returns>
        [Function("SearchFoodByDateRange")]
        public async Task<HttpResponseData> SearchFoodByDateRange(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-nutrition/search-by-date-range")] HttpRequestData req)
        {
            _logger.LogInformation("🔍 SearchFoodByDateRange function triggered");

            try
            {
                // Leer el cuerpo de la solicitud
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required");
                }

                // Deserializar la solicitud
                var searchRequest = JsonConvert.DeserializeObject<FoodSearchByDateRangeRequest>(requestBody);

                // Validar parámetros requeridos
                if (searchRequest == null || 
                    string.IsNullOrEmpty(searchRequest.TwinID) ||
                    searchRequest.FromYear <= 0 ||
                    searchRequest.FromMonth <= 0 ||
                    searchRequest.FromDay <= 0 ||
                    searchRequest.ToYear <= 0 ||
                    searchRequest.ToMonth <= 0 ||
                    searchRequest.ToDay <= 0)
                {
                    _logger.LogError("❌ TwinID, FromYear, FromMonth, FromDay, ToYear, ToMonth, and ToDay are required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "TwinID, FromYear, FromMonth, FromDay, ToYear, ToMonth, and ToDay are required");
                }

                _logger.LogInformation("📋 Searching food entries for Twin: {TwinID}, Date Range: {FromDate} to {ToDate}",
                    searchRequest.TwinID, 
                    $"{searchRequest.FromDay}/{searchRequest.FromMonth}/{searchRequest.FromYear}",
                    $"{searchRequest.ToDay}/{searchRequest.ToMonth}/{searchRequest.ToYear}");

                // Llamar al servicio con los parámetros del rango de fechas
                var foodEntries = await _cosmosService.GetFoodDiaryEntriesByDateRangeAsync(
                    searchRequest.TwinID,
                    searchRequest.FromYear,
                    searchRequest.FromMonth,
                    searchRequest.FromDay,
                    searchRequest.ToYear,
                    searchRequest.ToMonth,
                    searchRequest.ToDay);

                _logger.LogInformation("✅ Retrieved {Count} food entries. TwinID: {TwinID}, Date Range: {DateRange}",
                    foodEntries.Count, searchRequest.TwinID, 
                    $"{searchRequest.FromDay}/{searchRequest.FromMonth}/{searchRequest.FromYear} to {searchRequest.ToDay}/{searchRequest.ToMonth}/{searchRequest.ToYear}");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinID = searchRequest.TwinID,
                    dateRange = new 
                    { 
                        from = new { year = searchRequest.FromYear, month = searchRequest.FromMonth, day = searchRequest.FromDay },
                        to = new { year = searchRequest.ToYear, month = searchRequest.ToMonth, day = searchRequest.ToDay }
                    },
                    count = foodEntries.Count,
                    entries = foodEntries
                });

                return response;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Validation error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in SearchFoodByDateRange");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while searching food entries by date range");
            }
        }

        /// <summary>
        /// Azure Function para obtener la lista de todos los nombres de alimentos disponibles
        /// </summary>
        /// <param name="req">HTTP request GET</param>
        /// <returns>JSON con la lista de nombres de alimentos del contenedor TwinAlimentos</returns>
        [Function("GetAllFoodNames")]
        public async Task<HttpResponseData> GetAllFoodNames(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-nutrition/all-food-names")] HttpRequestData req)
        {
            _logger.LogInformation("🍎 GetAllFoodNames function triggered");

            try
            {
                _logger.LogInformation("🔍 Fetching all food names from TwinAlimentos");

                // Obtener la lista de todos los nombres de alimentos
                var foodNames = await _cosmosService.GetAllFoodNamesAsync();

                _logger.LogInformation("✅ Retrieved {Count} food names from TwinAlimentos", foodNames.Count);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    count = foodNames.Count,
                    foodNames = foodNames
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in GetAllFoodNames");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving food names");
            }
        }

        /// <summary>
        /// Azure Function para obtener la lista de todos los alimentos con información nutricional
        /// </summary>
        /// <param name="req">HTTP request GET</param>
        /// <returns>JSON con la lista completa de alimentos del contenedor TwinAlimentos</returns>
        [Function("GetAllFoods")]
        public async Task<HttpResponseData> GetAllFoods(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-nutrition/all-foods")] HttpRequestData req)
        {
            _logger.LogInformation("🍎 GetAllFoods function triggered");

            try
            {
                _logger.LogInformation("🔍 Fetching all foods from TwinAlimentos");

                // Obtener la lista de todos los alimentos con información completa
                var foods = await _cosmosService.GetAllFoodsAsync();

                _logger.LogInformation("✅ Retrieved {Count} foods from TwinAlimentos", foods.Count);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    count = foods.Count,
                    foods = foods
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in GetAllFoods");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving foods");
            }
        }

        /// <summary>
        /// Azure Function para obtener los detalles completos de un alimento específico por su nombre
        /// </summary>
        /// <param name="req">HTTP request GET</param>
        /// <param name="foodName">El nombre del alimento a buscar (único)</param>
        /// <returns>JSON con los detalles completos del alimento incluyendo información nutricional</returns>
        [Function("GetFoodDetails")]
        public async Task<HttpResponseData> GetFoodDetails(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-nutrition/food-details/{foodName}")] HttpRequestData req,
            string foodName)
        {
            _logger.LogInformation("📖 GetFoodDetails function triggered for FoodName: {FoodName}", foodName);

            try
            {
                if (string.IsNullOrEmpty(foodName))
                {
                    _logger.LogError("❌ FoodName is required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "FoodName is required");
                }

                _logger.LogInformation("🔍 Fetching food details for FoodName: {FoodName}", foodName);

                // Obtener los detalles completos del alimento usando foodName (requerido)
                var foodDetails = await _cosmosService.GetFoodStatsByNameRequiredAsync(foodName);

                _logger.LogInformation("✅ Retrieved food details for FoodName: {FoodName}", foodName);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    foodName = foodName,
                    data = foodDetails
                });

                return response;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ Food not found for FoodName: {FoodName}", foodName);
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, 
                    $"Food '{foodName}' not found in the database");
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Validation error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in GetFoodDetails");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving food details");
            }
        }

        /// <summary>
        /// Azure Function para obtener los detalles de un alimento usando POST con JSON
        /// Permite enviar el nombre del alimento en el body de la solicitud
        /// </summary>
        /// <param name="req">HTTP request POST con JSON body conteniendo foodName</param>
        /// <returns>JSON con los detalles completos del alimento incluyendo información nutricional</returns>
        [Function("SearchFoodDetails")]
        public async Task<HttpResponseData> SearchFoodDetails(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-nutrition/search-food-details")] HttpRequestData req)
        {
            _logger.LogInformation("🔍 SearchFoodDetails function triggered");

            try
            {
                // Leer el cuerpo de la solicitud
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required");
                }

                // Deserializar la solicitud
                var searchRequest = JsonConvert.DeserializeObject<FoodDetailsSearchRequest>(requestBody);

                // Validar parámetros requeridos
                if (searchRequest == null || string.IsNullOrEmpty(searchRequest.FoodName))
                {
                    _logger.LogError("❌ FoodName is required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "FoodName is required");
                }

                _logger.LogInformation("📋 Searching food details for FoodName: {FoodName}", searchRequest.FoodName);

                // Obtener los detalles completos del alimento
                var foodDetails = await _cosmosService.GetFoodStatsByNameRequiredAsync(searchRequest.FoodName);

                _logger.LogInformation("✅ Retrieved food details for FoodName: {FoodName}", searchRequest.FoodName);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    foodName = searchRequest.FoodName,
                    data = foodDetails
                });

                return response;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ Food not found");
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, 
                    "Food not found in the database");
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Validation error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in SearchFoodDetails");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving food details");
            }
        }

        /// <summary>
        /// Azure Function para responder preguntas sobre nutrición basadas en los alimentos consumidos en un día específico.
        /// Calcula los totales nutricionales y utiliza OpenAI para proporcionar respuestas personalizadas.
        /// </summary>
        /// <param name="req">HTTP request POST con JSON body</param>
        /// <returns>JSON con la respuesta de OpenAI sobre la pregunta de nutrición</returns>
        [Function("GetNutritionAnswer")]
        public async Task<HttpResponseData> GetNutritionAnswer(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-nutrition/nutrition-answer")] HttpRequestData req)
        {
            _logger.LogInformation("❓ GetNutritionAnswer function triggered");

            try
            {
                // Leer el cuerpo de la solicitud
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required");
                }

                // Deserializar la solicitud
                var nutritionRequest = JsonConvert.DeserializeObject<NutritionQuestionRequest>(requestBody);

                // Validar parámetros requeridos
                if (nutritionRequest == null || 
                    string.IsNullOrEmpty(nutritionRequest.TwinID) ||
                    nutritionRequest.Year <= 0 ||
                    nutritionRequest.Month <= 0 ||
                    nutritionRequest.Day <= 0 ||
                    string.IsNullOrEmpty(nutritionRequest.UserQuestion))
                {
                    _logger.LogError("❌ TwinID, Year, Month, Day, and UserQuestion are required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "TwinID, Year, Month, Day, and UserQuestion are required");
                }

                _logger.LogInformation(
                    "📋 Processing nutrition question for Twin: {TwinID}, Date: {Date}, Question: {Question}",
                    nutritionRequest.TwinID, 
                    $"{nutritionRequest.Day}/{nutritionRequest.Month}/{nutritionRequest.Year}",
                    nutritionRequest.UserQuestion);

                // Llamar al agente para obtener la respuesta
                var result = await _foodDieteryAgent.GetNutritionAnswerAsync(
                    nutritionRequest.TwinID,
                    nutritionRequest.Year,
                    nutritionRequest.Month,
                    nutritionRequest.Day,
                    nutritionRequest.UserQuestion,
                    nutritionRequest.SerializedThreadJson);

                if (!result.Success)
                {
                    _logger.LogError("❌ Error processing nutrition question: {ErrorMessage}", result.ErrorMessage);
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, result.ErrorMessage);
                }

                _logger.LogInformation("✅ Nutrition answer generated successfully for Twin: {TwinID}", result.TwinId);

                // Crear respuesta exitosa
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinId = result.TwinId,
                    queryDate = result.QueryDate,
                    userQuestion = result.UserQuestion,
                    foodEntriesCount = result.FoodEntriesCount,
                    nutritionTotals = result.NutritionTotals,
                    aiResponse = result.AIResponse,
                    serializedThreadJson = result.SerializedThreadJson,
                    processedAt = result.ProcessedTimestamp
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in GetNutritionAnswer");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while processing the nutrition question");
            }
        }

        /// <summary>
        /// Helper method para crear respuestas de error
        /// </summary>
        private async Task<HttpResponseData> CreateErrorResponse(
            HttpRequestData req, 
            HttpStatusCode statusCode, 
            string message)
        {
            var response = req.CreateResponse(statusCode);
            await response.WriteAsJsonAsync(new
            {
                success = false,
                error = message
            });
            return response;
        }
    }

    /// <summary>
    /// Modelo para la solicitud de registro de alimento
    /// </summary>
    public class FoodRegistrationRequest
    {
        [JsonProperty("foodDescription")]
        public string FoodDescription { get; set; }

        [JsonProperty("twinId")]
        public string TwinID { get; set; }
    }

    /// <summary>
    /// Modelo para la solicitud de búsqueda de alimento por fecha y hora
    /// </summary>
    public class FoodSearchByDateAndTimeRequest
    {
        [JsonProperty("twinId")]
        public string TwinID { get; set; }

        [JsonProperty("year")]
        public int Year { get; set; }

        [JsonProperty("month")]
        public int Month { get; set; }

        [JsonProperty("day")]
        public int Day { get; set; }

        [JsonProperty("time")]
        public string Time { get; set; }
    }

    /// <summary>
    /// Modelo para la solicitud de búsqueda de alimento por rango de fechas
    /// </summary>
    public class FoodSearchByDateRangeRequest
    {
        [JsonProperty("twinId")]
        public string TwinID { get; set; }

        [JsonProperty("fromYear")]
        public int FromYear { get; set; }

        [JsonProperty("fromMonth")]
        public int FromMonth { get; set; }

        [JsonProperty("fromDay")]
        public int FromDay { get; set; }

        [JsonProperty("toYear")]
        public int ToYear { get; set; }

        [JsonProperty("toMonth")]
        public int ToMonth { get; set; }

        [JsonProperty("toDay")]
        public int ToDay { get; set; }
    }

    /// <summary>
    /// Modelo para la solicitud de búsqueda de detalles de alimento
    /// </summary>
    public class FoodDetailsSearchRequest
    {
        [JsonProperty("foodName")]
        public string FoodName { get; set; }
    }

    /// <summary>
    /// Modelo para la solicitud de pregunta sobre nutrición
    /// </summary>
    public class NutritionQuestionRequest
    {
        [JsonProperty("twinId")]
        public string TwinID { get; set; }

        [JsonProperty("year")]
        public int Year { get; set; }

        [JsonProperty("month")]
        public int Month { get; set; }

        [JsonProperty("day")]
        public int Day { get; set; }

        [JsonProperty("userQuestion")]
        public string UserQuestion { get; set; }

        [JsonProperty("serializedThreadJson")]
        public string SerializedThreadJson { get; set; }
    }
}

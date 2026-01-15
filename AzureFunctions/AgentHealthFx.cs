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
using TwinAgentsNetwork.Services;
using TwinAgentsNetwork.Agents;
using Newtonsoft.Json;
using TwinAgentsLibrary.Models;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Function para gestionar métricas de salud
    /// Utiliza AgentHealthCosmosDB para guardar, recuperar y gestionar métricas de salud en Cosmos DB
    /// También utiliza AgentTwinHealth para generar recomendaciones personalizadas basadas en OpenAI
    /// </summary>
    public class AgentHealthFx
    {
        private readonly ILogger<AgentHealthFx> _logger;
        private readonly AgentHealthCosmosDB _healthService;
        private readonly AgentTwinHealth _healthAgent;

        public AgentHealthFx(
            ILogger<AgentHealthFx> logger,
            AgentHealthCosmosDB healthService,
            AgentTwinHealth healthAgent)
        {
            _logger = logger;
            _healthService = healthService;
            _healthAgent = healthAgent;
        }

        /// <summary>
        /// Azure Function para guardar nuevas métricas de salud
        /// Acepta HealthMetrics en JSON y guarda en Cosmos DB
        /// </summary>
        /// <param name="req">HTTP request POST con HealthMetrics en JSON</param>
        /// <returns>JSON con la métrica guardada</returns>
        [Function("SaveHealthMetrics")]
        public async Task<HttpResponseData> SaveHealthMetrics(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-health/save")] HttpRequestData req)
        {
            _logger.LogInformation("💾 SaveHealthMetrics function triggered");

            try
            {
                // Leer el cuerpo de la solicitud
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required");
                }

                // Deserializar la solicitud como HealthMetrics
                var healthMetrics = JsonConvert.DeserializeObject<HealthMetrics>(requestBody);

                // Validar parámetros requeridos
                if (healthMetrics == null || string.IsNullOrEmpty(healthMetrics.TwinID))
                {
                    _logger.LogError("❌ HealthMetrics and TwinID are required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "HealthMetrics and TwinID are required");
                }

                _logger.LogInformation("📋 Saving health metrics for Twin: {TwinID}", healthMetrics.TwinID);

                // Guardar en Cosmos DB
                var savedMetrics = await _healthService.SaveHealthMetricsAsync(healthMetrics);

                _logger.LogInformation("✅ Health metrics saved successfully. ID: {Id}", savedMetrics.Id);

                // Crear respuesta exitosa
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Health metrics saved successfully",
                    data = savedMetrics
                });

                return response;
            }
            catch (ArgumentNullException ex)
            {
                _logger.LogError(ex, "❌ Null argument error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "HealthMetrics cannot be null");
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Validation error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ Metrics already exist");
                return await CreateErrorResponse(req, HttpStatusCode.Conflict, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in SaveHealthMetrics");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                    "An error occurred while saving health metrics");
            }
        }

        /// <summary>
        /// Azure Function para obtener la métrica de salud más reciente de un usuario
        /// </summary>
        /// <param name="req">HTTP request GET</param>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <returns>JSON con la métrica más reciente</returns>
        [Function("GetLatestHealthMetrics")]
        public async Task<HttpResponseData> GetLatestHealthMetrics(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-health/latest/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔍 GetLatestHealthMetrics function triggered for Twin: {TwinID}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ TwinID is required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "TwinID is required");
                }

                _logger.LogInformation("🔍 Fetching latest health metrics for Twin: {TwinID}", twinId);

                // Obtener la métrica más reciente
                var latestMetrics = await _healthService.GetLatestHealthMetricsAsync(twinId);

                if (latestMetrics == null)
                {
                    _logger.LogWarning("⚠️ No health metrics found for Twin: {TwinID}", twinId);
                    return await CreateErrorResponse(req, HttpStatusCode.NotFound, 
                        "No health metrics found for this Twin");
                }

                _logger.LogInformation("✅ Retrieved latest health metrics for Twin: {TwinID}", twinId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinID = twinId,
                    data = latestMetrics
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
                _logger.LogError(ex, "❌ Unexpected error in GetLatestHealthMetrics");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving health metrics");
            }
        }

        /// <summary>
        /// Azure Function para obtener una métrica específica de salud
        /// </summary>
        /// <param name="req">HTTP request GET</param>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <param name="metricsId">El ID de la métrica</param>
        /// <returns>JSON con la métrica solicitada</returns>
        [Function("GetHealthMetrics")]
        public async Task<HttpResponseData> GetHealthMetrics(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-health/metrics/{twinId}/{metricsId}")] HttpRequestData req,
            string twinId,
            string metricsId)
        {
            _logger.LogInformation("📖 GetHealthMetrics function triggered. TwinID: {TwinID}, MetricsID: {MetricsID}", 
                twinId, metricsId);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(metricsId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "TwinID and MetricsID are required");
                }

                var metrics = await _healthService.GetHealthMetricsAsync(metricsId, twinId);

                if (metrics == null)
                {
                    _logger.LogWarning("⚠️ Health metrics not found. MetricsID: {MetricsID}", metricsId);
                    return await CreateErrorResponse(req, HttpStatusCode.NotFound, 
                        "Health metrics not found");
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    data = metrics
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in GetHealthMetrics");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                    "An error occurred while retrieving health metrics");
            }
        }

        /// <summary>
        /// Azure Function para obtener todas las métricas de un usuario
        /// </summary>
        /// <param name="req">HTTP request GET</param>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <returns>JSON con lista de todas las métricas</returns>
        [Function("GetHealthHistory")]
        public async Task<HttpResponseData> GetHealthHistory(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-health/history/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("📊 GetHealthHistory function triggered for Twin: {TwinID}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ TwinID is required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "TwinID is required");
                }

                _logger.LogInformation("🔍 Fetching health history for Twin: {TwinID}", twinId);

                // Obtener todas las métricas del usuario
                var allMetrics = await _healthService.GetHealthMetricsByTwinAsync(twinId);

                _logger.LogInformation("✅ Retrieved {Count} health metrics for Twin: {TwinID}", 
                    allMetrics.Count, twinId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinID = twinId,
                    count = allMetrics.Count,
                    metrics = allMetrics
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
                _logger.LogError(ex, "❌ Unexpected error in GetHealthHistory");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving health history");
            }
        }

        /// <summary>
        /// Azure Function para obtener métricas de salud en un rango de fechas
        /// </summary>
        /// <param name="req">HTTP request GET</param>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <param name="fromYear">Año de inicio</param>
        /// <param name="fromMonth">Mes de inicio</param>
        /// <param name="fromDay">Día de inicio</param>
        /// <param name="toYear">Año de fin</param>
        /// <param name="toMonth">Mes de fin</param>
        /// <param name="toDay">Día de fin</param>
        /// <returns>JSON con métricas en el rango de fechas</returns>
        [Function("GetHealthByDateRange")]
        public async Task<HttpResponseData> GetHealthByDateRange(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-health/by-date-range/{twinId}/{fromYear}/{fromMonth}/{fromDay}/{toYear}/{toMonth}/{toDay}")] HttpRequestData req,
            string twinId,
            int fromYear,
            int fromMonth,
            int fromDay,
            int toYear,
            int toMonth,
            int toDay)
        {
            _logger.LogInformation("📅 GetHealthByDateRange function triggered for Date Range: {FromDate} to {ToDate}, TwinID: {TwinID}",
                $"{fromDay}/{fromMonth}/{fromYear}", $"{toDay}/{toMonth}/{toYear}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "TwinID is required");
                }

                _logger.LogInformation("🔍 Fetching health metrics for Twin: {TwinID} from {FromDate} to {ToDate}",
                    twinId, $"{fromDay}/{fromMonth}/{fromYear}", $"{toDay}/{toMonth}/{toYear}");

                // Convertir a DateTime
                var fromDate = new DateTime(fromYear, fromMonth, fromDay);
                var toDate = new DateTime(toYear, toMonth, toDay);

                // Obtener métricas en el rango de fechas
                var metrics = await _healthService.GetHealthMetricsByDateRangeAsync(twinId, fromDate, toDate);

                _logger.LogInformation("✅ Retrieved {Count} health metrics for Twin: {TwinID} from {FromDate} to {ToDate}",
                    metrics.Count, twinId, $"{fromDay}/{fromMonth}/{fromYear}", $"{toDay}/{toMonth}/{toYear}");

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
                    count = metrics.Count,
                    metrics = metrics
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
                _logger.LogError(ex, "❌ Unexpected error in GetHealthByDateRange");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving health metrics by date range");
            }
        }

        /// <summary>
        /// Azure Function para actualizar métricas de salud existentes
        /// </summary>
        /// <param name="req">HTTP request PUT con HealthMetrics actualizado en JSON</param>
        /// <returns>JSON con la métrica actualizada</returns>
        [Function("UpdateHealthMetrics")]
        public async Task<HttpResponseData> UpdateHealthMetrics(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twin-health/update")] HttpRequestData req)
        {
            _logger.LogInformation("✏️ UpdateHealthMetrics function triggered");

            try
            {
                // Leer el cuerpo de la solicitud
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required");
                }

                // Deserializar la solicitud como HealthMetrics
                var healthMetrics = JsonConvert.DeserializeObject<HealthMetrics>(requestBody);

                // Validar parámetros requeridos
                if (healthMetrics == null || string.IsNullOrEmpty(healthMetrics.Id) || string.IsNullOrEmpty(healthMetrics.TwinID))
                {
                    _logger.LogError("❌ HealthMetrics, Id, and TwinID are required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "HealthMetrics with Id and TwinID are required");
                }

                _logger.LogInformation("📋 Updating health metrics for Twin: {TwinID}, ID: {Id}", 
                    healthMetrics.TwinID, healthMetrics.Id);

                // Actualizar en Cosmos DB
                var updatedMetrics = await _healthService.UpdateHealthMetricsAsync(healthMetrics);

                _logger.LogInformation("✅ Health metrics updated successfully. ID: {Id}", updatedMetrics.Id);

                // Crear respuesta exitosa
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Health metrics updated successfully",
                    data = updatedMetrics
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
                _logger.LogError(ex, "❌ Unexpected error in UpdateHealthMetrics");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                    "An error occurred while updating health metrics");
            }
        }

        /// <summary>
        /// Azure Function para eliminar una métrica de salud
        /// </summary>
        /// <param name="req">HTTP request DELETE</param>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <param name="metricsId">El ID de la métrica a eliminar</param>
        /// <returns>JSON con resultado de la operación</returns>
        [Function("DeleteHealthMetrics")]
        public async Task<HttpResponseData> DeleteHealthMetrics(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twin-health/metrics/{twinId}/{metricsId}")] HttpRequestData req,
            string twinId,
            string metricsId)
        {
            _logger.LogInformation("🗑️ DeleteHealthMetrics function triggered. TwinID: {TwinID}, MetricsID: {MetricsID}", 
                twinId, metricsId);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(metricsId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "TwinID and MetricsID are required");
                }

                await _healthService.DeleteHealthMetricsAsync(metricsId, twinId);

                _logger.LogInformation("✅ Health metrics deleted successfully. MetricsID: {MetricsID}", metricsId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Health metrics deleted successfully"
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in DeleteHealthMetrics");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                    "An error occurred while deleting health metrics");
            }
        }

        /// <summary>
        /// Azure Function para obtener métricas con IMC anormal
        /// </summary>
        /// <param name="req">HTTP request GET</param>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <returns>JSON con métricas que tienen IMC anormal</returns>
        [Function("GetAbnormalIMC")]
        public async Task<HttpResponseData> GetAbnormalIMC(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-health/abnormal-imc/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("⚠️ GetAbnormalIMC function triggered for Twin: {TwinID}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "TwinID is required");
                }

                _logger.LogInformation("🔍 Fetching abnormal IMC metrics for Twin: {TwinID}", twinId);

                // Obtener métricas con IMC anormal
                var abnormalIMC = await _healthService.GetHealthMetricsWithAbnormalIMCAsync(twinId);

                _logger.LogInformation("✅ Retrieved {Count} abnormal IMC metrics for Twin: {TwinID}", 
                    abnormalIMC.Count, twinId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinID = twinId,
                    count = abnormalIMC.Count,
                    metrics = abnormalIMC
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
                _logger.LogError(ex, "❌ Unexpected error in GetAbnormalIMC");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving abnormal IMC metrics");
            }
        }

        /// <summary>
        /// Azure Function para obtener métricas con valores de sangre anormales
        /// </summary>
        /// <param name="req">HTTP request GET</param>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <returns>JSON con métricas que tienen valores de sangre anormales</returns>
        [Function("GetAbnormalBloodValues")]
        public async Task<HttpResponseData> GetAbnormalBloodValues(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-health/abnormal-blood/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("⚠️ GetAbnormalBloodValues function triggered for Twin: {TwinID}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "TwinID is required");
                }

                _logger.LogInformation("🔍 Fetching abnormal blood value metrics for Twin: {TwinID}", twinId);

                // Obtener métricas con valores de sangre anormales
                var abnormalBlood = await _healthService.GetHealthMetricsWithAbnormalBloodValuesAsync(twinId);

                _logger.LogInformation("✅ Retrieved {Count} abnormal blood value metrics for Twin: {TwinID}", 
                    abnormalBlood.Count, twinId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinID = twinId,
                    count = abnormalBlood.Count,
                    metrics = abnormalBlood
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
                _logger.LogError(ex, "❌ Unexpected error in GetAbnormalBloodValues");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving abnormal blood value metrics");
            }
        }

        /// <summary>
        /// Azure Function para generar recomendaciones de salud personalizadas
        /// Utiliza OpenAI para analizar métricas de salud y crear un plan de recomendaciones
        /// </summary>
        /// <param name="req">HTTP request POST con metricsId y twinId</param>
        /// <returns>JSON con recomendaciones de salud personalizadas</returns>
        [Function("GetHealthRecommendations")]
        public async Task<HttpResponseData> GetHealthRecommendations(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-health/recommendations")] HttpRequestData req)
        {
            _logger.LogInformation("🤖 GetHealthRecommendations function triggered");

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
                var recommendationRequest = JsonConvert.DeserializeObject<HealthRecommendationRequest>(requestBody);

                // Validar parámetros requeridos
                if (recommendationRequest == null || 
                    string.IsNullOrEmpty(recommendationRequest.TwinID) ||
                    string.IsNullOrEmpty(recommendationRequest.MetricsId))
                {
                    _logger.LogError("❌ TwinID and MetricsId are required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "TwinID and MetricsId are required");
                }

                _logger.LogInformation("📋 Processing health recommendations for Twin: {TwinID}, MetricsId: {MetricsId}", 
                    recommendationRequest.TwinID, recommendationRequest.MetricsId);

                // Llamar al agente para generar recomendaciones
                var healthRecommendations = await _healthAgent.GetHealthRecommendationsAsync(
                    recommendationRequest.MetricsId, 
                    recommendationRequest.TwinID);

                if (healthRecommendations == null)
                {
                    _logger.LogError("❌ Failed to generate health recommendations");
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                        "Failed to generate health recommendations");
                }

                _logger.LogInformation("✅ Health recommendations generated successfully. TwinID: {TwinID}", 
                    recommendationRequest.TwinID);

                // Crear respuesta exitosa
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Health recommendations generated successfully",
                    data = healthRecommendations
                });

                return response;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Validation error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ Invalid operation error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in GetHealthRecommendations");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                    "An error occurred while generating health recommendations");
            }
        }

        /// <summary>
        /// Azure Function para generar recomendaciones basadas en las métricas más recientes
        /// Obtiene automáticamente la última métrica del usuario y genera recomendaciones
        /// </summary>
        /// <param name="req">HTTP request GET</param>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <returns>JSON con recomendaciones basadas en las métricas más recientes</returns>
        [Function("GetLatestHealthRecommendations")]
        public async Task<HttpResponseData> GetLatestHealthRecommendations(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-health/recommendations/latest/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🤖 GetLatestHealthRecommendations function triggered for Twin: {TwinID}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ TwinID is required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "TwinID is required");
                }

                _logger.LogInformation("📋 Processing latest health recommendations for Twin: {TwinID}", twinId);

                // Llamar al agente para obtener recomendaciones basadas en las métricas más recientes
                var healthRecommendations = await _healthAgent.GetLatestHealthRecommendationsAsync(twinId);

                if (healthRecommendations == null)
                {
                    _logger.LogError("❌ Failed to generate health recommendations for Twin: {TwinID}", twinId);
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                        "Failed to generate health recommendations");
                }

                _logger.LogInformation("✅ Health recommendations generated successfully. TwinID: {TwinID}", twinId);

                // Crear respuesta exitosa
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Health recommendations generated successfully",
                    data = healthRecommendations
                });

                return response;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Validation error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ Invalid operation error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in GetLatestHealthRecommendations");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                    "An error occurred while generating health recommendations");
            }
        }

        /// <summary>
        /// Azure Function para guardar las recomendaciones de salud generadas
        /// Almacena un objeto Health en el contenedor TwinHealth de Cosmos DB
        /// Si ya existe recomendaciones para el usuario, las elimina primero (replace-on-save)
        /// </summary>
        /// <param name="req">HTTP request POST con Health en JSON</param>
        /// <returns>JSON con las recomendaciones guardadas</returns>
        [Function("SaveHealthRecommendations")]
        public async Task<HttpResponseData> SaveHealthRecommendations(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-health/recommendations/save")] HttpRequestData req)
        {
            _logger.LogInformation("💾 SaveHealthRecommendations function triggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required");
                }

                var health = JsonConvert.DeserializeObject<Health>(requestBody);

                if (health == null || string.IsNullOrEmpty(health.TwinID))
                {
                    _logger.LogError("❌ Health and TwinID are required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Health and TwinID are required");
                }

                string twinId = health.TwinID;
                _logger.LogInformation("📋 Saving health recommendations for Twin: {TwinID}", twinId);

                // 1. Buscar si ya existen recomendaciones para este usuario
                _logger.LogInformation("🔍 Checking for existing recommendations for Twin: {TwinID}", twinId);
                var existingRecommendations = await _healthService.GetHealthRecommendationsByTwinAsync(twinId);

                // 2. Si existen, eliminarlas
                if (existingRecommendations != null && existingRecommendations.Count > 0)
                {
                    _logger.LogInformation("🗑️ Found {Count} existing recommendations for Twin: {TwinID}. Deleting...", existingRecommendations.Count, twinId);
                    await _healthService.DeleteHealthRecommendationsByTwinAsync(twinId);
                    _logger.LogInformation("✅ Existing recommendations deleted for Twin: {TwinID}", twinId);
                }
                else
                {
                    _logger.LogInformation("ℹ️ No existing recommendations found for Twin: {TwinID}", twinId);
                }

                // 3. Guardar las nuevas recomendaciones
                var savedHealth = await _healthService.SaveHealthRecommendationsAsync(health);
                _logger.LogInformation("✅ Health recommendations saved successfully. ID: {Id}", savedHealth.Id);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Health recommendations saved successfully",
                    data = savedHealth
                });

                return response;
            }
            catch (ArgumentNullException ex)
            {
                _logger.LogError(ex, "❌ Null argument error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Health cannot be null");
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "❌ Validation error");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "❌ Error in save operation");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in SaveHealthRecommendations");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "An error occurred while saving health recommendations");
            }
        }

        /// <summary>
        /// Azure Function para obtener el registro Health de un usuario
        /// Retorna el único registro Health que existe por TwinID del contenedor TwinHealth
        /// </summary>
        /// <param name="req">HTTP request GET</param>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <returns>JSON con el registro Health del usuario</returns>
        [Function("GetHealthByTwinId")]
        public async Task<HttpResponseData> GetHealthByTwinId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-health/health/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🏥 GetHealthByTwinId function triggered for Twin: {TwinID}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ TwinID is required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "TwinID is required");
                }

                _logger.LogInformation("🔍 Fetching Health record for Twin: {TwinID}", twinId);

                // Obtener el único registro Health del usuario
                var health = await _healthService.GetHealthByTwinIdAsync(twinId);

                if (health == null)
                {
                    _logger.LogWarning("⚠️ No Health record found for Twin: {TwinID}", twinId);
                    return await CreateErrorResponse(req, HttpStatusCode.NotFound, 
                        "No Health record found for this Twin");
                }

                _logger.LogInformation("✅ Retrieved Health record for Twin: {TwinID}", twinId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinID = twinId,
                    data = health
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
                _logger.LogError(ex, "❌ Unexpected error in GetHealthByTwinId");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving Health record");
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
    /// Modelo para la solicitud de recomendaciones de salud
    /// </summary>
    public class HealthRecommendationRequest
    {
        [JsonProperty("twinId")]
        public string TwinID { get; set; }

        [JsonProperty("metricsId")]
        public string MetricsId { get; set; }
    }
}

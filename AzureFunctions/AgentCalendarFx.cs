using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Functions para gestionar citas de calendario
    /// Utiliza AgentCalendarCosmosDB para operaciones CRUD
    /// </summary>
    public class AgentCalendarFx
    {
        private readonly ILogger<AgentCalendarFx> _logger;
        private readonly IConfiguration _configuration;

        public AgentCalendarFx(
            ILogger<AgentCalendarFx> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        #region Save Appointment

        /// <summary>
        /// OPTIONS handler for SaveAppointment endpoint (CORS preflight)
        /// </summary>
        [Function("SaveAppointmentOptions")]
        public async Task<HttpResponseData> HandleSaveAppointmentOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "calendar/save")] HttpRequestData req)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for calendar/save");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Azure Function para guardar una nueva cita de calendario
        /// POST /api/calendar/save
        /// Body: { "twinId": "...", "microsoftOID": "...", "appointment": {...} }
        /// </summary>
        [Function("SaveAppointment")]
        public async Task<HttpResponseData> SaveAppointment(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "calendar/save")] HttpRequestData req)
        {
            _logger.LogInformation("📅 SaveAppointment function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "Request body is required" });
                    return badResponse;
                }

                var request = JsonConvert.DeserializeObject<SaveAppointmentRequest>(requestBody);

                if (request == null || request.Appointment == null || string.IsNullOrEmpty(request.TwinId))
                {
                    _logger.LogError("❌ TwinId and Appointment are required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "TwinId and Appointment are required" });
                    return badResponse;
                }

                _logger.LogInformation("📅 Saving appointment: {Title} on {Date}", 
                    request.Appointment.Titulo, request.Appointment.Fecha.ToString("yyyy-MM-dd"));

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var calendarLogger = loggerFactory.CreateLogger<AgentCalendarCosmosDB>();
                var calendarService = new AgentCalendarCosmosDB(calendarLogger, _configuration);
                var result = await calendarService.SaveAppointmentAsync(
                    request.Appointment, 
                    request.TwinId, 
                    request.MicrosoftOID ?? "");

                if (!result.Success)
                {
                    _logger.LogError("❌ Error saving appointment: {ErrorMessage}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = result.ErrorMessage
                    });
                    return errorResponse;
                }

                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogInformation("✅ Appointment saved successfully. Document ID: {DocumentId}", result.DocumentId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Appointment saved successfully",
                    documentId = result.DocumentId,
                    ruConsumed = result.RUConsumed,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Unexpected error in SaveAppointment");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while saving the appointment",
                    details = ex.Message,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                });
                return errorResponse;
            }
        }

        #endregion

        #region Get Appointment by ID

        /// <summary>
        /// OPTIONS handler for GetAppointmentById endpoint (CORS preflight)
        /// </summary>
        [Function("GetAppointmentByIdOptions")]
        public async Task<HttpResponseData> HandleGetAppointmentByIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "calendar/get/{twinId}/{appointmentId}")] HttpRequestData req,
            string twinId,
            string appointmentId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for calendar/get/{TwinId}/{AppointmentId}", twinId, appointmentId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Azure Function para obtener una cita por ID
        /// GET /api/calendar/get/{twinId}/{appointmentId}
        /// </summary>
        [Function("GetAppointmentById")]
        public async Task<HttpResponseData> GetAppointmentById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "calendar/get/{twinId}/{appointmentId}")] HttpRequestData req,
            string twinId,
            string appointmentId)
        {
            _logger.LogInformation("📅 GetAppointmentById function triggered. TwinId: {TwinId}, AppointmentId: {AppointmentId}",
                twinId, appointmentId);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(appointmentId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "TwinId and AppointmentId are required" });
                    return badResponse;
                }

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var calendarLogger = loggerFactory.CreateLogger<AgentCalendarCosmosDB>();
                var calendarService = new AgentCalendarCosmosDB(calendarLogger, _configuration);
                var result = await calendarService.GetAppointmentByIdAsync(appointmentId, twinId);

                if (!result.Success)
                {
                    _logger.LogWarning("⚠️ Appointment not found: {AppointmentId}", appointmentId);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return notFoundResponse;
                }

                _logger.LogInformation("✅ Appointment retrieved successfully");

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    appointment = result.Appointment,
                    ruConsumed = result.RUConsumed,
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in GetAppointmentById");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while retrieving the appointment"
                });
                return errorResponse;
            }
        }

        #endregion

        #region Get Appointments by TwinId

        /// <summary>
        /// OPTIONS handler for GetAppointmentsByTwinId endpoint (CORS preflight)
        /// </summary>
        [Function("GetAppointmentsByTwinIdOptions")]
        public async Task<HttpResponseData> HandleGetAppointmentsByTwinIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "calendar/twin/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for calendar/twin/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Azure Function para obtener todas las citas de un Twin
        /// GET /api/calendar/twin/{twinId}
        /// </summary>
        [Function("GetAppointmentsByTwinId")]
        public async Task<HttpResponseData> GetAppointmentsByTwinId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "calendar/twin/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("📅 GetAppointmentsByTwinId function triggered for TwinId: {TwinId}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "TwinId is required" });
                    return badResponse;
                }

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var calendarLogger = loggerFactory.CreateLogger<AgentCalendarCosmosDB>();
                var calendarService = new AgentCalendarCosmosDB(calendarLogger, _configuration);
                var result = await calendarService.GetAppointmentsByTwinIdAsync(twinId);

                if (!result.Success)
                {
                    _logger.LogError("❌ Error retrieving appointments: {ErrorMessage}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                _logger.LogInformation("✅ Retrieved {Count} appointments for TwinId: {TwinId}", 
                    result.AppointmentCount, twinId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinId = twinId,
                    count = result.AppointmentCount,
                    appointments = result.Appointments,
                    ruConsumed = result.RUConsumed,
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in GetAppointmentsByTwinId");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while retrieving appointments"
                });
                return errorResponse;
            }
        }

        #endregion

        #region Get Appointments by Date Range

        /// <summary>
        /// OPTIONS handler for GetAppointmentsByDateRange endpoint (CORS preflight)
        /// </summary>
        [Function("GetAppointmentsByDateRangeOptions")]
        public async Task<HttpResponseData> HandleGetAppointmentsByDateRangeOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "calendar/date-range/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for calendar/date-range/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Azure Function para obtener citas por rango de fechas
        /// GET /api/calendar/date-range/{twinId}?startDate=2025-12-01&endDate=2025-12-31
        /// </summary>
        [Function("GetAppointmentsByDateRange")]
        public async Task<HttpResponseData> GetAppointmentsByDateRange(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "calendar/date-range/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("📅 GetAppointmentsByDateRange function triggered for TwinId: {TwinId}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "TwinId is required" });
                    return badResponse;
                }

                // Parse query parameters
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var startDateStr = query["startDate"];
                var endDateStr = query["endDate"];

                if (string.IsNullOrEmpty(startDateStr) || string.IsNullOrEmpty(endDateStr))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "startDate and endDate query parameters are required (format: YYYY-MM-DD)"
                    });
                    return badResponse;
                }

                if (!DateTime.TryParse(startDateStr, out DateTime startDate) ||
                    !DateTime.TryParse(endDateStr, out DateTime endDate))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "Invalid date format. Use YYYY-MM-DD format"
                    });
                    return badResponse;
                }

                _logger.LogInformation("📅 Date range: {StartDate} to {EndDate}", 
                    startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var calendarLogger = loggerFactory.CreateLogger<AgentCalendarCosmosDB>();
                var calendarService = new AgentCalendarCosmosDB(calendarLogger, _configuration);
                var result = await calendarService.GetAppointmentsByDateRangeAsync(twinId, startDate, endDate);

                if (!result.Success)
                {
                    _logger.LogError("❌ Error retrieving appointments: {ErrorMessage}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                _logger.LogInformation("✅ Retrieved {Count} appointments in date range", result.AppointmentCount);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinId = twinId,
                    startDate = startDate.ToString("yyyy-MM-dd"),
                    endDate = endDate.ToString("yyyy-MM-dd"),
                    count = result.AppointmentCount,
                    appointments = result.Appointments,
                    ruConsumed = result.RUConsumed,
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error in GetAppointmentsByDateRange");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while retrieving appointments by date range"
                });
                return errorResponse;
            }
        }

        #endregion

        #region Update Appointment

        /// <summary>
        /// OPTIONS handler for UpdateAppointment endpoint (CORS preflight)
        /// </summary>
        [Function("UpdateAppointmentOptions")]
        public async Task<HttpResponseData> HandleUpdateAppointmentOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "calendar/update")] HttpRequestData req)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for calendar/update");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Azure Function para actualizar una cita existente
        /// PUT /api/calendar/update
        /// Body: { "twinId": "...", "appointment": {...} }
        /// </summary>
        [Function("UpdateAppointment")]
        public async Task<HttpResponseData> UpdateAppointment(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "calendar/update")] HttpRequestData req)
        {
            _logger.LogInformation("📅 UpdateAppointment function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new { success = false, error = "Request body is required" });
                    return badResponse;
                }

                var request = JsonConvert.DeserializeObject<UpdateAppointmentRequest>(requestBody);

                if (request == null || 
                    request.Appointment == null || 
                    string.IsNullOrEmpty(request.TwinId) ||
                    string.IsNullOrEmpty(request.Appointment.Id))
                {
                    _logger.LogError("❌ TwinId and Appointment with Id are required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "TwinId and Appointment with Id are required"
                    });
                    return badResponse;
                }

                _logger.LogInformation("📅 Updating appointment: {AppointmentId}", request.Appointment.Id);

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var calendarLogger = loggerFactory.CreateLogger<AgentCalendarCosmosDB>();
                var calendarService = new AgentCalendarCosmosDB(calendarLogger, _configuration);
                var result = await calendarService.UpdateAppointmentAsync(request.Appointment, request.TwinId);

                if (!result.Success)
                {
                    _logger.LogError("❌ Error updating appointment: {ErrorMessage}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogInformation("✅ Appointment updated successfully");

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Appointment updated successfully",
                    documentId = result.DocumentId,
                    ruConsumed = result.RUConsumed,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Unexpected error in UpdateAppointment");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while updating the appointment",
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                });
                return errorResponse;
            }
        }

        #endregion

        #region Delete Appointment

        /// <summary>
        /// OPTIONS handler for DeleteAppointment endpoint (CORS preflight)
        /// </summary>
        [Function("DeleteAppointmentOptions")]
        public async Task<HttpResponseData> HandleDeleteAppointmentOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "calendar/delete/{twinId}/{appointmentId}")] HttpRequestData req,
            string twinId,
            string appointmentId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for calendar/delete/{TwinId}/{AppointmentId}", twinId, appointmentId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Azure Function para eliminar una cita
        /// DELETE /api/calendar/delete/{twinId}/{appointmentId}
        /// </summary>
        [Function("DeleteAppointment")]
        public async Task<HttpResponseData> DeleteAppointment(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "calendar/delete/{twinId}/{appointmentId}")] HttpRequestData req,
            string twinId,
            string appointmentId)
        {
            _logger.LogInformation("🗑️ DeleteAppointment function triggered. TwinId: {TwinId}, AppointmentId: {AppointmentId}",
                twinId, appointmentId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(appointmentId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "TwinId and AppointmentId are required"
                    });
                    return badResponse;
                }

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var calendarLogger = loggerFactory.CreateLogger<AgentCalendarCosmosDB>();
                var calendarService = new AgentCalendarCosmosDB(calendarLogger, _configuration);
                var result = await calendarService.DeleteAppointmentAsync(appointmentId, twinId);

                if (!result.Success)
                {
                    _logger.LogError("❌ Error deleting appointment: {ErrorMessage}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = result.ErrorMessage });
                    return errorResponse;
                }

                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogInformation("✅ Appointment deleted successfully");

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Appointment deleted successfully",
                    ruConsumed = result.RUConsumed,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    timestamp = DateTime.UtcNow
                });

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Unexpected error in DeleteAppointment");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while deleting the appointment",
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                });
                return errorResponse;
            }
        }

        #endregion

        #region CORS Helper

        /// <summary>
        /// Adds CORS headers to the response for cross-origin requests
        /// </summary>
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

    #region Request Models

    /// <summary>
    /// Modelo para la solicitud de guardar cita
    /// </summary>
    public class SaveAppointmentRequest
    {
        [JsonProperty("twinId")]
        public string TwinId { get; set; } = string.Empty;

        [JsonProperty("microsoftOID")]
        public string? MicrosoftOID { get; set; }

        [JsonProperty("appointment")]
        public CalendarAppointmentData Appointment { get; set; } = new();
    }

    /// <summary>
    /// Modelo para la solicitud de actualizar cita
    /// </summary>
    public class UpdateAppointmentRequest
    {
        [JsonProperty("twinId")]
        public string TwinId { get; set; } = string.Empty;

        [JsonProperty("appointment")]
        public CalendarAppointmentData Appointment { get; set; } = new();
    }

    #endregion
}

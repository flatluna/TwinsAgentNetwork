using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace TwinAgentsNetwork.Services
{
    /// <summary>
    /// Servicio para gestionar citas de calendario en Azure Cosmos DB
    /// Database: twinmicasadb
    /// Container: twinagentecalendario
    /// Partition Key: /TwinID
    /// </summary>
    public class AgentCalendarCosmosDB
    {
        private readonly ILogger<AgentCalendarCosmosDB> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "twinagentecalendario";
        private CosmosClient _cosmosClient;

        public AgentCalendarCosmosDB(ILogger<AgentCalendarCosmosDB> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            _cosmosEndpoint = configuration["Values:MICASA_COSMOS_ENDPOINT"] ??
                             configuration["MICASA_COSMOS_ENDPOINT"] ??
                             Environment.GetEnvironmentVariable("MICASA_COSMOS_ENDPOINT") ??
                             "https://twinmicasacosmosdb.documents.azure.com:443/";

            _cosmosKey = configuration["Values:MICASA_COSMOS_KEY"] ??
                        configuration["MICASA_COSMOS_KEY"] ??
                        Environment.GetEnvironmentVariable("MICASA_COSMOS_KEY") ??
                        string.Empty;
        }

        /// <summary>
        /// Inicializa el cliente de Cosmos DB
        /// </summary>
        private async Task InitializeCosmosClientAsync()
        {
            if (_cosmosClient == null)
            {
                try
                {
                    if (string.IsNullOrEmpty(_cosmosKey))
                    {
                        throw new InvalidOperationException("MICASA_COSMOS_KEY environment variable is not configured.");
                    }

                    _cosmosClient = new CosmosClient(_cosmosEndpoint, _cosmosKey);
                    var database = _cosmosClient.GetDatabase(_databaseName);
                    await database.ReadAsync();

                    _logger.LogInformation("✅ Successfully connected to Calendar Cosmos DB");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error connecting to Cosmos DB");
                    throw;
                }
            }
        }

        /// <summary>
        /// Guarda una nueva cita de calendario en Cosmos DB
        /// </summary>
        /// <param name="cita">Datos de la cita a guardar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <param name="microsoftOID">Microsoft Object ID del usuario</param>
        /// <returns>Resultado de la operación de guardado</returns>
        public async Task<SaveCalendarAppointmentResult> SaveAppointmentAsync(CalendarAppointmentData cita, string twinId, string microsoftOID)
        {
            if (cita == null)
            {
                return new SaveCalendarAppointmentResult
                {
                    Success = false,
                    ErrorMessage = "Calendar appointment data cannot be null",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new SaveCalendarAppointmentResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    DocumentId = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                // Generate new ID if not exists
                if (string.IsNullOrEmpty(cita.Id))
                {
                    cita.Id = Guid.NewGuid().ToString();
                }

                // Set TwinID and MicrosoftOID
                cita.TwinID = twinId;
                cita.MicrosoftOID = microsoftOID;

                // Set timestamps
                if (cita.FechaCreacion == DateTime.MinValue)
                {
                    cita.FechaCreacion = DateTime.UtcNow;
                }
                cita.FechaActualizacion = DateTime.UtcNow;

                _logger.LogInformation("📅 Saving calendar appointment to Cosmos DB: {AppointmentId}, Title: {Title}", cita.Id, cita.Titulo);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);
                var response = await container.UpsertItemAsync(cita, new PartitionKey(twinId));

                _logger.LogInformation("✅ Calendar appointment saved successfully. Document ID: {DocumentId}, RU consumed: {RU}",
                    cita.Id, response.RequestCharge);

                return new SaveCalendarAppointmentResult
                {
                    Success = true,
                    DocumentId = cita.Id,
                    RUConsumed = response.RequestCharge,
                    Message = "Calendar appointment saved successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving calendar appointment to Cosmos DB");
                return new SaveCalendarAppointmentResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = null
                };
            }
        }

        /// <summary>
        /// Obtiene una cita de calendario por su ID
        /// </summary>
        /// <param name="appointmentId">ID de la cita</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Datos de la cita o null si no existe</returns>
        public async Task<GetCalendarAppointmentResult> GetAppointmentByIdAsync(string appointmentId, string twinId)
        {
            if (string.IsNullOrEmpty(appointmentId) || string.IsNullOrEmpty(twinId))
            {
                return new GetCalendarAppointmentResult
                {
                    Success = false,
                    ErrorMessage = "AppointmentId and TwinId are required",
                    Appointment = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("📅 Getting calendar appointment from Cosmos DB: {AppointmentId}, TwinId: {TwinId}", appointmentId, twinId);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);
                var response = await container.ReadItemAsync<CalendarAppointmentData>(appointmentId, new PartitionKey(twinId));

                _logger.LogInformation("✅ Calendar appointment retrieved successfully. RU consumed: {RU}", response.RequestCharge);

                return new GetCalendarAppointmentResult
                {
                    Success = true,
                    Appointment = response.Resource,
                    RUConsumed = response.RequestCharge,
                    Message = "Calendar appointment retrieved successfully"
                };
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Calendar appointment not found: {AppointmentId}", appointmentId);
                return new GetCalendarAppointmentResult
                {
                    Success = false,
                    ErrorMessage = "Calendar appointment not found",
                    Appointment = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting calendar appointment from Cosmos DB");
                return new GetCalendarAppointmentResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Appointment = null
                };
            }
        }

        /// <summary>
        /// Obtiene todas las citas de calendario de un Twin
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <returns>Lista de citas del twin</returns>
        public async Task<GetCalendarAppointmentsResult> GetAppointmentsByTwinIdAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetCalendarAppointmentsResult
                {
                    Success = false,
                    ErrorMessage = "TwinId is required",
                    Appointments = new List<CalendarAppointmentData>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("📅 Getting all calendar appointments for TwinId: {TwinId}", twinId);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                var queryDefinition = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.Fecha")
                    .WithParameter("@twinId", twinId);

                var query = container.GetItemQueryIterator<CalendarAppointmentData>(queryDefinition);

                var appointments = new List<CalendarAppointmentData>();
                double totalRU = 0;

                while (query.HasMoreResults)
                {
                    var response = await query.ReadNextAsync();
                    appointments.AddRange(response.ToList());
                    totalRU += response.RequestCharge;
                }

                _logger.LogInformation("✅ Retrieved {Count} calendar appointments. Total RU consumed: {RU}", appointments.Count, totalRU);

                return new GetCalendarAppointmentsResult
                {
                    Success = true,
                    Appointments = appointments,
                    AppointmentCount = appointments.Count,
                    RUConsumed = totalRU,
                    Message = $"Retrieved {appointments.Count} calendar appointments"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting calendar appointments from Cosmos DB");
                return new GetCalendarAppointmentsResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Appointments = new List<CalendarAppointmentData>()
                };
            }
        }

        /// <summary>
        /// Obtiene citas de calendario por rango de fechas
        /// </summary>
        /// <param name="twinId">ID del twin</param>
        /// <param name="startDate">Fecha de inicio</param>
        /// <param name="endDate">Fecha de fin</param>
        /// <returns>Lista de citas en el rango de fechas</returns>
        public async Task<GetCalendarAppointmentsResult> GetAppointmentsByDateRangeAsync(string twinId, DateTime startDate, DateTime endDate)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetCalendarAppointmentsResult
                {
                    Success = false,
                    ErrorMessage = "TwinId is required",
                    Appointments = new List<CalendarAppointmentData>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("📅 Getting calendar appointments for TwinId: {TwinId}, DateRange: {Start} to {End}",
                    twinId, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query without ORDER BY to avoid composite index requirement
                // Sorting will be done in-memory after fetching the data
                var queryDefinition = new QueryDefinition(
                    "SELECT * FROM c WHERE c.TwinID = @twinId AND c.Fecha >= @startDate AND c.Fecha <= @endDate")
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@startDate", startDate.Date)
                    .WithParameter("@endDate", endDate.Date);

                var query = container.GetItemQueryIterator<CalendarAppointmentData>(queryDefinition);

                var appointments = new List<CalendarAppointmentData>();
                double totalRU = 0;

                while (query.HasMoreResults)
                {
                    var response = await query.ReadNextAsync();
                    appointments.AddRange(response.ToList());
                    totalRU += response.RequestCharge;
                }

                // Sort in-memory by Fecha ASC, then HoraInicio ASC
                appointments = appointments
                    .OrderBy(a => a.Fecha)
                    .ThenBy(a => a.HoraInicio)
                    .ToList();

                _logger.LogInformation("✅ Retrieved {Count} calendar appointments in date range. Total RU consumed: {RU}",
                    appointments.Count, totalRU);

                return new GetCalendarAppointmentsResult
                {
                    Success = true,
                    Appointments = appointments,
                    AppointmentCount = appointments.Count,
                    RUConsumed = totalRU,
                    Message = $"Retrieved {appointments.Count} calendar appointments in date range"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting calendar appointments by date range from Cosmos DB");
                return new GetCalendarAppointmentsResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Appointments = new List<CalendarAppointmentData>()
                };
            }
        }

        /// <summary>
        /// Actualiza una cita de calendario existente
        /// </summary>
        /// <param name="cita">Datos actualizados de la cita</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Resultado de la operación de actualización</returns>
        public async Task<SaveCalendarAppointmentResult> UpdateAppointmentAsync(CalendarAppointmentData cita, string twinId)
        {
            if (cita == null || string.IsNullOrEmpty(cita.Id))
            {
                return new SaveCalendarAppointmentResult
                {
                    Success = false,
                    ErrorMessage = "Calendar appointment data and ID are required",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new SaveCalendarAppointmentResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    DocumentId = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                // Update timestamp
                cita.FechaActualizacion = DateTime.UtcNow;

                _logger.LogInformation("📅 Updating calendar appointment in Cosmos DB: {AppointmentId}", cita.Id);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);
                var response = await container.ReplaceItemAsync(cita, cita.Id, new PartitionKey(twinId));

                _logger.LogInformation("✅ Calendar appointment updated successfully. RU consumed: {RU}", response.RequestCharge);

                return new SaveCalendarAppointmentResult
                {
                    Success = true,
                    DocumentId = cita.Id,
                    RUConsumed = response.RequestCharge,
                    Message = "Calendar appointment updated successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating calendar appointment in Cosmos DB");
                return new SaveCalendarAppointmentResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = null
                };
            }
        }

        /// <summary>
        /// Elimina una cita de calendario
        /// </summary>
        /// <param name="appointmentId">ID de la cita a eliminar</param>
        /// <param name="twinId">ID del twin (partition key)</param>
        /// <returns>Resultado de la operación de eliminación</returns>
        public async Task<DeleteCalendarAppointmentResult> DeleteAppointmentAsync(string appointmentId, string twinId)
        {
            if (string.IsNullOrEmpty(appointmentId) || string.IsNullOrEmpty(twinId))
            {
                return new DeleteCalendarAppointmentResult
                {
                    Success = false,
                    ErrorMessage = "AppointmentId and TwinId are required"
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                _logger.LogInformation("📅 Deleting calendar appointment from Cosmos DB: {AppointmentId}", appointmentId);

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);
                var response = await container.DeleteItemAsync<CalendarAppointmentData>(appointmentId, new PartitionKey(twinId));

                _logger.LogInformation("✅ Calendar appointment deleted successfully. RU consumed: {RU}", response.RequestCharge);

                return new DeleteCalendarAppointmentResult
                {
                    Success = true,
                    RUConsumed = response.RequestCharge,
                    Message = "Calendar appointment deleted successfully"
                };
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Calendar appointment not found for deletion: {AppointmentId}", appointmentId);
                return new DeleteCalendarAppointmentResult
                {
                    Success = false,
                    ErrorMessage = "Calendar appointment not found"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting calendar appointment from Cosmos DB");
                return new DeleteCalendarAppointmentResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }

    #region Data Models

    /// <summary>
    /// Modelo de datos para una cita de calendario
    /// </summary>
    public class CalendarAppointmentData
    {
        /// <summary>
        /// ID único del documento en Cosmos DB
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// ID del Twin (Partition Key)
        /// </summary>
        [JsonProperty("TwinID")]
        public string TwinID { get; set; } = string.Empty;

        /// <summary>
        /// Microsoft Object ID del usuario
        /// </summary>
        [JsonProperty("MicrosoftOID")]
        public string MicrosoftOID { get; set; } = string.Empty;

        /// <summary>
        /// Título de la cita (Ej: "Reunión con cliente", "Visita a propiedad")
        /// </summary>
        [JsonProperty("Titulo")]
        public string Titulo { get; set; } = string.Empty;

        /// <summary>
        /// Fecha de la cita
        /// </summary>
        [JsonProperty("Fecha")]
        public DateTime Fecha { get; set; }

        /// <summary>
        /// Indica si la cita es de todo el día
        /// </summary>
        [JsonProperty("TodoElDia")]
        public bool TodoElDia { get; set; }

        /// <summary>
        /// Hora de inicio (Ej: "09:00 AM")
        /// </summary>
        [JsonProperty("HoraInicio")]
        public string HoraInicio { get; set; } = string.Empty;

        /// <summary>
        /// Hora de fin (Ej: "10:00 AM")
        /// </summary>
        [JsonProperty("HoraFin")]
        public string HoraFin { get; set; } = string.Empty;

        /// <summary>
        /// Descripción y detalles adicionales sobre la cita
        /// </summary>
        [JsonProperty("Descripcion")]
        public string Descripcion { get; set; } = string.Empty;

        /// <summary>
        /// Fecha de creación del registro
        /// </summary>
        [JsonProperty("FechaCreacion")]
        public DateTime FechaCreacion { get; set; }

        /// <summary>
        /// Fecha de última actualización del registro
        /// </summary>
        [JsonProperty("FechaActualizacion")]
        public DateTime FechaActualizacion { get; set; }
    }

    /// <summary>
    /// Resultado de la operación de guardar/actualizar cita
    /// </summary>
    public class SaveCalendarAppointmentResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? DocumentId { get; set; }
        public double RUConsumed { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// Resultado de la operación de obtener una cita
    /// </summary>
    public class GetCalendarAppointmentResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public CalendarAppointmentData? Appointment { get; set; }
        public double RUConsumed { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// Resultado de la operación de obtener múltiples citas
    /// </summary>
    public class GetCalendarAppointmentsResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<CalendarAppointmentData> Appointments { get; set; } = new();
        public int AppointmentCount { get; set; }
        public double RUConsumed { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// Resultado de la operación de eliminar cita
    /// </summary>
    public class DeleteCalendarAppointmentResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public double RUConsumed { get; set; }
        public string? Message { get; set; }
    }

    #endregion
}

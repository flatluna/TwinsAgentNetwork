using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TwinAgentsLibrary.Models;

namespace TwinAgentsNetwork.Services
{
    /// <summary>
    /// Servicio para gestionar operaciones de base de datos Cosmos DB relacionadas con nutrición
    /// </summary>
    public class AgentNutritionCosmosDB
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AgentNutritionCosmosDB> _logger;
        private CosmosClient _cosmosClient;
        private Database _database;
        private Container _container;

        private Container _containerAlimentos;

        private const string ContainerTwinFoodName = "TwinFood";
        private const string ContainerTwinAlimentos = "TwinAlimentos";
        private const string DatabaseName = "TwinHumanDB";

        public AgentNutritionCosmosDB(IConfiguration configuration, ILogger<AgentNutritionCosmosDB> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            InitializeCosmosDbClient();
        }

        /// <summary>
        /// Inicializa el cliente de Cosmos DB con la configuración del archivo local.settings.json
        /// </summary>
        private void InitializeCosmosDbClient()
        {
            try
            {
                string cosmosEndpoint = _configuration["COSMOS_ENDPOINT"];
                string cosmosKey = _configuration["COSMOS_KEY"];

                if (string.IsNullOrEmpty(cosmosEndpoint) || string.IsNullOrEmpty(cosmosKey))
                {
                    throw new InvalidOperationException("Cosmos DB endpoint and key are required in configuration");
                }

                _cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey);
                _database = _cosmosClient.GetDatabase(DatabaseName);
                _container = _database.GetContainer(ContainerTwinFoodName);
                _containerAlimentos = _database.GetContainer(ContainerTwinAlimentos);

                _logger.LogInformation("✅ Cliente de Cosmos DB inicializado correctamente");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error inicializando cliente de Cosmos DB");
                throw;
            }
        }

        /// <summary>
        /// Guarda un FoodDiaryEntry en Cosmos DB
        /// </summary>
        /// <param name="foodDiaryEntry">La entrada del diario de alimentos a guardar</param>
        /// <returns>El FoodDiaryEntry guardado con propiedades del sistema de Cosmos DB</returns>
        public async Task<FoodDiaryCosmosEntry> SaveFoodDiaryEntryAsync(FoodDiaryCosmosEntry foodDiaryEntry)
        {
            if (foodDiaryEntry == null)
            {
                throw new ArgumentNullException(nameof(foodDiaryEntry), "FoodDiaryEntry cannot be null");
            }
             

            if (string.IsNullOrEmpty(foodDiaryEntry.TwinID))
            {
                throw new ArgumentException("TwinID is required", nameof(foodDiaryEntry));
            }

            try
            {
                _logger.LogInformation("💾 Guardando FoodDiaryEntry para TwinID: {TwinID}, Food: {DiaryFood}", 
                    foodDiaryEntry.TwinID, foodDiaryEntry.DiaryFood);
                
                // Utilizar TwinID como partition key
                ItemResponse<FoodDiaryCosmosEntry> response = await _container.CreateItemAsync(
                    foodDiaryEntry);

             

                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogError("⚠️ El documento ya existe. Id: {Id}", foodDiaryEntry.Id);
                throw new InvalidOperationException($"FoodDiaryEntry with Id {foodDiaryEntry.Id} already exists", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error guardando FoodDiaryEntry. TwinID: {TwinID}, Food: {DiaryFood}", 
                    foodDiaryEntry.TwinID, foodDiaryEntry.DiaryFood);
                throw;
            }
        }
        /// </summary>
        /// <param name="foodDiaryEntry">La entrada del diario de alimentos a guardar</param>
        /// <returns>El FoodDiaryEntry guardado con propiedades del sistema de Cosmos DB</returns>
        public async Task<FoodStats> SaveFoodAlimentosAsync(FoodStats foodStats)
        {
            if (foodStats == null)
            {
                throw new ArgumentNullException(nameof(foodStats), "FoodDiaryEntry cannot be null");
            }
             

            try
            {
                

                // Utilizar TwinID como partition key
                ItemResponse<FoodStats> response = await _containerAlimentos.CreateItemAsync(
                    foodStats);

              

                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogError("⚠️ El documento ya existe. Id: {Id}", foodStats.id);
                throw new InvalidOperationException($"FoodDiaryEntry with Id {foodStats.id} already exists", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error guardando FoodDiaryEntry. TwinID: {TwinID}, Food: {DiaryFood}",
                    foodStats.TwinID, foodStats.FoodName);
                throw;
            }
        }

        /// <summary>
        /// Obtiene un FoodDiaryEntry de Cosmos DB por su ID
        /// </summary>
        /// <param name="id">El ID del documento</param>
        /// <param name="twinId">El TwinID (partition key)</param>
        /// <returns>El FoodDiaryEntry si existe, null en caso contrario</returns>
        public async Task<FoodDiaryEntry> GetFoodDiaryEntryAsync(string id, string twinId)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Id cannot be null or empty", nameof(id));
            }

            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            try
            {
                _logger.LogInformation("🔍 Obteniendo FoodDiaryEntry. Id: {Id}, TwinID: {TwinID}", id, twinId);

                ItemResponse<FoodDiaryEntry> response = await _container.ReadItemAsync<FoodDiaryEntry>(
                    id,
                    new PartitionKey(twinId));

                _logger.LogInformation("✅ FoodDiaryEntry obtenido exitosamente. Id: {Id}", id);
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ FoodDiaryEntry no encontrado. Id: {Id}", id);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo FoodDiaryEntry. Id: {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Obtiene todos los FoodDiaryEntry de un Twin
        /// </summary>
        /// <param name="twinId">El TwinID para filtrar</param>
        /// <returns>Lista de FoodDiaryEntry del Twin especificado</returns>
        public async Task<List<FoodDiaryEntry>> GetFoodDiaryEntriesByTwinAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            try
            {
                _logger.LogInformation("🔍 Obteniendo FoodDiaryEntries para TwinID: {TwinID}", twinId);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.dateTimeConsumed DESC";
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                var diaryEntries = new List<FoodDiaryCosmosEntry>();
                using (FeedIterator<FoodDiaryCosmosEntry> feedIterator = _container.GetItemQueryIterator<FoodDiaryCosmosEntry>(queryDefinition))
                {
                    while (feedIterator.HasMoreResults)
                    {
                        FeedResponse<FoodDiaryCosmosEntry> response = await feedIterator.ReadNextAsync();
                        diaryEntries.AddRange(response);
                    }
                }

                // Enriquecer con FoodStats usando FoodID desde el contenedor TwinFood
                var enrichedEntries = new List<FoodDiaryEntry>();
                foreach (var diaryEntry in diaryEntries)
                {
                    var enrichedEntry = await EnrichFoodDiaryEntryAsync(diaryEntry);
                    enrichedEntries.Add(enrichedEntry);
                }

                _logger.LogInformation("✅ Se obtuvieron {Count} FoodDiaryEntries para TwinID: {TwinID}", 
                    enrichedEntries.Count, twinId);
                return enrichedEntries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo FoodDiaryEntries para TwinID: {TwinID}", twinId);
                throw;
            }
        }

        /// <summary>
        /// Elimina un FoodDiaryEntry de Cosmos DB
        /// </summary>
        /// <param name="id">El ID del documento a eliminar</param>
        /// <param name="twinId">El TwinID (partition key)</param>
        public async Task DeleteFoodDiaryEntryAsync(string id, string twinId)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Id cannot be null or empty", nameof(id));
            }

            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            try
            {
                _logger.LogInformation("🗑️ Eliminando FoodDiaryEntry. Id: {Id}, TwinID: {TwinID}", id, twinId);

                await _container.DeleteItemAsync<FoodDiaryEntry>(id, new PartitionKey(twinId));

                _logger.LogInformation("✅ FoodDiaryEntry eliminado exitosamente. Id: {Id}", id);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ FoodDiaryEntry no encontrado para eliminar. Id: {Id}", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error eliminando FoodDiaryEntry. Id: {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Actualiza un FoodDiaryEntry existente en Cosmos DB
        /// </summary>
        /// <param name="foodDiaryEntry">El FoodDiaryEntry con los datos a actualizar</param>
        /// <returns>El FoodDiaryEntry actualizado</returns>
        public async Task<FoodDiaryEntry> UpdateFoodDiaryEntryAsync(FoodDiaryEntry foodDiaryEntry)
        {
            if (foodDiaryEntry == null)
            {
                throw new ArgumentNullException(nameof(foodDiaryEntry));
            }

            if (string.IsNullOrEmpty(foodDiaryEntry.Id))
            {
                throw new ArgumentException("Id is required for update", nameof(foodDiaryEntry));
            }

            if (string.IsNullOrEmpty(foodDiaryEntry.TwinID))
            {
                throw new ArgumentException("TwinID is required for update", nameof(foodDiaryEntry));
            }

            try
            {
                _logger.LogInformation("✏️ Actualizando FoodDiaryEntry. Id: {Id}, TwinID: {TwinID}", 
                    foodDiaryEntry.Id, foodDiaryEntry.TwinID);

                ItemResponse<FoodDiaryEntry> response = await _container.UpsertItemAsync(
                    foodDiaryEntry,
                    new PartitionKey(foodDiaryEntry.TwinID));

                _logger.LogInformation("✅ FoodDiaryEntry actualizado exitosamente. Id: {Id}", response.Resource.Id);
                return response.Resource;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error actualizando FoodDiaryEntry. Id: {Id}", foodDiaryEntry.Id);
                throw;
            }
        }

        /// <summary>
        /// Libera los recursos del cliente de Cosmos DB
        /// </summary>
        public void Dispose()
        {
            _cosmosClient?.Dispose();
            _logger.LogInformation("✅ Cliente de Cosmos DB disposed");
        }

        /// <summary>
        /// Obtiene FoodDiaryEntries filtrados por fecha específica, hora y TwinID
        /// </summary>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <param name="year">Año (ejemplo: 2025)</param>
        /// <param name="month">Mes (1-12)</param>
        /// <param name="day">Día del mes (1-31)</param>
        /// <param name="time">Hora en formato HH:mm (opcional, ejemplo: "14:30")</param>
        /// <returns>Lista de FoodDiaryEntry que coinciden con los criterios</returns>
        public async Task<List<FoodDiaryEntry>> GetFoodDiaryEntriesByDateAndTimeAsync(
            string twinId, 
            int year, 
            int month, 
            int day, 
            string time = null)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            if (month < 1 || month > 12)
            {
                throw new ArgumentException("Month must be between 1 and 12", nameof(month));
            }

            if (day < 1 || day > 31)
            {
                throw new ArgumentException("Day must be between 1 and 31", nameof(day));
            }

            try
            {
                _logger.LogInformation(
                    "🔍 Obteniendo FoodDiaryEntries para TwinID: {TwinID}, Fecha: {Day}/{Month}/{Year}, Hora: {Time}",
                    twinId, day, month, year, time ?? "todas");

                // Crear fechas ISO 8601 para comparación con strings
                // Formato: YYYY-MM-DDTHH:mm:ss
                string startDateISO = $"{year:D4}-{month:D2}-{day:D2}T00:00:00";
                string endDateISO = $"{year:D4}-{month:D2}-{day:D2}T23:59:59";

                // Construir la query SQL comparando strings ISO 8601
                // Ya que dateTimeConsumed está almacenado como string en formato ISO
                string query = "SELECT * FROM c WHERE c.TwinID = @twinId " +
                    "AND c.dateTimeConsumed >= @startDate AND c.dateTimeConsumed <= @endDate";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@startDate", startDateISO)
                    .WithParameter("@endDate", endDateISO);

                var diaryEntries = new List<FoodDiaryCosmosEntry>();
                using (FeedIterator<FoodDiaryCosmosEntry> feedIterator = _container.GetItemQueryIterator<FoodDiaryCosmosEntry>(queryDefinition))
                {
                    while (feedIterator.HasMoreResults)
                    {
                        FeedResponse<FoodDiaryCosmosEntry> response = await feedIterator.ReadNextAsync();
                        diaryEntries.AddRange(response);
                    }
                }

                // Filtrar por hora si se proporciona
                if (!string.IsNullOrEmpty(time))
                {
                    diaryEntries = diaryEntries.Where(entry => entry.Time == time).ToList();
                }

                // Enriquecer con FoodStats usando FoodID desde el contenedor TwinFood
                var enrichedEntries = new List<FoodDiaryEntry>();
                foreach (var diaryEntry in diaryEntries)
                {
                    var enrichedEntry = await EnrichFoodDiaryEntryAsync(diaryEntry);
                    enrichedEntries.Add(enrichedEntry);
                }

                if (!string.IsNullOrEmpty(time))
                {
                    _logger.LogInformation(
                        "✅ Se obtuvieron {Count} FoodDiaryEntries para TwinID: {TwinID}, Fecha: {Date}, Hora: {Time}",
                        enrichedEntries.Count, twinId, $"{day}/{month}/{year}", time);
                }
                else
                {
                    _logger.LogInformation(
                        "✅ Se obtuvieron {Count} FoodDiaryEntries para TwinID: {TwinID}, Fecha: {Date}",
                        enrichedEntries.Count, twinId, $"{day}/{month}/{year}");
                }

                return enrichedEntries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "❌ Error obteniendo FoodDiaryEntries para TwinID: {TwinID}, Fecha: {Date}",
                    twinId, $"{day}/{month}/{year}");
                throw;
            }
        }

        /// <summary>
        /// Obtiene FoodDiaryEntries filtrados por rango de fechas (sin filtro de hora)
        /// </summary>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <param name="fromYear">Año de inicio (ejemplo: 2025)</param>
        /// <param name="fromMonth">Mes de inicio (1-12)</param>
        /// <param name="fromDay">Día de inicio del mes (1-31)</param>
        /// <param name="toYear">Año de fin (ejemplo: 2025)</param>
        /// <param name="toMonth">Mes de fin (1-12)</param>
        /// <param name="toDay">Día de fin del mes (1-31)</param>
        /// <returns>Lista de FoodDiaryEntry que coinciden con el rango de fechas y TwinID</returns>
        public async Task<List<FoodDiaryEntry>> GetFoodDiaryEntriesByDateRangeAsync(
            string twinId,
            int fromYear,
            int fromMonth,
            int fromDay,
            int toYear,
            int toMonth,
            int toDay)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            if (fromMonth < 1 || fromMonth > 12)
            {
                throw new ArgumentException("From month must be between 1 and 12", nameof(fromMonth));
            }

            if (fromDay < 1 || fromDay > 31)
            {
                throw new ArgumentException("From day must be between 1 and 31", nameof(fromDay));
            }

            if (toMonth < 1 || toMonth > 12)
            {
                throw new ArgumentException("To month must be between 1 and 12", nameof(toMonth));
            }

            if (toDay < 1 || toDay > 31)
            {
                throw new ArgumentException("To day must be between 1 and 31", nameof(toDay));
            }

            try
            {
                _logger.LogInformation(
                    "🔍 Obteniendo FoodDiaryEntries para TwinID: {TwinID}, Rango de fechas: {FromDate} a {ToDate}",
                    twinId, $"{fromDay}/{fromMonth}/{fromYear}", $"{toDay}/{toMonth}/{toYear}");

                // Crear fechas ISO 8601 para comparación con strings
                // Formato: YYYY-MM-DDTHH:mm:ss
                string startDateISO = $"{fromYear:D4}-{fromMonth:D2}-{fromDay:D2}T00:00:00";
                string endDateISO = $"{toYear:D4}-{toMonth:D2}-{toDay:D2}T23:59:59";

                // Construir la query SQL comparando strings ISO 8601
                // Ya que dateTimeConsumed está almacenado como string en formato ISO
                string query = "SELECT * FROM c WHERE c.TwinID = @twinId " +
                    "AND c.dateTimeConsumed >= @startDate AND c.dateTimeConsumed <= @endDate " +
                    "ORDER BY c.dateTimeConsumed DESC";

                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@startDate", startDateISO)
                    .WithParameter("@endDate", endDateISO);

                var diaryEntries = new List<FoodDiaryCosmosEntry>();
                using (FeedIterator<FoodDiaryCosmosEntry> feedIterator = _container.GetItemQueryIterator<FoodDiaryCosmosEntry>(queryDefinition))
                {
                    while (feedIterator.HasMoreResults)
                    {
                        FeedResponse<FoodDiaryCosmosEntry> response = await feedIterator.ReadNextAsync();
                        diaryEntries.AddRange(response);
                    }
                }

                // Enriquecer con FoodStats usando FoodID desde el contenedor TwinFood
                var enrichedEntries = new List<FoodDiaryEntry>();
                foreach (var diaryEntry in diaryEntries)
                {
                    var enrichedEntry = await EnrichFoodDiaryEntryAsync(diaryEntry);
                    enrichedEntries.Add(enrichedEntry);
                }

                _logger.LogInformation(
                    "✅ Se obtuvieron {Count} FoodDiaryEntries para TwinID: {TwinID}, Rango: {FromDate} a {ToDate}",
                    enrichedEntries.Count, twinId, $"{fromDay}/{fromMonth}/{fromYear}", $"{toDay}/{toMonth}/{toYear}");

                return enrichedEntries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "❌ Error obteniendo FoodDiaryEntries para TwinID: {TwinID}, Rango: {FromDate} a {ToDate}",
                    twinId, $"{fromDay}/{fromMonth}/{fromYear}", $"{toDay}/{toMonth}/{toYear}");
                throw;
            }
        }

        /// <summary>
        /// Obtiene FoodDiaryEntries filtrados por rango de fechas DateTime (sin filtro de hora)
        /// </summary>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <param name="fromDate">Fecha de inicio en formato DateTime (se usarán solo año, mes y día)</param>
        /// <param name="toDate">Fecha de fin en formato DateTime (se usarán solo año, mes y día)</param>
        /// <returns>Lista de FoodDiaryEntry que coinciden con el rango de fechas y TwinID</returns>
        public async Task<List<FoodDiaryEntry>> GetFoodDiaryEntriesByDateRangeAsync(
            string twinId,
            DateTime fromDate,
            DateTime toDate)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            return await GetFoodDiaryEntriesByDateRangeAsync(
                twinId,
                fromDate.Year,
                fromDate.Month,
                fromDate.Day,
                toDate.Year,
                toDate.Month,
                toDate.Day);
        }

        /// <summary>
        /// Obtiene un FoodStats por su ID desde el contenedor TwinFood
        /// </summary>
        /// <param name="foodId">El ID del alimento</param>
        /// <returns>FoodStats si existe, caso contrario un FoodStats vacío</returns>
        private async Task<FoodStats> GetFoodStatsByIdAsync(string foodId)
        {
            if (string.IsNullOrEmpty(foodId))
            {
                return new FoodStats();
            }

            try
            {
                _logger.LogInformation("🔍 Buscando FoodStats para FoodID: {FoodID}", foodId);
                
                // Buscar FoodStats en el contenedor TwinFood usando el ID
                var foodStatsQuery = "SELECT * FROM c WHERE c.id = @foodId";
                var foodStatsDefinition = new QueryDefinition(foodStatsQuery)
                    .WithParameter("@foodId", foodId);

                using (FeedIterator<FoodStats> foodIterator = _container.GetItemQueryIterator<FoodStats>(foodStatsDefinition))
                {
                    if (foodIterator.HasMoreResults)
                    {
                        var foodResponse = await foodIterator.ReadNextAsync();
                        if (foodResponse.Count > 0)
                        {
                            var foodStats = foodResponse.First();
                            _logger.LogInformation("✅ FoodStats encontrado para FoodID: {FoodID}", foodId);
                            return foodStats;
                        }
                    }
                }

                _logger.LogWarning("⚠️ FoodStats no encontrado para FoodID: {FoodID}", foodId);
                return new FoodStats();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error obteniendo FoodStats para FoodID: {FoodID}", foodId);
                return new FoodStats();
            }
        }

        /// <summary>
        /// Obtiene un FoodStats del contenedor TwinAlimentos por nombre de alimento (foodName)
        /// El foodName es único en el contenedor TwinAlimentos
        /// </summary>
        /// <param name="foodName">El nombre del alimento (único)</param>
        /// <returns>FoodStats si existe, caso contrario un FoodStats vacío</returns>
        public async Task<FoodStats> GetFoodStatsByNameAsync(string foodName)
        {
            if (string.IsNullOrEmpty(foodName))
            {
                _logger.LogWarning("⚠️ FoodName no puede ser nulo o vacío");
                return new FoodStats();
            }

            try
            {
                _logger.LogInformation("🔍 Buscando FoodStats en TwinAlimentos por foodName: {FoodName}", foodName);
                
                // Buscar FoodStats en el contenedor TwinAlimentos usando foodName (único)
                var foodStatsQuery = "SELECT * FROM c WHERE c.foodName = @foodName";
                var foodStatsDefinition = new QueryDefinition(foodStatsQuery)
                    .WithParameter("@foodName", foodName);

                using (FeedIterator<FoodStats> foodIterator = _containerAlimentos.GetItemQueryIterator<FoodStats>(foodStatsDefinition))
                {
                    if (foodIterator.HasMoreResults)
                    {
                        var foodResponse = await foodIterator.ReadNextAsync();
                        if (foodResponse.Count > 0)
                        {
                            var foodStats = foodResponse.First();
                            _logger.LogInformation("✅ FoodStats encontrado en TwinAlimentos para foodName: {FoodName}", foodName);
                            return foodStats;
                        }
                    }
                }

                _logger.LogWarning("⚠️ FoodStats no encontrado en TwinAlimentos para foodName: {FoodName}", foodName);
                return new FoodStats();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error obteniendo FoodStats del contenedor TwinAlimentos para foodName: {FoodName}", foodName);
                return new FoodStats();
            }
        }

        /// <summary>
        /// Obtiene un FoodStats del contenedor TwinAlimentos por nombre de alimento (foodName)
        /// de forma asincrónica con manejo específico de errores
        /// </summary>
        /// <param name="foodName">El nombre del alimento (único)</param>
        /// <returns>FoodStats si existe, lanza excepción si no encuentra</returns>
        public async Task<FoodStats> GetFoodStatsByNameRequiredAsync(string foodName)
        {
            if (string.IsNullOrEmpty(foodName))
            {
                throw new ArgumentException("FoodName no puede ser nulo o vacío", nameof(foodName));
            }

            try
            {
                _logger.LogInformation("🔍 Buscando FoodStats requerido en TwinAlimentos por foodName: {FoodName}", foodName);
                
                // Buscar FoodStats en el contenedor TwinAlimentos usando foodName (único)
                var foodStatsQuery = "SELECT * FROM c WHERE c.foodName = @foodName";
                var foodStatsDefinition = new QueryDefinition(foodStatsQuery)
                    .WithParameter("@foodName", foodName);

                using (FeedIterator<FoodStats> foodIterator = _containerAlimentos.GetItemQueryIterator<FoodStats>(foodStatsDefinition))
                {
                    if (foodIterator.HasMoreResults)
                    {
                        var foodResponse = await foodIterator.ReadNextAsync();
                        if (foodResponse.Count > 0)
                        {
                            var foodStats = foodResponse.First();
                            _logger.LogInformation("✅ FoodStats encontrado en TwinAlimentos para foodName: {FoodName}", foodName);
                            return foodStats;
                        }
                    }
                }

                // Si no encuentra el alimento, lanzar excepción
                var errorMessage = $"FoodStats no encontrado en TwinAlimentos para foodName: {foodName}";
                _logger.LogError(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo FoodStats del contenedor TwinAlimentos para foodName: {FoodName}", foodName);
                throw;
            }
        }

        /// <summary>
        /// Verifica si un alimento existe en el contenedor TwinAlimentos por su nombre (Food)
        /// </summary>
        /// <param name="foodName">El nombre del alimento a verificar</param>
        /// <param name="twinId">El TwinID del usuario (para logging)</param>
        /// <returns>true si el alimento existe, false si no existe</returns>
        public async Task<bool> CheckFoodExistsAsync(string foodName, string twinId)
        {
            if (string.IsNullOrEmpty(foodName))
            {
                _logger.LogWarning("⚠️ FoodName no puede ser nulo o vacío para CheckFoodExistsAsync");
                return false;
            }

            try
            {
                _logger.LogInformation("🔍 Verificando si el alimento existe en TwinAlimentos. FoodName: {FoodName}, TwinID: {TwinID}", 
                    foodName, twinId);
                
                // Buscar FoodStats en el contenedor TwinAlimentos usando foodName
                var foodStatsQuery = "SELECT * FROM c WHERE c.foodName = @foodName";
                var foodStatsDefinition = new QueryDefinition(foodStatsQuery)
                    .WithParameter("@foodName", foodName);

                using (FeedIterator<FoodStats> foodIterator = _containerAlimentos.GetItemQueryIterator<FoodStats>(foodStatsDefinition))
                {
                    if (foodIterator.HasMoreResults)
                    {
                        var foodResponse = await foodIterator.ReadNextAsync();
                        if (foodResponse.Count > 0)
                        {
                            _logger.LogInformation("✅ El alimento existe en TwinAlimentos. FoodName: {FoodName}", foodName);
                            return true;
                        }
                    }
                }

                _logger.LogWarning("⚠️ El alimento NO existe en TwinAlimentos. FoodName: {FoodName}", foodName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error verificando si el alimento existe en TwinAlimentos. FoodName: {FoodName}", foodName);
                return false;
            }
        }

        /// <summary>
        /// Obtiene una lista con todos los nombres de alimentos (foodName) del contenedor TwinAlimentos
        /// </summary>
        /// <returns>Lista de strings con los nombres de todos los alimentos en TwinAlimentos</returns>
        public async Task<List<string>> GetAllFoodNamesAsync()
        {
            try
            {
                _logger.LogInformation("🔍 Obteniendo lista de todos los nombres de alimentos desde TwinAlimentos");

                // Consulta para obtener todos los foodName del contenedor TwinAlimentos
                var foodNamesQuery = "SELECT c.foodName FROM c WHERE c.foodName != null ORDER BY c.foodName ASC";
                var foodNamesDefinition = new QueryDefinition(foodNamesQuery);

                var foodNamesList = new List<string>();

                using (FeedIterator<dynamic> foodIterator = _containerAlimentos.GetItemQueryIterator<dynamic>(foodNamesDefinition))
                {
                    while (foodIterator.HasMoreResults)
                    {
                        var foodResponse = await foodIterator.ReadNextAsync();
                        foreach (var item in foodResponse)
                        {
                            // Extraer el foodName de cada documento
                            if (item != null && item.foodName != null)
                            {
                                foodNamesList.Add(item.foodName.ToString());
                            }
                        }
                    }
                }

                _logger.LogInformation("✅ Se obtuvieron {Count} nombres de alimentos desde TwinAlimentos", foodNamesList.Count);
                return foodNamesList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo lista de nombres de alimentos desde TwinAlimentos");
                return new List<string>();
            }
        }

        /// <summary>
        /// Obtiene una lista con todos los alimentos (FoodStats) del contenedor TwinAlimentos
        /// </summary>
        /// <returns>Lista de FoodStats con todos los alimentos en TwinAlimentos</returns>
        public async Task<List<FoodStats>> GetAllFoodsAsync()
        {
            try
            {
                _logger.LogInformation("🔍 Obteniendo lista de todos los alimentos desde TwinAlimentos");

                // Consulta para obtener todos los documentos FoodStats del contenedor TwinAlimentos
                var allFoodsQuery = "SELECT * FROM c ORDER BY c.foodName ASC";
                var allFoodsDefinition = new QueryDefinition(allFoodsQuery);

                var foodsList = new List<FoodStats>();

                using (FeedIterator<FoodStats> foodIterator = _containerAlimentos.GetItemQueryIterator<FoodStats>(allFoodsDefinition))
                {
                    while (foodIterator.HasMoreResults)
                    {
                        var foodResponse = await foodIterator.ReadNextAsync();
                        foodsList.AddRange(foodResponse);
                    }
                }

                _logger.LogInformation("✅ Se obtuvieron {Count} alimentos desde TwinAlimentos", foodsList.Count);
                return foodsList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo lista de alimentos desde TwinAlimentos");
                return new List<FoodStats>();
            }
        }

        /// <summary>
        /// Enriquece un FoodDiaryCosmosEntry convirtiéndolo a FoodDiaryEntry y añadiendo FoodStats
        /// </summary>
        /// <param name="cosmosEntry">Entrada desde Cosmos DB</param>
        /// <returns>FoodDiaryEntry enriquecido con FoodStats</returns>
        private async Task<FoodDiaryEntry> EnrichFoodDiaryEntryAsync(FoodDiaryCosmosEntry cosmosEntry)
        {
            var foodEntry = new FoodDiaryEntry
            {
                BingResults = cosmosEntry.BingResults,
                DateTimeConsumed = cosmosEntry.DateTimeConsumed,
                DateTimeCreated = cosmosEntry.DateTimeCreated,
                TwinID = cosmosEntry.TwinID,
                FoodID = cosmosEntry.FoodID,
                FoodName = cosmosEntry.FoodName,
                Id = cosmosEntry.Id,
                Time = cosmosEntry.Time,
                DiaryFood = cosmosEntry.DiaryFood,
             
                FoodStats = new FoodStats() // Inicializar vacío
            };

            // Si existe FoodID, obtener los FoodStats asociados del contenedor TwinFood
            if (!string.IsNullOrEmpty(cosmosEntry.FoodName))
            {
                foodEntry.FoodStats = await GetFoodStatsByNameAsync(cosmosEntry.FoodName);
            }

            return foodEntry;
        }
    }
}

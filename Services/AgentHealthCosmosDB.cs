using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TwinAgentsLibrary.Models;

namespace TwinAgentsNetwork.Services
{
    /// <summary>
    /// Servicio para gestionar operaciones de base de datos Cosmos DB relacionadas con métricas de salud
    /// Almacena y gestiona datos de salud como métricas corporales y de sangre para cada usuario (Twin)
    /// </summary>
    public class AgentHealthCosmosDB : IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AgentHealthCosmosDB> _logger;
        private CosmosClient _cosmosClient;
        private Database _database;
        private Container _container;
        private Container _healthRecommendationsContainer;

        private const string ContainerTwinHealthMetricsName = "TwinHealthMetrics";
        private const string ContainerTwinHealthName = "TwinHealth";
        private const string DatabaseName = "TwinHumanDB";

        public AgentHealthCosmosDB(IConfiguration configuration, ILogger<AgentHealthCosmosDB> logger)
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
                _container = _database.GetContainer(ContainerTwinHealthMetricsName);
                _healthRecommendationsContainer = _database.GetContainer(ContainerTwinHealthName);

                _logger.LogInformation("✅ Cliente de Cosmos DB inicializado correctamente para HealthMetrics");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error inicializando cliente de Cosmos DB");
                throw;
            }
        }

        /// <summary>
        /// Guarda las métricas de salud de un usuario en Cosmos DB
        /// </summary>
        /// <param name="healthMetrics">Las métricas de salud a guardar</param>
        /// <returns>Las métricas de salud guardadas con propiedades del sistema de Cosmos DB</returns>
        public async Task<HealthMetrics> SaveHealthMetricsAsync(HealthMetrics healthMetrics)
        {
            if (healthMetrics == null)
            {
                throw new ArgumentNullException(nameof(healthMetrics), "HealthMetrics cannot be null");
            }

            if (string.IsNullOrEmpty(healthMetrics.TwinID))
            {
                throw new ArgumentException("TwinID is required", nameof(healthMetrics));
            }

            try
            {
                // Asegurar que el ID está establecido
                if (string.IsNullOrEmpty(healthMetrics.Id))
                {
                    healthMetrics.Id = Guid.NewGuid().ToString();
                }

                // Asegurar que UltimaActualizacion está establecido
                healthMetrics.UltimaActualizacion = DateTime.UtcNow;

                _logger.LogInformation("💾 Guardando HealthMetrics para TwinID: {TwinID}, Id: {Id}", 
                    healthMetrics.TwinID, healthMetrics.Id);
                
                // Utilizar TwinID como partition key
                ItemResponse<HealthMetrics> response = await _container.CreateItemAsync(
                    healthMetrics,
                    new PartitionKey(healthMetrics.TwinID));

                _logger.LogInformation("✅ HealthMetrics guardado exitosamente. Id: {Id}, TwinID: {TwinID}", 
                    response.Resource.Id, response.Resource.TwinID);

                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogError("⚠️ El documento ya existe. Id: {Id}", healthMetrics.Id);
                throw new InvalidOperationException($"HealthMetrics with Id {healthMetrics.Id} already exists", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error guardando HealthMetrics. TwinID: {TwinID}", 
                    healthMetrics.TwinID);
                throw;
            }
        }

        /// <summary>
        /// Obtiene las métricas de salud de Cosmos DB por su ID
        /// </summary>
        /// <param name="id">El ID del documento</param>
        /// <param name="twinId">El TwinID (partition key)</param>
        /// <returns>Las HealthMetrics si existen, null en caso contrario</returns>
        public async Task<HealthMetrics> GetHealthMetricsAsync(string id, string twinId)
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
                _logger.LogInformation("🔍 Obteniendo HealthMetrics. Id: {Id}, TwinID: {TwinID}", id, twinId);

                ItemResponse<HealthMetrics> response = await _container.ReadItemAsync<HealthMetrics>(
                    id,
                    new PartitionKey(twinId));

                _logger.LogInformation("✅ HealthMetrics obtenido exitosamente. Id: {Id}", id);
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ HealthMetrics no encontrado. Id: {Id}", id);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo HealthMetrics. Id: {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Obtiene todas las métricas de salud de un usuario Twin, ordenadas por fecha descendente
        /// </summary>
        /// <param name="twinId">El TwinID para filtrar</param>
        /// <returns>Lista de HealthMetrics del usuario especificado</returns>
        public async Task<List<HealthMetrics>> GetHealthMetricsByTwinAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            try
            {
                _logger.LogInformation("🔍 Obteniendo todas las HealthMetrics para TwinID: {TwinID}", twinId);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.UltimaActualizacion DESC";
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                var healthMetricsList = new List<HealthMetrics>();
                using (FeedIterator<HealthMetrics> feedIterator = _container.GetItemQueryIterator<HealthMetrics>(queryDefinition))
                {
                    while (feedIterator.HasMoreResults)
                    {
                        FeedResponse<HealthMetrics> response = await feedIterator.ReadNextAsync();
                        healthMetricsList.AddRange(response);
                    }
                }

                _logger.LogInformation("✅ Se obtuvieron {Count} HealthMetrics para TwinID: {TwinID}", 
                    healthMetricsList.Count, twinId);
                return healthMetricsList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo HealthMetrics para TwinID: {TwinID}", twinId);
                throw;
            }
        }

        /// <summary>
        /// Obtiene la métrica de salud más reciente de un usuario
        /// </summary>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <returns>La métrica de salud más reciente, null si no existe</returns>
        public async Task<HealthMetrics> GetLatestHealthMetricsAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            try
            {
                _logger.LogInformation("🔍 Obteniendo la métrica de salud más reciente para TwinID: {TwinID}", twinId);

                string query = "SELECT TOP 1 * FROM c WHERE c.TwinID = @twinId ORDER BY c.UltimaActualizacion DESC";
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                using (FeedIterator<HealthMetrics> feedIterator = _container.GetItemQueryIterator<HealthMetrics>(queryDefinition))
                {
                    if (feedIterator.HasMoreResults)
                    {
                        FeedResponse<HealthMetrics> response = await feedIterator.ReadNextAsync();
                        if (response.Count > 0)
                        {
                            var latestMetrics = response.First();
                            _logger.LogInformation("✅ Métrica de salud más reciente obtenida para TwinID: {TwinID}", twinId);
                            return latestMetrics;
                        }
                    }
                }

                _logger.LogWarning("⚠️ No se encontraron métricas de salud para TwinID: {TwinID}", twinId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo la métrica de salud más reciente para TwinID: {TwinID}", twinId);
                throw;
            }
        }

        /// <summary>
        /// Obtiene las métricas de salud filtradas por rango de fechas
        /// </summary>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <param name="fromDate">Fecha de inicio</param>
        /// <param name="toDate">Fecha de fin</param>
        /// <returns>Lista de HealthMetrics dentro del rango de fechas</returns>
        public async Task<List<HealthMetrics>> GetHealthMetricsByDateRangeAsync(
            string twinId,
            DateTime fromDate,
            DateTime toDate)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            if (toDate < fromDate)
            {
                throw new ArgumentException("toDate must be greater than or equal to fromDate");
            }

            try
            {
                _logger.LogInformation(
                    "🔍 Obteniendo HealthMetrics para TwinID: {TwinID}, Rango de fechas: {FromDate} a {ToDate}",
                    twinId, fromDate.ToString("yyyy-MM-dd"), toDate.ToString("yyyy-MM-dd"));

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId " +
                    "AND c.UltimaActualizacion >= @fromDate AND c.UltimaActualizacion <= @toDate " +
                    "ORDER BY c.UltimaActualizacion DESC";

                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@fromDate", fromDate)
                    .WithParameter("@toDate", toDate);

                var healthMetricsList = new List<HealthMetrics>();
                using (FeedIterator<HealthMetrics> feedIterator = _container.GetItemQueryIterator<HealthMetrics>(queryDefinition))
                {
                    while (feedIterator.HasMoreResults)
                    {
                        FeedResponse<HealthMetrics> response = await feedIterator.ReadNextAsync();
                        healthMetricsList.AddRange(response);
                    }
                }

                _logger.LogInformation(
                    "✅ Se obtuvieron {Count} HealthMetrics para TwinID: {TwinID}, Rango: {FromDate} a {ToDate}",
                    healthMetricsList.Count, twinId, fromDate.ToString("yyyy-MM-dd"), toDate.ToString("yyyy-MM-dd"));

                return healthMetricsList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "❌ Error obteniendo HealthMetrics para TwinID: {TwinID}, Rango: {FromDate} a {ToDate}",
                    twinId, fromDate.ToString("yyyy-MM-dd"), toDate.ToString("yyyy-MM-dd"));
                throw;
            }
        }

        /// <summary>
        /// Elimina una métrica de salud de Cosmos DB
        /// </summary>
        /// <param name="id">El ID del documento a eliminar</param>
        /// <param name="twinId">El TwinID (partition key)</param>
        public async Task DeleteHealthMetricsAsync(string id, string twinId)
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
                _logger.LogInformation("🗑️ Eliminando HealthMetrics. Id: {Id}, TwinID: {TwinID}", id, twinId);

                await _container.DeleteItemAsync<HealthMetrics>(id, new PartitionKey(twinId));

                _logger.LogInformation("✅ HealthMetrics eliminado exitosamente. Id: {Id}", id);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ HealthMetrics no encontrado para eliminar. Id: {Id}", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error eliminando HealthMetrics. Id: {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Actualiza las métricas de salud existentes en Cosmos DB
        /// </summary>
        /// <param name="healthMetrics">Las HealthMetrics con los datos a actualizar</param>
        /// <returns>Las HealthMetrics actualizadas</returns>
        public async Task<HealthMetrics> UpdateHealthMetricsAsync(HealthMetrics healthMetrics)
        {
            if (healthMetrics == null)
            {
                throw new ArgumentNullException(nameof(healthMetrics));
            }

            if (string.IsNullOrEmpty(healthMetrics.Id))
            {
                throw new ArgumentException("Id is required for update", nameof(healthMetrics));
            }

            if (string.IsNullOrEmpty(healthMetrics.TwinID))
            {
                throw new ArgumentException("TwinID is required for update", nameof(healthMetrics));
            }

            try
            {
                _logger.LogInformation("✏️ Actualizando HealthMetrics. Id: {Id}, TwinID: {TwinID}", 
                    healthMetrics.Id, healthMetrics.TwinID);

                // Actualizar la fecha de última actualización
                healthMetrics.UltimaActualizacion = DateTime.UtcNow;

                ItemResponse<HealthMetrics> response = await _container.UpsertItemAsync(
                    healthMetrics,
                    new PartitionKey(healthMetrics.TwinID));

                _logger.LogInformation("✅ HealthMetrics actualizado exitosamente. Id: {Id}", response.Resource.Id);
                return response.Resource;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error actualizando HealthMetrics. Id: {Id}", healthMetrics.Id);
                throw;
            }
        }

        /// <summary>
        /// Obtiene métricas de salud filtrando por categoría de obesidad
        /// </summary>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <param name="categoriaObesidad">La categoría de obesidad a filtrar</param>
        /// <returns>Lista de HealthMetrics con la categoría especificada</returns>
        public async Task<List<HealthMetrics>> GetHealthMetricsByObesityCategoryAsync(string twinId, string categoriaObesidad)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            if (string.IsNullOrEmpty(categoriaObesidad))
            {
                throw new ArgumentException("CategoriaObesidad cannot be null or empty", nameof(categoriaObesidad));
            }

            try
            {
                _logger.LogInformation(
                    "🔍 Obteniendo HealthMetrics para TwinID: {TwinID}, CategoriaObesidad: {CategoriaObesidad}",
                    twinId, categoriaObesidad);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId " +
                    "AND c.MetricasCorporales.CategoriaObesidad = @categoriaObesidad " +
                    "ORDER BY c.UltimaActualizacion DESC";

                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@categoriaObesidad", categoriaObesidad);

                var healthMetricsList = new List<HealthMetrics>();
                using (FeedIterator<HealthMetrics> feedIterator = _container.GetItemQueryIterator<HealthMetrics>(queryDefinition))
                {
                    while (feedIterator.HasMoreResults)
                    {
                        FeedResponse<HealthMetrics> response = await feedIterator.ReadNextAsync();
                        healthMetricsList.AddRange(response);
                    }
                }

                _logger.LogInformation(
                    "✅ Se obtuvieron {Count} HealthMetrics con CategoriaObesidad: {CategoriaObesidad}",
                    healthMetricsList.Count, categoriaObesidad);

                return healthMetricsList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "❌ Error obteniendo HealthMetrics por CategoriaObesidad: {CategoriaObesidad}",
                    categoriaObesidad);
                throw;
            }
        }

        /// <summary>
        /// Obtiene métricas de salud donde el IMC está en rango anormal
        /// </summary>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <returns>Lista de HealthMetrics con IMC anormal</returns>
        public async Task<List<HealthMetrics>> GetHealthMetricsWithAbnormalIMCAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            try
            {
                _logger.LogInformation("🔍 Obteniendo HealthMetrics con IMC anormal para TwinID: {TwinID}", twinId);

                // IMC < 18.5 (bajo peso) o > 25 (sobrepeso)
                string query = "SELECT * FROM c WHERE c.TwinID = @twinId " +
                    "AND (c.MetricasCorporales.IMC < 18.5 OR c.MetricasCorporales.IMC > 25) " +
                    "ORDER BY c.UltimaActualizacion DESC";

                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                var healthMetricsList = new List<HealthMetrics>();
                using (FeedIterator<HealthMetrics> feedIterator = _container.GetItemQueryIterator<HealthMetrics>(queryDefinition))
                {
                    while (feedIterator.HasMoreResults)
                    {
                        FeedResponse<HealthMetrics> response = await feedIterator.ReadNextAsync();
                        healthMetricsList.AddRange(response);
                    }
                }

                _logger.LogInformation(
                    "✅ Se obtuvieron {Count} HealthMetrics con IMC anormal para TwinID: {TwinID}",
                    healthMetricsList.Count, twinId);

                return healthMetricsList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo HealthMetrics con IMC anormal para TwinID: {TwinID}", twinId);
                throw;
            }
        }

        /// <summary>
        /// Obtiene métricas de sangre anormales (valores fuera de rangos normales)
        /// </summary>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <returns>Lista de HealthMetrics con métricas de sangre anormales</returns>
        public async Task<List<HealthMetrics>> GetHealthMetricsWithAbnormalBloodValuesAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            try
            {
                _logger.LogInformation("🔍 Obteniendo HealthMetrics con valores de sangre anormales para TwinID: {TwinID}", twinId);

                // Valores de sangre anormales:
                // ColesterolTotal > 200, ColesterolLDL > 100, Glucosa > 100, PresionArterialSistolica > 140
                string query = "SELECT * FROM c WHERE c.TwinID = @twinId " +
                    "AND (c.MetricasSangre.ColesterolTotal > 200 " +
                    "OR c.MetricasSangre.ColesterolLDL > 100 " +
                    "OR c.MetricasSangre.Glucosa > 100 " +
                    "OR c.MetricasSangre.PresionArterialSistolica > 140) " +
                    "ORDER BY c.UltimaActualizacion DESC";

                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                var healthMetricsList = new List<HealthMetrics>();
                using (FeedIterator<HealthMetrics> feedIterator = _container.GetItemQueryIterator<HealthMetrics>(queryDefinition))
                {
                    while (feedIterator.HasMoreResults)
                    {
                        FeedResponse<HealthMetrics> response = await feedIterator.ReadNextAsync();
                        healthMetricsList.AddRange(response);
                    }
                }

                _logger.LogInformation(
                    "✅ Se obtuvieron {Count} HealthMetrics con valores de sangre anormales para TwinID: {TwinID}",
                    healthMetricsList.Count, twinId);

                return healthMetricsList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo HealthMetrics con valores de sangre anormales para TwinID: {TwinID}", twinId);
                throw;
            }
        }

        /// <summary>
        /// Guarda las recomendaciones de salud generadas por el agente en Cosmos DB
        /// </summary>
        /// <param name="health">El objeto Health con recomendaciones personalizadas</param>
        /// <returns>El objeto Health guardado con propiedades del sistema de Cosmos DB</returns>
        public async Task<Health> SaveHealthRecommendationsAsync(Health health)
        {
            if (health == null)
            {
                throw new ArgumentNullException(nameof(health), "Health cannot be null");
            }

            if (string.IsNullOrEmpty(health.TwinID))
            {
                throw new ArgumentException("TwinID is required", nameof(health));
            }

            try
            {
                // Asegurar que el ID está establecido
                if (string.IsNullOrEmpty(health.Id))
                {
                    health.Id = Guid.NewGuid().ToString();
                }

                // Asegurar que FechaCreacion está establecida
                if (health.FechaCreacion == default(DateTime))
                {
                    health.FechaCreacion = DateTime.UtcNow;
                }

                _logger.LogInformation("💾 Guardando Health Recommendations para TwinID: {TwinID}, Id: {Id}", 
                    health.TwinID, health.Id);
                
                // Utilizar TwinID como partition key
                ItemResponse<Health> response = await _healthRecommendationsContainer.CreateItemAsync(
                    health,
                    new PartitionKey(health.TwinID));

                _logger.LogInformation("✅ Health Recommendations guardado exitosamente. Id: {Id}, TwinID: {TwinID}", 
                    response.Resource.Id, response.Resource.TwinID);

                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogError("⚠️ El documento de recomendaciones ya existe. Id: {Id}", health.Id);
                throw new InvalidOperationException($"Health recommendations with Id {health.Id} already exists", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error guardando Health Recommendations. TwinID: {TwinID}", 
                    health.TwinID);
                throw;
            }
        }

        /// <summary>
        /// Obtiene las recomendaciones de salud guardadas de un usuario
        /// </summary>
        /// <param name="id">El ID del documento de recomendaciones</param>
        /// <param name="twinId">El TwinID (partition key)</param>
        /// <returns>El objeto Health si existe, null en caso contrario</returns>
        public async Task<Health> GetHealthRecommendationsAsync(string id, string twinId)
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
                _logger.LogInformation("🔍 Obteniendo Health Recommendations. Id: {Id}, TwinID: {TwinID}", id, twinId);

                ItemResponse<Health> response = await _healthRecommendationsContainer.ReadItemAsync<Health>(
                    id,
                    new PartitionKey(twinId));

                _logger.LogInformation("✅ Health Recommendations obtenido exitosamente. Id: {Id}", id);
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Health Recommendations no encontrado. Id: {Id}", id);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo Health Recommendations. Id: {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Obtiene las recomendaciones de salud más recientes de un usuario
        /// </summary>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <returns>Las recomendaciones más recientes, null si no existen</returns>
        public async Task<Health> GetLatestHealthRecommendationsAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            try
            {
                _logger.LogInformation("🔍 Obteniendo las recomendaciones de salud más recientes para TwinID: {TwinID}", twinId);

                string query = "SELECT TOP 1 * FROM c WHERE c.TwinID = @twinId ORDER BY c.FechaCreacion DESC";
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                using (FeedIterator<Health> feedIterator = _healthRecommendationsContainer.GetItemQueryIterator<Health>(queryDefinition))
                {
                    if (feedIterator.HasMoreResults)
                    {
                        FeedResponse<Health> response = await feedIterator.ReadNextAsync();
                        if (response.Count > 0)
                        {
                            var latestRecommendations = response.First();
                            _logger.LogInformation("✅ Recomendaciones más recientes obtenidas para TwinID: {TwinID}", twinId);
                            return latestRecommendations;
                        }
                    }
                }

                _logger.LogWarning("⚠️ No se encontraron recomendaciones de salud para TwinID: {TwinID}", twinId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo las recomendaciones de salud más recientes para TwinID: {TwinID}", twinId);
                throw;
            }
        }

        /// <summary>
        /// Obtiene todas las recomendaciones de salud de un usuario
        /// </summary>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <returns>Lista de recomendaciones del usuario</returns>
        public async Task<List<Health>> GetHealthRecommendationsByTwinAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            try
            {
                _logger.LogInformation("🔍 Obteniendo todas las Health Recommendations para TwinID: {TwinID}", twinId);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.FechaCreacion DESC";
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                var healthRecommendationsList = new List<Health>();
                using (FeedIterator<Health> feedIterator = _healthRecommendationsContainer.GetItemQueryIterator<Health>(queryDefinition))
                {
                    while (feedIterator.HasMoreResults)
                    {
                        FeedResponse<Health> response = await feedIterator.ReadNextAsync();
                        healthRecommendationsList.AddRange(response);
                    }
                }

                _logger.LogInformation("✅ Se obtuvieron {Count} Health Recommendations para TwinID: {TwinID}", 
                    healthRecommendationsList.Count, twinId);
                return healthRecommendationsList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo Health Recommendations para TwinID: {TwinID}", twinId);
                throw;
            }
        }

        /// <summary>
        /// Elimina todas las recomendaciones de salud anteriores de un usuario
        /// </summary>
        /// <param name="twinId">El TwinID del usuario</param>
        public async Task DeleteHealthRecommendationsByTwinAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            try
            {
                _logger.LogInformation("🗑️ Eliminando todas las Health Recommendations para TwinID: {TwinID}", twinId);

                // Obtener todas las recomendaciones del usuario
                var recommendations = await GetHealthRecommendationsByTwinAsync(twinId);

                // Eliminar cada una
                foreach (var rec in recommendations)
                {
                    try
                    {
                        await _healthRecommendationsContainer.DeleteItemAsync<Health>(rec.Id, new PartitionKey(twinId));
                        _logger.LogInformation("✅ Health Recommendation eliminado. Id: {Id}", rec.Id);
                    }
                    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        _logger.LogWarning("⚠️ Health Recommendation no encontrado para eliminar. Id: {Id}", rec.Id);
                    }
                }

                _logger.LogInformation("✅ Todas las Health Recommendations eliminadas para TwinID: {TwinID}", twinId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error eliminando Health Recommendations para TwinID: {TwinID}", twinId);
                throw;
            }
        }

        /// <summary>
        /// Obtiene el registro de Health de la base de datos, dado que solo existe uno por TwinID
        /// </summary>
        /// <param name="twinId">El TwinID del usuario</param>
        /// <returns>El objeto Health si existe, null en caso contrario</returns>
        public async Task<Health> GetHealthByTwinIdAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
            }

            try
            {
                _logger.LogInformation("🔍 Obteniendo Health para TwinID: {TwinID}", twinId);

                // Como solo debe haber un Health por TwinID, usar una consulta que retorne el único registro
                string query = "SELECT TOP 1 * FROM c WHERE c.TwinID = @twinId";
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                using (FeedIterator<Health> feedIterator = _healthRecommendationsContainer.GetItemQueryIterator<Health>(queryDefinition))
                {
                    if (feedIterator.HasMoreResults)
                    {
                        FeedResponse<Health> response = await feedIterator.ReadNextAsync();
                        if (response.Count > 0)
                        {
                            var health = response.First();
                            _logger.LogInformation("✅ Health obtenido exitosamente para TwinID: {TwinID}", twinId);
                            return health;
                        }
                    }
                }

                _logger.LogWarning("⚠️ No se encontró Health para TwinID: {TwinID}", twinId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error obteniendo Health para TwinID: {TwinID}", twinId);
                throw;
            }
        }

        /// <summary>
        /// Libera los recursos del cliente de Cosmos DB
        /// </summary>
        public void Dispose()
        {
            _cosmosClient?.Dispose();
            _logger.LogInformation("✅ Cliente de Cosmos DB para HealthMetrics disposed");
        }
    }

    #region Model Classes
     

    #endregion
}

using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwinAgentsLibrary.Models;

namespace TwinAgentsNetwork.Services
{
    public class AgentMiCasaFotosCosmosDB
    {
        private readonly ILogger<AgentMiCasaFotosCosmosDB> _logger;
        private readonly IConfiguration _configuration;
        private readonly CosmosClient _client;
        private readonly Database _database;
        private readonly Container _memoriasContainer;
        public AgentMiCasaFotosCosmosDB(ILogger<AgentMiCasaFotosCosmosDB>
            logger, IOptions<CosmosDbSettings> cosmosOptions, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            var cosmosSettings = cosmosOptions.Value;

            _logger.LogInformation("🧠 Initializing MiMemoria Cosmos DB Service");
            _logger.LogInformation($"   🔗 Endpoint: {cosmosSettings.Endpoint}");
            _logger.LogInformation($"   💾 Database: {cosmosSettings.DatabaseName}");
            _logger.LogInformation($"   📦 Container: TwinMiMemoria");

            if (string.IsNullOrEmpty(cosmosSettings.Key))
            {
                _logger.LogError("❌ COSMOS_KEY is required but not found in configuration");
                throw new InvalidOperationException("COSMOS_KEY is required but not found in configuration");
            }

            if (string.IsNullOrEmpty(cosmosSettings.Endpoint))
            {
                _logger.LogError("❌ COSMOS_ENDPOINT is required but not found in configuration");
                throw new InvalidOperationException("COSMOS_ENDPOINT is required but not found in configuration");
            }

            try
            {
                _client = new CosmosClient(cosmosSettings.Endpoint, cosmosSettings.Key);
                _database = _client.GetDatabase(cosmosSettings.DatabaseName);
                _memoriasContainer = _database.GetContainer("TwinMiMemoria");

                _logger.LogInformation("✅ MiMemoria Cosmos DB Service initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize MiMemoria Cosmos DB client");
                throw;
            }
        }

        /// <summary>
        /// Retrieves a Memoria (Memory) by ID
        /// </summary>
        public async Task<dynamic> GetMemoriaByIdAsync(string memoriaId, string twinId)
        {
            if (string.IsNullOrEmpty(memoriaId))
            {
                throw new ArgumentException("Memory ID cannot be null or empty", nameof(memoriaId));
            }

            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("Twin ID cannot be null or empty", nameof(twinId));
            }

            try
            {
                var response = await _memoriasContainer.ReadItemAsync<dynamic>(memoriaId, new PartitionKey(twinId));
                _logger.LogInformation("✅ Retrieved memoria: {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Memoria not found: {MemoriaId} for Twin: {TwinId}", memoriaId, twinId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving memoria: {MemoriaId}", memoriaId);
                throw;
            }
        }

        /// <summary>
        /// CosmosDB Settings class
        /// </summary>
        public class CosmosDbSettings
        {
            public string Endpoint { get; set; } = string.Empty;
            public string Key { get; set; } = string.Empty;
            public string DatabaseName { get; set; } = string.Empty;
        }
    }
}


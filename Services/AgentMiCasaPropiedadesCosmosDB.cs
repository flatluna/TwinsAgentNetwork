using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using LibModels = TwinAgentsLibrary.Models;

namespace TwinAgentsNetwork.Services
{
    /// <summary>
    /// Service for managing MiCasa property operations in Azure Cosmos DB
    /// Database: twinmicasadb
    /// Container: micasaclientecontainer
    /// </summary>
    public class AgentMiCasaPropiedadesCosmosDB
    {
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "micasaclientecontainer";
        private readonly IConfiguration _configuration;
        private CosmosClient _cosmosClient;

        public AgentMiCasaPropiedadesCosmosDB(IConfiguration configuration = null)
        {
            _configuration = configuration;
            _cosmosEndpoint = Environment.GetEnvironmentVariable("MICASA_COSMOS_ENDPOINT") ?? "https://twinmicasacosmosdb.documents.azure.com:443/";
            _cosmosKey = Environment.GetEnvironmentVariable("MICASA_COSMOS_KEY") ?? string.Empty;
        }

        /// <summary>
        /// Initialize Cosmos DB client connection
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
                    
                    Console.WriteLine("✅ Successfully connected to MiCasa Cosmos DB");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error connecting to Cosmos DB: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Retrieves a specific property by client ID, TwinID, and property ID
        /// Uses Cosmos DB JOIN to unnest the propiedad array
        /// Also generates SAS URL for Fachada.jpg if configuration is available
        /// </summary>
        /// <param name="clientId">ID of the client document</param>
        /// <param name="twinId">TwinID for validation</param>
        /// <param name="propiedadId">ID of the property to retrieve</param>
        /// <returns>Property data if found</returns>
        public async Task<GetPropertyByClientTwinAndIdResult> GetPropertyByClientTwinAndIdAsync(
            string clientId, 
            string twinId, 
            string propiedadId)
        {
            if (string.IsNullOrEmpty(clientId))
                return new GetPropertyByClientTwinAndIdResult 
                { 
                    Success = false, 
                    ErrorMessage = "Client ID cannot be null or empty", 
                    Propiedad = null 
                };

            if (string.IsNullOrEmpty(twinId))
                return new GetPropertyByClientTwinAndIdResult 
                { 
                    Success = false, 
                    ErrorMessage = "TwinID cannot be null or empty", 
                    Propiedad = null 
                };

            if (string.IsNullOrEmpty(propiedadId))
                return new GetPropertyByClientTwinAndIdResult 
                { 
                    Success = false, 
                    ErrorMessage = "Property ID cannot be null or empty", 
                    Propiedad = null 
                };

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = @"SELECT p 
                    FROM c 
                    JOIN p IN c.propiedad 
                    WHERE c.id = @clientId 
                    AND c.TwinID = @twinId 
                    AND p.id = @propiedadId";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@clientId", clientId)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@propiedadId", propiedadId);

                using FeedIterator<dynamic> feed = container.GetItemQueryIterator<dynamic>(queryDefinition);

                LibModels.Propiedad propiedad = null;
                double totalRU = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<dynamic> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    if (response.Count > 0)
                    {
                        var item = response.FirstOrDefault();
                        if (item != null)
                        {
                            string propiedadJson = JsonConvert.SerializeObject(item.p);
                            propiedad = JsonConvert.DeserializeObject<LibModels.Propiedad>(propiedadJson);
                            break;
                        }
                    }
                }

                if (propiedad == null)
                {
                    Console.WriteLine($"⚠️ Property not found: clientId={clientId}, twinId={twinId}, propiedadId={propiedadId}");
                    return new GetPropertyByClientTwinAndIdResult 
                    { 
                        Success = false, 
                        ErrorMessage = $"Property with ID '{propiedadId}' not found for client '{clientId}' and TwinID '{twinId}'", 
                        Propiedad = null 
                    };
                }

                // Generate SAS URL for Fachada.jpg from Data Lake if configuration is available
                if (_configuration != null && !string.IsNullOrEmpty(twinId))
                {
                    try
                    {
                        Console.WriteLine($"📸 Generating SAS URL for Fachada at path: MiCasa/Fachada/Fachada.jpg for TwinID: {twinId}");
                        
                        // Create DataLakeClient factory
                        var loggerFactory = LoggerFactory.Create(builder => { });
                        var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                        var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                        
                        // Path for fachada: MiCasa/Fachada/Fachada.jpg
                        const string fachadaPath = "MiCasa/Fachada/Fachada.jpg";
                        
                        // Generate SAS URL with 24-hour expiration
                        var fachadaSasUrl = await dataLakeClient.GenerateSasUrlAsync(fachadaPath, TimeSpan.FromHours(24));
                        
                        if (!string.IsNullOrEmpty(fachadaSasUrl))
                        {
                            propiedad.FachadaURL = fachadaSasUrl;
                            Console.WriteLine($"✅ SAS URL generated successfully for Fachada");
                        }
                        else
                        {
                            Console.WriteLine($"⚠️ Failed to generate SAS URL for Fachada - file may not exist at path: {fachadaPath}");
                            propiedad.FachadaURL = string.Empty;
                        }
                    }
                    catch (Exception sasEx)
                    {
                        Console.WriteLine($"⚠️ Error generating SAS URL for Fachada: {sasEx.Message}");
                        // Continue without SAS URL - don't fail the entire operation
                        propiedad.FachadaURL = string.Empty;
                    }
                }

                Console.WriteLine($"✅ Retrieved property: clientId={clientId}, twinId={twinId}, propiedadId={propiedadId}. RU consumed: {totalRU:F2}");
                
                return new GetPropertyByClientTwinAndIdResult 
                { 
                    Success = true, 
                    ClientId = clientId,
                    TwinId = twinId,
                    Propiedad = propiedad, 
                    RUConsumed = totalRU, 
                    Timestamp = DateTime.UtcNow 
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                Console.WriteLine($"❌ {errorMessage}");
                return new GetPropertyByClientTwinAndIdResult 
                { 
                    Success = false, 
                    ErrorMessage = errorMessage, 
                    Propiedad = null 
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error retrieving property: {ex.Message}");
                return new GetPropertyByClientTwinAndIdResult 
                { 
                    Success = false, 
                    ErrorMessage = ex.Message, 
                    Propiedad = null 
                };
            }
        }

        /// <summary>
        /// Closes Cosmos DB client connection
        /// </summary>
        public void Dispose()
        {
            _cosmosClient?.Dispose();
        }
    }

    #region Data Models

    /// <summary>
    /// Result of retrieving a specific property by client ID, TwinID, and property ID
    /// </summary>
    public class GetPropertyByClientTwinAndIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string TwinId { get; set; } = "";
        public LibModels.Propiedad? Propiedad { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

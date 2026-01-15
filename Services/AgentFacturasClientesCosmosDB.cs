using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TwinAgentsNetwork.Agents;

namespace TwinAgentsNetwork.Services
{
    public class AgentFacturasClientesCosmosDB
    {
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "twinfacturascliente";
        private CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AgentFacturasClientesCosmosDB> _logger;

        public AgentFacturasClientesCosmosDB(ILogger<AgentFacturasClientesCosmosDB> logger, IConfiguration configuration = null)
        {
            _logger = logger;
            _configuration = configuration;

            _cosmosEndpoint = _configuration?["Values:MICASA_COSMOS_ENDPOINT"] ?? 
                             _configuration?["MICASA_COSMOS_ENDPOINT"] ?? 
                             Environment.GetEnvironmentVariable("MICASA_COSMOS_ENDPOINT") ?? 
                             "https://twinfacturascosmosdb.documents.azure.com:443/";
            
            _cosmosKey = _configuration?["Values:MICASA_COSMOS_KEY"] ?? 
                        _configuration?["MICASA_COSMOS_KEY"] ?? 
                        Environment.GetEnvironmentVariable("MICASA_COSMOS_KEY") ?? 
                        string.Empty;
        }

        private async Task InitializeCosmosClientAsync()
        {
            if (_cosmosClient == null)
            {
                try
                {
                    if (string.IsNullOrEmpty(_cosmosKey))
                    {
                        throw new InvalidOperationException("FACTURAS_COSMOS_KEY environment variable is not configured.");
                    }

                    _cosmosClient = new CosmosClient(_cosmosEndpoint, _cosmosKey);
                    var database = _cosmosClient.GetDatabase(_databaseName);
                    await database.ReadAsync();
                    
                    _logger.LogInformation("✅ Successfully connected to Facturas Cosmos DB");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error connecting to Cosmos DB");
                    throw;
                }
            }
        }

        public async Task<SaveFacturaResult> SaveFacturaAsync(FacturaClienteData facturaData, string twinID, string customerID)
        {
            if (facturaData == null)
            {
                return new SaveFacturaResult
                {
                    Success = false,
                    ErrorMessage = "Factura data cannot be null",
                    DocumentId = null
                };
            }

            if (string.IsNullOrEmpty(twinID))
            {
                return new SaveFacturaResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    DocumentId = null
                };
            }

            try
            { 

                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                facturaData.id = Guid.NewGuid().ToString();
                
                var documentData = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(facturaData));
                documentData["TwinID"] = twinID;
                documentData["CustomerID"] = customerID ?? string.Empty;
                documentData["IdCliente"] = customerID ?? string.Empty;
                documentData["type"] = "factura_cliente";
                documentData["fechaGuardado"] = DateTime.UtcNow;

                var response = await container.CreateItemAsync(documentData);

                _logger.LogInformation("✅ Factura saved successfully. Document ID: {DocumentId}", facturaData.id);

                return new SaveFacturaResult
                {
                    Success = true,
                    DocumentId = facturaData.id,
                    RUConsumed = response.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new SaveFacturaResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    DocumentId = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving factura");
                return new SaveFacturaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = null
                };
            }
        }

        public async Task<GetFacturasByTwinIdAndClienteResult> GetFacturasByTwinIdAndIdClienteAsync(string twinId, string idCliente)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetFacturasByTwinIdAndClienteResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Facturas = new List<FacturaClienteData>()
                };
            }

            if (string.IsNullOrEmpty(idCliente))
            {
                return new GetFacturasByTwinIdAndClienteResult
                {
                    Success = false,
                    ErrorMessage = "IdCliente cannot be null or empty",
                    Facturas = new List<FacturaClienteData>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.TwinID = @twinId AND c.CustomerID = @idCliente";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@idCliente", idCliente);

                using FeedIterator<FacturaClienteData> feed = container.GetItemQueryIterator<FacturaClienteData>(queryDefinition);

                var facturas = new List<FacturaClienteData>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<FacturaClienteData> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (var factura in response)
                    {
                        facturas.Add(factura);
                        count++;
                    }
                }

                if (_configuration != null && facturas.Count > 0)
                {
                    _logger.LogInformation("📎 Generating SAS URLs for {Count} facturas...", facturas.Count);
                    
                    var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                    var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                    var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                    foreach (var factura in facturas)
                    {
                        if (!string.IsNullOrEmpty(factura.NombreArchivo) && !string.IsNullOrEmpty(factura.Path))
                        {
                            try
                            {
                                var fullFilePath = $"{factura.Path}/{factura.NombreArchivo}";
                                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(24));
                                
                                if (!string.IsNullOrEmpty(sasUrl))
                                {
                                    factura.SASURL = sasUrl;
                                    _logger.LogInformation("✅ SAS URL generated for factura: {FileName}", factura.NombreArchivo);
                                }
                                else
                                {
                                    _logger.LogWarning("⚠️ Failed to generate SAS URL for: {FileName}", factura.NombreArchivo);
                                }
                            }
                            catch (Exception sasEx)
                            {
                                _logger.LogWarning(sasEx, "⚠️ Error generating SAS URL for factura: {FileName}", factura.NombreArchivo);
                            }
                        }
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} facturas for TwinID: {TwinId}, IdCliente: {IdCliente}. RU consumed: {RU:F2}", 
                    count, twinId, idCliente, totalRU);

                return new GetFacturasByTwinIdAndClienteResult
                {
                    Success = true,
                    Facturas = facturas,
                    FacturaCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                _logger.LogError("❌ {ErrorMessage}", errorMessage);

                return new GetFacturasByTwinIdAndClienteResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Facturas = new List<FacturaClienteData>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving facturas");
                return new GetFacturasByTwinIdAndClienteResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Facturas = new List<FacturaClienteData>()
                };
            }
        }

        public void Dispose()
        {
            _cosmosClient?.Dispose();
        }
    }

    public class SaveFacturaResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string DocumentId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class GetFacturasByTwinIdAndClienteResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<FacturaClienteData> Facturas { get; set; } = new();
        public int FacturaCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using LibModels = TwinAgentsLibrary.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TwinAgentsNetwork.Services
{
    /// <summary>
    /// Service for managing MiCasa clients and properties in Azure Cosmos DB
    /// Database: twinmicasadb
    /// Container: micasaclientecontainer
    /// </summary>
    public class AgentTwinMiCasaCosmosDB
    {
        private readonly string _cosmosEndpoint;
        private readonly string _cosmosKey;
        private readonly string _databaseName = "twinmicasadb";
        private readonly string _containerName = "micasaclientecontainer";
        private CosmosClient _cosmosClient;
        private readonly IConfiguration _configuration;

        public AgentTwinMiCasaCosmosDB(IConfiguration configuration = null)
        {
            _cosmosEndpoint = Environment.GetEnvironmentVariable("MICASA_COSMOS_ENDPOINT") ?? "https://twinmicasacosmosdb.documents.azure.com:443/";
            _cosmosKey = Environment.GetEnvironmentVariable("MICASA_COSMOS_KEY") ?? string.Empty;
            _configuration = configuration;
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
                    // Validate that the key is configured
                    if (string.IsNullOrEmpty(_cosmosKey))
                    {
                        throw new InvalidOperationException("MICASA_COSMOS_KEY environment variable is not configured. Please set it before using this service.");
                    }

                    _cosmosClient = new CosmosClient(_cosmosEndpoint, _cosmosKey);
                    
                    // Verify connection
                    var database = _cosmosClient.GetDatabase(_databaseName);
                    await database.ReadAsync();
                    
                    Console.WriteLine("? Successfully connected to MiCasa Cosmos DB");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"? Error connecting to Cosmos DB: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Saves a MiCasa client with property information to Cosmos DB
        /// </summary>
        /// <param name="clientData">Client document containing cliente and propiedad information</param>
        /// <returns>Success status and document ID</returns>
        public async Task<MiCasaSaveResult> SaveClientWithPropertyAsync(LibModels.MiCasaClientes clientData)
        {
            if (clientData == null)
            {
                return new MiCasaSaveResult
                {
                    Success = false,
                    ErrorMessage = "Client data cannot be null",
                    DocumentId = null
                };
            }

            try
            {
                // Validate configuration before trying to initialize
                if (string.IsNullOrEmpty(_cosmosKey))
                {
                    return new MiCasaSaveResult
                    {
                        Success = false,
                        ErrorMessage = "MiCasa Cosmos DB is not configured. Please set MICASA_COSMOS_KEY environment variable.",
                        DocumentId = null
                    };
                }

                await InitializeCosmosClientAsync();

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Generate unique document ID if not provided
                if (string.IsNullOrEmpty(clientData.Id))
                {
                    clientData.Id = Guid.NewGuid().ToString();
                }

                // Set metadata
                clientData.Tipo = "micasacliente";
                clientData.FechaCreacion = DateTime.UtcNow;
                clientData.FechaActualizacion = DateTime.UtcNow;

                // Save to Cosmos DB using strongly-typed client
                var response = await container.CreateItemAsync(clientData);

                Console.WriteLine($"? Client saved successfully. Document ID: {clientData.Id}");

                return new MiCasaSaveResult
                {
                    Success = true,
                    DocumentId = clientData.Id,
                    RUConsumed = response.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                Console.WriteLine($"? {errorMessage}");

                return new MiCasaSaveResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    DocumentId = null
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error saving client: {ex.Message}");

                return new MiCasaSaveResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = null
                };
            }
        }

        /// <summary>
        /// Retrieves a client by document ID using SQL SELECT query
        /// </summary>
        public async Task<GetClientByIdResult> GetClientByIdAsync(string documentId)
        {
            if (string.IsNullOrEmpty(documentId))
            {
                return new GetClientByIdResult
                {
                    Success = false,
                    ErrorMessage = "Document ID cannot be null or empty",
                    Client = null
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query to get client by document ID using SELECT
                string query = "SELECT * FROM c WHERE c.id = @documentId";

                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@documentId", documentId);

                using FeedIterator<LibModels.MiCasaClientes> feed = container.GetItemQueryIterator<LibModels.MiCasaClientes>(queryDefinition);

                LibModels.MiCasaClientes client = null;
                double totalRU = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<LibModels.MiCasaClientes> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    if (response.Count > 0)
                    {
                        client = response.FirstOrDefault();
                        break;
                    }
                }

                if (client == null)
                {
                    Console.WriteLine($"?? Client not found: {documentId}");
                    return new GetClientByIdResult
                    {
                        Success = false,
                        ErrorMessage = $"Client with ID '{documentId}' not found",
                        Client = null
                    };
                }

                Console.WriteLine($"? Retrieved client: {documentId}");

                return new GetClientByIdResult
                {
                    Success = true,
                    Client = client,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                Console.WriteLine($"? {errorMessage}");

                return new GetClientByIdResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Client = null
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error retrieving client: {ex.Message}");
                return new GetClientByIdResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Client = null
                };
            }
        }

        
        /// <summary>
        /// Retrieves all clients for a specific TwinID
        /// </summary>
        /// <param name="twinId">The TwinID to search for</param>
        /// <returns>List of MiCasaClientes for the specified TwinID</returns>
        public async Task<GetClientsByTwinIdResult> GetClientsByTwinIdAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetClientsByTwinIdResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    Clients = new List<LibModels.MiCasaClientes>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query to get all clients for the specified TwinID
                string query = "SELECT * FROM c WHERE c.TwinID = @twinId";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<LibModels.MiCasaClientes> feed = container.GetItemQueryIterator<LibModels.MiCasaClientes>(queryDefinition);

                var clients = new List<LibModels.MiCasaClientes>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<LibModels.MiCasaClientes> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (LibModels.MiCasaClientes client in response)
                    {
                        clients.Add(client);
                        count++;
                    }
                }

                Console.WriteLine($"? Retrieved {count} clients for TwinID: {twinId}. RU consumed: {totalRU:F2}");

                return new GetClientsByTwinIdResult
                {
                    Success = true,
                    Clients = clients,
                    ClientCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                Console.WriteLine($"? {errorMessage}");

                return new GetClientsByTwinIdResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Clients = new List<LibModels.MiCasaClientes>()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error retrieving clients: {ex.Message}");

                return new GetClientsByTwinIdResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Clients = new List<LibModels.MiCasaClientes>()
                };
            }
        }

        /// <summary>
        /// Retrieves all clients from the Cosmos DB container without any restrictions
        /// Returns all MiCasaClientes documents stored in the container
        /// </summary>
        /// <returns>List of all MiCasaClientes in the container</returns>
        public async Task<GetAllClientsResult> GetAllClientsAsync()
        {
            try
            {
                // Validate configuration before trying to initialize
                if (string.IsNullOrEmpty(_cosmosKey))
                {
                    return new GetAllClientsResult
                    {
                        Success = false,
                        ErrorMessage = "MiCasa Cosmos DB is not configured. Please set MICASA_COSMOS_KEY environment variable.",
                        Clients = new List<LibModels.MiCasaClientes>()
                    };
                }

                await InitializeCosmosClientAsync();

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query to get all clients without any filter
                string query = "SELECT * FROM c";

                var queryDefinition = new QueryDefinition(query);

                using FeedIterator<LibModels.MiCasaClientes> feed = container.GetItemQueryIterator<LibModels.MiCasaClientes>(queryDefinition);

                var clients = new List<LibModels.MiCasaClientes>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<LibModels.MiCasaClientes> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (LibModels.MiCasaClientes client in response)
                    {
                        clients.Add(client);
                        count++;
                    }
                }

                Console.WriteLine($"? Retrieved {count} total clients from container. RU consumed: {totalRU:F2}");

                return new GetAllClientsResult
                {
                    Success = true,
                    Clients = clients,
                    ClientCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                Console.WriteLine($"? {errorMessage}");

                return new GetAllClientsResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Clients = new List<LibModels.MiCasaClientes>()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error retrieving all clients: {ex.Message}");

                return new GetAllClientsResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Clients = new List<LibModels.MiCasaClientes>()
                };
            }
        }

        /// <summary>
        /// Retrieves all clients by MicrosoftOID
        /// </summary>
        /// <param name="microsoftOID">The MicrosoftOID to search for</param>
        /// <returns>List of MiCasaClientes for the specified MicrosoftOID</returns>
        public async Task<GetClientsByMicrosoftOIDResult> GetClientsByMicrosoftOIDARSAsync(string microsoftOIDRSA)
        {
            if (string.IsNullOrEmpty(microsoftOIDRSA))
            {
                return new GetClientsByMicrosoftOIDResult
                {
                    Success = false,
                    ErrorMessage = "MicrosoftOID cannot be null or empty",
                    Clients = new List<LibModels.MiCasaClientes>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query to get all clients by MicrosoftOID
                string query = "SELECT * FROM c WHERE c.microsoftOIDRSA = @microsoftOIDRSA";
                
                var queryDefinition = new QueryDefinition(query) 
                    .WithParameter("@microsoftOIDRSA", microsoftOIDRSA);

                using FeedIterator<LibModels.MiCasaClientes> feed = container.GetItemQueryIterator<LibModels.MiCasaClientes>(queryDefinition);

                var clients = new List<LibModels.MiCasaClientes>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<LibModels.MiCasaClientes> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (LibModels.MiCasaClientes client in response)
                    {
                        clients.Add(client);
                        count++;
                    }
                }

                if (clients.Count == 0)
                {
                    Console.WriteLine($"?? No clients found for MicrosoftOID: {microsoftOIDRSA}");
                    return new GetClientsByMicrosoftOIDResult
                    {
                        Success = true,
                        Clients = new List<LibModels.MiCasaClientes>(),
                        ClientCount = 0,
                        RUConsumed = totalRU,
                        Timestamp = DateTime.UtcNow,
                        Message = $"No clients found for MicrosoftOID '{microsoftOIDRSA}'"
                    };
                }

                Console.WriteLine($"? Retrieved {count} client(s) for MicrosoftOID: {microsoftOIDRSA}. RU consumed: {totalRU:F2}");

                return new GetClientsByMicrosoftOIDResult
                {
                    Success = true,
                    Clients = clients,
                    ClientCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                Console.WriteLine($"? {errorMessage}");

                return new GetClientsByMicrosoftOIDResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Clients = new List<LibModels.MiCasaClientes>()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error retrieving clients: {ex.Message}");

                return new GetClientsByMicrosoftOIDResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Clients = new List<LibModels.MiCasaClientes>()
                };
            }
        }

        /// <summary>
        /// Retrieves all clients for a specific TwinID with property summary information
        /// </summary>
        /// <param name="twinId">The TwinID to search for</param>
        /// <returns>List of client summaries with property details for the specified TwinID</returns>
        public async Task<GetClientsSummaryByTwinIdResult> GetClientsSummaryByTwinIdAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new GetClientsSummaryByTwinIdResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    ClientSummaries = new List<ClientSummary>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query to get client info and property details using aliases
                string query = @"SELECT c.id, c.nombreCliente, c.apellidoCliente, 
                    c.propiedad[0].id AS propiedadId, 
                    c.propiedad[0].tipoPropiedad, 
                    c.propiedad[0].tipoOperacion, 
                    c.propiedad[0].precio,
                    c.propiedad[0].moneda,
                    c.propiedad[0].direccion,
                    c.propiedad[0].descripcion,
                    c.propiedad[0].caracteristicas,
                    c.propiedad[0].amenidades,
                    c.propiedad[0].motivoVenta,
                    c.propiedad[0].urgencia,
                    c.propiedad[0].disponibilidad,
                    c.propiedad[0].estatus,
                    c.propiedad[0].fachadaURL,
                    c.propiedad[0].fachadaFileName,
                    c.AnalysisResult
                FROM c WHERE c.TwinID = @twinId";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@twinId", twinId);

                using FeedIterator<dynamic> feed = container.GetItemQueryIterator<dynamic>(queryDefinition);

                var clientSummaries = new List<ClientSummary>();
                double totalRU = 0;
                int count = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<dynamic> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (dynamic item in response)
                    {
                        var summary = new ClientSummary
                        {
                            Id = item.id,
                            NombreCliente = item.nombreCliente,
                            ApellidoCliente = item.apellidoCliente,
                            PropiedadId = item.propiedadId?.ToString(),
                            TipoPropiedad = item.tipoPropiedad?.ToString(),
                            TipoOperacion = item.tipoOperacion?.ToString(),
                            Precio = item.precio != null ? (decimal?)item.precio : null,
                            Moneda = item.moneda?.ToString() ?? "MXN",
                            Direccion = item.direccion != null ? JsonConvert.DeserializeObject<LibModels.Direccion>(JsonConvert.SerializeObject(item.direccion)) : null,
                            Descripcion = item.descripcion?.ToString() ?? string.Empty,
                            Caracteristicas = item.caracteristicas != null ? JsonConvert.DeserializeObject<LibModels.Caracteristicas>(JsonConvert.SerializeObject(item.caracteristicas)) : null,
                            Amenidades = item.amenidades != null ? JsonConvert.DeserializeObject<List<string>>(JsonConvert.SerializeObject(item.amenidades)) : new List<string>(),
                            MotivoVenta = item.motivoVenta?.ToString() ?? string.Empty,
                            Urgencia = item.urgencia?.ToString() ?? string.Empty,
                            Disponibilidad = item.disponibilidad?.ToString() ?? string.Empty,
                            Estatus = item.estatus?.ToString() ?? string.Empty,
                            FachadaFileName = item.fachadaFileName?.ToString() ?? string.Empty,
                            AnalysisResult = item.AnalysisResult != null ? new AnalysisResultSummary
                            { 
                                SumarioEjecutivo = item.AnalysisResult.sumarioEjecutivo?.ToString() ?? string.Empty
                            } : null
                        };

                        // Generate SAS URL for Fachada if filename exists
                        if (!string.IsNullOrEmpty(summary.FachadaFileName) && _configuration != null)
                        {
                            try
                            {
                                Console.WriteLine($"?? Generating SAS URL for Fachada: {summary.FachadaFileName}");
                                
                                // Create DataLakeClient factory
                                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                                var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                                
                                // Build full path: MiCasa/Fachada/fachadaFileName
                                const string fachadaPath = "MiCasa/Fachada";
                                var fullFilePath = $"{fachadaPath}/{summary.FachadaFileName}";
                                
                                // Generate SAS URL with 24-hour expiration
                                var fachadaUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(24));
                                
                                if (!string.IsNullOrEmpty(fachadaUrl))
                                {
                                    summary.FachadaURL = fachadaUrl;
                                    Console.WriteLine($"? SAS URL generated for {summary.FachadaFileName}");
                                }
                                else
                                {
                                    Console.WriteLine($"?? Failed to generate SAS URL for {summary.FachadaFileName}");
                                }
                            }
                            catch (Exception sasEx)
                            {
                                Console.WriteLine($"?? Error generating SAS URL for {summary.FachadaFileName}: {sasEx.Message}");
                                // Continue without SAS URL - don't fail the entire operation
                            }
                        }

                        clientSummaries.Add(summary);
                        count++;
                    }
                }

                Console.WriteLine($"? Retrieved {count} client summaries for TwinID: {twinId}. RU consumed: {totalRU:F2}");

                return new GetClientsSummaryByTwinIdResult
                {
                    Success = true,
                    ClientSummaries = clientSummaries,
                    ClientCount = count,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                Console.WriteLine($"? {errorMessage}");

                return new GetClientsSummaryByTwinIdResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    ClientSummaries = new List<ClientSummary>()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error retrieving client summaries: {ex.Message}");

                return new GetClientsSummaryByTwinIdResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ClientSummaries = new List<ClientSummary>()
                };
            }
        }

        /// <summary>
        /// Adiciona una propiedad (casa) a un cliente existente
        /// </summary>
        /// <param name="clientId">ID del cliente</param>
        /// <param name="twinId">TwinID del cliente</param>
        /// <param name="propiedad">Datos de la propiedad a adicionar</param>
        /// <returns>Success status y datos actualizados del cliente</returns>
        public async Task<AdicionaCasaResult> AdicionaCasaAsync(string clientId, string twinId, LibModels.Propiedad propiedad)
        {
            if (string.IsNullOrEmpty(clientId))
            {
                return new AdicionaCasaResult
                {
                    Success = false,
                    ErrorMessage = "Client ID cannot be null or empty",
                    ClientId = null
                };
            }

            if (string.IsNullOrEmpty(twinId))
            {
                return new AdicionaCasaResult
                {
                    Success = false,
                    ErrorMessage = "TwinID cannot be null or empty",
                    ClientId = null
                };
            }

            if (propiedad == null)
            {
                return new AdicionaCasaResult
                {
                    Success = false,
                    ErrorMessage = "Property data cannot be null",
                    ClientId = null
                };
            }

            try
            {
                // Validar configuración
                if (string.IsNullOrEmpty(_cosmosKey))
                {
                    return new AdicionaCasaResult
                    {
                        Success = false,
                        ErrorMessage = "MiCasa Cosmos DB is not configured. Please set MICASA_COSMOS_KEY environment variable.",
                        ClientId = null
                    };
                }

                await InitializeCosmosClientAsync();

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Recuperar el cliente existente
                string query = "SELECT * FROM c WHERE c.id = @clientId";
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@clientId", clientId);

                using FeedIterator<LibModels.MiCasaClientes> feed = container.GetItemQueryIterator<LibModels.MiCasaClientes>(queryDefinition);

                LibModels.MiCasaClientes cliente = null;
                while (feed.HasMoreResults)
                {
                    FeedResponse<LibModels.MiCasaClientes> response = await feed.ReadNextAsync();
                    if (response.Count > 0)
                    {
                        cliente = response.FirstOrDefault();
                        break;
                    }
                }

                if (cliente == null)
                {
                    Console.WriteLine($"?? Client not found: {clientId}");
                    return new AdicionaCasaResult
                    {
                        Success = false,
                        ErrorMessage = $"Client with ID '{clientId}' not found",
                        ClientId = null
                    };
                }

                // Verificar que el cliente pertenece al TwinID
                if (cliente.TwinID != twinId)
                {
                    Console.WriteLine($"?? Client {clientId} does not belong to TwinID {twinId}");
                    return new AdicionaCasaResult
                    {
                        Success = false,
                        ErrorMessage = $"Client with ID '{clientId}' does not belong to TwinID '{twinId}'",
                        ClientId = null
                    };
                }

                // Inicializar lista de propiedades si es necesario
                if (cliente.Propiedad == null)
                {
                    cliente.Propiedad = new List<LibModels.Propiedad>();
                }

                // Crear copia de la propiedad con metadata
                var propiedadRegistro = new LibModels.Propiedad
                {
                    Id = Guid.NewGuid().ToString(),
                    FechaRegistro = DateTime.UtcNow
                };

                // Copiar propiedades de la propiedad recibida
                JsonConvert.PopulateObject(JsonConvert.SerializeObject(propiedad), propiedadRegistro);

                // Procesando datos de dirección si existen
                if (propiedad.Direccion != null)
                {
                    Console.WriteLine($"? Property address data included: {propiedadRegistro.Direccion?.Completa}");
                }

                // Adicionar la propiedad a la lista
                cliente.Propiedad.Add(propiedadRegistro);

                // Actualizar el documento en Cosmos DB
                var updateResponse = await container.UpsertItemAsync(cliente, new PartitionKey(clientId));
 
                Console.WriteLine($"? Property added successfully to client {clientId}. Property ID: {propiedadRegistro.Id}");

                return new AdicionaCasaResult
                {
                    Success = true,
                    ClientId = clientId,
                    TwinId = twinId,
                    PropertyId = propiedadRegistro.Id,
                    Client = cliente,
                    RUConsumed = updateResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                Console.WriteLine($"? {errorMessage}");

                return new AdicionaCasaResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    ClientId = clientId
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error adding property to client: {ex.Message}");

                return new AdicionaCasaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ClientId = clientId
                };
            }
        }

        /// <summary>
        /// Updates a MiCasa client document in Cosmos DB with new data
        /// Allows updating client information after initial save
        /// </summary>
        /// <param name="documentId">Document ID to update</param>
        /// <param name="updateResult">Updated MiCasaClientes containing new information</param>
        /// <returns>Success status and updated document information</returns>
        public async Task<MiCasaSaveResult> UpdateMiCasaSaveResultAsync(string documentId, LibModels.MiCasaClientes updateResult)
        {
            if (string.IsNullOrEmpty(documentId))
            {
                return new MiCasaSaveResult
                {
                    Success = false,
                    ErrorMessage = "Document ID cannot be null or empty",
                    DocumentId = null
                };
            }

            if (updateResult == null)
            {
                return new MiCasaSaveResult
                {
                    Success = false,
                    ErrorMessage = "Update result cannot be null",
                    DocumentId = null
                };
            }

            try
            {
                // Validate configuration before trying to initialize
                if (string.IsNullOrEmpty(_cosmosKey))
                {
                    return new MiCasaSaveResult
                    {
                        Success = false,
                        ErrorMessage = "MiCasa Cosmos DB is not configured. Please set MICASA_COSMOS_KEY environment variable.",
                        DocumentId = null
                    };
                }

                await InitializeCosmosClientAsync();

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Ensure document has correct ID
                updateResult.Id = documentId;
                
                // Update metadata
                updateResult.FechaActualizacion = DateTime.UtcNow;

                // Upsert document directly in Cosmos DB
                var response = await container.UpsertItemAsync(updateResult);

                Console.WriteLine($"? Document updated successfully. Document ID: {documentId}");

                return new MiCasaSaveResult
                {
                    Success = true,
                    DocumentId = documentId,
                    RUConsumed = response.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                Console.WriteLine($"? {errorMessage}");

                return new MiCasaSaveResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    DocumentId = documentId
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error updating document: {ex.Message}");

                return new MiCasaSaveResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DocumentId = documentId
                };
            }
        }

        /// <summary>
        /// Retrieves analysis summary (sumarioEjecutivo) for a specific client property
        /// Extracts only the executive summary from the analysis result in flat structure
        /// </summary>
        /// <param name="clientId">The client ID to search for</param>
        /// <returns>Client ID and sumarioEjecutivo flattened</returns>
        public async Task<GetAnalysisSummaryResult> GetAnalysisSummaryByClientIdAsync(string clientId)
        {
            if (string.IsNullOrEmpty(clientId))
            {
                return new GetAnalysisSummaryResult
                {
                    Success = false,
                    ErrorMessage = "Client ID cannot be null or empty",
                    Summaries = new List<AnalysisSummary>()
                };
            }

            try
            {
                await InitializeCosmosClientAsync();

                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                // Query to extract only analysisResult.sumarioEjecutivo
                string query = @"SELECT c.id, c.analysisResult.sumarioEjecutivo AS sumarioEjecutivo
                FROM c WHERE c.id = @clientId";
                
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@clientId", clientId);

                using FeedIterator<dynamic> feed = container.GetItemQueryIterator<dynamic>(queryDefinition);

                var summaries = new List<AnalysisSummary>();
                double totalRU = 0;

                while (feed.HasMoreResults)
                {
                    FeedResponse<dynamic> response = await feed.ReadNextAsync();
                    totalRU += response.RequestCharge;

                    foreach (dynamic item in response)
                    {
                        var summary = new AnalysisSummary
                        {
                            Id = item.id,
                            SumarioEjecutivo = item.sumarioEjecutivo?.ToString() ?? string.Empty
                        };
                        summaries.Add(summary);
                    }
                }

                if (summaries.Count == 0)
                {
                    Console.WriteLine($"?? No analysis summary found for Client ID: {clientId}");
                    return new GetAnalysisSummaryResult
                    {
                        Success = true,
                        Message = $"No analysis summary found for client '{clientId}'",
                        Summaries = new List<AnalysisSummary>(),
                        RUConsumed = totalRU,
                        Timestamp = DateTime.UtcNow
                    };
                }

                Console.WriteLine($"? Retrieved analysis summary for Client ID: {clientId}. RU consumed: {totalRU:F2}");

                return new GetAnalysisSummaryResult
                {
                    Success = true,
                    Message = $"Analysis summary retrieved successfully",
                    Summaries = summaries,
                    RUConsumed = totalRU,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                Console.WriteLine($"? {errorMessage}");

                return new GetAnalysisSummaryResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    Summaries = new List<AnalysisSummary>()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error retrieving analysis summary: {ex.Message}");

                return new GetAnalysisSummaryResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Summaries = new List<AnalysisSummary>()
                };
            }
        }

        /// <summary>
        /// Updates a propiedad (property) for an existing client
        /// </summary>
        /// <param name="clientId">ID del cliente</param>
        /// <param name="twinId">TwinID del cliente</param>
        /// <param name="propiedadId">ID de la propiedad a actualizar</param>
        /// <param name="propiedadActualizada">Datos actualizados de la propiedad</param>
        /// <returns>Success status y datos actualizados de la propiedad</returns>
        public async Task<UpdatePropiedadResult> UpdatePropiedadAsync(string clientId, string twinId, string propiedadId, LibModels.Propiedad propiedadActualizada)
        {
            if (string.IsNullOrEmpty(clientId))
                return new UpdatePropiedadResult { Success = false, ErrorMessage = "Client ID cannot be null or empty", ClientId = null };
            
            if (string.IsNullOrEmpty(twinId))
                return new UpdatePropiedadResult { Success = false, ErrorMessage = "TwinID cannot be null or empty", ClientId = null };
            
            if (string.IsNullOrEmpty(propiedadId))
                return new UpdatePropiedadResult { Success = false, ErrorMessage = "Property ID cannot be null or empty", ClientId = null };
            
            if (propiedadActualizada == null)
                return new UpdatePropiedadResult { Success = false, ErrorMessage = "Property data cannot be null", ClientId = null };

            try
            {
                if (string.IsNullOrEmpty(_cosmosKey))
                    return new UpdatePropiedadResult { Success = false, ErrorMessage = "MiCasa Cosmos DB is not configured.", ClientId = null };

                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT * FROM c WHERE c.id = @clientId";
                var queryDefinition = new QueryDefinition(query).WithParameter("@clientId", clientId);

                using FeedIterator<LibModels.MiCasaClientes> feed = container.GetItemQueryIterator<LibModels.MiCasaClientes>(queryDefinition);

                LibModels.MiCasaClientes cliente = null;
                while (feed.HasMoreResults)
                {
                    FeedResponse<LibModels.MiCasaClientes> response = await feed.ReadNextAsync();
                    if (response.Count > 0)
                    {
                        cliente = response.FirstOrDefault();
                        break;
                    }
                }

                if (cliente == null)
                    return new UpdatePropiedadResult { Success = false, ErrorMessage = $"Client with ID '{clientId}' not found", ClientId = null };

                if (cliente.TwinID != twinId)
                    return new UpdatePropiedadResult { Success = false, ErrorMessage = $"Client does not belong to TwinID '{twinId}'", ClientId = null };

                if (cliente.Propiedad == null || cliente.Propiedad.Count == 0)
                    return new UpdatePropiedadResult { Success = false, ErrorMessage = $"Client has no properties", ClientId = null };

                var propiedadExistente = cliente.Propiedad.FirstOrDefault(p => p.Id == propiedadId);
                if (propiedadExistente == null)
                    return new UpdatePropiedadResult { Success = false, ErrorMessage = $"Property with ID '{propiedadId}' not found", ClientId = clientId };

                propiedadActualizada.Id = propiedadId;
                propiedadActualizada.FechaActualizacion = DateTime.UtcNow;

                var indexProp = cliente.Propiedad.FindIndex(p => p.Id == propiedadId);
                cliente.Propiedad[indexProp] = propiedadActualizada;
                cliente.FechaActualizacion = DateTime.UtcNow;

                var updateResponse = await container.UpsertItemAsync(cliente);

                Console.WriteLine($"? Property updated successfully for client {clientId}. Property ID: {propiedadId}");

                return new UpdatePropiedadResult
                {
                    Success = true,
                    ClientId = clientId,
                    TwinId = twinId,
                    PropertyId = propiedadId,
                    Propiedad = propiedadActualizada,
                    RUConsumed = updateResponse.RequestCharge,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (CosmosException cosmosEx)
            {
                string errorMessage = $"Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})";
                Console.WriteLine($"? {errorMessage}");
                return new UpdatePropiedadResult { Success = false, ErrorMessage = errorMessage, ClientId = clientId };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error updating property: {ex.Message}");
                return new UpdatePropiedadResult { Success = false, ErrorMessage = ex.Message, ClientId = clientId };
            }
        }

        /// <summary>
        /// Retrieves a specific property by its ID from a client document
        /// Uses Cosmos DB JOIN to unnest the propiedad array and filter by property ID
        /// </summary>
        public async Task<GetPropertyByIdResult> GetPropertyByIdAsync(string clientId, string propiedadId)
        {
            if (string.IsNullOrEmpty(clientId))
                return new GetPropertyByIdResult { Success = false, ErrorMessage = "Client ID cannot be null or empty", Propiedad = null };

            if (string.IsNullOrEmpty(propiedadId))
                return new GetPropertyByIdResult { Success = false, ErrorMessage = "Property ID cannot be null or empty", Propiedad = null };

            try
            {
                await InitializeCosmosClientAsync();
                var container = _cosmosClient.GetContainer(_databaseName, _containerName);

                string query = "SELECT p FROM c JOIN p IN c.propiedad WHERE c.id = @clientId AND p.id = @propiedadId";
                var queryDefinition = new QueryDefinition(query)
                    .WithParameter("@clientId", clientId)
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
                    Console.WriteLine($"?? Property not found: clientId={clientId}, propiedadId={propiedadId}");
                    return new GetPropertyByIdResult { Success = false, ErrorMessage = $"Property with ID '{propiedadId}' not found in client '{clientId}'", Propiedad = null };
                }

                Console.WriteLine($"? Retrieved property: clientId={clientId}, propiedadId={propiedadId}. RU consumed: {totalRU:F2}");
                return new GetPropertyByIdResult { Success = true, ClientId = clientId, Propiedad = propiedad, RUConsumed = totalRU, Timestamp = DateTime.UtcNow };
            }
            catch (CosmosException cosmosEx)
            {
                Console.WriteLine($"? Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})");
                return new GetPropertyByIdResult { Success = false, ErrorMessage = $"Cosmos DB error: {cosmosEx.Message}", Propiedad = null };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error retrieving property: {ex.Message}");
                return new GetPropertyByIdResult { Success = false, ErrorMessage = ex.Message, Propiedad = null };
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
    /// Result of saving a MiCasa client to Cosmos DB
    /// </summary>
    public class MiCasaSaveResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string DocumentId { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of retrieving a client by ID
    /// </summary>
    public class GetClientByIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public LibModels.MiCasaClientes? Client { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of retrieving clients by TwinID
    /// </summary>
    public class GetClientsByTwinIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<LibModels.MiCasaClientes> Clients { get; set; } = new();
        public int ClientCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of retrieving all clients from the container
    /// </summary>
    public class GetAllClientsResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<LibModels.MiCasaClientes> Clients { get; set; } = new();
        public int ClientCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of retrieving a client by TwinID and MicrosoftOID
    /// </summary>
    public class GetClientByTwinIdAndMicrosoftOIDResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public LibModels.MiCasaClientes? Client { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of retrieving clients by MicrosoftOID
    /// </summary>
    public class GetClientsByMicrosoftOIDResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Message { get; set; } = "";
        public List<LibModels.MiCasaClientes> Clients { get; set; } = new();
        public int ClientCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Summary of a MiCasa client with property information
    /// </summary>
    public class ClientSummary
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("nombreCliente")]
        public string NombreCliente { get; set; } = string.Empty;

        [JsonProperty("apellidoCliente")]
        public string ApellidoCliente { get; set; } = string.Empty;

        [JsonProperty("propiedadId")]
        public string PropiedadId { get; set; } = string.Empty;

        [JsonProperty("tipoPropiedad")]
        public string TipoPropiedad { get; set; } = string.Empty;

        [JsonProperty("tipoOperacion")]
        public string TipoOperacion { get; set; } = string.Empty;

        [JsonProperty("precio")]
        public decimal? Precio { get; set; }

        [JsonProperty("moneda")]
        public string Moneda { get; set; } = "MXN";

        [JsonProperty("direccion")]
        public LibModels.Direccion Direccion { get; set; }

        [JsonProperty("descripcion")]
        public string Descripcion { get; set; } = string.Empty;

        [JsonProperty("caracteristicas")]
        public LibModels.Caracteristicas Caracteristicas { get; set; }

        [JsonProperty("amenidades")]
        public List<string> Amenidades { get; set; } = new List<string>();

        [JsonProperty("motivoVenta")]
        public string MotivoVenta { get; set; } = string.Empty;

        [JsonProperty("urgencia")]
        public string Urgencia { get; set; } = string.Empty;

        [JsonProperty("disponibilidad")]
        public string Disponibilidad { get; set; } = string.Empty;

        [JsonProperty("estatus")]
        public string Estatus { get; set; } = string.Empty;

        [JsonProperty("fachadaFileName")]
        public string FachadaFileName { get; set; } = string.Empty;

        [JsonProperty("fachadaURL")]
        public string FachadaURL { get; set; } = string.Empty;

        [JsonProperty("analysisResult")]
        public AnalysisResultSummary AnalysisResult { get; set; }
    }

    /// <summary>
    /// Summary of analysis result containing only the executive summary
    /// </summary>
    public class AnalysisResultSummary
    {
        [JsonProperty("sumarioEjecutivo")]
        public string SumarioEjecutivo { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of retrieving client summaries by TwinID
    /// </summary>
    public class GetClientsSummaryByTwinIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<ClientSummary> ClientSummaries { get; set; } = new();
        public int ClientCount { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of adding a property (casa) to a client
    /// </summary>
    public class AdicionaCasaResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string ClientId { get; set; }
        public string TwinId { get; set; }
        public string PropertyId { get; set; }
        public LibModels.MiCasaClientes? Client { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Flat structure for analysis summary (only sumarioEjecutivo)
    /// </summary>
    public class AnalysisSummary
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("sumarioEjecutivo")]
        public string SumarioEjecutivo { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of retrieving analysis summary by client ID
    /// </summary>
    public class GetAnalysisSummaryResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string Message { get; set; } = "";
        public List<AnalysisSummary> Summaries { get; set; } = new();
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of updating a property for a client
    /// </summary>
    public class UpdatePropiedadResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string ClientId { get; set; }
        public string TwinId { get; set; }
        public string PropertyId { get; set; }
        public LibModels.Propiedad Propiedad { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of retrieving a specific property by ID from a client document
    /// </summary>
    public class GetPropertyByIdResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string ClientId { get; set; } = "";
        public LibModels.Propiedad? Propiedad { get; set; }
        public double RUConsumed { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

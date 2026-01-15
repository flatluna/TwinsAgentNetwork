using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text.Json;
using TwinAgentsNetwork.Services;
using TwinAgentsNetwork.Models;
using TwinAgentsNetwork.Agents;
using Newtonsoft.Json;
using TwinAgentsLibrary.Models;

namespace TwinAgentsNetwork.AzureFunctions
{
    public class AgentMiCasaClienteFx
    {
        private readonly ILogger<AgentMiCasaClienteFx> _logger;
        private readonly AgentTwinMiCasaCosmosDB _miCasaCosmosDB;
        private readonly IConfiguration _configuration;

        public AgentMiCasaClienteFx(ILogger<AgentMiCasaClienteFx> logger, AgentTwinMiCasaCosmosDB miCasaCosmosDB, IConfiguration configuration)
        {
            _logger = logger;
            _miCasaCosmosDB = miCasaCosmosDB;
            _configuration = configuration;
        }

        [Function("SaveMiCasaClient")]
        public async Task<HttpResponseData> SaveMiCasaClient(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "micasa/clients/save")] HttpRequestData req)
        {
            _logger.LogInformation("🏠 SaveMiCasaClient function triggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Request body is required"
                    }));
                    return badResponse;
                }

                // Deserialize to MiCasaClientes object
                var clientData = JsonConvert.DeserializeObject<MiCasaClientes>(requestBody);

                if (clientData == null)
                {
                    _logger.LogError("❌ Invalid JSON format for MiCasaClientes");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Invalid JSON format for MiCasaClientes"
                    }));
                    return badResponse;
                }

                // Validate required fields
                if (string.IsNullOrEmpty(clientData.NombreCliente) || string.IsNullOrEmpty(clientData.TwinID))
                {
                    _logger.LogError("❌ Missing required fields: NombreCliente and TwinID are required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Missing required fields: NombreCliente and TwinID are required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation($"💾 Saving MiCasa client to Cosmos DB - Cliente: {clientData.NombreCliente}, TwinID: {clientData.TwinID}");

                clientData.TwinID = clientData.MicrosoftOID;
                // Save to Cosmos DB
                var result = await _miCasaCosmosDB.SaveClientWithPropertyAsync(clientData);

                if (!result.Success)
                {
                    _logger.LogError($"❌ Error saving client: {result.ErrorMessage}");
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = result.ErrorMessage
                    }));
                    return errorResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = true,
                    documentId = result.DocumentId,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp,
                    message = "Client saved successfully"
                }, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                _logger.LogInformation($"✅ Client saved successfully. Document ID: {result.DocumentId}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error in SaveMiCasaClient: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while saving client"
                }));
                return errorResponse;
            }
        }

        [Function("SaveMiCasaClientOptions")]
        public async Task<HttpResponseData> SaveMiCasaClientOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "micasa/clients/save")] HttpRequestData req)
        {
            _logger.LogInformation("✅ CORS preflight request handled");
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, req);
            return response;
        }

        /// <summary>
        /// Updates an existing MiCasa client in Cosmos DB
        /// PUT /api/micasa/clients/{clientId}
        /// </summary>
        [Function("UpdateMiCasaClient")]
        public async Task<HttpResponseData> UpdateMiCasaClient(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "micasa/clients/{clientId}")] HttpRequestData req,
            string clientId)
        {
            _logger.LogInformation("🏠 UpdateMiCasaClient function triggered for ClientID: {ClientId}", clientId);

            try
            {
                if (string.IsNullOrEmpty(clientId))
                {
                    _logger.LogError("❌ ClientID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "ClientID parameter is required"
                    }));
                    return badResponse;
                }

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Request body is required"
                    }));
                    return badResponse;
                }

                // Deserialize to MiCasaClientes object
                var clientData = JsonConvert.DeserializeObject<MiCasaClientes>(requestBody);

                if (clientData == null)
                {
                    _logger.LogError("❌ Invalid JSON format for MiCasaClientes");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Invalid JSON format for MiCasaClientes"
                    }));
                    return badResponse;
                }

                // Validate required fields
                if (string.IsNullOrEmpty(clientData.NombreCliente) || string.IsNullOrEmpty(clientData.TwinID))
                {
                    _logger.LogError("❌ Missing required fields: NombreCliente and TwinID are required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Missing required fields: NombreCliente and TwinID are required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("💾 Updating MiCasa client in Cosmos DB - ClientID: {ClientId}, Cliente: {Cliente}, TwinID: {TwinId}", 
                    clientId, clientData.NombreCliente, clientData.TwinID);

                // Update in Cosmos DB
                var result = await _miCasaCosmosDB.UpdateMiCasaSaveResultAsync(clientId, clientData);

                if (!result.Success)
                {
                    _logger.LogError("❌ Error updating client: {Error}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = result.ErrorMessage
                    }));
                    return errorResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = true,
                    clientId = clientId,
                    documentId = result.DocumentId,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp,
                    message = "Client updated successfully"
                }, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                _logger.LogInformation("✅ Client updated successfully. Document ID: {DocumentId}", result.DocumentId);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in UpdateMiCasaClient: {Message}", ex.Message);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while updating client"
                }));
                return errorResponse;
            }
        }

        /// <summary>
        /// CORS preflight for UpdateMiCasaClient
        /// </summary>
        [Function("UpdateMiCasaClientOptions")]
        public async Task<HttpResponseData> UpdateMiCasaClientOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "micasa/clients/{clientId}")] HttpRequestData req,
            string clientId)
        {
            _logger.LogInformation("✅ CORS preflight request handled for UpdateMiCasaClient");
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, req);
            return response;
        }

        [Function("GetMiCasaClientsByTwinId")]
        public async Task<HttpResponseData> GetMiCasaClientsByTwinId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "micasa/clients/twin/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"🏠 GetMiCasaClientsByTwinId function triggered for TwinID: {twinId}");

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ TwinID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "TwinID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation($"🔍 Retrieving clients for TwinID: {twinId}");

                // Get clients from Cosmos DB
                var result = await _miCasaCosmosDB.GetClientsByTwinIdAsync(twinId);

                if (!result.Success)
                {
                    _logger.LogError($"❌ Error retrieving clients: {result.ErrorMessage}");
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = result.ErrorMessage
                    }));
                    return errorResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = true,
                    twinId = twinId,
                    clientCount = result.ClientCount,
                    clients = result.Clients,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp,
                    message = $"Retrieved {result.ClientCount} clients successfully"
                }, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                _logger.LogInformation($"✅ Retrieved {result.ClientCount} clients for TwinID: {twinId}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error in GetMiCasaClientsByTwinId: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while retrieving clients"
                }));
                return errorResponse;
            }
        }

        [Function("GetMiCasaClientsByTwinIdOptions")]
        public async Task<HttpResponseData> GetMiCasaClientsByTwinIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "micasa/clients/twin/{twinId}")] HttpRequestData req)
        {
            _logger.LogInformation("✅ CORS preflight request handled for GetMiCasaClientsByTwinId");
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, req);
            return response;
        }

        /// <summary>
        /// Retrieves all MiCasa clients from the Cosmos DB container without any restrictions
        /// GET /api/micasa/clients/all
        /// </summary>
        [Function("GetAllMiCasaClients")]
        public async Task<HttpResponseData> GetAllMiCasaClients(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "micasa/clients/all")] HttpRequestData req)
        {
            _logger.LogInformation("🏠 GetAllMiCasaClients function triggered");

            try
            {
                _logger.LogInformation("🔍 Retrieving all clients from container");

                // Get all clients from Cosmos DB
                var result = await _miCasaCosmosDB.GetAllClientsAsync();

                if (!result.Success)
                {
                    _logger.LogError("❌ Error retrieving all clients: {ErrorMessage}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = result.ErrorMessage
                    }));
                    return errorResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = true,
                    clientCount = result.ClientCount,
                    clients = result.Clients,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp,
                    message = $"Retrieved {result.ClientCount} clients successfully"
                }, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                _logger.LogInformation("✅ Retrieved {ClientCount} total clients from container", result.ClientCount);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in GetAllMiCasaClients: {Message}", ex.Message);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while retrieving all clients"
                }));
                return errorResponse;
            }
        }

        /// <summary>
        /// CORS preflight for GetAllMiCasaClients
        /// </summary>
        [Function("GetAllMiCasaClientsOptions")]
        public async Task<HttpResponseData> GetAllMiCasaClientsOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "micasa/clients/all")] HttpRequestData req)
        {
            _logger.LogInformation("✅ CORS preflight request handled for GetAllMiCasaClients");
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, req);
            return response;
        }

        /// <summary>
        /// Azure Function para obtener clientes por MicrosoftOID
        /// GET /api/micasa/clients/microsoft-oid/{microsoftOID}
        /// </summary>
        [Function("GetMiCasaClientsByMicrosoftOID")]
        public async Task<HttpResponseData> GetMiCasaClientsByMicrosoftOID(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "micasa/clients/microsoft-oid/{microsoftOID}")] HttpRequestData req,
            string microsoftOID)
        {
            _logger.LogInformation("🏠 GetMiCasaClientsByMicrosoftOID function triggered. MicrosoftOID: {MicrosoftOID}", microsoftOID);

            try
            {
                if (string.IsNullOrEmpty(microsoftOID))
                {
                    _logger.LogError("❌ MicrosoftOID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "MicrosoftOID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🔍 Retrieving clients for MicrosoftOID: {MicrosoftOID}", microsoftOID);

                // Get clients from Cosmos DB
                var result = await _miCasaCosmosDB.GetClientsByMicrosoftOIDARSAsync(microsoftOID);

                if (!result.Success)
                {
                    _logger.LogError("❌ Error retrieving clients: {ErrorMessage}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = result.ErrorMessage
                    }));
                    return errorResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = true,
                    microsoftOID = microsoftOID,
                    clientCount = result.ClientCount,
                    clients = result.Clients,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp,
                    message = result.ClientCount > 0 
                        ? $"Retrieved {result.ClientCount} client(s) successfully" 
                        : result.Message
                }, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                _logger.LogInformation("✅ Retrieved {ClientCount} client(s) for MicrosoftOID: {MicrosoftOID}", result.ClientCount, microsoftOID);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in GetMiCasaClientsByMicrosoftOID: {Message}", ex.Message);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while retrieving the clients"
                }));
                return errorResponse;
            }
        }

        /// <summary>
        /// CORS preflight for GetMiCasaClientsByMicrosoftOID
        /// </summary>
        [Function("GetMiCasaClientsByMicrosoftOIDOptions")]
        public async Task<HttpResponseData> GetMiCasaClientsByMicrosoftOIDOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "micasa/clients/microsoft-oid/{microsoftOID}")] HttpRequestData req,
            string microsoftOID)
        {
            _logger.LogInformation("✅ CORS preflight request handled for GetMiCasaClientsByMicrosoftOID");
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, req);
            return response;
        }

        [Function("GetMiCasaClientsSummaryByTwinId")]
        public async Task<HttpResponseData> GetMiCasaClientsSummaryByTwinId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "micasa/clients/twin/{twinId}/summary")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation($"🏠 GetMiCasaClientsSummaryByTwinId function triggered for TwinID: {twinId}");

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ TwinID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "TwinID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation($"🔍 Retrieving client summaries for TwinID: {twinId}");

                // Get client summaries from Cosmos DB
                var result = await _miCasaCosmosDB.GetClientsSummaryByTwinIdAsync(twinId);

                if (!result.Success)
                {
                    _logger.LogError($"❌ Error retrieving client summaries: {result.ErrorMessage}");
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = result.ErrorMessage
                    }));
                    return errorResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = true,
                    twinId = twinId,
                    clientCount = result.ClientCount,
                    clientSummaries = result.ClientSummaries,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp,
                    message = $"Retrieved {result.ClientCount} client summaries successfully"
                }, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                _logger.LogInformation($"✅ Retrieved {result.ClientCount} client summaries for TwinID: {twinId}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error in GetMiCasaClientsSummaryByTwinId: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while retrieving client summaries"
                }));
                return errorResponse;
            }
        }

        [Function("GetMiCasaClientsSummaryByTwinIdOptions")]
        public async Task<HttpResponseData> GetMiCasaClientsSummaryByTwinIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "micasa/clients/twin/{twinId}/summary")] HttpRequestData req)
        {
            _logger.LogInformation("✅ CORS preflight request handled for GetMiCasaClientsSummaryByTwinId");
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, req);
            return response;
        }

        [Function("GetMiCasaClientById")]
        public async Task<HttpResponseData> GetMiCasaClientById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "micasa/clients/{clientId}")] HttpRequestData req,
            string clientId)
        {
            _logger.LogInformation($"🏠 GetMiCasaClientById function triggered for ClientID: {clientId}");

            try
            {
                if (string.IsNullOrEmpty(clientId))
                {
                    _logger.LogError("❌ ClientID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "ClientID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation($"🔍 Retrieving client with ID: {clientId}");

                // Get client from Cosmos DB
                var result = await _miCasaCosmosDB.GetClientByIdAsync(clientId);

                if (!result.Success)
                {
                    _logger.LogWarning($"⚠️ Client not found: {clientId}");
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = result.ErrorMessage
                    }));
                    return notFoundResponse;
                }

                // Generate SAS URL for Fachada from MiCasaFotosIndex if TwinID exists
                if (result.Client != null && !string.IsNullOrEmpty(result.Client.TwinID))
                {
                    try
                    {
                        _logger.LogInformation($"📸 Searching for Fachada photo in MiCasaFotosIndex for TwinID: {result.Client.TwinID}");
                        
                        // Create MiCasaFotosIndex service
                        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                        var miCasaFotosIndex = new MiCasaFotosIndex(
                            loggerFactory.CreateLogger<MiCasaFotosIndex>(),
                            _configuration);

                        // Get all properties to find casaId values
                        if (result.Client.Propiedad != null && result.Client.Propiedad.Count > 0)
                        {
                            // Get the first property to use its casaId (or we could iterate through all)
                            var firstProperty = result.Client.Propiedad.FirstOrDefault();
                            if (firstProperty != null && !string.IsNullOrEmpty(result.Client.Id))
                            {
                                // Search in MiCasaFotosIndex for Fachada images`
                                // We need to search all photos for this Twin and look for tipoSeccion = "Fachada"
                                var photosResult = await miCasaFotosIndex.GetPhotosByCasaIdAsync(result.Client.Id, result.Client.TwinID);

                                if (photosResult.Success && photosResult.Documents.Count > 0)
                                {
                                    // Find the Fachada photo
                                    var fachadaPhoto = photosResult.Documents.FirstOrDefault(d => 
                                        d.TipoSeccion?.Equals("Fachada", StringComparison.OrdinalIgnoreCase) == true);

                                    if (fachadaPhoto != null && !string.IsNullOrEmpty(fachadaPhoto.FilePath) && !string.IsNullOrEmpty(fachadaPhoto.FileName))
                                    {
                                        _logger.LogInformation($"📸 Found Fachada photo: {fachadaPhoto.FileName}");
                                        
                                        try
                                        {
                                            // Create DataLakeClient factory
                                            var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                                            var dataLakeClient = dataLakeFactory.CreateClient(result.Client.TwinID);
                                            
                                            // Build full path from filePath and fileName
                                            var fullFilePath = $"{fachadaPhoto.FilePath}/{fachadaPhoto.FileName}";
                                            
                                            // Generate SAS URL with 24-hour expiration
                                            var fachadaUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(24));
                                            
                                            if (!string.IsNullOrEmpty(fachadaUrl))
                                            {
                                                result.Client.FachadaURL = fachadaUrl;
                                                _logger.LogInformation($"✅ SAS URL generated successfully for Fachada: {fachadaUrl.Substring(0, Math.Min(100, fachadaUrl.Length))}...");
                                            }
                                            else
                                            {
                                                _logger.LogWarning($"⚠️ Failed to generate SAS URL for Fachada - returned empty");
                                            }
                                        }
                                        catch (Exception sasEx)
                                        {
                                            _logger.LogWarning(sasEx, $"⚠️ Error generating SAS URL for Fachada: {sasEx.Message}");
                                        }
                                    }
                                    else
                                    {
                                        _logger.LogInformation($"ℹ️ No Fachada photo found in MiCasaFotosIndex for this property");
                                    }
                                }
                                else
                                {
                                    _logger.LogInformation($"ℹ️ No photos found in index for property: {firstProperty.Id}");
                                }
                            }
                        }
                        else
                        {
                            _logger.LogInformation($"ℹ️ Client has no properties associated");
                        }
                    }
                    catch (Exception indexEx)
                    {
                        _logger.LogWarning(indexEx, $"⚠️ Error searching MiCasaFotosIndex for Fachada: {indexEx.Message}");
                        // Continue without SAS URL - don't fail the entire operation
                    }
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = true,
                    clientId = clientId,
                    client = result.Client,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp,
                    message = "Client retrieved successfully"
                }, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                _logger.LogInformation($"✅ Retrieved client: {clientId}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error in GetMiCasaClientById: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while retrieving the client"
                }));
                return errorResponse;
            }
        }

        [Function("GetMiCasaClientByIdOptions")]
        public async Task<HttpResponseData> GetMiCasaClientByIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "micasa/clients/{clientId}")] HttpRequestData req)
        {
            _logger.LogInformation("✅ CORS preflight request handled for GetMiCasaClientById");
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, req);
            return response;
        }

        /// <summary>
        /// Retrieves a specific property by client ID and property ID
        /// GET /api/micasa/clients/{clientId}/properties/{propertyId}
        /// </summary>
        [Function("GetPropertyById")]
        public async Task<HttpResponseData> GetPropertyById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "micasa/clients/{clientId}/properties/{propertyId}")] HttpRequestData req,
            string clientId,
            string propertyId)
        {
            _logger.LogInformation("🏠 GetPropertyById function triggered for ClientID: {ClientId}, PropertyID: {PropertyId}", clientId, propertyId);

            try
            {
                if (string.IsNullOrEmpty(clientId))
                {
                    _logger.LogError("❌ ClientID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "ClientID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(propertyId))
                {
                    _logger.LogError("❌ PropertyID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "PropertyID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🔍 Retrieving property {PropertyId} for client {ClientId}", propertyId, clientId);

                // Get property from Cosmos DB
                var result = await _miCasaCosmosDB.GetPropertyByIdAsync(clientId, propertyId);

                if (!result.Success)
                {
                    _logger.LogWarning("⚠️ Property not found: ClientID={ClientId}, PropertyID={PropertyId}", clientId, propertyId);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = result.ErrorMessage
                    }));
                    return notFoundResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = true,
                    clientId = result.ClientId,
                    propertyId = propertyId,
                    propiedad = result.Propiedad,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp,
                    message = "Property retrieved successfully"
                }, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                _logger.LogInformation("✅ Retrieved property: ClientID={ClientId}, PropertyID={PropertyId}", clientId, propertyId);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in GetPropertyById: {Message}", ex.Message);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while retrieving the property"
                }));
                return errorResponse;
            }
        }

        /// <summary>
        /// CORS preflight for GetPropertyById
        /// </summary>
        [Function("GetPropertyByIdOptions")]
        public async Task<HttpResponseData> GetPropertyByIdOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "micasa/clients/{clientId}/properties/{propertyId}")] HttpRequestData req)
        {
            _logger.LogInformation("✅ CORS preflight request handled for GetPropertyById");
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, req);
            return response;
        }

        [Function("AddPropertyToClient")]
        public async Task<HttpResponseData> AddPropertyToClient(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "micasa/clients/{clientId}/properties")] HttpRequestData req,
            string clientId)
        {
            _logger.LogInformation($"🏠 AddPropertyToClient function triggered for ClientID: {clientId}");

            try
            {
                if (string.IsNullOrEmpty(clientId))
                {
                    _logger.LogError("❌ ClientID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "ClientID parameter is required"
                    }));
                    return badResponse;
                }

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Request body is required"
                    }));
                    return badResponse;
                }

                // Parse request body
                var requestData = System.Text.Json.JsonSerializer.Deserialize<AddPropertyRequest>(requestBody, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (requestData == null || string.IsNullOrEmpty(requestData.TwinId) || requestData.Propiedad == null)
                {
                    _logger.LogError("❌ Invalid request format or TwinID/Propiedad missing");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "TwinID and property data are required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation($"🏠 Adding property to Client: {clientId}, TwinID: {requestData.TwinId}");

                // Convertir dynamic a Propiedad
                var propiedad = JsonConvert.DeserializeObject<Propiedad>(JsonConvert.SerializeObject(requestData.Propiedad));

                // Call service to add property
                var result = await _miCasaCosmosDB.AdicionaCasaAsync(clientId, requestData.TwinId, propiedad);

                if (!result.Success)
                {
                    _logger.LogError($"❌ Error adding property: {result.ErrorMessage}");
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = result.ErrorMessage
                    }));
                    return errorResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = true,
                    clientId = result.ClientId,
                    twinId = result.TwinId,
                    propertyId = result.PropertyId,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp,
                    message = $"Property added successfully to client '{clientId}'"
                }, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                _logger.LogInformation($"✅ Property added successfully. Property ID: {result.PropertyId}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error in AddPropertyToClient: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while adding property to client"
                }));
                return errorResponse;
            }
        }

        [Function("AddPropertyToClientOptions")]
        public async Task<HttpResponseData> AddPropertyToClientOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "micasa/clients/{clientId}/properties")] HttpRequestData req)
        {
            _logger.LogInformation("✅ CORS preflight request handled for AddPropertyToClient");
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, req);
            return response;
        }

        [Function("UpdatePropertyInClient")]
        public async Task<HttpResponseData> UpdatePropertyInClient(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "micasa/clients/{clientId}/properties/{propiedadId}")] HttpRequestData req,
            string clientId,
            string propiedadId)
        {
            _logger.LogInformation($"🏠 UpdatePropertyInClient function triggered for ClientID: {clientId}, PropertyID: {propiedadId}");

            try
            {
                if (string.IsNullOrEmpty(clientId))
                {
                    _logger.LogError("❌ ClientID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "ClientID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(propiedadId))
                {
                    _logger.LogError("❌ PropertyID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "PropertyID parameter is required"
                    }));
                    return badResponse;
                }

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Request body is required"
                    }));
                    return badResponse;
                }

                // Parse request body
                var requestData = System.Text.Json.JsonSerializer.Deserialize<UpdatePropertyRequest>(requestBody, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (requestData == null || string.IsNullOrEmpty(requestData.TwinId) || requestData.Propiedad == null)
                {
                    _logger.LogError("❌ Invalid request format or TwinID/Propiedad missing");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "TwinID and property data are required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation($"🏠 Updating property {propiedadId} for Client: {clientId}, TwinID: {requestData.TwinId}");

                // Convert dynamic to Propiedad
                var propiedad = JsonConvert.DeserializeObject<Propiedad>(JsonConvert.SerializeObject(requestData.Propiedad));

                // Call service to update property
                var result = await _miCasaCosmosDB.UpdatePropiedadAsync(clientId, requestData.TwinId, propiedadId, propiedad);

                if (!result.Success)
                {
                    _logger.LogError($"❌ Error updating property: {result.ErrorMessage}");
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = result.ErrorMessage
                    }));
                    return errorResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");
                await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = true,
                    clientId = result.ClientId,
                    twinId = result.TwinId,
                    propertyId = result.PropertyId,
                    propiedad = result.Propiedad,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp,
                    message = $"Property '{propiedadId}' updated successfully"
                }, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                _logger.LogInformation($"✅ Property updated successfully. Property ID: {result.PropertyId}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error in UpdatePropertyInClient: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while updating property"
                }));
                return errorResponse;
            }
        }

        [Function("UpdatePropertyInClientOptions")]
        public async Task<HttpResponseData> UpdatePropertyInClientOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "micasa/clients/{clientId}/properties/{propiedadId}")] HttpRequestData req)
        {
            _logger.LogInformation("✅ CORS preflight request handled for UpdatePropertyInClient");
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, req);
            return response;
        }

        /// <summary>
        /// Genera un análisis comprehensivo de una propiedad integrando datos de cliente, propiedad y fotos
        /// Endpoint: POST /twins/{twinId}/properties/{propiedadId}/analysis
        /// </summary>
        [Function("GeneratePropertyComprehensiveAnalysis")]
        public async Task<HttpResponseData> GeneratePropertyComprehensiveAnalysis(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/properties/{propiedadId}/analysis")] HttpRequestData req,
            string twinId,
            string propiedadId)
        {
            _logger.LogInformation("🏠 GeneratePropertyComprehensiveAnalysis function triggered for Twin: {TwinId}, Property: {PropiedadId}",
                twinId, propiedadId);

            try
            {
                // Validar parámetros requeridos
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ TwinID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "TwinID parameter is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(propiedadId))
                {
                    _logger.LogError("❌ Property ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Property ID parameter is required"
                    }));
                    return badResponse;
                }

                // Leer el cuerpo de la solicitud para obtener el casaId
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("❌ Request body is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "Request body with casaId is required"
                    }));
                    return badResponse;
                }

                // Parsear el cuerpo para extraer casaId
                var requestData = System.Text.Json.JsonSerializer.Deserialize<GenerateAnalysisRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (requestData == null || string.IsNullOrEmpty(requestData.CasaId))
                {
                    _logger.LogError("❌ CasaId is required in request body");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorMessage = "CasaId is required in request body"
                    }));
                    return badResponse;
                }

                string casaId = requestData.CasaId;

                _logger.LogInformation("📊 Starting comprehensive analysis for Twin: {TwinId}, Property: {PropiedadId}, Casa: {CasaId}",
                    twinId, propiedadId, casaId);

                // Create a proper logger factory and loggers
                var loggerFactory = LoggerFactory.Create(builder => 
                    builder.AddConsole().AddDebug());
                
                var agentLogger = loggerFactory.CreateLogger<AgentTwinMiCasa>();
                var fotosIndexLogger = loggerFactory.CreateLogger<MiCasaFotosIndex>();

                // Crear instancia del agente MiCasa con loggers correctamente tipados
                var miCasaAgent = new AgentTwinMiCasa(
                    agentLogger,
                    _configuration,
                    new MiCasaFotosIndex(fotosIndexLogger, _configuration)
                );

                // Llamar al método para generar análisis comprehensivo
                var analysisResult = await miCasaAgent.GeneratePropertyComprehensiveAnalysisAsync(
                    twinId,
                    propiedadId,
                    casaId
                );

                // Preparar respuesta
                var response = req.CreateResponse(
                    analysisResult.Success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json; charset=utf-8");

                await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = analysisResult.Success,
                    errorMessage = analysisResult.ErrorMessage,
                    twinId = analysisResult.TwinId,
                    propiedadId = analysisResult.PropiedadId,
                    sumarioEjecutivo = analysisResult.SumarioEjecutivo,
                    descripcionDetallada = analysisResult.DescripcionDetallada,
                    htmlCompleto = analysisResult.HtmlCompleto,
                    totalMetrosCuadrados = analysisResult.TotalMetrosCuadrados,
                    totalCuartos = analysisResult.TotalCuartos,
                    observacionesAdicionales = analysisResult.ObservacionesAdicionales,
                    recomendaciones = analysisResult.Recomendaciones,
                    totalPhotosAnalyzed = analysisResult.TotalPhotosAnalyzed,
                    processingTimeMs = analysisResult.ProcessingTimeMs,
                    timestamp = DateTime.UtcNow
                }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));

                if (analysisResult.Success)
                {
                    _logger.LogInformation("✅ Property analysis completed successfully. Processing time: {ProcessingTime}ms, Total photos: {TotalPhotos}",
                        analysisResult.ProcessingTimeMs, analysisResult.TotalPhotosAnalyzed);
                }
                else
                {
                    _logger.LogError("❌ Property analysis failed: {ErrorMessage}", analysisResult.ErrorMessage);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in GeneratePropertyComprehensiveAnalysis");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "An error occurred while generating property analysis",
                    exception = ex.Message
                }));
                return errorResponse;
            }
        }

        /// <summary>
        /// CORS preflight for GeneratePropertyComprehensiveAnalysis
        /// </summary>
        [Function("GeneratePropertyComprehensiveAnalysisOptions")]
        public async Task<HttpResponseData> GeneratePropertyComprehensiveAnalysisOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/properties/{propiedadId}/analysis")] HttpRequestData req)
        {
            _logger.LogInformation("✅ CORS preflight request handled for GeneratePropertyComprehensiveAnalysis");
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(response, req);
            return response;
        }

        private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            // Get origin from request or use wildcard
            string origin = request.Headers.Contains("Origin") 
                ? request.Headers.GetValues("Origin").First() 
                : "*";

            response.Headers.Add("Access-Control-Allow-Origin", origin);
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS, PUT, DELETE");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Requested-With");
            response.Headers.Add("Access-Control-Max-Age", "3600");
            response.Headers.Add("Access-Control-Allow-Credentials", "true");
        }
    }

    /// <summary>
    /// Request model for generating property comprehensive analysis
    /// </summary>
    public class GenerateAnalysisRequest
    {
        public string CasaId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request model for adding a property to a client
    /// </summary>
    public class AddPropertyRequest
    {
        public string TwinId { get; set; } = string.Empty;
        public Propiedad Propiedad { get; set; }
    }

    /// <summary>
    /// Request model for updating a property in a client
    /// </summary>
    public class UpdatePropertyRequest
    {
        public string TwinId { get; set; } = string.Empty;
        public Propiedad Propiedad { get; set; }
    }
}

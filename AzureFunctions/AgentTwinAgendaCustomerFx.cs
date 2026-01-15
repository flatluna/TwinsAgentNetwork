using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Function para gestionar agendas de visitas de clientes inmobiliarios
    /// Utiliza AgentTwinAgendaCustomerCosmosDB para CRUD operations
    /// </summary>
    public class AgentTwinAgendaCustomerFx
    {
        private readonly ILogger<AgentTwinAgendaCustomerFx> _logger;
        private readonly AgentTwinAgendaCustomerCosmosDB _agendaService;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

        public AgentTwinAgendaCustomerFx(
            ILogger<AgentTwinAgendaCustomerFx> logger,
            AgentTwinAgendaCustomerCosmosDB agendaService,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _logger = logger;
            _agendaService = agendaService;
            _configuration = configuration;
        }

        /// <summary>
        /// Azure Function para guardar una nueva agenda de visita
        /// POST /api/twin-agenda/save
        /// </summary>
        [Function("SaveAgenda")]
        public async Task<HttpResponseData> SaveAgenda(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-agenda/save")] HttpRequestData req)
        {
            _logger.LogInformation("?? SaveAgenda function triggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("? Request body is empty");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required");
                }

                var agendaRequest = JsonConvert.DeserializeObject<SaveAgendaRequest>(requestBody);

                if (agendaRequest == null || agendaRequest.AgendaData == null || string.IsNullOrEmpty(agendaRequest.TwinId))
                {
                    _logger.LogError("? AgendaData and TwinId are required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "AgendaData and TwinId are required");
                }

                _logger.LogInformation("?? Preparing to save agenda for Twin: {TwinId}, Casa: {Direccion}", 
                    agendaRequest.TwinId, agendaRequest.AgendaData.DireccionCompleta);

                // FIRST: Get the interes to extract UrlPropiedad and assign to CasaURL
                if (!string.IsNullOrEmpty(agendaRequest.AgendaData.CasaProspectoID))
                {
                    _logger.LogInformation("?? Reading interes casa to extract UrlPropiedad. CasaProspectoID: {CasaProspectoID}", 
                        agendaRequest.AgendaData.CasaProspectoID);

                    try
                    {
                        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                        var interesLogger = loggerFactory.CreateLogger<AgentCasaCompraInteresCosmosDB>();
                        var interesService = new AgentCasaCompraInteresCosmosDB(interesLogger, _configuration);

                        // Get the existing interes to read it
                        var existingInteresResult = await interesService.GetInteresesByTwinIdAsync(agendaRequest.TwinId);
                        
                        if (existingInteresResult.Success && existingInteresResult.Intereses != null)
                        {
                            // Find the interes matching the CasaProspectoID
                            var interesData = existingInteresResult.Intereses
                                .FirstOrDefault(i => i.Id == agendaRequest.AgendaData.CasaProspectoID);

                            if (interesData != null)
                            {
                                // Extract UrlPropiedad and assign to CasaURL
                                if (!string.IsNullOrEmpty(interesData.UrlPropiedad))
                                {
                                    agendaRequest.AgendaData.CasaURL = interesData.UrlPropiedad;
                                    _logger.LogInformation("? UrlPropiedad extracted and assigned to CasaURL: {CasaURL}", 
                                        agendaRequest.AgendaData.CasaURL);
                                }
                                else
                                {
                                    _logger.LogWarning("?? UrlPropiedad is empty in interes with CasaProspectoID: {CasaProspectoID}", 
                                        agendaRequest.AgendaData.CasaProspectoID);
                                }
                            }
                            else
                            {
                                _logger.LogWarning("?? No interes found with CasaProspectoID: {CasaProspectoID}", 
                                    agendaRequest.AgendaData.CasaProspectoID);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log the error but don't fail the agenda save
                        _logger.LogError(ex, "? Error reading interes to extract UrlPropiedad");
                    }
                }

                // SECOND: Save the agenda with the CasaURL populated
                var result = await _agendaService.SaveAgendaAsync(agendaRequest.AgendaData, agendaRequest.TwinId);

                if (!result.Success)
                {
                    _logger.LogError("? Error saving agenda: {ErrorMessage}", result.ErrorMessage);
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, result.ErrorMessage);
                }

                _logger.LogInformation("? Agenda saved successfully. Document ID: {DocumentId}", result.DocumentId);

                // THIRD: Update the CasaInteresRequest to mark it as prospected
                if (!string.IsNullOrEmpty(agendaRequest.AgendaData.CasaProspectoID))
                {
                    _logger.LogInformation("?? Updating interes casa to mark as prospected. CasaProspectoID: {CasaProspectoID}", 
                        agendaRequest.AgendaData.CasaProspectoID);

                    try
                    {
                        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                        var interesLogger = loggerFactory.CreateLogger<AgentCasaCompraInteresCosmosDB>();
                        var interesService = new AgentCasaCompraInteresCosmosDB(interesLogger, _configuration);

                        // Get the existing interes to update it
                        var existingInteresResult = await interesService.GetInteresesByTwinIdAsync(agendaRequest.TwinId);
                        
                        if (existingInteresResult.Success && existingInteresResult.Intereses != null)
                        {
                            // Find the interes matching the CasaProspectoID
                            var interesToUpdate = existingInteresResult.Intereses
                                .FirstOrDefault(i => i.Id == agendaRequest.AgendaData.CasaProspectoID);

                            if (interesToUpdate != null)
                            {
                                // Update the prospectado fields
                                interesToUpdate.Prospectado = true;
                                interesToUpdate.DateProspectado = DateTime.UtcNow;

                                // Update the interes in Cosmos DB
                                var updateResult = await interesService.UpdateInteresAsync(
                                    interesToUpdate.Id, 
                                    agendaRequest.TwinId.ToLowerInvariant(), 
                                    interesToUpdate);

                                if (updateResult.Success)
                                {
                                    _logger.LogInformation("? Interes updated successfully as prospected. InteresID: {InteresId}", 
                                        interesToUpdate.Id);
                                }
                                else
                                {
                                    _logger.LogWarning("?? Failed to update interes: {ErrorMessage}", updateResult.ErrorMessage);
                                }
                            }
                            else
                            {
                                _logger.LogWarning("?? No interes found with CasaProspectoID: {CasaProspectoID}", 
                                    agendaRequest.AgendaData.CasaProspectoID);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log the error but don't fail the agenda save
                        _logger.LogError(ex, "? Error updating interes after agenda save");
                    }
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Agenda saved successfully",
                    documentId = result.DocumentId,
                    twinId = result.TwinId,
                    casaURL = agendaRequest.AgendaData.CasaURL, // Include CasaURL in response
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in SaveAgenda");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while saving the agenda");
            }
        }

        /// <summary>
        /// Azure Function para obtener una agenda por ID
        /// GET /api/twin-agenda/get/{twinId}/{documentId}
        /// </summary>
        [Function("GetAgendaById")]
        public async Task<HttpResponseData> GetAgendaById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-agenda/get/{twinId}/{documentId}")] HttpRequestData req,
            string twinId,
            string documentId)
        {
            _logger.LogInformation("?? GetAgendaById function triggered. TwinID: {TwinId}, DocumentID: {DocumentId}", 
                twinId, documentId);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(documentId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "TwinID and DocumentID are required");
                }

                var result = await _agendaService.GetAgendaByIdAsync(documentId, twinId);

                if (!result.Success)
                {
                    _logger.LogWarning("?? Agenda not found: {DocumentId}", documentId);
                    return await CreateErrorResponse(req, HttpStatusCode.NotFound, result.ErrorMessage);
                }

                _logger.LogInformation("? Agenda retrieved successfully. DocumentID: {DocumentId}", documentId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    data = result.Agenda,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in GetAgendaById");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving the agenda");
            }
        }

        /// <summary>
        /// Azure Function para obtener todas las agendas de un Twin
        /// GET /api/twin-agenda/twin/{twinId}
        /// </summary>
        [Function("GetAgendasByTwinId")]
        public async Task<HttpResponseData> GetAgendasByTwinId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-agenda/twin/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? GetAgendasByTwinId function triggered for TwinID: {TwinId}", twinId);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "TwinID is required");
                }

                var result = await _agendaService.GetAgendasByTwinIdAsync(twinId);

                if (!result.Success)
                {
                    _logger.LogError("? Error retrieving agendas: {ErrorMessage}", result.ErrorMessage);
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, result.ErrorMessage);
                }

                _logger.LogInformation("? Retrieved {Count} agendas for TwinID: {TwinId}", result.AgendaCount, twinId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinId = twinId,
                    count = result.AgendaCount,
                    agendas = result.Agendas,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in GetAgendasByTwinId");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving agendas");
            }
        }

        /// <summary>
        /// Azure Function para obtener agendas de un cliente específico
        /// GET /api/twin-agenda/cliente/{twinId}/{clienteId}
        /// </summary>
        [Function("GetAgendasByClienteId")]
        public async Task<HttpResponseData> GetAgendasByClienteId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-agenda/cliente/{twinId}/{clienteId}")] HttpRequestData req,
            string twinId,
            string clienteId)
        {
            _logger.LogInformation("?? GetAgendasByClienteId function triggered. TwinID: {TwinId}, ClienteID: {ClienteId}", 
                twinId, clienteId);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(clienteId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "TwinID and ClienteID are required");
                }

                var result = await _agendaService.GetAgendasByClienteIdAsync(clienteId, twinId);

                if (!result.Success)
                {
                    _logger.LogError("? Error retrieving agendas: {ErrorMessage}", result.ErrorMessage);
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, result.ErrorMessage);
                }

                _logger.LogInformation("? Retrieved {Count} agendas for ClienteID: {ClienteId}", 
                    result.AgendaCount, clienteId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinId = twinId,
                    clienteId = result.ClienteId,
                    count = result.AgendaCount,
                    agendas = result.Agendas,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in GetAgendasByClienteId");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving agendas for cliente");
            }
        }

        /// <summary>
        /// Azure Function para obtener agendas de casas "Prospecto" de un cliente específico
        /// GET /api/twin-agenda/prospecto/{twinId}/{clienteId}
        /// </summary>
        [Function("GetProspectoAgendasByClienteId")]
        public async Task<HttpResponseData> GetProspectoAgendasByClienteId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-agenda/prospecto/{twinId}/{clienteId}]")] HttpRequestData req,
            string twinId,
            string clienteId)
        {
            _logger.LogInformation("?? GetProspectoAgendasByClienteId function triggered. TwinID: {TwinId}, ClienteID: {ClienteId}", 
                twinId, clienteId);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(clienteId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "TwinID and ClienteID are required");
                }

                var result = await _agendaService.GetProspectoAgendasByClienteIdAsync(clienteId, twinId);

                if (!result.Success)
                {
                    _logger.LogError("? Error retrieving prospect agendas: {ErrorMessage}", result.ErrorMessage);
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, result.ErrorMessage);
                }

                _logger.LogInformation("? Retrieved {Count} prospect agendas for ClienteID: {ClienteId}", 
                    result.AgendaCount, clienteId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinId = result.TwinId,
                    clienteId = result.ClienteId,
                    estatusCasa = "Prospecto",
                    count = result.AgendaCount,
                    agendas = result.Agendas,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in GetProspectoAgendasByClienteId");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving prospect agendas for cliente");
            }
        }
        /// <summary>
        /// Azure Function para obtener TODAS las agendas de un cliente específico (sin filtro de estatus)
        /// GET /api/twin-agenda/agenda/{twinId}/{clienteId}
        /// </summary>
        [Function("GetYaAgendasByClienteId")]
        public async Task<HttpResponseData> GetYaAgendasByClienteId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-agenda/agenda/{twinId}/{clienteId}")] HttpRequestData req,
            string twinId,
            string clienteId)
        {
            _logger.LogInformation("?? GetYaAgendasByClienteId function triggered. TwinID: {TwinId}, ClienteID: {ClienteId}",
                twinId, clienteId);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(clienteId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest,
                        "TwinID and ClienteID are required");
                }

                // Usar GetAgendasByClienteIdAsync para obtener TODAS las agendas del cliente (sin filtro)
                var result = await _agendaService.GetAgendasByClienteIdAsync(clienteId, twinId);

                if (!result.Success)
                {
                    _logger.LogError("? Error retrieving all agendas: {ErrorMessage}", result.ErrorMessage);
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, result.ErrorMessage);
                }

                _logger.LogInformation("? Retrieved {Count} total agendas for ClienteID: {ClienteId}",
                    result.AgendaCount, clienteId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinId = twinId,
                    clienteId = result.ClienteId,
                    estatusCasa = "Todas", // Indica que no hay filtro de estatus
                    count = result.AgendaCount,
                    agendas = result.Agendas,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in GetYaAgendasByClienteId");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving all agendas for cliente");
            }
        }

        /// <summary>
        /// Azure Function para obtener agendas de casas "Agendado" de un cliente específico
        /// GET /api/twin-agenda/agendado/{twinId}/{clienteId}
        /// </summary>
        [Function("GetAgendadoAgendasByClienteId")]
        public async Task<HttpResponseData> GetAgendadoAgendasByClienteId(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-agenda/agendado/{twinId}/{clienteId}")] HttpRequestData req,
            string twinId,
            string clienteId)
        {
            _logger.LogInformation("?? GetAgendadoAgendasByClienteId function triggered. TwinID: {TwinId}, ClienteID: {ClienteId}", 
                twinId, clienteId);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(clienteId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "TwinID and ClienteID are required");
                }

                var result = await _agendaService.GetAgendadoAgendasByClienteIdAsync(clienteId, twinId);

                if (!result.Success)
                {
                    _logger.LogError("? Error retrieving scheduled agendas: {ErrorMessage}", result.ErrorMessage);
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, result.ErrorMessage);
                }

                _logger.LogInformation("? Retrieved {Count} scheduled agendas for ClienteID: {ClienteId}", 
                    result.AgendaCount, clienteId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinId = result.TwinId,
                    clienteId = result.ClienteId,
                    estatusCasa = "Agendado",
                    count = result.AgendaCount,
                    agendas = result.Agendas,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in GetAgendadoAgendasByClienteId");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving scheduled agendas for cliente");
            }
        }

        /// <summary>
        /// OPTIONS handler for GetAgendasByMicrosoftOID endpoint (CORS preflight)
        /// </summary>
        [Function("GetAgendasByMicrosoftOIDOptions")]
        public async Task<HttpResponseData> HandleGetAgendasByMicrosoftOIDOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twin-agenda/microsoft-oid/{twinId}/{microsoftOID}")] HttpRequestData req,
            string twinId,
            string microsoftOID)
        {
            _logger.LogInformation("?? OPTIONS preflight request for microsoft-oid/{TwinId}/{MicrosoftOID}", twinId, microsoftOID);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Azure Function para obtener agendas por MicrosoftOID (user identity)
        /// GET /api/twin-agenda/microsoft-oid/{twinId}/{microsoftOID}
        /// </summary>
        [Function("GetAgendasByMicrosoftOID")]
        public async Task<HttpResponseData> GetAgendasByMicrosoftOID(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-agenda/microsoft-oid/{twinId}/{microsoftOID}")] HttpRequestData req,
            string twinId,
            string microsoftOID)
        {
            _logger.LogInformation("?? GetAgendasByMicrosoftOID function triggered. TwinID: {TwinId}, MicrosoftOID: {MicrosoftOID}", 
                twinId, microsoftOID);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(microsoftOID))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "TwinID and MicrosoftOID are required"
                    });
                    return badResponse;
                }

                var result = await _agendaService.GetAgendasByMicrosoftOIDAsync(microsoftOID, twinId);

                if (!result.Success)
                {
                    _logger.LogError("? Error retrieving agendas by Microsoft OID: {ErrorMessage}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = result.ErrorMessage
                    });
                    return errorResponse;
                }

                _logger.LogInformation("? Retrieved {Count} agendas for MicrosoftOID: {MicrosoftOID}", 
                    result.AgendaCount, microsoftOID);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinId = result.TwinId,
                    microsoftOID = result.MicrosoftOID,
                    count = result.AgendaCount,
                    agendas = result.Agendas,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in GetAgendasByMicrosoftOID");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while retrieving agendas by Microsoft OID"
                });
                return errorResponse;
            }
        }

        /// <summary>
        /// Azure Function para obtener agendas por MicrosoftOID (user identity)
        /// GET /api/twin-agenda/microsoft-oid/{twinId}/{microsoftOID}
        /// </summary>
        [Function("GetAgendasByOIDClientID")]
        public async Task<HttpResponseData> GetAgendasByOIDClientID(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-agenda/microsoft-oid-client/{clientId}/{microsoftOID}")] HttpRequestData req,
            string clientId,
            string microsoftOID)
        {
            _logger.LogInformation("?? GetAgendasByMicrosoftOID function triggered. TwinID: {TwinId}, MicrosoftOID: {MicrosoftOID}",
                clientId, microsoftOID);

            try
            {
                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(microsoftOID))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "TwinID and MicrosoftOID are required"
                    });
                    return badResponse;
                }

                var result = await _agendaService.GetAgendasOIDClientIDAsync(microsoftOID, clientId);

                if (!result.Success)
                {
                    _logger.LogError("? Error retrieving agendas by Microsoft OID: {ErrorMessage}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = result.ErrorMessage
                    });
                    return errorResponse;
                }

                _logger.LogInformation("? Retrieved {Count} agendas for MicrosoftOID: {MicrosoftOID}",
                    result.AgendaCount, microsoftOID);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinId = result.TwinId,
                    microsoftOID = result.MicrosoftOID,
                    count = result.AgendaCount,
                    agendas = result.Agendas,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in GetAgendasByMicrosoftOID");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while retrieving agendas by Microsoft OID"
                });
                return errorResponse;
            }
        }
        /// <summary>
        /// Azure Function para obtener agendas de una fecha específica
        /// GET /api/twin-agenda/date/{twinId}/{fechaVisita}
        /// Ejemplo: /api/twin-agenda/date/twin-123/12-09-2025
        /// </summary>
        [Function("GetAgendasByDate")]
        public async Task<HttpResponseData> GetAgendasByDate(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-agenda/date/{twinId}/{fechaVisita}")] HttpRequestData req,
            string twinId,
            string fechaVisita)
        {
            _logger.LogInformation("?? GetAgendasByDate function triggered. TwinID: {TwinId}, Fecha: {Fecha}", 
                twinId, fechaVisita);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(fechaVisita))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "TwinID and FechaVisita are required");
                }

                // Convertir formato de URL (12-09-2025) a formato esperado (12/09/2025)
                string fechaFormateada = fechaVisita.Replace("-", "/");

                var result = await _agendaService.GetAgendasByDateAsync(twinId, fechaFormateada);

                if (!result.Success)
                {
                    _logger.LogError("? Error retrieving agendas: {ErrorMessage}", result.ErrorMessage);
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, result.ErrorMessage);
                }

                _logger.LogInformation("? Retrieved {Count} agendas for date: {Fecha}", 
                    result.AgendaCount, fechaFormateada);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    twinId = twinId,
                    fechaVisita = result.FechaVisita,
                    count = result.AgendaCount,
                    agendas = result.Agendas,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in GetAgendasByDate");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving agendas by date");
            }
        }

        /// <summary>
        /// Azure Function para actualizar una agenda existente
        /// PUT /api/twin-agenda/update
        /// </summary>
        [Function("UpdateAgendaOptions")]
        public async Task<HttpResponseData> HandleUpdateAgendaOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twin-agenda/update")] HttpRequestData req)
        {
            _logger.LogInformation("?? OPTIONS preflight request for update");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Azure Function para actualizar una agenda existente
        /// PUT /api/twin-agenda/update
        /// </summary>
        [Function("UpdateAgenda")]
        public async Task<HttpResponseData> UpdateAgenda(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twin-agenda/update")] HttpRequestData req)
        {
            _logger.LogInformation("?? UpdateAgenda function triggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("? Request body is empty");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required");
                }

                var updateRequest = JsonConvert.DeserializeObject<UpdateAgendaRequest>(requestBody);

                if (updateRequest == null || 
                    string.IsNullOrEmpty(updateRequest.DocumentId) || 
                    string.IsNullOrEmpty(updateRequest.TwinId) || 
                    updateRequest.AgendaData == null)
                {
                    _logger.LogError("? DocumentId, TwinId, and AgendaData are required");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "DocumentId, TwinId, and AgendaData are required");
                }

                _logger.LogInformation("?? Updating agenda. DocumentID: {DocumentId}, TwinID: {TwinId}", 
                    updateRequest.DocumentId, updateRequest.TwinId);

                var result = await _agendaService.UpdateAgendaAsync(
                    updateRequest.DocumentId, 
                    updateRequest.TwinId, 
                    updateRequest.AgendaData);

                if (!result.Success)
                {
                    _logger.LogError("? Error updating agenda: {ErrorMessage}", result.ErrorMessage);
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, result.ErrorMessage);
                }

                _logger.LogInformation("? Agenda updated successfully. DocumentID: {DocumentId}", result.DocumentId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Agenda updated successfully",
                    documentId = result.DocumentId,
                    twinId = result.TwinId,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in UpdateAgenda");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while updating the agenda");
            }
        }

        /// <summary>
        /// Azure Function para eliminar una agenda
        /// DELETE /api/twin-agenda/delete/{twinId}/{documentId}
        /// </summary>
        [Function("DeleteAgenda")]
        public async Task<HttpResponseData> DeleteAgenda(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twin-agenda/delete/{twinId}/{documentId}")] HttpRequestData req,
            string twinId,
            string documentId)
        {
            _logger.LogInformation("??? DeleteAgenda function triggered. TwinID: {TwinId}, DocumentID: {DocumentId}", 
                twinId, documentId);

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(documentId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "TwinID and DocumentID are required");
                }

                var result = await _agendaService.DeleteAgendaAsync(documentId, twinId);

                if (!result.Success)
                {
                    _logger.LogError("? Error deleting agenda: {ErrorMessage}", result.ErrorMessage);
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, result.ErrorMessage);
                }

                _logger.LogInformation("? Agenda deleted successfully. DocumentID: {DocumentId}", documentId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Agenda deleted successfully",
                    documentId = result.DocumentId,
                    ruConsumed = result.RUConsumed,
                    timestamp = result.Timestamp
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Unexpected error in DeleteAgenda");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError,
                    "An error occurred while deleting the agenda");
            }
        }

        /// <summary>
        /// OPTIONS handler for GenerateAgendaEmail endpoint (CORS preflight)
        /// </summary>
        [Function("GenerateAgendaEmailOptions")]
        public async Task<HttpResponseData> HandleGenerateAgendaEmailOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twin-agenda/generate-email")] HttpRequestData req)
        {
            _logger.LogInformation("?? OPTIONS preflight request for generate-email");
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Azure Function para generar un email HTML mejorado de agenda de visita usando AI
        /// POST /api/twin-agenda/generate-email
        /// Body: { direccionCasa, direccionExacta, puntoPartida, fechaVisita, horaVisita, mapaUrl, nombreCliente?, nombreAgente?, idioma? }
        /// </summary>
        [Function("GenerateAgendaEmail")]
        public async Task<HttpResponseData> GenerateAgendaEmail(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-agenda/generate-email")] HttpRequestData req)
        {
            _logger.LogInformation("?? GenerateAgendaEmail function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogError("? Request body is empty");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "Request body is required"
                    });
                    return badResponse;
                }

                var emailRequest = JsonConvert.DeserializeObject<GenerateAgendaEmailRequest>(requestBody);

                if (emailRequest == null)
                {
                    _logger.LogError("? Failed to parse request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "Invalid request data format"
                    });
                    return badResponse;
                }

                // Validar campos requeridos
                if (string.IsNullOrEmpty(emailRequest.DireccionCasa))
                {
                    _logger.LogError("? DireccionCasa is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "DireccionCasa is required"
                    });
                    return badResponse;
                }

                if (string.IsNullOrEmpty(emailRequest.DireccionExacta))
                {
                    _logger.LogError("? DireccionExacta is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "DireccionExacta is required"
                    });
                    return badResponse;
                }

                if (string.IsNullOrEmpty(emailRequest.PuntoPartida))
                {
                    _logger.LogError("? PuntoPartida is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "PuntoPartida is required"
                    });
                    return badResponse;
                }

                if (string.IsNullOrEmpty(emailRequest.FechaVisita))
                {
                    _logger.LogError("? FechaVisita is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "FechaVisita is required"
                    });
                    return badResponse;
                }

                if (string.IsNullOrEmpty(emailRequest.HoraVisita))
                {
                    _logger.LogError("? HoraVisita is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "HoraVisita is required"
                    });
                    return badResponse;
                }

                if (string.IsNullOrEmpty(emailRequest.MapaUrl))
                {
                    _logger.LogError("? MapaUrl is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "MapaUrl is required"
                    });
                    return badResponse;
                }

                _logger.LogInformation("?? Request details:");
                _logger.LogInformation("   ?? Casa: {DireccionCasa}", emailRequest.DireccionCasa);
                _logger.LogInformation("   ?? Fecha: {Fecha} | Hora: {Hora}", emailRequest.FechaVisita, emailRequest.HoraVisita);
                _logger.LogInformation("   ?? Cliente: {Cliente} | Agente: {Agente}", 
                    emailRequest.NombreCliente ?? "N/A", emailRequest.NombreAgente ?? "N/A");
                _logger.LogInformation("   ?? Idioma: {Idioma}", emailRequest.Idioma ?? "es");

                // Crear el agente de agenda con configuración
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var agendaLogger = loggerFactory.CreateLogger<AgentTwinAgenda>();
                var agentAgenda = new AgentTwinAgenda(agendaLogger, _configuration);

                // Llamar al agente para mejorar el mensaje
                var result = await agentAgenda.MejorarMensajeAgendaAsync(
                    direccionCasa: emailRequest.DireccionCasa,
                    direccionExacta: emailRequest.DireccionExacta,
                    puntoPartida: emailRequest.PuntoPartida,
                    fechaVisita: emailRequest.FechaVisita,
                    horaVisita: emailRequest.HoraVisita,
                    mapaUrl: emailRequest.MapaUrl,
                    nombreCliente: emailRequest.NombreCliente ?? "",
                    nombreAgente: emailRequest.NombreAgente ?? "",
                    idioma: emailRequest.Idioma ?? "es"
                );

                var processingTime = DateTime.UtcNow - startTime;

                if (!result.Success)
                {
                    _logger.LogError("? Failed to generate email: {ErrorMessage}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = result.ErrorMessage,
                        processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                        timestamp = DateTime.UtcNow
                    });
                    return errorResponse;
                }

                _logger.LogInformation("? Email HTML generated successfully");
                _logger.LogInformation("?? HTML size: {Size} characters", result.EmailHTMLMejorado.Length);
                _logger.LogInformation("?? Processing time: {Time:F2} seconds", processingTime.TotalSeconds);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Email HTML generated successfully",
                    emailHTMLMejorado = result.EmailHTMLMejorado,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    timestamp = result.ProcessedAt
                });

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "? Unexpected error in GenerateAgendaEmail");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "An error occurred while generating the email",
                    details = ex.Message,
                    processingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    timestamp = DateTime.UtcNow
                });
                return errorResponse;
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
    }

    #region Request Models

    /// <summary>
    /// Modelo para la solicitud de guardar agenda
    /// </summary>
    public class SaveAgendaRequest
    {
        [JsonProperty("twinId")]
        public string TwinId { get; set; }

        [JsonProperty("agendaData")]
        public DatosCasaExtraidos AgendaData { get; set; }
    }

    /// <summary>
    /// Modelo para la solicitud de actualizar agenda
    /// </summary>
    public class UpdateAgendaRequest
    {
        [JsonProperty("documentId")]
        public string DocumentId { get; set; }

        [JsonProperty("twinId")]
        public string TwinId { get; set; }

        [JsonProperty("agendaData")]
        public DatosCasaExtraidos AgendaData { get; set; }
    }

    /// <summary>
    /// Modelo para la solicitud de generar email HTML de agenda
    /// </summary>
    public class GenerateAgendaEmailRequest
    {
        [JsonProperty("direccionCasa")]
        public string DireccionCasa { get; set; }

        [JsonProperty("direccionExacta")]
        public string DireccionExacta { get; set; }

        [JsonProperty("puntoPartida")]
        public string PuntoPartida { get; set; }

        [JsonProperty("fechaVisita")]
        public string FechaVisita { get; set; }

        [JsonProperty("horaVisita")]
        public string HoraVisita { get; set; }

        [JsonProperty("mapaUrl")]
        public string MapaUrl { get; set; }

        [JsonProperty("nombreCliente")]
        public string NombreCliente { get; set; }

        [JsonProperty("nombreAgente")]
        public string NombreAgente { get; set; }

        [JsonProperty("idioma")]
        public string Idioma { get; set; }
    }

    #endregion
}

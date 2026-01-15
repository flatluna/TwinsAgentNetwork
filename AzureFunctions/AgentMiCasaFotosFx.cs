using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwinAgentsLibrary.Models;
using TwinAgentsLibrary.Services;
using TwinAgentsNetwork.Models;
using TwinAgentsNetwork.Services;
using static TwinAgentsNetwork.Services.AgentMiCasaFotosCosmosDB;

namespace TwinAgentsNetwork.AzureFunctions
{
    public class AgentMiCasaFotosFx
    {
        private readonly ILogger<AgentMiCasaFotosFx> _logger;
        private readonly IConfiguration _configuration;

        public AgentMiCasaFotosFx(ILogger<AgentMiCasaFotosFx> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }


        /// </summary>
        /// Analiza una foto de MiCasa y extrae información de secciones y propiedades
        [Function("AnalyzeMiCasaFoto")]
        public async Task<IActionResult> AnalyzeMiCasaFoto(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/analyze")] HttpRequest req,
            string twinId)
        {
            _logger.LogInformation("🏠 AnalyzeMiCasaFoto function triggered for Twin: {TwinId}", twinId);
            AddCorsHeaders(req);

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return new BadRequestObjectResult(new { success = false, errorMessage = "Twin ID parameter is required" });
                }

                // Extraer parámetros del formulario en una clase
                var analysisRequest = ExtractAnalysisRequest(req, twinId);

                // Validar la solicitud
                var (isValid, errorMessage) = analysisRequest.Validate();
                if (!isValid)
                {
                    return new BadRequestObjectResult(new { success = false, errorMessage });
                }

                var uploadedFile = analysisRequest.UploadedFile;

                _logger.LogInformation("📸 Processing photo for analysis: {FileName}, Size: {Size} bytes, TipoSeccion: {TipoSeccion}, NombreSeccion: {NombreSeccion}",
                    uploadedFile.FileName, uploadedFile.Length, analysisRequest.TipoSeccion, analysisRequest.NombreSeccion);

                // Configurar DataLake client
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                // Generar nombre único para el archivo
                var fileExtension = Path.GetExtension(uploadedFile.FileName).ToLowerInvariant();
                if (string.IsNullOrEmpty(fileExtension))
                {
                    fileExtension = GetExtensionFromContentType(uploadedFile.ContentType);
                }

                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var fileName = uploadedFile.FileName ?? $"micasa_{analysisRequest.TipoSeccion}_{timestamp}{fileExtension}";

                // Usar contenedor TwinID y path por tipo de sección
                var containerName = twinId.ToLowerInvariant();
                var filePath = $"MiCasa/{analysisRequest.TipoSeccion}"; // Path organizado por tipo de sección
                var fullFilePath = $"{filePath}/{fileName}";

                _logger.LogInformation("📸 Uploading to DataLake: Container={Container}, Path={Path}", containerName, fullFilePath);

                // Subir archivo al Data Lake
                using var fileStream = uploadedFile.OpenReadStream();
                fileStream.Position = 0;
                var uploadSuccess = await dataLakeClient.UploadFileAsync(
                    containerName,
                    filePath,
                    fileName,
                    fileStream,
                    uploadedFile.ContentType ?? "image/jpeg"
                );

                if (!uploadSuccess)
                {
                    return new ObjectResult(new { success = false, errorMessage = "Failed to upload file to storage for analysis" })
                    {
                        StatusCode = 500
                    };
                }

                // Generar SAS URL para acceso temporal (24 horas)
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(24));

                if (string.IsNullOrEmpty(sasUrl))
                {
                    return new ObjectResult(new { success = false, errorMessage = "Failed to generate SAS URL for uploaded file" })
                    {
                        StatusCode = 500
                    };
                }

                _logger.LogInformation("📸 File uploaded successfully, generated SAS URL for analysis");

                // Crear instancia del agente de análisis de IA
                var miCasaAgent = new Agents.AgentTwinMiCasa(
                    LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<Agents.AgentTwinMiCasa>(),
                    _configuration,
                    new MiCasaFotosIndex(
                        LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<MiCasaFotosIndex>(),
                        _configuration));

                // Ejecutar análisis de IA usando el SAS URL
                var imageAI = await miCasaAgent.AnalyzePhotoSimpleFormatAsync(sasUrl, analysisRequest);

                _logger.LogInformation("✅ AI analysis completed successfully for photo: {FileName}", fileName);

                return new OkObjectResult(new
                {
                    success = true,
                    twinId = analysisRequest.TwinId,
                    tipoSeccion = analysisRequest.TipoSeccion,
                    nombreSeccion = analysisRequest.NombreSeccion,
                    photo = new
                    {
                        fileName = fileName,
                        filePath = fullFilePath,
                        containerName = containerName,
                        sasUrl = sasUrl,
                        uploadedAt = DateTime.UtcNow
                    },
                    analysis = new
                    {
                        descripcionGenerica = imageAI.DescripcionGenerica,
                        elementosVisuales = imageAI.ElementosVisuales,
                        analisisPisos = imageAI.AnalisisPisos,
                        elementosDecorativosAcabados = imageAI.ElementosDecorativosAcabados,
                        condicionesGenerales = imageAI.CondicionesGenerales,
                        funcionalidad = imageAI.Funcionalidad,
                        calidadGeneral = imageAI.CalidadGeneral,
                        validacionContexto = imageAI.ValidacionContexto, 
                        datos = imageAI.Datos,
                        detailsHTML = imageAI.HTMLFullDescription,
                        dimensiones = new
                        {
                            ancho = imageAI.Dimensiones?.Ancho,
                            largo = imageAI.Dimensiones?.Largo,
                            alto = imageAI.Dimensiones?.Alto,
                            diametro = imageAI.Dimensiones?.Diametro,
                            observaciones = imageAI.Dimensiones?.Observaciones
                        },
                        metrosCuadrados = imageAI.MetrosCuadrados
                    },
                    propiedades = analysisRequest.Propiedades,
                    observaciones = analysisRequest.Observaciones,
                    dimensionesRequestadas = analysisRequest.Dimensiones != null ? new
                    {
                        ancho = analysisRequest.Dimensiones.Ancho,
                        largo = analysisRequest.Dimensiones.Largo,
                        alto = analysisRequest.Dimensiones.Alto,
                        diametro = analysisRequest.Dimensiones.Diametro
                    } : null,
                    metadata = analysisRequest.Metadata,
                    message = $"AI analysis completed for photo '{fileName}' in section '{analysisRequest.NombreSeccion}'",
                    analyzedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error analyzing photo for Twin: {TwinId}", twinId);
                return new ObjectResult(new { success = false, errorMessage = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Obtener extensión de archivo basada en el content type
        /// </summary>
        private string GetExtensionFromContentType(string? contentType)
        {
            return contentType?.ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg",
                "image/jpg" => ".jpg",
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "image/bmp" => ".bmp",
                _ => ".jpg"
            };
        }

        /// <summary>
        /// Agregar headers CORS a la respuesta HTTP
        /// </summary>
        private void AddCorsHeaders(HttpRequest req)
        {
            // Get origin from request or use wildcard
            string origin = req.Headers.ContainsKey("Origin")
                ? req.Headers["Origin"].ToString()
                : "*";

            req.HttpContext.Response.Headers.Add("Access-Control-Allow-Origin", origin);
            req.HttpContext.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS, PUT, DELETE");
            req.HttpContext.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Requested-With");
            req.HttpContext.Response.Headers.Add("Access-Control-Max-Age", "3600");
            req.HttpContext.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
        }

        private AgentMiCasaFotosCosmosDB CreateMemoriasService()
        {
            var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new AgentMiCasaFotosCosmosDB.CosmosDbSettings
            {
                Endpoint = _configuration["Values:COSMOS_ENDPOINT"] ?? _configuration["COSMOS_ENDPOINT"] ?? "",
                Key = _configuration["Values:COSMOS_KEY"] ?? _configuration["COSMOS_KEY"] ?? "",
                DatabaseName = _configuration["Values:COSMOS_DATABASE_NAME"] ?? _configuration["COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
            });

            var serviceLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<AgentMiCasaFotosCosmosDB>();
            return new AgentMiCasaFotosCosmosDB(serviceLogger, cosmosOptions, _configuration);
        }

        /// <summary>
        /// Extrae los parámetros de análisis de foto desde el formulario HTTP
        /// </summary>
        private MiCasaFotoAnalysisRequest ExtractAnalysisRequest(HttpRequest req, string twinId)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var containerName = twinId.ToLowerInvariant();
            var tipoSeccion = req.Form["tipoSeccion"].ToString() ?? string.Empty;
            var filePath = $"MiCasa/{tipoSeccion}";

            var request = new MiCasaFotoAnalysisRequest
            {
                TwinId = twinId,
                Piso = req.Form["piso"].ToString() ?? string.Empty,
                PropiedadId = req.Form["propiedadId"].ToString() ?? string.Empty,
                CasaId = req.Form["casaId"].ToString() ?? string.Empty,
                UploadedFile = req.Form.Files.Count > 0 ? req.Form.Files[0] : null,
                TipoSeccion = tipoSeccion,
                NombreSeccion = req.Form["nombreSeccion"].ToString() ?? string.Empty,
                Description = req.Form.ContainsKey("description") ? req.Form["description"].ToString()?.Trim() : null,
                Observaciones = req.Form.ContainsKey("observaciones") ? req.Form["observaciones"].ToString()?.Trim() : null,
                ContainerName = containerName,
                FilePath = filePath,
                FileUploadedAt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
            };

            // Extraer propiedades (JSON array)
            if (req.Form.ContainsKey("propiedades") && !string.IsNullOrEmpty(req.Form["propiedades"].ToString()))
            {
                try
                {
                    string propiedadesJson = req.Form["propiedades"].ToString();
                    var jsonOptions = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    request.Propiedades = System.Text.Json.JsonSerializer.Deserialize<List<PropiedadSeccion>>(propiedadesJson, jsonOptions) ?? new();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ Error deserializing propiedades: {Error}", ex.Message);
                    request.Propiedades = new();
                }
            }

            // Extraer dimensiones (JSON object)
            if (req.Form.ContainsKey("dimensiones") && !string.IsNullOrEmpty(req.Form["dimensiones"].ToString()))
            {
                try
                {
                    string dimensionesJson = req.Form["dimensiones"].ToString();
                    var jsonOptions = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    request.Dimensiones = System.Text.Json.JsonSerializer.Deserialize<Dimensiones>(dimensionesJson, jsonOptions);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ Error deserializing dimensiones: {Error}", ex.Message);
                    request.Dimensiones = null;
                }
            }

            // Extraer metadata (JSON object)
            if (req.Form.ContainsKey("metadata") && !string.IsNullOrEmpty(req.Form["metadata"].ToString()))
            {
                try
                {
                    string metadataJson = req.Form["metadata"].ToString();
                    var jsonOptions = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    request.Metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson, jsonOptions) ?? new();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ Error deserializing metadata: {Error}", ex.Message);
                    request.Metadata = new();
                }
            }

            return request;
        }

        /// <summary>
        /// Obtiene todos los análisis de fotos para una casa específica
        /// Filtra por casaId y twinId para obtener los documentos pertenecientes a un Twin y su casa
        /// </summary>
        [Function("GetMiCasaPhotosByHouse")]
        public async Task<IActionResult> GetMiCasaPhotosByHouse(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/casa/{casaId}/photos")] HttpRequest req,
            string twinId,
            string casaId)
        {
            _logger.LogInformation("📷 GetMiCasaPhotosByHouse function triggered for Twin: {TwinId}, Casa: {CasaId}", twinId, casaId);
            AddCorsHeaders(req);

            try
            {
                // Validar parámetros requeridos
                if (string.IsNullOrEmpty(twinId))
                {
                    return new BadRequestObjectResult(new 
                    { 
                        success = false, 
                        errorMessage = "Twin ID parameter is required" 
                    });
                }

                if (string.IsNullOrEmpty(casaId))
                {
                    return new BadRequestObjectResult(new 
                    { 
                        success = false, 
                        errorMessage = "Casa ID parameter is required" 
                    });
                }

                _logger.LogInformation("🔍 Fetching photo analysis documents for Twin: {TwinId}, Casa: {CasaId}", twinId, casaId);

                // Crear instancia del servicio de índice MiCasa Fotos
                var miCasaFotosIndex = new MiCasaFotosIndex(
                    LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<MiCasaFotosIndex>(),
                    _configuration);

                // Obtener documentos filtrando por casaId y twinId
                var result = await miCasaFotosIndex.GetPhotosByCasaIdAsync(casaId, twinId);

                if (!result.Success && !string.IsNullOrEmpty(result.Error))
                {
                    return new ObjectResult(new 
                    { 
                        success = false, 
                        errorMessage = result.Error 
                    })
                    {
                        StatusCode = 500
                    };
                }

                // Preparar respuesta con los documentos encontrados
                var responseData = new
                {
                    success = true,
                    twinId = twinId,
                    casaId = casaId,
                    message = result.Message,
                    totalCount = result.TotalCount,
                    indexName = result.IndexName,
                    documents = result.Documents.Select(doc => new
                    {
                        id = doc.Id,
                        designedImagesSAS = doc.DesignedImagesSAS,
                        twinId = doc.TwinId,
                        piso = doc.Piso,
                        casaId = doc.CasaId,
                        tipoSeccion = doc.TipoSeccion,
                        nombreSeccion = doc.NombreSeccion,
                        descripcionGenerica = doc.DescripcionGenerica,
                        analisisDetallado = doc.AnalisisDetallado,
                        calidadGeneral = doc.CalidadGeneral,
                        estadoGeneral = doc.EstadoGeneral,
                        tipoPiso = doc.TipoPiso,
                        calidadPiso = doc.CalidadPiso,
                        cortinas = doc.Cortinas,
                        muebles = doc.Muebles,
                        iluminacion = doc.Iluminacion,
                        fotos = doc.Fotos,
                        limpieza = doc.Limpieza,
                        habitabilidad = doc.Habitabilidad,
                        htmlFullDescription = doc.HtmlFullDescription,
                        propiedadId = doc.PropiedadId,
                        fileName = doc.FileName,
                        containerName = doc.ContainerName,
                        filePath = doc.FilePath,
                        url = doc.URL,
                        dimensiones = new
                        {
                            ancho = doc.DimensionesAncho,
                            largo = doc.DimensionesLargo,
                            alto = doc.DimensionesAlto,
                            diametro = doc.DimensionesDiametro
                        },
                        fileSize = doc.FileSize,
                        fileUploadedAt = doc.FileUploadedAt,
                        analyzedAt = doc.AnalyzedAt
                    }).ToList()
                };

                _logger.LogInformation("✅ Retrieved {DocumentCount} photo analysis documents for Twin: {TwinId}, Casa: {CasaId}",
                    result.TotalCount, twinId, casaId);

                return new OkObjectResult(responseData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving photo analysis documents for Twin: {TwinId}, Casa: {CasaId}", twinId, casaId);
                return new ObjectResult(new 
                { 
                    success = false, 
                    errorMessage = $"Error retrieving documents: {ex.Message}" 
                })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Actualiza un documento de análisis de foto MiCasa en el índice Azure Search
        /// </summary>
        [Function("UpdateMiCasaPhotoDocument")]
        public async Task<IActionResult> UpdateMiCasaPhotoDocument(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/photos/{documentId}")] HttpRequest req,
            string twinId,
            string documentId)
        {
            _logger.LogInformation("📷 UpdateMiCasaPhotoDocument function triggered for Twin: {TwinId}, DocumentId: {DocumentId}", twinId, documentId);
            AddCorsHeaders(req);

            try
            {
                // Validar parámetros requeridos
                if (string.IsNullOrEmpty(twinId))
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        errorMessage = "Twin ID parameter is required"
                    });
                }

                if (string.IsNullOrEmpty(documentId))
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        errorMessage = "Document ID parameter is required"
                    });
                }

                // Leer el cuerpo de la solicitud como JSON
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        errorMessage = "Request body cannot be empty"
                    });
                }

                // Deserializar el documento de la solicitud
                var jsonOptions = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                
                var photoDocument = System.Text.Json.JsonSerializer.Deserialize<MiCasaPhotoDocument>(
                    requestBody, 
                    jsonOptions);

                if (photoDocument == null)
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        errorMessage = "Failed to parse MiCasaPhotoDocument from request body"
                    });
                }

                // Validar que el documentId coincida
                if (photoDocument.Id != documentId)
                {
                    photoDocument.Id = documentId;
                }

                // Validar que el twinId coincida
                if (string.IsNullOrEmpty(photoDocument.TwinId) || photoDocument.TwinId != twinId)
                {
                    photoDocument.TwinId = twinId;
                }

                _logger.LogInformation("📸 Updating photo document: {DocumentId}, Section: {Section}, Piso: {Piso}",
                    documentId, photoDocument.NombreSeccion, photoDocument.Piso);

                // Crear instancia del servicio de índice MiCasa Fotos
                var miCasaFotosIndex = new Services.MiCasaFotosIndex(
                    LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<Services.MiCasaFotosIndex>(),
                    _configuration);

                // Actualizar documento en el índice
                var updateResult = await miCasaFotosIndex.UpdatePhotoAnalysisInIndexAsync(documentId, photoDocument);

                if (!updateResult.Success)
                {
                    _logger.LogError("❌ Error updating photo document: {Error}", updateResult.Error);
                    return new ObjectResult(new
                    {
                        success = false,
                        errorMessage = updateResult.Error,
                        documentId = documentId
                    })
                    {
                        StatusCode = 500
                    };
                }

                _logger.LogInformation("✅ Photo document updated successfully: {DocumentId}", documentId);

                return new OkObjectResult(new
                {
                    success = true,
                    documentId = documentId,
                    twinId = twinId,
                    message = updateResult.Message,
                    indexName = updateResult.IndexName,
                    hasVectorEmbeddings = updateResult.HasVectorEmbeddings,
                    vectorDimensions = updateResult.VectorDimensions,
                    updatedAt = DateTime.UtcNow,
                    photoDocument = new
                    {
                        id = photoDocument.Id,
                        twinId = photoDocument.TwinId,
                        piso = photoDocument.Piso,
                        tipoSeccion = photoDocument.TipoSeccion,
                        nombreSeccion = photoDocument.NombreSeccion,
                        descripcionGenerica = photoDocument.DescripcionGenerica,
                        analisisDetallado = photoDocument.AnalisisDetallado,
                        calidadGeneral = photoDocument.CalidadGeneral,
                        estadoGeneral = photoDocument.EstadoGeneral,
                        tipoPiso = photoDocument.TipoPiso,
                        calidadPiso = photoDocument.CalidadPiso,
                        cortinas = photoDocument.Cortinas,
                        muebles = photoDocument.Muebles,
                        iluminacion = photoDocument.Iluminacion,
                        limpieza = photoDocument.Limpieza,
                        habitabilidad = photoDocument.Habitabilidad,
                        htmlFullDescription = photoDocument.HtmlFullDescription,
                        propiedadId = photoDocument.PropiedadId,
                        casaId = photoDocument.CasaId,
                        fileName = photoDocument.FileName,
                        containerName = photoDocument.ContainerName,
                        filePath = photoDocument.FilePath,
                        url = photoDocument.URL,
                        dimensiones = new
                        {
                            ancho = photoDocument.DimensionesAncho,
                            largo = photoDocument.DimensionesLargo,
                            alto = photoDocument.DimensionesAlto,
                            diametro = photoDocument.DimensionesDiametro
                        },
                        fileSize = photoDocument.FileSize,
                        fileUploadedAt = photoDocument.FileUploadedAt,
                        analyzedAt = photoDocument.AnalyzedAt
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating photo document for Twin: {TwinId}, DocumentId: {DocumentId}", twinId, documentId);
                return new ObjectResult(new
                {
                    success = false,
                    errorMessage = $"Error updating photo document: {ex.Message}",
                    documentId = documentId
                })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Maneja la solicitud preflight (OPTIONS) para UpdateMiCasaPhotoDocument y DeleteMiCasaPhotoDocument
        /// Una sola función OPTIONS puede servir para múltiples métodos HTTP en la misma ruta
        /// </summary>
        [Function("MiCasaPhotoDocumentOptions")]
        public IActionResult MiCasaPhotoDocumentOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/photos/{documentId}")] HttpRequest req,
            string twinId,
            string documentId)
        {
            _logger.LogInformation("✅ CORS preflight request handled for MiCasaPhotoDocument (PUT/DELETE)");
            AddCorsHeaders(req);
            return new OkResult();
        }

        /// <summary>
        /// Elimina un documento de análisis de foto MiCasa del índice Azure Search
        /// </summary>
        [Function("DeleteMiCasaPhotoDocument")]
        public async Task<IActionResult> DeleteMiCasaPhotoDocument(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "twins/{twinId}/photos/{documentId}")] HttpRequest req,
            string twinId,
            string documentId)
        {
            _logger.LogInformation("📷 DeleteMiCasaPhotoDocument function triggered for Twin: {TwinId}, DocumentId: {DocumentId}", twinId, documentId);
            AddCorsHeaders(req);

            try
            {
                // Validar parámetros requeridos
                if (string.IsNullOrEmpty(twinId))
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        errorMessage = "Twin ID parameter is required"
                    });
                }

                if (string.IsNullOrEmpty(documentId))
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        errorMessage = "Document ID parameter is required"
                    });
                }

                _logger.LogInformation("🗑️ Deleting photo document: {DocumentId}, TwinId: {TwinId}",
                    documentId, twinId);

                // Crear instancia del servicio de índice MiCasa Fotos
                var miCasaFotosIndex = new Services.MiCasaFotosIndex(
                    LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<Services.MiCasaFotosIndex>(),
                    _configuration);

                // Eliminar documento del índice
                var deleteResult = await miCasaFotosIndex.DeletePhotoAnalysisFromIndexAsync(documentId, twinId);

                if (!deleteResult.Success)
                {
                    _logger.LogError("❌ Error deleting photo document: {Error}", deleteResult.Error);
                    return new ObjectResult(new
                    {
                        success = false,
                        errorMessage = deleteResult.Error,
                        documentId = documentId
                    })
                    {
                        StatusCode = 500
                    };
                }

                _logger.LogInformation("✅ Photo document deleted successfully: {DocumentId}", documentId);

                return new OkObjectResult(new
                {
                    success = true,
                    documentId = documentId,
                    twinId = twinId,
                    message = deleteResult.Message,
                    indexName = deleteResult.IndexName,
                    deletedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting photo document for Twin: {TwinId}, DocumentId: {DocumentId}", twinId, documentId);
                return new ObjectResult(new
                {
                    success = false,
                    errorMessage = $"Error deleting photo document: {ex.Message}",
                    documentId = documentId
                })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Maneja la solicitud preflight (OPTIONS) para DeleteMiCasaPhotoDocument
        /// </summary>
        [Function("DeleteMiCasaPhotoDocumentOptions")]
        public IActionResult DeleteMiCasaPhotoDocumentOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/photos/{documentId}")] HttpRequest req,
            string twinId,
            string documentId)
        {
            _logger.LogInformation("✅ CORS preflight request handled for DeleteMiCasaPhotoDocument");
            AddCorsHeaders(req);
            return new OkResult();
        }
    }

    /// <summary>
    /// Extensión estática para crear DataLakeClientFactory desde IConfiguration
    /// </summary>
    public static class DataLakeConfigurationExtensions
    {
        public static DataLakeClientFactory CreateDataLakeFactory(this IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            var storageSettings = new AzureStorageSettings
            {
                AccountName = configuration["AZURE_STORAGE_ACCOUNT_NAME"] ?? configuration["Values:AZURE_STORAGE_ACCOUNT_NAME"] ?? "",
                AccountKey = configuration["AZURE_STORAGE_ACCOUNT_KEY"] ?? configuration["Values:AZURE_STORAGE_ACCOUNT_KEY"] ?? ""
            };

            var options = Microsoft.Extensions.Options.Options.Create(storageSettings);
            return new DataLakeClientFactory(loggerFactory, options);
        }
    }

    /// <summary>
    /// Representa una solicitud de análisis de foto para MiCasa
    /// Captura todos los parámetros del formulario multipart
    /// </summary>
    public class MiCasaFotoAnalysisRequest
    {
        /// <summary>
        /// ID del Twin
        /// </summary>
        public string TwinId { get; set; } = string.Empty;

        public string Piso { get; set; } = string.Empty;

        /// <summary>
        /// ID de la propiedad
        /// </summary>
        public string? PropiedadId { get; set; }

        public string? CasaId { get; set; }

        /// <summary>
        /// Archivo de imagen a analizar
        /// </summary>
        public IFormFile UploadedFile { get; set; }

        /// <summary>
        /// Tipo de sección (e.g., 'jardin', 'cocina', 'sala', 'comedor', 'baño')
        /// </summary>
        public string TipoSeccion { get; set; } = string.Empty;

        /// <summary>
        /// Nombre de la sección (e.g., 'Jardín Principal', 'Cocina Integrada')
        /// </summary>
        public string NombreSeccion { get; set; } = string.Empty;

        /// <summary>
        /// Lista de propiedades/características de la sección
        /// </summary>
        public List<PropiedadSeccion> Propiedades { get; set; } = new();

        /// <summary>
        /// Descripción adicional de la foto (opcional)
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Observaciones sobre la sección (opcional)
        /// </summary>
        public string? Observaciones { get; set; }

        /// <summary>
        /// Metadata adicional (JSON object)
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();

        /// <summary>
        /// Nombre del contenedor en Azure Storage
        /// </summary>
        public string? ContainerName { get; set; }

        /// <summary>
        /// Ruta completa del archivo en Storage
        /// </summary>
        public string? FilePath { get; set; }

        /// <summary>
        /// Timestamp cuando se subió el archivo
        /// </summary>
        public DateTimeOffset? FileUploadedAt { get; set; }

        /// <summary>
        /// Dimensiones de la sección (ancho, largo, alto, diámetro)
        /// </summary>
        public Dimensiones? Dimensiones { get; set; }

        /// <summary>
        /// Validar que los campos requeridos están presentes
        /// </summary>
        /// <returns>Tupla con validez y mensaje de error</returns>
        public (bool IsValid, string ErrorMessage) Validate()
        {
            if (string.IsNullOrEmpty(TwinId))
                return (false, "TwinID parameter is required");

            if (UploadedFile == null || UploadedFile.Length == 0)
                return (false, "No file uploaded or file is empty");

            var allowedContentTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp", "image/bmp" };
            if (!allowedContentTypes.Contains(UploadedFile.ContentType?.ToLowerInvariant() ?? ""))
                return (false, $"Invalid file type. Allowed types: {string.Join(", ", allowedContentTypes)}");

            if (string.IsNullOrEmpty(TipoSeccion))
                return (false, "tipoSeccion parameter is required (e.g., 'jardin', 'cocina', 'sala')");

            if (string.IsNullOrEmpty(NombreSeccion))
                return (false, "nombreSeccion parameter is required (e.g., 'Jardín Principal')");

            return (true, "");
        }
    }

    /// <summary>
    /// Representa las dimensiones de una sección
    /// </summary>
    public class Dimensiones
    {
        /// <summary>
        /// Ancho en metros o unidad especificada (opcional)
        /// </summary>
        public double? Ancho { get; set; }

        /// <summary>
        /// Largo en metros o unidad especificada (opcional)
        /// </summary>
        public double? Largo { get; set; }

        /// <summary>
        /// Alto en metros o unidad especificada (opcional)
        /// </summary>
        public double? Alto { get; set; }

        /// <summary>
        /// Diámetro en metros o unidad especificada (para objetos circulares, opcional)
        /// </summary>
        public double? Diametro { get; set; }
    }

    /// <summary>
    /// Representa una propiedad de una sección de MiCasa
    /// </summary>
    public class PropiedadSeccion
    {
        /// <summary>
        /// Nombre de la propiedad (e.g., "Capacidad 6 Personas", "Dimensiones", "Estado")
        /// </summary>
        public string Nombre { get; set; } = string.Empty;

        /// <summary>
        /// Valor de la propiedad (opcional, e.g., "30 X 30", "excelente")
        /// </summary>
        public string? Valor { get; set; }

        /// <summary>
        /// Tipo de la propiedad (e.g., "caracteristica", "acabado", "dimension", "estado")
        /// </summary>
        public string Tipo { get; set; } = string.Empty;
    }
}

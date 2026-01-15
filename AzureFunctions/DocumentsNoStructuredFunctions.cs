using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinAgentsNetwork.Agents;
using TwinAgentsNetwork.Services;
using TwinFx.Agents;

namespace TwinAgentsNetwork.AzureFunctions;

public class DocumentsNoStructuredFunctions
{
    private readonly ILogger<DocumentsNoStructuredFunctions> _logger;
    private readonly IConfiguration _configuration;

    public DocumentsNoStructuredFunctions(ILogger<DocumentsNoStructuredFunctions> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [Function("DocumentsNoStructuredFunctions")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }

    [Function("UploadNoStructuredDocumentOptions")]
    public async Task<HttpResponseData> HandleUploadNoStructuredDocumentOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "upload-no-structured-document/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"?? OPTIONS preflight request for upload-no-structured-document/{twinId}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("UploadNoStructuredDocument")]
    public async Task<HttpResponseData> UploadNoStructuredDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "upload-no-structured-document/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("?? UploadNoStructuredDocument function triggered");
        var startTime = DateTime.UtcNow; // Track processing time

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("? Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadNoStructuredDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            _logger.LogInformation($"?? Processing no-structured document upload for Twin ID: {twinId}");

            // Read request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"?? Request body length: {requestBody.Length} characters");

            // Parse JSON request
            var uploadRequest = JsonSerializer.Deserialize<UploadNoStructuredDocumentRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (uploadRequest == null)
            {
                _logger.LogError("? Failed to parse upload request data");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadNoStructuredDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid upload request data format"
                }));
                return badResponse;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(uploadRequest.FileName))
            {
                _logger.LogError("? File name is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadNoStructuredDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "File name is required"
                }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(uploadRequest.FileContent))
            {
                _logger.LogError("? File content is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadNoStructuredDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "File content is required"
                }));
                return badResponse;
            }

            // Extract CustomerID from request
            var customerID = uploadRequest.CustomerID ?? string.Empty;

            _logger.LogInformation($"?? Upload details: FileName={uploadRequest.FileName}, CustomerID={customerID}, FilePath={uploadRequest.FilePath}");

            // Create DataLake client factory
            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

        
            // Convert base64 file content to bytes
            byte[] fileBytes;
            try
            {
                fileBytes = Convert.FromBase64String(uploadRequest.FileContent);
                _logger.LogInformation($"?? File size: {fileBytes.Length} bytes");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Failed to decode base64 file content");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadNoStructuredDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid base64 file content"
                }));
                return badResponse;
            }

            // Determine file path: uploadRequest.FilePath/CustomerID/FileName
            var filePath = !string.IsNullOrEmpty(uploadRequest.FilePath) && !string.IsNullOrEmpty(customerID)
                ? Path.Combine(uploadRequest.FilePath, customerID, uploadRequest.FileName).Replace("\\", "/")
                : !string.IsNullOrEmpty(uploadRequest.FilePath)
                    ? Path.Combine(uploadRequest.FilePath, uploadRequest.FileName).Replace("\\", "/")
                    : uploadRequest.FileName;
          

            _logger.LogInformation($"?? Final file path: {filePath}");

            // Determine MIME type
            var mimeType = GetMimeType(uploadRequest.FileName);
            _logger.LogInformation($"??? MIME type: {mimeType}");
            _logger.LogInformation($"? Using directory-first upload pattern for better performance");

            // Parse file path into directory and filename for the new pattern
            var directoryPath = Path.GetDirectoryName(filePath)?.Replace("\\", "/") ?? "";
            var fileName = Path.GetFileName(filePath);
            
            if (string.IsNullOrEmpty(fileName))
            {
                _logger.LogError("? Invalid file path - no filename found: {FilePath}", filePath);
                var invalidPathResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(invalidPathResponse, req);
                await invalidPathResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadNoStructuredDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Invalid file path - no filename found"
                }));
                return invalidPathResponse;
            }

            _logger.LogInformation($"?? Parsed path - Directory: '{directoryPath}', File: '{fileName}'");

            // Use the directory-first pattern with stream
            using var fileStream = new MemoryStream(fileBytes);
            var uploadSuccess = await dataLakeClient.UploadFileAsync(
                twinId.ToLowerInvariant(), // fileSystemName (must be lowercase for Data Lake Gen2)
                directoryPath,             // directoryName
                fileName,                  // fileName
                fileStream,                // fileData as Stream
                mimeType                   // mimeType
            );

            if (!uploadSuccess)
            {
                _logger.LogError("? Failed to upload file to DataLake");
                var uploadErrorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(uploadErrorResponse, req);
                await uploadErrorResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadNoStructuredDocumentResponse
                {
                    Success = false,
                    ErrorMessage = "Failed to upload file to storage"
                }));
                return uploadErrorResponse;
            }

            _logger.LogInformation($"? File uploaded successfully: {filePath}");

            // Initialize document analysis variables
            int totalPaginas = 0;
            bool tieneIndice = false;

            // Process document with DocumentsNoStructuredAgent to extract data
            _logger.LogInformation("?? Processing document with DocumentsNoStructuredAgent for data extraction...");
            try
            {
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var agentLogger = loggerFactory.CreateLogger<DocumentsNoStructuredAgent>();
                var noStructuredAgent = new DocumentsNoStructuredAgent(agentLogger, _configuration,uploadRequest.Model);
                int StartIndex = uploadRequest.StartIndex;
                int endIndex = uploadRequest.EndIndex;
                // Call the ExtractDocumentDataAsync method
                var aiResult = await noStructuredAgent.ExtractDocumentDataAsync( 
                     
                    StartIndex,
                    endIndex,
                    uploadRequest.TieneIndice,
                    uploadRequest.RequiereTraduccion,
                    uploadRequest.IdiomaDestino,
                    twinId.ToLowerInvariant(),    // containerName (file system name)
                    directoryPath,                // filePath (directory within file system)
                    fileName , customerID                    // fileName
                                     // subcategoria (from request)
                );

                if (aiResult.Success)
                {
                    // Extract document metadata from AI results
                    totalPaginas = aiResult.TotalPages;
                    
                    // Determine if document has an index based on content analysis
                    tieneIndice = DetermineIfDocumentHasIndex(aiResult);

                 
                }
                else
                {
                    _logger.LogWarning($"?? DocumentsNoStructuredAgent processing failed: {aiResult.ErrorMessage}");
                    // Set default values if AI processing fails
                    totalPaginas = 1; // Default to 1 page if we can't determine
                    tieneIndice = false;
                }
            }
            catch (Exception aiEx)
            {
                _logger.LogError(aiEx, "? Error during DocumentsNoStructuredAgent processing");
                // Continue with the upload response even if AI processing fails
                totalPaginas = 1; // Default to 1 page if we can't determine
                tieneIndice = false;
            }

            // Get file info to extract metadata
            var fileInfo = await dataLakeClient.GetFileInfoAsync(filePath);
            
            // Generate SAS URL for access
            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));

            _logger.LogInformation($"? File uploaded successfully: {filePath}");

            // Calculate processing time
            var processingTime = DateTime.UtcNow - startTime;
            
            var processingMessage = "El documento no estructurado ha sido procesado por la IA exitosamente";
            
            _logger.LogInformation($"?? Complete processing finished in {processingTime.TotalSeconds:F2} seconds");

            // Create simplified response for UI
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new UploadNoStructuredDocumentResponse
            {
                Success = true,
                TwinId = twinId,
                FileName = fileName,
                FilePath = filePath,
                ContainerName = twinId.ToLowerInvariant(),
                FileSize = fileBytes.Length,
                MimeType = mimeType,
                Url = sasUrl,
                UploadedAt = DateTime.UtcNow,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = processingMessage,
                Metadata = fileInfo?.Metadata,
                
                // New fields requested 
                TotalPaginas = totalPaginas,
                TieneIndice = tieneIndice ? "Sí" : "No"
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "? Error uploading no-structured document after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadNoStructuredDocumentResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                Message = "Error durante el procesamiento del documento no estructurado"
            }));
            
            return errorResponse;
        }
    }

    [Function("SearchNoStructuredDocumentsOptions")]
    public async Task<HttpResponseData> HandleSearchNoStructuredDocumentsOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "search-no-structured-documents/{twinId}/{estructura}")] HttpRequestData req,
        string twinId,
        string estructura)
    {
        _logger.LogInformation($"?? OPTIONS preflight request for search-no-structured-documents/{twinId}/{estructura}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("SearchNoStructuredDocuments")]
    public async Task<HttpResponseData> SearchNoStructuredDocuments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "search-no-structured-documents/{twinId}/{estructura}")] HttpRequestData req,
        string twinId,
        string estructura)
    {
        _logger.LogInformation("?? SearchNoStructuredDocuments function triggered for TwinID: {TwinId}, Estructura: {Estructura}", twinId, estructura);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("? Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new { Success = false, ErrorMessage = "Twin ID parameter is required" }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(estructura))
            {
                _logger.LogError("? Estructura parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new { Success = false, ErrorMessage = "Estructura parameter is required" }));
                return badResponse;
            }

            _logger.LogInformation("?? Searching no-structured documents for TwinID: {TwinId}, Estructura: {Estructura}", twinId, estructura);

            // Initialize DocumentsNoStructuredIndex
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var indexLogger = loggerFactory.CreateLogger<DocumentsNoStructuredIndex>();
            var documentsIndex = new DocumentsNoStructuredIndex(indexLogger, _configuration);

            // Call search method
            var searchResult = await documentsIndex.SearchByEstructuraAndTwinAsync(estructura, twinId);

            // Create response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            await response.WriteStringAsync(JsonSerializer.Serialize(searchResult, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            _logger.LogInformation("? Search completed successfully. Found {DocumentCount} documents with {ChapterCount} total chapters", 
                searchResult.Documents.Count, searchResult.TotalChapters);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error searching no-structured documents");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                Success = false,
                ErrorMessage = ex.Message
            }));

            return errorResponse;
        }
    }

    [Function("SearchNoStructuredDocumentsMetadataOptions")]
    public async Task<HttpResponseData> HandleSearchNoStructuredDocumentsMetadataOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "search-no-structured-documents-metadata/{twinId}/{estructura}")] HttpRequestData req,
        string twinId,
        string estructura)
    {
        _logger.LogInformation($"?? OPTIONS preflight request for search-no-structured-documents-metadata/{twinId}/{estructura}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("SearchNoStructuredDocumentsMetadata")]
    public async Task<HttpResponseData> SearchNoStructuredDocumentsMetadata(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "search-no-structured-documents-metadata/{twinId}/{estructura}")] HttpRequestData req,
        string twinId,
        string estructura)
    {
        _logger.LogInformation("?? SearchNoStructuredDocumentsMetadata function triggered for TwinID: {TwinId}, Estructura: {Estructura}", twinId, estructura);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("? Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new { Success = false, ErrorMessage = "Twin ID parameter is required" }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(estructura))
            {
                _logger.LogError("? Estructura parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new { Success = false, ErrorMessage = "Estructura parameter is required" }));
                return badResponse;
            }

            _logger.LogInformation("?? Searching no-structured documents metadata for TwinID: {TwinId}, Estructura: {Estructura}", twinId, estructura);

            // Initialize DocumentsNoStructuredIndex
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var indexLogger = loggerFactory.CreateLogger<DocumentsNoStructuredIndex>();
            var documentsIndex = new DocumentsNoStructuredIndex(indexLogger, _configuration);

            // Call search metadata method - NO chapters content
            var searchResult = await documentsIndex.SearchDocumentMetadataByEstructuraAndTwinAsync(estructura, twinId);

            // Create response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            await response.WriteStringAsync(JsonSerializer.Serialize(searchResult, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            _logger.LogInformation("? Metadata search completed successfully. Found {DocumentCount} documents metadata with {ChapterCount} total chapters", 
                searchResult.Documents.Count, searchResult.TotalChapters);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error searching no-structured documents metadata");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                Success = false,
                ErrorMessage = ex.Message
            }));

            return errorResponse;
        }
    }

    [Function("GetNoStructuredDocumentOptions")]
    public async Task<HttpResponseData> HandleGetNoStructuredDocumentOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-no-structured-document/{twinId}/{documentId}")] HttpRequestData req,
        string twinId,
        string documentId)
    {
        _logger.LogInformation($"?? OPTIONS preflight request for get-no-structured-document/{twinId}/{documentId}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("GetNoStructuredDocument")]
    public async Task<HttpResponseData> GetNoStructuredDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-no-structured-document/{twinId}/{documentId}")] HttpRequestData req,
        string twinId,
        string documentId)
    {
        _logger.LogInformation("?? GetNoStructuredDocument function triggered for TwinID: {TwinId}, DocumentID: {DocumentId}", twinId, documentId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("? Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new { Success = false, ErrorMessage = "Twin ID parameter is required" }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(documentId))
            {
                _logger.LogError("? DocumentID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new { Success = false, ErrorMessage = "DocumentID parameter is required" }));
                return badResponse;
            }

            _logger.LogInformation("?? Getting specific no-structured document for TwinID: {TwinId}, FileName: {DocumentId}", twinId, documentId);

            // Initialize DocumentsNoStructuredIndex
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var indexLogger = loggerFactory.CreateLogger<DocumentsNoStructuredIndex>();
            var documentsIndex = new DocumentsNoStructuredIndex(indexLogger, _configuration);

            // Get specific document chapters by TwinID and FileName (documentId parameter represents FileName)
            var chapters = await documentsIndex.GetDocumentByTwinIdAndDocumentIdAsync(twinId, documentId);

            if (chapters == null || chapters.Count == 0)
            {
                _logger.LogWarning("?? No chapters found for TwinID: {TwinId}, FileName: {DocumentId}", twinId, documentId);
                
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                AddCorsHeaders(notFoundResponse, req);
                await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    Success = false,
                    ErrorMessage = $"No document found with FileName '{documentId}' for TwinID '{twinId}'",
                    TwinId = twinId,
                    FileName = documentId
                }));
                return notFoundResponse;
            }

            // Calculate summary statistics from chapters
            var totalTokens = chapters.Sum(c => c.TotalTokens + c.TotalTokensSub);
            var totalPages = CalculateDocumentTotalPages(chapters);
            var fileName = chapters.First().FileName;
            var filePath = chapters.First().FilePath;
            var subcategoria = chapters.First().Subcategoria;

            _logger.LogInformation("? Document retrieved successfully. FileName: {FileName}, Chapters: {ChapterCount}, Tokens: {TotalTokens}", 
                fileName, chapters.Count, totalTokens);

            // Create success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            // Wrap the chapters list in a response structure with metadata
            var responseData = new
            {
                Success = true,
                TwinId = twinId,
                FileName = fileName,
                FilePath = filePath,
                Subcategoria = subcategoria,
                TotalChapters = chapters.Count,
                DocumentData = chapters,
                TotalTokens = totalTokens,
                TotalPages = totalPages,
                Chapters = chapters,
                Message = $"Document retrieved successfully with {chapters.Count} chapters"
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting no-structured document for TwinID: {TwinId}, DocumentID: {DocumentId}", twinId, documentId);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                Success = false,
                ErrorMessage = ex.Message,
                TwinId = twinId,
                DocumentId = documentId
            }));

            return errorResponse;
        }
    }

    [Function("DeleteNoStructuredDocumentOptions")]
    public async Task<HttpResponseData> HandleDeleteNoStructuredDocumentOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "delete-no-structured-document/{twinId}/{documentId}")] HttpRequestData req,
        string twinId,
        string documentId)
    {
        _logger.LogInformation($"??? OPTIONS preflight request for delete-no-structured-document/{twinId}/{documentId}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    [Function("DeleteNoStructuredDocument")]
    public async Task<HttpResponseData> DeleteNoStructuredDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "delete-no-structured-document/{twinId}/{documentId}")] HttpRequestData req,
        string twinId,
        string documentId)
    {
        _logger.LogInformation("??? DeleteNoStructuredDocument function triggered for TwinID: {TwinId}, DocumentID: {DocumentId}", twinId, documentId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("? Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new { Success = false, ErrorMessage = "Twin ID parameter is required" }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(documentId))
            {
                _logger.LogError("? DocumentID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new { Success = false, ErrorMessage = "DocumentID parameter is required" }));
                return badResponse;
            }

            _logger.LogInformation("??? Deleting no-structured document for TwinID: {TwinId}, DocumentID: {DocumentId}", twinId, documentId);

            // Initialize DocumentsNoStructuredIndex
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var indexLogger = loggerFactory.CreateLogger<DocumentsNoStructuredIndex>();
            var documentsIndex = new DocumentsNoStructuredIndex(indexLogger, _configuration);

            // Delete document by TwinID and DocumentID
            var deleteResult = await documentsIndex.DeleteDocumentByDocumentIdAsync(documentId, twinId);

            if (!deleteResult.Success)
            {
                _logger.LogError("? Failed to delete document for TwinID: {TwinId}, DocumentID: {DocumentId} - {Error}", 
                    twinId, documentId, deleteResult.Error);
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new { 
                    Success = false, 
                    ErrorMessage = deleteResult.Error ?? "Unknown error occurred during deletion",
                    DocumentId = documentId,
                    deleteResult.DeletedChaptersCount,
                    deleteResult.TotalChaptersFound,
                    deleteResult.Errors
                }));
                return errorResponse;
            }

            // Create success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            // Wrap the delete result in a response structure
            var responseData = new
            {
                Success = true,
                DocumentId = documentId,
                deleteResult.DeletedChaptersCount,
                deleteResult.TotalChaptersFound,
                deleteResult.Message,
                deleteResult.Errors
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            _logger.LogInformation("? Document deleted successfully. DocumentID: {DocumentId}, Deleted Chapters: {DeletedCount}/{TotalCount}", 
                documentId, deleteResult.DeletedChaptersCount, deleteResult.TotalChaptersFound);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error deleting no-structured document for TwinID: {TwinId}, DocumentID: {DocumentId}", twinId, documentId);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                Success = false,
                ErrorMessage = ex.Message,
                DocumentId = documentId
            }));

            return errorResponse;
        }
    }

    [Function("AnswerSearchQuestionFx")]
    public async Task<HttpResponseData> AnswerSearchQuestionFx(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "answer-search-question/{twinId}/{fileName}")] HttpRequestData req,
        string twinId,
        string fileName)
    {
        _logger.LogInformation("?? AnswerSearchQuestionFx function triggered for TwinID: {TwinId}, FileName: {FileName}", twinId, fileName);

        TwinAgentDocumentRequest? questionRequest = null; // Declare at function scope

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("? Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new { Success = false, ErrorMessage = "Twin ID parameter is required" }));
                return badResponse;
            }

            if (string.IsNullOrEmpty(fileName))
            {
                _logger.LogError("? FileName parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new { Success = false, ErrorMessage = "FileName parameter is required" }));
                return badResponse;
            }

            // Read request body to get the question
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"?? Request body length: {requestBody.Length} characters");

            // Parse JSON request
            questionRequest = JsonSerializer.Deserialize<TwinAgentDocumentRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (questionRequest == null || string.IsNullOrEmpty(questionRequest.Question))
            {
                _logger.LogError("? Question is required in request body");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new { Success = false, ErrorMessage = "Question is required in request body" }));
                return badResponse;
            }

            _logger.LogInformation("?? Processing question: {Question}", questionRequest.Question);

            // Initialize DocumentsNoStructuredAgent
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var agentLogger = loggerFactory.CreateLogger<DocumentsNoStructuredAgent>();
            
            // Use the model from the request if provided, otherwise default to "gpt4mini"
            var modeloNombre = !string.IsNullOrEmpty(questionRequest.ModeloNombre) ? questionRequest.ModeloNombre : "gpt4mini";
            var noStructuredAgent = new DocumentsNoStructuredAgent(agentLogger, _configuration, modeloNombre);

            // Call the AnswerSearchQuestion method
            var aiResponse = await noStructuredAgent.AnswerSearchQuestion(
                questionRequest.Idioma,
                questionRequest.Question, 
                twinId, fileName);

            // Create success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new TwinAgentDocumentResponse
            {
                Success = true,
                Question = questionRequest.Question,
                Answer = aiResponse,
                TwinId = twinId,
                FileName = fileName,
                ModeloNombre = modeloNombre,
                Idioma = questionRequest.Idioma ?? "es", // Default to Spanish if not provided
                ProcessingTimeMs = 0, // Could be enhanced with actual timing
                ProcessedAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            _logger.LogInformation("? Question answered successfully for TwinID: {TwinId}, FileName: {FileName}", twinId, fileName);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error answering search question for TwinID: {TwinId}, FileName: {FileName}", twinId, fileName);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new TwinAgentDocumentResponse
            {
                Success = false,
                Question = questionRequest?.Question ?? "",
                Answer = "",
                TwinId = twinId,
                FileName = fileName,
                ModeloNombre = questionRequest?.ModeloNombre ?? "gpt4mini",
                Idioma = questionRequest?.Idioma ?? "es",
                ProcessingTimeMs = 0,
                ProcessedAt = DateTime.UtcNow,
                ErrorMessage = ex.Message
            }));

            return errorResponse;
        }
    }

    [Function("AnswerSearchAllDocumentsQuestionFx")]
    public async Task<HttpResponseData> AnswerSearchAllDocumentsQuestionFx(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "answer-search-all-documents/{twinId}/{fileName}")] HttpRequestData req,
        string twinId
       )
    {
        _logger.LogInformation("?? AnswerSearchAllDocumentsQuestionFx function triggered for TwinID: {TwinId}", twinId );

        TwinAgentDocumentRequest? questionRequest = null; // Declare at function scope

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("? Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new { Success = false, ErrorMessage = "Twin ID parameter is required" }));
                return badResponse;
            }

         

            // Read request body to get the question
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"?? Request body length: {requestBody.Length} characters");

            // Parse JSON request
            questionRequest = JsonSerializer.Deserialize<TwinAgentDocumentRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (questionRequest == null || string.IsNullOrEmpty(questionRequest.Question))
            {
                _logger.LogError("? Question is required in request body");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new { Success = false, ErrorMessage = "Question is required in request body" }));
                return badResponse;
            }

            _logger.LogInformation("?? Processing question: {Question}", questionRequest.Question);

            // Initialize DocumentsNoStructuredAgent
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var agentLogger = loggerFactory.CreateLogger<DocumentsNoStructuredAgent>();

            // Use the model from the request if provided, otherwise default to "gpt4mini"
            var modeloNombre = !string.IsNullOrEmpty(questionRequest.ModeloNombre) ? questionRequest.ModeloNombre : "gpt4mini";
            var noStructuredAgent = new DocumentsNoStructuredAgent(agentLogger, _configuration, modeloNombre);

            // Call the AnswerSearchQuestion method
            var aiResponse = await noStructuredAgent.AnswerSearchAllDocumentsQuestion(
                questionRequest.Idioma,
                questionRequest.Question,
                twinId );

            // Create success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            var responseData = new TwinAgentDocumentResponse
            {
                Success = true,
                Question = questionRequest.Question,
                Answer = aiResponse,
                TwinId = twinId, 
                ModeloNombre = modeloNombre,
                Idioma = questionRequest.Idioma ?? "es", // Default to Spanish if not provided
                ProcessingTimeMs = 0, // Could be enhanced with actual timing
                ProcessedAt = DateTime.UtcNow
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            _logger.LogInformation("? Question answered successfully for TwinID: {TwinId}", twinId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error answering search question for TwinID: {TwinId}", twinId);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new TwinAgentDocumentResponse
            {
                Success = false,
                Question = questionRequest?.Question ?? "",
                Answer = "",
                TwinId = twinId, 
                ModeloNombre = questionRequest?.ModeloNombre ?? "gpt4mini",
                Idioma = questionRequest?.Idioma ?? "es",
                ProcessingTimeMs = 0,
                ProcessedAt = DateTime.UtcNow,
                ErrorMessage = ex.Message
            }));

            return errorResponse;
        }
    }

    /// <summary>
    /// OPTIONS handler for GetUniqueDocuments endpoint
    /// </summary>
    [Function("GetUniqueDocumentsOptions")]
    public async Task<HttpResponseData> HandleGetUniqueDocumentsOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "get-unique-documents/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("?? OPTIONS preflight request for get-unique-documents/{TwinId}", twinId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        AddCorsHeaders(response, req);
        await response.WriteStringAsync("");
        return response;
    }

    /// <summary>
    /// Get all unique documents for a TwinID (one per documentID, no duplicates)
    /// GET /api/get-unique-documents/{twinId}?top=50&customerID=xxx
    /// </summary>
    [Function("GetUniqueDocuments")]
    public async Task<HttpResponseData> GetUniqueDocuments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "get-unique-documents/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation("?? GetUniqueDocuments function triggered for TwinID: {TwinId}", twinId);

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                _logger.LogError("? Twin ID parameter is required");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new 
                { 
                    success = false, 
                    error = "Twin ID parameter is required" 
                }));
                return badResponse;
            }

            // Parse optional query parameters
            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var topResultsStr = queryParams["top"] ?? "50";
            var customerID = queryParams["customerID"]; // Optional customerID filter

            if (!int.TryParse(topResultsStr, out int topResults))
            {
                topResults = 50;
            }

            // Validate topResults range
            if (topResults < 1 || topResults > 1000)
            {
                _logger.LogWarning("?? Invalid topResults value: {TopResults}, using default 50", topResults);
                topResults = 50;
            }

            _logger.LogInformation("?? Getting unique documents for TwinID: {TwinID}, CustomerID: {CustomerID}, TopResults={TopResults}",
                twinId, customerID ?? "ALL", topResults);

            // Initialize DocumentIndex
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var indexLogger = loggerFactory.CreateLogger<DocumentIndex>();
            var documentIndex = new DocumentIndex(indexLogger, _configuration);

            // Call GetUniqueDocumentsByTwinIdAsync with optional customerID
            var searchResult = await documentIndex.GetUniqueDocumentsByTwinIdAsync(
                twinId: twinId,
                customerID: customerID,  // Pass customerID filter
                topResults: topResults
            );

            if (!searchResult.Success)
            {
                _logger.LogError("? Get unique documents failed: {Error}", searchResult.Error);
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = searchResult.Error,
                    twinId,
                    customerID
                }));
                return errorResponse;
            }

            // Create success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            response.Headers.Add("Content-Type", "application/json");

            // Transform documents to clean JSON
            var documentList = searchResult.Documents.Select(doc => new
            {
                id = doc.Id,
                twinID = doc.TwinID,
                documentID = doc.DocumentID,
                customerID = doc.CustomerID,
                tituloDocumento = doc.TituloDocumento,
                documentName = doc.DocumentName,
                resumenEjecutivo = doc.ResumenEjecutivo,
                totalPages = doc.TotalPages,
                totalTokensInput = doc.TotalTokensInput,
                totalTokensOutput = doc.TotalTokensOutput,
                filePath = doc.FilePath,
                fileName = doc.FileName, 
                URL = doc.URL, 
                processedAt = doc.ProcessedAt
              }).ToList(); 

            var responseData = new
            {
                success = true,
                message = searchResult.Message,
                twinId,
                customerID,
                totalCount = searchResult.TotalCount,
                documentsReturned = documentList.Count,
                documents = documentList
            };

            await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

            _logger.LogInformation("? Retrieved {UniqueCount} unique documents successfully", documentList.Count);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Error getting unique documents");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            }));

            return errorResponse;
        }
    }

    /// <summary>
    /// Calculate total pages from a list of chapters
    /// </summary>
    private static int CalculateDocumentTotalPages(List<TwinAgentsNetwork.Services.ExractedChapterSubsIndex> chapters)
    {
        if (chapters == null || chapters.Count == 0)
            return 0;

        var minPage = chapters
            .Select(c => Math.Min(
                c.FromPageChapter == 0 ? c.FromPageSub : c.FromPageChapter,
                c.FromPageSub == 0 ? c.FromPageChapter : c.FromPageSub))
            .Where(p => p > 0)
            .DefaultIfEmpty(1)
            .Min();

        var maxPage = chapters
            .Select(c => Math.Max(c.ToPageChapter, c.ToPageSub))
            .Where(p => p > 0)
            .DefaultIfEmpty(1)
            .Max();

        return Math.Max(1, maxPage - minPage + 1);
    }

    /// <summary>
    /// Determines if the document has an index based on AI analysis results
    /// </summary>
    private bool DetermineIfDocumentHasIndex(UnstructuredDocumentResult aiResult)
    {
        try
        {
            // Check if any section mentions "índice", "index", "tabla de contenidos", "contents"
            var indexKeywords = new[] { "índice", "index", "tabla de contenidos", "contents", "contenido", "tabla de contenido", "sumario" };
            
            // Check in extracted content
            if (aiResult.ExtractedContent?.ImportantSections != null)
            {
                foreach (var section in aiResult.ExtractedContent.ImportantSections)
                {
                    var sectionText = $"{section.Title} {section.Content}".ToLowerInvariant();
                    if (indexKeywords.Any(keyword => sectionText.Contains(keyword)))
                    {
                        return true;
                    }
                }
            }

            // Check in key insights
            if (aiResult.KeyInsights != null)
            {
                foreach (var insight in aiResult.KeyInsights)
                {
                    var insightText = $"{insight.Insight} {insight.Details}".ToLowerInvariant();
                    if (indexKeywords.Any(keyword => insightText.Contains(keyword)))
                    {
                        return true;
                    }
                }
            }

            // Check in executive summary
            if (!string.IsNullOrEmpty(aiResult.ExecutiveSummary))
            {
                var summaryText = aiResult.ExecutiveSummary.ToLowerInvariant();
                if (indexKeywords.Any(keyword => summaryText.Contains(keyword)))
                {
                    return true;
                }
            }

            // Check raw text content for index patterns
            if (!string.IsNullOrEmpty(aiResult.RawTextContent))
            {
                var rawText = aiResult.RawTextContent.ToLowerInvariant();
                
                // Look for common index patterns
                var indexPatterns = new[]
                {
                    "índice",
                    "tabla de contenidos",
                    "contenido",
                    "sumario",
                    "capítulo 1",
                    "chapter 1",
                    "página",
                    "page"
                };

                var indexCount = indexPatterns.Count(pattern => rawText.Contains(pattern));
                
                // If we find multiple index-related terms, likely has an index
                if (indexCount >= 2)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "?? Error determining if document has index, defaulting to false");
            return false;
        }
    }

    private static string GetMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".7z" => "application/x-7z-compressed",
            _ => "application/octet-stream"
        };
    }

    private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
    {
        // Get origin from request headers
        var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
        var origin = originHeader.Key != null ? originHeader.Value?.FirstOrDefault() : null;
        
        // Allow specific origins for development
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

/// <summary>
/// Request model for no-structured document upload
/// </summary>
public class UploadNoStructuredDocumentRequest
{
    /// <summary>
    /// File name with extension
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Customer ID for organizing documents
    /// </summary>
    public string CustomerID { get; set; } = string.Empty;

    /// <summary>
    /// Base64 encoded file content
    /// </summary>
    public string FileContent { get; set; } = string.Empty;

    /// <summary>
    /// Optional container name (defaults to twinId if not provided)
    /// </summary>
    public string? ContainerName { get; set; }

    /// <summary>
    /// Optional file path within container
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Optional MIME type (auto-detected if not provided)
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Total number of pages in the document
    /// </summary>
    public int TotalPaginas { get; set; } = 0;

    /// <summary>
    /// Indicates if the document has an index/table of contents
    /// </summary>
    public bool TieneIndice { get; set; } = false;

    /// <summary>
    /// Indicates if the document requires translation
    /// </summary>
    public bool RequiereTraduccion { get; set; } = false;

    /// <summary>
    /// Target language code for translation (e.g., 'en', 'fr', 'de')
    /// </summary>
    public string? IdiomaDestino { get; set; }

    /// <summary>
    /// AI model name to use for processing
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Start page index for document processing
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// End page index for document processing
    /// </summary>
    public int EndIndex { get; set; }
}

/// <summary>
/// Response model for no-structured document upload
/// </summary>
public class UploadNoStructuredDocumentResponse
{
    /// <summary>
    /// Whether the upload was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if upload failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Success message for UI display
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Total processing time in seconds
    /// </summary>
    public double ProcessingTimeSeconds { get; set; }

    /// <summary>
    /// Twin ID
    /// </summary>
    public string TwinId { get; set; } = string.Empty;

    /// <summary>
    /// Uploaded file name
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// File path in storage
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Container name in storage
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// MIME type of the file
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// SAS URL for accessing the file
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// When the file was uploaded
    /// </summary>
    public DateTime UploadedAt { get; set; }

    /// <summary>
    /// File metadata from storage
    /// </summary>
    public IDictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Document structure type (e.g., "no-estructurado")
    /// </summary>
    public string Estructura { get; set; } = string.Empty;

    /// <summary>
    /// Document subcategory (e.g., "general", "contratos", "manuales")
    /// </summary>
    public string Subcategoria { get; set; } = string.Empty;

    /// <summary>
    /// Total number of pages in the document
    /// </summary>
    public int TotalPaginas { get; set; }

    /// <summary>
    /// Whether the document has an index ("Sí" or "No")
    /// </summary>
    public string TieneIndice { get; set; } = string.Empty;
}

/// <summary>
/// Request model for TwinAgentDocument function
/// </summary>
public class TwinAgentDocumentRequest
{
    /// <summary>
    /// Question from the user about the document
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// AI model name to use for processing (e.g., "gpt4mini", "gpt-4", "gpt-3.5-turbo")
    /// </summary>
    public string? ModeloNombre { get; set; }

    /// <summary>
    /// Language for the response (e.g., "es", "en", "fr")
    /// </summary>
    public string? Idioma { get; set; }
}

/// <summary>
/// Response model for TwinAgentDocument function
/// </summary>
public class TwinAgentDocumentResponse
{
    /// <summary>
    /// Whether the operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The original question asked by the user
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// AI-generated answer based on document content
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Twin ID
    /// </summary>
    public string TwinId { get; set; } = string.Empty;

    /// <summary>
    /// Document file name
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// AI model name used for processing
    /// </summary>
    public string ModeloNombre { get; set; } = string.Empty;

    /// <summary>
    /// Language used for the response
    /// </summary>
    public string Idioma { get; set; } = string.Empty;

    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public double ProcessingTimeMs { get; set; }

    /// <summary>
    /// When the question was processed
    /// </summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// Error message if Success = false
    /// </summary>
    public string? ErrorMessage { get; set; }
}

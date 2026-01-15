using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using TwinAgentsNetwork.Agents;
using TwinAgentsLibrary.Models;
using Microsoft.Azure.Cosmos;
using System.ComponentModel.DataAnnotations;

namespace TwinAgentsNetwork.AzureFunctions;

public class SemiStructuredDataFx
{
    private readonly ILogger<SemiStructuredDataFx> _logger;
    private readonly AgentTwinSemiStructured _agentTwinSemiStructured;
    private readonly CosmosClient _cosmosClient;
    private readonly string _databaseName;
    private readonly string _containerName;

    public SemiStructuredDataFx(ILogger<SemiStructuredDataFx> logger, AgentTwinSemiStructured agentTwinSemiStructured)
    {
        _logger = logger;
        _agentTwinSemiStructured = agentTwinSemiStructured;

        // Initialize Cosmos DB client
        var cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT") ?? throw new InvalidOperationException("COSMOS_ENDPOINT not configured");
        var cosmosKey = Environment.GetEnvironmentVariable("COSMOS_KEY") ?? throw new InvalidOperationException("COSMOS_KEY not configured");
        _databaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME") ?? "TwinHumanDB";
        _containerName = "TwinSemiStructured";

        _cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey);
    }

    /// <summary>
    /// Azure Function to process semi-structured data and save to Cosmos DB
    /// </summary>
    /// <param name="req">HTTP request containing semi-structured data processing parameters</param>
    /// <returns>JSON response with processing results and Cosmos DB save confirmation</returns>
    [Function("ProcessSemiStructuredData")]
    public async Task<HttpResponseData> ProcessSemiStructuredData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twin-semistructured/process")] HttpRequestData req)
    {
        _logger.LogInformation("?? ProcessSemiStructuredData function triggered");

        try
        {
            // Read and parse the request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            
            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogError("? Request body is empty");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Request body is required"
                }));
                return badResponse;
            }

            // Deserialize the request
            var requestData = JsonSerializer.Deserialize<SemiStructuredDataRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Validate required parameters
            var validationResult = ValidateRequest(requestData);
            if (!validationResult.IsValid)
            {
                _logger.LogError($"? Validation error: {validationResult.ErrorMessage}");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = validationResult.ErrorMessage
                }));
                return badResponse;
            }

            _logger.LogInformation($"?? Processing semi-structured data for Twin ID: {requestData!.TwinId}, Format: {requestData.DataFormat}");

            // Process the semi-structured data using the AI agent
            var analysisResult = await _agentTwinSemiStructured.ProcessSemiStructuredDataAsync(
                requestData.TwinId,
                requestData.SemiStructuredData,
                requestData.DataFormat ?? "json",
                requestData.Language ?? "English",
                requestData.AnalysisType ?? "insights"
            );

            if (!analysisResult.Success)
            {
                _logger.LogError($"? AI processing failed: {analysisResult.ErrorMessage}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = $"AI processing failed: {analysisResult.ErrorMessage}"
                }));
                return errorResponse;
            }

            // Create SemistructuredDocument from the analysis result and request data
            var semiStructuredDoc = CreateSemistructuredDocument(requestData, analysisResult);

            // Save to Cosmos DB
            var cosmosResult = await SaveToCosmosDB(semiStructuredDoc);

            if (!cosmosResult.Success)
            {
                _logger.LogError($"? Cosmos DB save failed: {cosmosResult.ErrorMessage}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = $"Database save failed: {cosmosResult.ErrorMessage}",
                    analysisResult = analysisResult // Still return the analysis even if save failed
                }));
                return errorResponse;
            }

            _logger.LogInformation($"? Successfully processed and saved semi-structured data for Twin ID: {requestData.TwinId}");

            // Create successful response
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = true,
                twinId = requestData.TwinId,
                documentId = semiStructuredDoc.Id,
                dataFormat = requestData.DataFormat,
                analysisType = requestData.AnalysisType,
                language = requestData.Language,
                analysisResult = analysisResult,
                cosmosDb = new
                {
                    saved = true,
                    documentId = semiStructuredDoc.Id,
                    containerName = _containerName,
                    etag = cosmosResult.ETag
                },
                processedAt = DateTime.UtcNow
            }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (JsonException ex)
        {
            _logger.LogError($"? JSON parsing error: {ex.Message}");
            var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = "Invalid JSON format in request body"
            }));
            return errorResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError($"? Unexpected error in ProcessSemiStructuredData: {ex.Message}");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = "An unexpected error occurred while processing semi-structured data"
            }));
            return errorResponse;
        }
    }

    /// <summary>
    /// Azure Function to retrieve semi-structured data by Twin ID
    /// </summary>
    [Function("GetSemiStructuredData")]
    public async Task<HttpResponseData> GetSemiStructuredData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twin-semistructured/{twinId}")] HttpRequestData req,
        string twinId)
    {
        _logger.LogInformation($"?? GetSemiStructuredData function triggered for Twin ID: {twinId}");

        try
        {
            if (string.IsNullOrEmpty(twinId))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                AddCorsHeaders(badResponse, req);
                await badResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    errorMessage = "Twin ID parameter is required"
                }));
                return badResponse;
            }

            // Query Cosmos DB for documents by TwinId
            var documents = await QueryDocumentsByTwinId(twinId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = true,
                twinId = twinId,
                documentCount = documents.Count,
                documents = documents
            }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"? Error retrieving semi-structured data: {ex.Message}");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            AddCorsHeaders(errorResponse, req);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage = "Error retrieving semi-structured data"
            }));
            return errorResponse;
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Validates the incoming request data
    /// </summary>
    private ValidationResult ValidateRequest(SemiStructuredDataRequest? requestData)
    {
        if (requestData == null)
        {
            return new ValidationResult { IsValid = false, ErrorMessage = "Request data is required" };
        }

        if (string.IsNullOrEmpty(requestData.TwinId))
        {
            return new ValidationResult { IsValid = false, ErrorMessage = "TwinId parameter is required" };
        }

        if (string.IsNullOrEmpty(requestData.SemiStructuredData))
        {
            return new ValidationResult { IsValid = false, ErrorMessage = "SemiStructuredData parameter is required" };
        }

        return new ValidationResult { IsValid = true };
    }

    /// <summary>
    /// Creates a SemistructuredDocument from the request and analysis result
    /// </summary>
    private SemistructuredDocument CreateSemistructuredDocument(SemiStructuredDataRequest requestData, TwinSemiStructuredResult analysisResult)
    {
        var document = new SemistructuredDocument
        {
            Id = requestData.DocumentId ?? Guid.NewGuid().ToString(),
            TwinId = requestData.TwinId,
            DocumentType = requestData.DocumentType ?? analysisResult.DataFormat,
            FileName = requestData.FileName ?? $"processed_data_{DateTime.UtcNow:yyyyMMdd_HHmmss}.{analysisResult.DataFormat}",
            ReporteTextoPlano = requestData.SemiStructuredData,
            ProcessedAt = DateTimeOffset.UtcNow,
            FilePath = requestData.FilePath,
            ContainerName = requestData.ContainerName,
            FileSize = requestData.FileSize,
            MimeType = requestData.MimeType,
            ProcessingStatus = "completed",
            Metadata = new Dictionary<string, object>
            {
                ["analysisType"] = analysisResult.AnalysisType,
                ["language"] = analysisResult.Language,
                ["dataFormat"] = analysisResult.DataFormat,
                ["extractedFieldsCount"] = analysisResult.ExtractedFields.Count,
                ["dataQualityScore"] = analysisResult.DataQuality.OverallScore,
                ["aiAnalysisLength"] = analysisResult.AIAnalysis.Length,
                ["processedTimestamp"] = analysisResult.ProcessedTimestamp,
                ["originalRequestMetadata"] = requestData.Metadata ?? new Dictionary<string, object>()
            }
        };

        return document;
    }

    /// <summary>
    /// Saves the document to Cosmos DB
    /// </summary>
    private async Task<CosmosDbResult> SaveToCosmosDB(SemistructuredDocument document)
    {
        try
        {
            var database = _cosmosClient.GetDatabase(_databaseName);
            var container = database.GetContainer(_containerName);

            var response = await container.CreateItemAsync(document, new PartitionKey(document.TwinId));

            return new CosmosDbResult
            {
                Success = true,
                DocumentId = document.Id,
                ETag = response.ETag
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"? Cosmos DB save error: {ex.Message}");
            return new CosmosDbResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Queries documents from Cosmos DB by Twin ID
    /// </summary>
    private async Task<List<SemistructuredDocument>> QueryDocumentsByTwinId(string twinId)
    {
        try
        {
            var database = _cosmosClient.GetDatabase(_databaseName);
            var container = database.GetContainer(_containerName);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.twinId = @twinId")
                .WithParameter("@twinId", twinId);

            var documents = new List<SemistructuredDocument>();
            using var resultSet = container.GetItemQueryIterator<SemistructuredDocument>(query);

            while (resultSet.HasMoreResults)
            {
                var response = await resultSet.ReadNextAsync();
                documents.AddRange(response);
            }

            return documents;
        }
        catch (Exception ex)
        {
            _logger.LogError($"? Cosmos DB query error: {ex.Message}");
            return new List<SemistructuredDocument>();
        }
    }

    /// <summary>
    /// Adds CORS headers to the response
    /// </summary>
    private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
    {
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
    }

    #endregion
}

#region Request/Response Models

/// <summary>
/// Request model for semi-structured data processing
/// </summary>
public class SemiStructuredDataRequest
{
    public string TwinId { get; set; } = string.Empty;
    public string SemiStructuredData { get; set; } = string.Empty;
    public string? DataFormat { get; set; } = "json";
    public string? Language { get; set; } = "English";
    public string? AnalysisType { get; set; } = "insights";
    
    // Optional fields for document metadata
    public string? DocumentId { get; set; }
    public string? DocumentType { get; set; }
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public string? ContainerName { get; set; }
    public long? FileSize { get; set; }
    public string? MimeType { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Validation result for request data
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Result of Cosmos DB operation
/// </summary>
public class CosmosDbResult
{
    public bool Success { get; set; }
    public string DocumentId { get; set; } = string.Empty;
    public string ETag { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

#endregion
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using TwinAgentsLibrary.Services;
using DataLakeInfo = TwinAgentsLibrary.Services.DirectoryInfo;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Functions para gestionar archivos y directorios en Azure Data Lake
    /// </summary>
    public class AgentDataLakeFiles
    {
        private readonly ILogger<AgentDataLakeFiles> _logger;
        private readonly IConfiguration _configuration;

        public AgentDataLakeFiles(ILogger<AgentDataLakeFiles> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        #region Create Directory Functions

        [Function("CreateDirectoryOptions")]
        public async Task<HttpResponseData> HandleCreateDirectoryOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "datalake/create-directory/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? OPTIONS preflight request for datalake/create-directory/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("CreateDirectory")]
        public async Task<HttpResponseData> CreateDirectory(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "datalake/create-directory/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? CreateDirectory function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("? Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new CreateDirectoryResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var createRequest = JsonSerializer.Deserialize<CreateDirectoryRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (createRequest == null || string.IsNullOrEmpty(createRequest.DirectoryPath))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new CreateDirectoryResponse
                    {
                        Success = false,
                        ErrorMessage = "Directory path is required"
                    }));
                    return badResponse;
                }

                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                var createResult = await dataLakeClient.CreateNestedDirectoriesAsync(createRequest.DirectoryPath, createRequest.Metadata);

                if (!createResult.Success)
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new CreateDirectoryResponse
                    {
                        Success = false,
                        ErrorMessage = createResult.Error ?? "Failed to create directory"
                    }));
                    return errorResponse;
                }

                var processingTime = DateTime.UtcNow - startTime;
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                await response.WriteStringAsync(JsonSerializer.Serialize(new CreateDirectoryResponse
                {
                    Success = true,
                    TwinId = twinId,
                    DirectoryPath = createResult.DirectoryPath,
                    FileSystemName = createResult.FileSystemName,
                    FullPath = $"{createResult.FileSystemName}/{createResult.DirectoryPath}",
                    CreatedAt = DateTime.UtcNow,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = $"Directory created successfully"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "? Error creating directory");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new CreateDirectoryResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return errorResponse;
            }
        }

        #endregion

        #region Get Directory Info Functions

        [Function("GetDirectoryInfoOptions")]
        public async Task<HttpResponseData> HandleGetDirectoryInfoOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "datalake/get-directory-info/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? OPTIONS preflight request for datalake/get-directory-info/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GetDirectoryInfo")]
        public async Task<HttpResponseData> GetDirectoryInfo(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "datalake/get-directory-info/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? GetDirectoryInfo function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetDirectoryInfoResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var getRequest = JsonSerializer.Deserialize<GetDirectoryInfoRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (getRequest == null || string.IsNullOrEmpty(getRequest.DirectoryPath))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GetDirectoryInfoResponse
                    {
                        Success = false,
                        ErrorMessage = "Directory path is required"
                    }));
                    return badResponse;
                }

                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                DataLakeInfo? directoryInfo = await dataLakeClient.GetDirectoryInfoAsync(getRequest.DirectoryPath);

                if (directoryInfo == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new GetDirectoryInfoResponse
                    {
                        Success = false,
                        ErrorMessage = $"Directory '{getRequest.DirectoryPath}' not found",
                        Exists = false
                    }));
                    return notFoundResponse;
                }

                var processingTime = DateTime.UtcNow - startTime;
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                await response.WriteStringAsync(JsonSerializer.Serialize(new GetDirectoryInfoResponse
                {
                    Success = true,
                    TwinId = twinId,
                    DirectoryPath = getRequest.DirectoryPath,
                    FileSystemName = twinId.ToLowerInvariant(),
                    FullPath = $"{twinId.ToLowerInvariant()}/{getRequest.DirectoryPath}",
                    Exists = true,
                    CreatedOn = directoryInfo.CreatedOn,
                    LastModified = directoryInfo.LastModified,
                    Metadata = directoryInfo.Metadata,
                    ETag = directoryInfo.ETag,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "? Error getting directory info");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GetDirectoryInfoResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return errorResponse;
            }
        }

        #endregion

        #region List Directories Functions

        [Function("ListDirectoriesOptions")]
        public async Task<HttpResponseData> HandleListDirectoriesOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "datalake/list-directories/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? OPTIONS preflight request for datalake/list-directories/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("ListDirectories")]
        public async Task<HttpResponseData> ListDirectories(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "datalake/list-directories/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? ListDirectories function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ListDirectoriesResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var listRequest = JsonSerializer.Deserialize<ListDirectoriesRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                string parentPath = listRequest?.ParentPath ?? "";
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                var directories = await dataLakeClient.ListDirectoriesAsync(parentPath);

                var processingTime = DateTime.UtcNow - startTime;
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                await response.WriteStringAsync(JsonSerializer.Serialize(new ListDirectoriesResponse
                {
                    Success = true,
                    TwinId = twinId,
                    ParentPath = parentPath,
                    FileSystemName = twinId.ToLowerInvariant(),
                    Directories = directories.Select(d => new DirectoryItem
                    {
                        Name = d.Name,
                        FullPath = d.Name,
                        Url = d.Url,
                        CreatedOn = d.CreatedOn,
                        LastModified = d.LastModified,
                        ETag = d.ETag,
                        Metadata = d.Metadata != null ? new Dictionary<string, string>(d.Metadata) : new Dictionary<string, string>()
                    }).ToList(),
                    DirectoryCount = directories.Count,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = $"Found {directories.Count} directories"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "? Error listing directories");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new ListDirectoriesResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return errorResponse;
            }
        }

        #endregion

        #region List Files Functions

        [Function("ListFilesInDirectoryOptions")]
        public async Task<HttpResponseData> HandleListFilesInDirectoryOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "datalake/list-files/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? OPTIONS preflight request for datalake/list-files/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("ListFilesInDirectory")]
        public async Task<HttpResponseData> ListFilesInDirectory(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "datalake/list-files/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? ListFilesInDirectory function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ListFilesResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var listRequest = JsonSerializer.Deserialize<ListFilesRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                string directoryPath = listRequest?.DirectoryPath ?? "";
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                var files = await dataLakeClient.ListFilesInDirectoryAsync(directoryPath);

                var processingTime = DateTime.UtcNow - startTime;
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                await response.WriteStringAsync(JsonSerializer.Serialize(new ListFilesResponse
                {
                    Success = true,
                    TwinId = twinId,
                    DirectoryPath = directoryPath,
                    FileSystemName = twinId.ToLowerInvariant(),
                    Files = files.Select(f => new FileItem
                    {
                        Name = f.Name,
                        FullPath = f.Name,
                        Size = f.Size,
                        ContentType = f.ContentType,
                        Url = f.Url,
                        CreatedOn = f.CreatedOn,
                        LastModified = f.LastModified,
                        ETag = f.ETag,
                        Metadata = f.Metadata != null ? new Dictionary<string, string>(f.Metadata) : new Dictionary<string, string>()
                    }).ToList(),
                    FileCount = files.Count,
                    TotalSize = files.Sum(f => f.Size),
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = $"Found {files.Count} files"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "? Error listing files");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new ListFilesResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return errorResponse;
            }
        }

        #endregion

        #region Upload File Functions

        [Function("UploadFileOptions")]
        public async Task<HttpResponseData> HandleUploadFileOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "datalake/upload-file/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? OPTIONS preflight request for datalake/upload-file/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("UploadFile")]
        public async Task<HttpResponseData> UploadFile(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "datalake/upload-file/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? UploadFile function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadFileResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var uploadRequest = JsonSerializer.Deserialize<UploadFileRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (uploadRequest == null || string.IsNullOrEmpty(uploadRequest.FileName) || string.IsNullOrEmpty(uploadRequest.FileContent))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadFileResponse
                    {
                        Success = false,
                        ErrorMessage = "File name and content are required"
                    }));
                    return badResponse;
                }

                byte[] fileBytes;
                try
                {
                    fileBytes = Convert.FromBase64String(uploadRequest.FileContent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "? Failed to decode base64 file content");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadFileResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid base64 file content"
                    }));
                    return badResponse;
                }

                var mimeType = string.IsNullOrEmpty(uploadRequest.MimeType) ? GetMimeType(uploadRequest.FileName) : uploadRequest.MimeType;
                var directoryPath = uploadRequest.DirectoryPath ?? "";
                var fileName = uploadRequest.FileName;

                // Preparar metadata personalizada del archivo
                var customMetadata = uploadRequest.Metadata;
                
                _logger.LogInformation("?? File metadata: {MetadataCount} entries", customMetadata?.Count ?? 0);
                if (customMetadata != null && customMetadata.ContainsKey("Description"))
                {
                    _logger.LogInformation("?? File description: {Description}", customMetadata["Description"]);
                }

                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                using var fileStream = new MemoryStream(fileBytes);
                var uploadSuccess = await dataLakeClient.UploadFileAsync(
                    twinId.ToLowerInvariant(), 
                    directoryPath, 
                    fileName, 
                    fileStream, 
                    mimeType,
                    customMetadata  // Pasar metadata personalizada al método de upload
                );

                if (!uploadSuccess)
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadFileResponse
                    {
                        Success = false,
                        ErrorMessage = "Failed to upload file to storage"
                    }));
                    return errorResponse;
                }

                var fullPath = string.IsNullOrEmpty(directoryPath) ? fileName : $"{directoryPath}/{fileName}";
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullPath, TimeSpan.FromHours(24));
                var fileInfo = await dataLakeClient.GetFileInfoAsync(fullPath);

                var processingTime = DateTime.UtcNow - startTime;
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                await response.WriteStringAsync(JsonSerializer.Serialize(new UploadFileResponse
                {
                    Success = true,
                    TwinId = twinId,
                    FileName = fileName,
                    DirectoryPath = directoryPath,
                    FullPath = fullPath,
                    FileSystemName = twinId.ToLowerInvariant(),
                    FileSize = fileBytes.Length,
                    MimeType = mimeType,
                    Url = sasUrl,
                    UploadedAt = DateTime.UtcNow,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = $"File '{fileName}' uploaded successfully",
                    Metadata = fileInfo?.Metadata
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "? Error uploading file");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new UploadFileResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return errorResponse;
            }
        }

        #endregion

        #region Generate SAS URL Functions

        [Function("GenerateSasUrlOptions")]
        public async Task<HttpResponseData> HandleGenerateSasUrlOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "datalake/generate-sas-url/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? OPTIONS preflight request for datalake/generate-sas-url/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("GenerateSasUrl")]
        public async Task<HttpResponseData> GenerateSasUrl(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "datalake/generate-sas-url/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? GenerateSasUrl function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateSasUrlResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var sasRequest = JsonSerializer.Deserialize<GenerateSasUrlRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (sasRequest == null || string.IsNullOrEmpty(sasRequest.FilePath))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateSasUrlResponse
                    {
                        Success = false,
                        ErrorMessage = "File path is required"
                    }));
                    return badResponse;
                }

                var validForHours = sasRequest.ValidForHours > 0 ? sasRequest.ValidForHours : 24;
                var validFor = TimeSpan.FromHours(validForHours);

                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(sasRequest.FilePath, validFor);

                if (string.IsNullOrEmpty(sasUrl))
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateSasUrlResponse
                    {
                        Success = false,
                        ErrorMessage = $"File '{sasRequest.FilePath}' not found"
                    }));
                    return notFoundResponse;
                }

                var fileInfo = await dataLakeClient.GetFileInfoAsync(sasRequest.FilePath);
                var processingTime = DateTime.UtcNow - startTime;
                var expiresAt = DateTime.UtcNow.Add(validFor);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                await response.WriteStringAsync(JsonSerializer.Serialize(new GenerateSasUrlResponse
                {
                    Success = true,
                    TwinId = twinId,
                    FilePath = sasRequest.FilePath,
                    FileSystemName = twinId.ToLowerInvariant(),
                    SasUrl = sasUrl,
                    ValidForHours = validForHours,
                    ExpiresAt = expiresAt,
                    GeneratedAt = DateTime.UtcNow,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = $"SAS URL generated successfully",
                    FileSize = fileInfo?.Size ?? 0,
                    ContentType = fileInfo?.ContentType ?? "application/octet-stream",
                    LastModified = fileInfo?.LastModified ?? DateTime.UtcNow
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "? Error generating SAS URL");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateSasUrlResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return errorResponse;
            }
        }

        #endregion

        #region Delete File Functions

        [Function("DeleteFileOptions")]
        public async Task<HttpResponseData> HandleDeleteFileOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "datalake/delete-file/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("?? OPTIONS preflight request for datalake/delete-file/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        [Function("DeleteFile")]
        public async Task<HttpResponseData> DeleteFile(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "datalake/delete-file/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("??? DeleteFile function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteFileResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var deleteRequest = JsonSerializer.Deserialize<DeleteFileRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (deleteRequest == null || string.IsNullOrEmpty(deleteRequest.FilePath))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteFileResponse
                    {
                        Success = false,
                        ErrorMessage = "File path is required"
                    }));
                    return badResponse;
                }

                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                var fileInfo = await dataLakeClient.GetFileInfoAsync(deleteRequest.FilePath);
                var deleteSuccess = await dataLakeClient.DeleteFileAsync(deleteRequest.FilePath);

                if (!deleteSuccess)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteFileResponse
                    {
                        Success = false,
                        ErrorMessage = $"File '{deleteRequest.FilePath}' not found or deletion failed"
                    }));
                    return notFoundResponse;
                }

                var processingTime = DateTime.UtcNow - startTime;
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                await response.WriteStringAsync(JsonSerializer.Serialize(new DeleteFileResponse
                {
                    Success = true,
                    TwinId = twinId,
                    FilePath = deleteRequest.FilePath,
                    FileSystemName = twinId.ToLowerInvariant(),
                    DeletedAt = DateTime.UtcNow,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = $"File deleted successfully",
                    FileSize = fileInfo?.Size ?? 0,
                    ContentType = fileInfo?.ContentType ?? "unknown"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "? Error deleting file");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DeleteFileResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));
                return errorResponse;
            }
        }

        #endregion

        #region CORS Helper

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
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".zip" => "application/zip",
                ".rar" => "application/x-rar-compressed",
                ".7z" => "application/x-7z-compressed",
                ".mp4" => "video/mp4",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                _ => "application/octet-stream"
            };
        }

        #endregion
    }

    #region Request/Response Models

    public class CreateDirectoryRequest
    {
        public string DirectoryPath { get; set; } = string.Empty;
        public Dictionary<string, string>? Metadata { get; set; }
    }

    public class CreateDirectoryResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string FileSystemName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }

    public class GetDirectoryInfoRequest
    {
        public string DirectoryPath { get; set; } = string.Empty;
    }

    public class GetDirectoryInfoResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string FileSystemName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool Exists { get; set; }
        public DateTimeOffset? CreatedOn { get; set; }
        public DateTimeOffset? LastModified { get; set; }
        public string? ETag { get; set; }
        public IDictionary<string, string>? Metadata { get; set; }
    }

    public class ListDirectoriesRequest
    {
        public string ParentPath { get; set; } = string.Empty;
    }

    public class ListDirectoriesResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string ParentPath { get; set; } = string.Empty;
        public string FileSystemName { get; set; } = string.Empty;
        public List<DirectoryItem> Directories { get; set; } = new();
        public int DirectoryCount { get; set; }
    }

    public class DirectoryItem
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DateTime CreatedOn { get; set; }
        public DateTime LastModified { get; set; }
        public string ETag { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    public class UploadFileRequest
    {
        public string FileName { get; set; } = string.Empty;
        public string FileContent { get; set; } = string.Empty;
        public string? DirectoryPath { get; set; }
        public string? MimeType { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }

    public class UploadFileResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string FileSystemName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
        public IDictionary<string, string>? Metadata { get; set; }
    }

    public class ListFilesRequest
    {
        public string DirectoryPath { get; set; } = string.Empty;
    }

    public class ListFilesResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string FileSystemName { get; set; } = string.Empty;
        public List<FileItem> Files { get; set; } = new();
        public int FileCount { get; set; }
        public long TotalSize { get; set; }
    }

    public class FileItem
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long Size { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DateTime CreatedOn { get; set; }
        public DateTime LastModified { get; set; }
        public string ETag { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    public class GenerateSasUrlRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public int ValidForHours { get; set; } = 24;
    }

    public class GenerateSasUrlResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileSystemName { get; set; } = string.Empty;
        public string SasUrl { get; set; } = string.Empty;
        public int ValidForHours { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime GeneratedAt { get; set; }
        public long FileSize { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
    }

    public class DeleteFileRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }

    public class DeleteFileResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileSystemName { get; set; } = string.Empty;
        public DateTime DeletedAt { get; set; }
        public long FileSize { get; set; }
        public string ContentType { get; set; } = string.Empty;
    }

    #endregion
}

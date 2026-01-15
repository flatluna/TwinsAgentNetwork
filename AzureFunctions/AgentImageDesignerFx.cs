using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;
using TwinAgentsLibrary.Services;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Functions para transformar y editar imágenes usando Azure OpenAI Image API
    /// Proporciona endpoints para generar imágenes desde texto y editar imágenes existentes
    /// </summary>
    public class AgentImageDesignerFx
    {
        private readonly ILogger<AgentImageDesignerFx> _logger;
        private readonly IConfiguration _configuration;
        private readonly ILoggerFactory _loggerFactory;

        public AgentImageDesignerFx(ILogger<AgentImageDesignerFx> logger, IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _loggerFactory = loggerFactory;
        }

        #region Generate Image Functions

        /// <summary>
        /// OPTIONS handler for GenerateImageDesigner endpoint (CORS preflight)
        /// </summary>
        [Function("GenerateImageDesignerOptions")]
        public async Task<HttpResponseData> HandleGenerateImageDesignerOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "generate-image-designer/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for generate-image-designer/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Generate an image using Azure OpenAI Image API and save to Data Lake
        /// Receives an image from UI (base64), saves it, edits it with prompt, and saves the edited version
        /// POST /api/generate-image-designer/{twinId}
        /// Body: { "prompt": "Add modern furniture", "imageBase64": "data:image/png;base64,...", "size": "1536x1024", "quality": "high", "numberOfImages": 1, "filePath": "edited-images", "fileName": "original_image" }
        /// </summary>
        [Function("GenerateImageDesigner")]
        public async Task<HttpResponseData> GenerateImageDesigner(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "generate-image-designer/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🎨 GenerateImageDesigner function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                // Validate TwinID
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ImageDesignerGenerateResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                // Parse request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var designerRequest = JsonSerializer.Deserialize<ImageDesignerGenerateRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (designerRequest == null || string.IsNullOrEmpty(designerRequest.Prompt))
                {
                    _logger.LogError("❌ Prompt is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ImageDesignerGenerateResponse
                    {
                        Success = false,
                        ErrorMessage = "Prompt parameter is required"
                    }));
                    return badResponse;
                }

                // Validate that we have an image
                if (string.IsNullOrEmpty(designerRequest.ImageBase64))
                {
                    _logger.LogError("❌ Image data is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ImageDesignerGenerateResponse
                    {
                        Success = false,
                        ErrorMessage = "Image data (imageBase64) is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🎨 Editing image with prompt: {Prompt}", designerRequest.Prompt);
                _logger.LogInformation("   • Size: {Size}", designerRequest.Size ?? "1536x1024");
                _logger.LogInformation("   • Quality: {Quality}", designerRequest.Quality ?? "high");
                _logger.LogInformation("   • Number of Images: {Count}", designerRequest.NumberOfImages);
                _logger.LogInformation("   • File Path: {FilePath}", designerRequest.FilePath ?? "edited-images");
                _logger.LogInformation("   • File Name: {FileName}", designerRequest.FileName ?? "image");

                // Step 1: Decode base64 image
                byte[] imageBytes;
                try
                {
                    // Remove data URL prefix if present (e.g., "data:image/png;base64,")
                    var base64Data = designerRequest.ImageBase64;
                    if (base64Data.Contains(","))
                    {
                        base64Data = base64Data.Split(',')[1];
                    }
                    
                    imageBytes = Convert.FromBase64String(base64Data);
                    _logger.LogInformation("✅ Decoded image: {Size} bytes", imageBytes.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to decode base64 image");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ImageDesignerGenerateResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid base64 image data"
                    }));
                    return badResponse;
                }

                // Step 2: Save original image to Data Lake
                var directoryPath = designerRequest.FilePath ?? "edited-images";
                var baseFileName = designerRequest.FileName ?? "image";
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                var originalFileName = $"{baseFileName}.png";

                _logger.LogInformation("💾 Saving original image to Data Lake: {Path}/{File}", directoryPath, originalFileName);

                // Create Data Lake client
                var storageSettings = new TwinAgentsLibrary.Models.AzureStorageSettings
                {
                    AccountName = _configuration["Values:AzureStorage:AccountName"] ??
                                 _configuration["AzureStorage:AccountName"] ??
                                 "flatbitdatalake",
                    AccountKey = _configuration["Values:AzureStorage:AccountKey"] ??
                                _configuration["AzureStorage:AccountKey"] ??
                                Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_KEY") ??
                                throw new InvalidOperationException("Azure Storage Account Key is required")
                };

                var dataLakeLogger = _loggerFactory.CreateLogger<DataLakeClient>();
                var dataLakeClient = new DataLakeClient(twinId, dataLakeLogger, storageSettings);

                // Upload original image
                string originalImageSasUrl;
                using (var originalStream = new MemoryStream(imageBytes))
                {
                    var uploadSuccess = await dataLakeClient.UploadFileAsync(
                        twinId.ToLowerInvariant(),
                        directoryPath,
                        originalFileName,
                        originalStream,
                        "image/png"
                    );

                    if (!uploadSuccess)
                    {
                        _logger.LogError("❌ Failed to upload original image to Data Lake");
                        var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                        AddCorsHeaders(errorResponse, req);
                        await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new ImageDesignerGenerateResponse
                        {
                            Success = false,
                            ErrorMessage = "Failed to upload original image to Data Lake"
                        }));
                        return errorResponse;
                    }

                    var fullPath = $"{directoryPath}/{originalFileName}";
                    originalImageSasUrl = await dataLakeClient.GenerateSasUrlAsync(fullPath, TimeSpan.FromHours(24));
                    _logger.LogInformation("✅ Original image saved: {Path}", fullPath);
                }

                // Step 3: Edit image using AgentTwinImageDesigner
                _logger.LogInformation("✏️ Editing image with Azure OpenAI...");

                var imageDesignerLogger = _loggerFactory.CreateLogger<AgentTwinImageDesigner>();
                var imageDesigner = new AgentTwinImageDesigner(imageDesignerLogger, _configuration);

                var editResult = await imageDesigner.EditImageAsync(
                    imageBytes: imageBytes,
                    prompt: designerRequest.Prompt,
                    maskBytes: null,
                    size: designerRequest.Size ?? "1024x1024",
                    quality: designerRequest.Quality ?? "medium",
                    numberOfImages: designerRequest.NumberOfImages
                );

                if (!editResult.Success)
                {
                    _logger.LogError("❌ Image editing failed: {Error}", editResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new ImageDesignerGenerateResponse
                    {
                        Success = false,
                        ErrorMessage = editResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round((DateTime.UtcNow - startTime).TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Image edited successfully, generated {Count} variations", editResult.EditedImages.Count);

                // Step 4: Save edited images to Data Lake (same directory)
                var savedImageUrls = new System.Collections.Generic.List<string>();

                for (int i = 0; i < editResult.EditedImages.Count; i++)
                {
                    try
                    {
                        var editedImageBytes = Convert.FromBase64String(editResult.EditedImages[i]);
                        var editedFileName = $"{baseFileName}_edited_{timestamp}_{i + 1}.png";

                        _logger.LogInformation("💾 Saving edited image {Index}/{Total}: {FileName}", i + 1, editResult.EditedImages.Count, editedFileName);

                        using var imageStream = new MemoryStream(editedImageBytes);
                        var uploadSuccess = await dataLakeClient.UploadFileAsync(
                            twinId.ToLowerInvariant(),
                            directoryPath,
                            editedFileName,
                            imageStream,
                            "image/png"
                        );

                        if (uploadSuccess)
                        {
                            var fullPath = $"{directoryPath}/{editedFileName}";
                            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullPath, TimeSpan.FromHours(24));
                            savedImageUrls.Add(sasUrl);

                            _logger.LogInformation("✅ Saved edited image {Index}/{Total} successfully", i + 1, editResult.EditedImages.Count);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to upload edited image {Index}/{Total}", i + 1, editResult.EditedImages.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error saving edited image {Index}/{Total}", i + 1, editResult.EditedImages.Count);
                    }
                }

                var processingTime = DateTime.UtcNow - startTime;

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new ImageDesignerGenerateResponse
                {
                    Success = true,
                    TwinId = twinId,
                    Prompt = designerRequest.Prompt,
                    Size = editResult.Size,
                    Quality = editResult.Quality,
                    ImagesGenerated = editResult.EditedImages.Count,
                    SavedImageUrls = savedImageUrls,
                    OriginalImageUrl = originalImageSasUrl,
                    DirectoryPath = directoryPath,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = $"Successfully edited image from UI. Generated {editResult.EditedImages.Count} variation(s). {savedImageUrls.Count} saved to Data Lake",
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ GenerateImageDesigner completed successfully");
                _logger.LogInformation("   • Original image: {Original}", originalFileName);
                _logger.LogInformation("   • Edited images: {Count}", savedImageUrls.Count);
                _logger.LogInformation("   • Processing time: {Time:F2}s", processingTime.TotalSeconds);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error in GenerateImageDesigner after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new ImageDesignerGenerateResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error editing image"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Edit Image Functions

        /// <summary>
        /// OPTIONS handler for EditImageDesigner endpoint (CORS preflight)
        /// </summary>
        [Function("EditImageDesignerOptions")]
        public async Task<HttpResponseData> HandleEditImageDesignerOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "edit-image-designer/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for edit-image-designer/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Edit an image using Azure OpenAI Image API and save to Data Lake
        /// Downloads original image from Data Lake, edits it, and saves the result
        /// POST /api/edit-image-designer/{twinId}
        /// Body: { "prompt": "Add modern furniture", "filePath": "path/to/file", "fileName": "image.png", "maskFilePath": "path/to/mask", "maskFileName": "mask.png", "size": "1536x1024", "quality": "standard", "numberOfImages": 1 }
        /// </summary>
        [Function("EditImageDesigner")]
        public async Task<HttpResponseData> EditImageDesigner(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "edit-image-designer/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("✏️ EditImageDesigner function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                // Validate TwinID
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ImageDesignerEditResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                // Parse request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var editRequest = JsonSerializer.Deserialize<ImageDesignerEditRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (editRequest == null || string.IsNullOrEmpty(editRequest.Prompt) || 
                    string.IsNullOrEmpty(editRequest.FilePath) || string.IsNullOrEmpty(editRequest.FileName))
                {
                    _logger.LogError("❌ Prompt, filePath, and fileName are required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new ImageDesignerEditResponse
                    {
                        Success = false,
                        ErrorMessage = "Prompt, filePath, and fileName parameters are required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("✏️ Editing image with prompt: {Prompt}", editRequest.Prompt);
                _logger.LogInformation("   • Input File: {FilePath}/{FileName}", editRequest.FilePath, editRequest.FileName);

                // Create Data Lake client to download the image
                var storageSettings = new TwinAgentsLibrary.Models.AzureStorageSettings
                {
                    AccountName = _configuration["Values:AzureStorage:AccountName"] ??
                                 _configuration["AzureStorage:AccountName"] ??
                                 "flatbitdatalake",
                    AccountKey = _configuration["Values:AzureStorage:AccountKey"] ??
                                _configuration["AzureStorage:AccountKey"] ??
                                Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_KEY") ??
                                throw new InvalidOperationException("Azure Storage Account Key is required")
                };

                var dataLakeLogger = _loggerFactory.CreateLogger<DataLakeClient>();
                var dataLakeClient = new DataLakeClient(twinId, dataLakeLogger, storageSettings);

                // Download the original image
                var inputImagePath = $"{editRequest.FilePath}/{editRequest.FileName}";
                _logger.LogInformation("📥 Downloading input image from Data Lake: {Path}", inputImagePath);

                // Generate SAS URL and download the image
                var imageSasUrl = await dataLakeClient.GenerateSasUrlAsync(inputImagePath, TimeSpan.FromHours(1));
                
                if (string.IsNullOrEmpty(imageSasUrl))
                {
                    _logger.LogError("❌ Failed to generate SAS URL for input image");
                    var errorResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new ImageDesignerEditResponse
                    {
                        Success = false,
                        ErrorMessage = "Failed to generate SAS URL for input image"
                    }));
                    return errorResponse;
                }

                byte[] imageBytes;
                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    imageBytes = await httpClient.GetByteArrayAsync(imageSasUrl);
                }

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    _logger.LogError("❌ Failed to download input image from Data Lake");
                    var errorResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new ImageDesignerEditResponse
                    {
                        Success = false,
                        ErrorMessage = "Input image not found in Data Lake"
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Downloaded {Size} bytes", imageBytes.Length);

                // Download mask if provided
                byte[]? maskBytes = null;
                if (!string.IsNullOrEmpty(editRequest.MaskFilePath) && !string.IsNullOrEmpty(editRequest.MaskFileName))
                {
                    _logger.LogInformation("📥 Downloading mask image: {Path}/{File}", editRequest.MaskFilePath, editRequest.MaskFileName);
                    
                    var maskImagePath = $"{editRequest.MaskFilePath}/{editRequest.MaskFileName}";
                    var maskSasUrl = await dataLakeClient.GenerateSasUrlAsync(maskImagePath, TimeSpan.FromHours(1));

                    if (!string.IsNullOrEmpty(maskSasUrl))
                    {
                        using (var httpClient = new System.Net.Http.HttpClient())
                        {
                            maskBytes = await httpClient.GetByteArrayAsync(maskSasUrl);
                        }

                        if (maskBytes != null && maskBytes.Length > 0)
                        {
                            _logger.LogInformation("✅ Downloaded mask: {Size} bytes", maskBytes.Length);
                        }
                    }
                }

                // Create image designer agent and edit the image
                var imageDesignerLogger = _loggerFactory.CreateLogger<AgentTwinImageDesigner>();
                var imageDesigner = new AgentTwinImageDesigner(imageDesignerLogger, _configuration);

                var editResult = await imageDesigner.EditImageAsync(
                    imageBytes: imageBytes,
                    prompt: editRequest.Prompt,
                    maskBytes: maskBytes,
                    size: editRequest.Size ?? "1536x1024",
                    quality: editRequest.Quality ?? "medium",
                    numberOfImages: editRequest.NumberOfImages
                );

                if (!editResult.Success)
                {
                    _logger.LogError("❌ Image editing failed: {Error}", editResult.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new ImageDesignerEditResponse
                    {
                        Success = false,
                        ErrorMessage = editResult.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round((DateTime.UtcNow - startTime).TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Edited image successfully, generated {Count} variations", editResult.EditedImages.Count);

                // Save edited images to Data Lake
                var savedImageUrls = new System.Collections.Generic.List<string>();
                var outputDirectoryPath = editRequest.OutputFilePath ?? editRequest.FilePath;
                var baseFileName = Path.GetFileNameWithoutExtension(editRequest.FileName);

                for (int i = 0; i < editResult.EditedImages.Count; i++)
                {
                    try
                    {
                        var editedImageBytes = Convert.FromBase64String(editResult.EditedImages[i]);
                        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                        var fileName = $"{baseFileName}_edited_{timestamp}_{i + 1}.png";

                        _logger.LogInformation("💾 Saving edited image {Index}/{Total}: {FileName}", i + 1, editResult.EditedImages.Count, fileName);

                        using var imageStream = new MemoryStream(editedImageBytes);
                        var uploadSuccess = await dataLakeClient.UploadFileAsync(
                            twinId.ToLowerInvariant(),
                            outputDirectoryPath,
                            fileName,
                            imageStream,
                            "image/png"
                        );

                        if (uploadSuccess)
                        {
                            var fullPath = $"{outputDirectoryPath}/{fileName}";
                            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullPath, TimeSpan.FromHours(24));
                            savedImageUrls.Add(sasUrl);

                            _logger.LogInformation("✅ Saved edited image {Index}/{Total} successfully", i + 1, editResult.EditedImages.Count);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to upload edited image {Index}/{Total}", i + 1, editResult.EditedImages.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error saving edited image {Index}/{Total}", i + 1, editResult.EditedImages.Count);
                    }
                }

                var processingTime = DateTime.UtcNow - startTime;

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new ImageDesignerEditResponse
                {
                    Success = true,
                    TwinId = twinId,
                    Prompt = editRequest.Prompt,
                    InputFilePath = inputImagePath,
                    Size = editResult.Size,
                    Quality = editResult.Quality,
                    MaskUsed = editResult.MaskUsed,
                    ImagesGenerated = editResult.EditedImages.Count,
                    SavedImageUrls = savedImageUrls,
                    OutputDirectoryPath = outputDirectoryPath,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = $"Successfully edited image. Generated {editResult.EditedImages.Count} variation(s). {savedImageUrls.Count} saved to Data Lake",
                    Timestamp = DateTime.UtcNow
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
                _logger.LogError(ex, "❌ Error in EditImageDesigner after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new ImageDesignerEditResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error editing image"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Helper Methods

        private static void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
            var origin = originHeader.Key != null ? originHeader.Value?.FirstOrDefault() : null;

            var allowedOrigins = new[] { 
                "http://localhost:5173", 
                "http://localhost:5174",
                "http://localhost:3000", 
                "http://127.0.0.1:5173", 
                "http://127.0.0.1:5174",
                "http://127.0.0.1:3000" 
            };

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

        #endregion
    }

    #region Request/Response Models

    public class ImageDesignerGenerateRequest
    {
        public string Prompt { get; set; } = "";
        public string? ImageBase64 { get; set; }  // Base64 encoded image from UI
        public string? Size { get; set; }
        public string? Quality { get; set; }
        public int NumberOfImages { get; set; } = 1;
        public string? FilePath { get; set; }
        public string? FileName { get; set; }
    }

    public class ImageDesignerGenerateResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public string TwinId { get; set; } = "";
        public string Prompt { get; set; } = "";
        public string Size { get; set; } = "";
        public string Quality { get; set; } = "";
        public int ImagesGenerated { get; set; }
        public System.Collections.Generic.List<string> SavedImageUrls { get; set; } = new();
        public string OriginalImageUrl { get; set; } = "";  // URL of original image saved to Data Lake
        public string DirectoryPath { get; set; } = "";
        public double ProcessingTimeSeconds { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ImageDesignerEditRequest
    {
        public string Prompt { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string? MaskFilePath { get; set; }
        public string? MaskFileName { get; set; }
        public string? OutputFilePath { get; set; }
        public string? Size { get; set; }
        public string? Quality { get; set; }
        public int NumberOfImages { get; set; } = 1;
    }

    public class ImageDesignerEditResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public string TwinId { get; set; } = "";
        public string Prompt { get; set; } = "";
        public string InputFilePath { get; set; } = "";
        public string Size { get; set; } = "";
        public string Quality { get; set; } = "";
        public bool MaskUsed { get; set; }
        public int ImagesGenerated { get; set; }
        public System.Collections.Generic.List<string> SavedImageUrls { get; set; } = new();
        public string OutputDirectoryPath { get; set; } = "";
        public double ProcessingTimeSeconds { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

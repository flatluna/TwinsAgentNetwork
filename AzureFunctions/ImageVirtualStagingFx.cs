using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Function for Virtual Staging using HomeDesigns.ai API
    /// Adds furniture and decor to empty or outdated spaces
    /// </summary>
    public class ImageVirtualStagingFx
    {
        private readonly ILogger<ImageVirtualStagingFx> _logger;
        private readonly IConfiguration _configuration;
        private readonly ILoggerFactory _loggerFactory;

        public ImageVirtualStagingFx(
            ILogger<ImageVirtualStagingFx> logger,
            IConfiguration configuration,
            ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _loggerFactory = loggerFactory;
        }

        /// <summary>
        /// OPTIONS handler for VirtualStaging endpoint (CORS preflight)
        /// </summary>
        [Function("VirtualStagingOptions")]
        public async Task<HttpResponseData> HandleVirtualStagingOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "virtual-staging/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for virtual-staging/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Transform empty spaces with virtual furniture and decor using HomeDesigns.ai Virtual Staging API
        /// Downloads image from Azure Data Lake and sends to HomeDesigns.ai
        /// POST /api/virtual-staging/{twinId}
        /// Body: { "filePath": "path/to/file", "fileName": "image.jpg", "designType": "Interior", ... }
        /// </summary>
        [Function("VirtualStaging")]
        public async Task<HttpResponseData> VirtualStaging(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "virtual-staging/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🪑 VirtualStaging function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                // Validate TwinID
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new VirtualStagingResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                // Read and parse JSON request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var stagingRequest = JsonSerializer.Deserialize<VirtualStagingRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (stagingRequest == null)
                {
                    _logger.LogError("❌ Failed to parse virtual staging request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new VirtualStagingResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid virtual staging request data format"
                    }));
                    return badResponse;
                }

                // Validate required fields
                if (string.IsNullOrEmpty(stagingRequest.FilePath))
                {
                    _logger.LogError("❌ File path is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new VirtualStagingResponse
                    {
                        Success = false,
                        ErrorMessage = "File path is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(stagingRequest.FileName))
                {
                    _logger.LogError("❌ File name is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new VirtualStagingResponse
                    {
                        Success = false,
                        ErrorMessage = "File name is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🪑 Virtual staging parameters:");
                _logger.LogInformation("   • File Path: {FilePath}", stagingRequest.FilePath);
                _logger.LogInformation("   • File Name: {FileName}", stagingRequest.FileName);
                _logger.LogInformation("   • Design Type: {DesignType}", stagingRequest.DesignType);
                _logger.LogInformation("   • AI Intervention: {AIIntervention}", stagingRequest.AIIntervention);
                _logger.LogInformation("   • Number of Designs: {NoDesign}", stagingRequest.NoDesign);
                _logger.LogInformation("   • Design Style: {DesignStyle}", stagingRequest.DesignStyle);

                // Step 1: Download file from Azure Data Lake
                _logger.LogInformation("📥 Downloading file from Azure Data Lake...");

                // Create DataLake client factory
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                // Combine file path and filename
                var fullFilePath = string.IsNullOrEmpty(stagingRequest.FilePath)
                    ? stagingRequest.FileName
                    : $"{stagingRequest.FilePath.TrimEnd('/')}/{stagingRequest.FileName}";

                _logger.LogInformation("📂 Downloading from: {FullFilePath}", fullFilePath);

                // Download file from Data Lake
                byte[]? imageBytes = await dataLakeClient.DownloadFileAsync(fullFilePath);

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    _logger.LogError("❌ File not found or empty in Data Lake: {FilePath}", fullFilePath);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new VirtualStagingResponse
                    {
                        Success = false,
                        ErrorMessage = $"File not found in Data Lake: {fullFilePath}"
                    }));
                    return notFoundResponse;
                }

                _logger.LogInformation("✅ File downloaded successfully. Size: {Size} bytes", imageBytes.Length);

                // Step 2: Validate image dimensions (HomeDesigns.ai requires minimum 512x512 pixels)
                _logger.LogInformation("🔍 Validating image dimensions...");

                try
                {
                    using var imageStream = new MemoryStream(imageBytes);
                    using var image = System.Drawing.Image.FromStream(imageStream);

                    _logger.LogInformation("📐 Image dimensions: {Width}x{Height} pixels", image.Width, image.Height);

                    if (image.Width < 512 || image.Height < 512)
                    {
                        _logger.LogError("❌ Image dimensions too small: {Width}x{Height}. Minimum required: 512x512 pixels",
                            image.Width, image.Height);

                        var dimensionErrorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(dimensionErrorResponse, req);
                        await dimensionErrorResponse.WriteStringAsync(JsonSerializer.Serialize(new VirtualStagingResponse
                        {
                            Success = false,
                            ErrorMessage = $"Image dimensions ({image.Width}x{image.Height}) are too small. Minimum required: 512x512 pixels for optimal results."
                        }));
                        return dimensionErrorResponse;
                    }

                    _logger.LogInformation("✅ Image dimensions validated successfully");
                }
                catch (Exception dimEx)
                {
                    _logger.LogWarning(dimEx, "⚠️ Could not validate image dimensions, continuing anyway");
                }

                // Step 3: Send to HomeDesigns.ai Virtual Staging API
                var homeDesignsToken = _configuration["HOMEDESIGNS_AI_TOKEN"] ??
                                     _configuration["Values:HOMEDESIGNS_AI_TOKEN"] ??
                                     "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJuYW1lIjoiSm9yZuiLCJlbWFpbCI6ImpvcmdlbHVuYUBmbGF0Yml0LmNvbSJ9.rWhV_6nBGG0Oh3t2dwvNQsPAfzEBBEhPGzfmmJstEkM";

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {homeDesignsToken}");

                // Prepare multipart form data
                using var content = new MultipartFormDataContent();

                // Add image from downloaded bytes
                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                    GetMimeTypeFromFileName(stagingRequest.FileName));
                content.Add(imageContent, "image", stagingRequest.FileName);

                // Add parameters
                content.Add(new StringContent(stagingRequest.DesignType), "design_type");
                content.Add(new StringContent(stagingRequest.AIIntervention), "ai_intervention");
                content.Add(new StringContent(stagingRequest.NoDesign.ToString()), "no_design");
                content.Add(new StringContent(stagingRequest.DesignStyle), "design_style");

                // room_type is REQUIRED for Interior designs
                if (stagingRequest.DesignType.Equals("Interior", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(stagingRequest.RoomType))
                    {
                        _logger.LogError("❌ room_type is required for Interior design type");
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse, req);
                        await badResponse.WriteStringAsync(JsonSerializer.Serialize(new VirtualStagingResponse
                        {
                            Success = false,
                            ErrorMessage = "room_type is required for Interior design type"
                        }));
                        return badResponse;
                    }
                    content.Add(new StringContent(stagingRequest.RoomType), "room_type");
                }
                else if (!string.IsNullOrEmpty(stagingRequest.RoomType))
                {
                    content.Add(new StringContent(stagingRequest.RoomType), "room_type");
                }

                if (!string.IsNullOrEmpty(stagingRequest.CustomInstruction))
                    content.Add(new StringContent(stagingRequest.CustomInstruction), "custom_instruction");

                _logger.LogInformation("📤 Sending request to HomeDesigns.ai Virtual Staging API...");
                _logger.LogInformation("📋 Request parameters:");
                _logger.LogInformation("   • design_type: {DesignType}", stagingRequest.DesignType);
                _logger.LogInformation("   • ai_intervention: {AIIntervention}", stagingRequest.AIIntervention);
                _logger.LogInformation("   • no_design: {NoDesign}", stagingRequest.NoDesign);
                _logger.LogInformation("   • design_style: {DesignStyle}", stagingRequest.DesignStyle);
                _logger.LogInformation("   • room_type: {RoomType}", stagingRequest.RoomType ?? "null");
                _logger.LogInformation("   • custom_instruction: {CustomInstruction}", stagingRequest.CustomInstruction ?? "null");

                // Send request to HomeDesigns.ai Virtual Staging endpoint
                var apiResponse = await httpClient.PostAsync("https://homedesigns.ai/api/v2/virtual_staging", content);
                var responseContent = await apiResponse.Content.ReadAsStringAsync();

                _logger.LogInformation("📨 API Response Status: {StatusCode}", apiResponse.StatusCode);
                _logger.LogInformation("📨 API Response Content: {Content}", responseContent);

                if (!apiResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ HomeDesigns.ai API error: {StatusCode} - {Response}", apiResponse.StatusCode, responseContent);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new VirtualStagingResponse
                    {
                        Success = false,
                        ErrorMessage = $"HomeDesigns.ai API error: {responseContent}"
                    }));
                    return errorResponse;
                }

                // Parse queue response
                var queueResponse = JsonSerializer.Deserialize<VirtualStagingQueueResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (queueResponse == null || string.IsNullOrEmpty(queueResponse.Id))
                {
                    _logger.LogError("❌ Invalid queue response from HomeDesigns.ai");
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new VirtualStagingResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid queue response from HomeDesigns.ai"
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ HomeDesigns.ai request queued successfully. Queue ID: {QueueId}", queueResponse.Id);

                // Poll status endpoint until completion (max 5 minutes)
                var maxAttempts = 60; // 5 minutes with 5-second intervals
                var attempt = 0;
                string? inputImage = null;
                List<string>? outputImages = null;

                while (attempt < maxAttempts)
                {
                    await Task.Delay(2000); // Wait 5 seconds between polls
                    attempt++;

                    _logger.LogInformation("🔄 Checking status (attempt {Attempt}/{MaxAttempts})...", attempt, maxAttempts);

                    var statusUrl = $"https://homedesigns.ai/api/v2/virtual_staging/status_check/{queueResponse.Id}";
                    var statusResult = await httpClient.GetAsync(statusUrl);
                    var statusContent = await statusResult.Content.ReadAsStringAsync();

                    // Log the raw response for debugging
                    _logger.LogInformation("📋 Raw status response: {StatusContent}", statusContent);

                    // Try Format 1: Queue status with data wrapper
                    try
                    {
                        var dataWrapperResponse = JsonSerializer.Deserialize<VirtualStagingDataWrapperResponse>(statusContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (dataWrapperResponse?.Data != null && !string.IsNullOrEmpty(dataWrapperResponse.Data.Status))
                        {
                            _logger.LogInformation("📊 Queue Status (data wrapper): {Status}, Worker: {Worker}, Delay: {Delay}ms", 
                                dataWrapperResponse.Data.Status, 
                                dataWrapperResponse.Data.WorkerId ?? "N/A",
                                dataWrapperResponse.Data.DelayTime);

                            // Check for error states
                            if (dataWrapperResponse.Data.Status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
                                dataWrapperResponse.Data.Status.Equals("error", StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogError("❌ HomeDesigns.ai processing failed: {Status}", dataWrapperResponse.Data.Status);
                                break;
                            }

                            // Continue polling if still in queue or in progress
                            continue;
                        }
                    }
                    catch (JsonException)
                    {
                        // Not data wrapper format, try next format
                    }

                    // Try Format 2: Simple status response
                    try
                    {
                        var simpleStatusResponse = JsonSerializer.Deserialize<VirtualStagingSimpleStatusResponse>(statusContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (simpleStatusResponse != null && !string.IsNullOrEmpty(simpleStatusResponse.Status))
                        {
                            _logger.LogInformation("📊 Simple Status: {Status}, Created: {Created}", 
                                simpleStatusResponse.Status,
                                simpleStatusResponse.CreatedAt ?? "N/A");

                            // Check for error states
                            if (simpleStatusResponse.Status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
                                simpleStatusResponse.Status.Equals("error", StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogError("❌ HomeDesigns.ai processing failed: {Status}", simpleStatusResponse.Status);
                                break;
                            }

                            // Continue polling if still starting or in progress
                            continue;
                        }
                    }
                    catch (JsonException)
                    {
                        // Not simple status format, try next format
                    }

                    // Try Format 3: Final response (success format with nested structure)
                    try
                    {
                        var finalResponse = JsonSerializer.Deserialize<VirtualStagingFinalResponse>(statusContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (finalResponse?.Success != null &&
                            finalResponse.Success.GeneratedImage != null &&
                            finalResponse.Success.GeneratedImage.Count > 0)
                        {
                            inputImage = finalResponse.Success.OriginalImage;
                            outputImages = finalResponse.Success.GeneratedImage;
                            _logger.LogInformation("✅ Virtual staging complete! Generated {Count} designs", outputImages.Count);
                            break;
                        }
                    }
                    catch (JsonException)
                    {
                        // Not final response format
                    }

                    // Try Format 4: Direct response format (input_image + output_images)
                    try
                    {
                        var directResponse = JsonSerializer.Deserialize<VirtualStagingDirectResponse>(statusContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (directResponse != null &&
                            !string.IsNullOrEmpty(directResponse.InputImage) &&
                            directResponse.OutputImages != null &&
                            directResponse.OutputImages.Count > 0)
                        {
                            inputImage = directResponse.InputImage;
                            outputImages = directResponse.OutputImages;
                            _logger.LogInformation("✅ Virtual staging complete! Generated {Count} designs", outputImages.Count);
                            break;
                        }
                    }
                    catch (JsonException)
                    {
                        // Not direct response format
                    }

                    // If we can't parse any format, log warning and continue
                    _logger.LogWarning("⚠️ Unable to parse status response in any known format. Content: {Content}", statusContent);
                }

                // Check final status
                if (outputImages == null || outputImages.Count == 0)
                {
                    _logger.LogError("❌ Timeout or error waiting for HomeDesigns.ai processing");
                    var errorResponse = req.CreateResponse(HttpStatusCode.RequestTimeout);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new VirtualStagingResponse
                    {
                        Success = false,
                        ErrorMessage = "Timeout waiting for virtual staging completion",
                        QueueId = queueResponse.Id,
                        Status = "timeout"
                    }));
                    return errorResponse;
                }

                // Step 4: Download and save output images to Data Lake
                _logger.LogInformation("💾 Saving {Count} virtual staging images to Data Lake...", outputImages.Count);

                var savedImageUrls = new List<string>();
                var directoryPath = stagingRequest.FilePath ?? "virtual-staging";
                var baseFileName = Path.GetFileNameWithoutExtension(stagingRequest.FileName);

                // Create fresh HTTP client for downloading images (without HomeDesigns.ai auth header)
                using var downloadClient = new HttpClient();

                for (int i = 0; i < outputImages.Count; i++)
                {
                    var outputUrl = outputImages[i];
                    try
                    {
                        _logger.LogInformation("📥 Downloading virtual staging image {Index}/{Total} from: {Url}", i + 1, outputImages.Count, outputUrl);

                        // Use clean HTTP client without authorization headers
                        var outputImageBytes = await downloadClient.GetByteArrayAsync(outputUrl);
                        _logger.LogInformation("✅ Downloaded {Size} bytes", outputImageBytes.Length);

                        // Generate filename with timestamp
                        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                        var fileName = $"{baseFileName}_staged_{timestamp}_{i + 1}.png";

                        // Upload to Data Lake
                        using var imageStream = new MemoryStream(outputImageBytes);
                        var uploadSuccess = await dataLakeClient.UploadFileAsync(
                            twinId.ToLowerInvariant(),
                            directoryPath,
                            fileName,
                            imageStream,
                            "image/png"
                        );

                        if (uploadSuccess)
                        {
                            // Generate SAS URL for the saved image
                            var fullPath = $"{directoryPath}/{fileName}";
                            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullPath, TimeSpan.FromHours(24));
                            savedImageUrls.Add(sasUrl);

                            _logger.LogInformation("✅ Saved virtual staging image {Index}/{Total} to: {Path}", i + 1, outputImages.Count, fullPath);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to upload virtual staging image {Index}/{Total}", i + 1, outputImages.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error processing output image {Index}/{Total}", i + 1, outputImages.Count);
                    }
                }

                var processingTime = DateTime.UtcNow - startTime;

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new VirtualStagingResponse
                {
                    Success = true,
                    TwinId = twinId,
                    QueueId = queueResponse.Id,
                    Status = "SUCCESS",
                    InputImage = inputImage,
                    OutputImages = outputImages,
                    SavedImageUrls = savedImageUrls,
                    DesignType = stagingRequest.DesignType,
                    DesignStyle = stagingRequest.DesignStyle,
                    AIIntervention = stagingRequest.AIIntervention,
                    NumberOfDesigns = outputImages.Count,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = $"Virtual staging completado exitosamente con {outputImages.Count} variaciones. {savedImageUrls.Count} imágenes guardadas en Data Lake",
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Virtual staging completed successfully in {Time} seconds", processingTime.TotalSeconds);
                _logger.LogInformation("   • Total designs: {Count}", outputImages.Count);
                _logger.LogInformation("   • Saved to Data Lake: {SavedCount}", savedImageUrls.Count);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error processing virtual staging after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new VirtualStagingResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al procesar el virtual staging"
                }));

                return errorResponse;
            }
        }

        #region Helper Methods

        /// <summary>
        /// Helper method to determine MIME type from filename
        /// </summary>
        private static string GetMimeTypeFromFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
        }

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

        #endregion
    }

    #region Request/Response Models

    /// <summary>
    /// Request model for virtual staging from Data Lake
    /// </summary>
    public class VirtualStagingRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DesignType { get; set; } = "Interior";
        public string AIIntervention { get; set; } = "Mid";
        public int NoDesign { get; set; } = 1;
        public string DesignStyle { get; set; } = "Modern";
        public string? RoomType { get; set; }
        public string? CustomInstruction { get; set; }
    }

    /// <summary>
    /// Response from HomeDesigns.ai virtual staging queue endpoint
    /// </summary>
    public class VirtualStagingQueueResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response with data wrapper (format: {"data": {...}, "http_status": 200})
    /// </summary>
    public class VirtualStagingDataWrapperResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("data")]
        public VirtualStagingDataObject? Data { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("http_status")]
        public int? HttpStatus { get; set; }
    }

    /// <summary>
    /// Data object within wrapper response
    /// </summary>
    public class VirtualStagingDataObject
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string? Status { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("workerId")]
        public string? WorkerId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("delayTime")]
        public int? DelayTime { get; set; }
    }

    /// <summary>
    /// Simple status response (format: {"status": "starting", "created_at": "..."})
    /// </summary>
    public class VirtualStagingSimpleStatusResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string? Status { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("started_at")]
        public string? StartedAt { get; set; }
    }

    /// <summary>
    /// Response from HomeDesigns.ai virtual staging status check endpoint
    /// </summary>
    public class VirtualStagingStatusResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string? Status { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("input_image")]
        public string? InputImage { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("output_images")]
        public List<string>? OutputImages { get; set; }
    }

    /// <summary>
    /// Final response from HomeDesigns.ai virtual staging (with nested success object)
    /// </summary>
    public class VirtualStagingFinalResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("success")]
        public VirtualStagingSuccessData? Success { get; set; }
    }

    /// <summary>
    /// Success data within Virtual Staging final response
    /// </summary>
    public class VirtualStagingSuccessData
    {
        [System.Text.Json.Serialization.JsonPropertyName("original_image")]
        public string? OriginalImage { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("generated_image")]
        public List<string>? GeneratedImage { get; set; }
    }

    /// <summary>
    /// Direct response format (input_image + output_images)
    /// </summary>
    public class VirtualStagingDirectResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("input_image")]
        public string? InputImage { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("output_images")]
        public List<string>? OutputImages { get; set; }
    }

    /// <summary>
    /// Response model for virtual staging
    /// </summary>
    public class VirtualStagingResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string QueueId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? InputImage { get; set; }
        public List<string>? OutputImages { get; set; }
        public List<string>? SavedImageUrls { get; set; }
        public string DesignType { get; set; } = string.Empty;
        public string DesignStyle { get; set; } = string.Empty;
        public string AIIntervention { get; set; } = string.Empty;
        public int NumberOfDesigns { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

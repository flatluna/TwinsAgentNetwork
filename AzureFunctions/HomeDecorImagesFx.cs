using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TwinAgentsNetwork.AzureFunctions
{


     public class HomeDecorImagesFx
    {

        private readonly ILogger<AgentImagesFx> _logger;
        private readonly IConfiguration _configuration;
        private readonly ILoggerFactory _loggerFactory;

        public HomeDecorImagesFx(
            ILogger<AgentImagesFx> logger,
            IConfiguration configuration,
            ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _loggerFactory = loggerFactory;
        }
        /// <summary>
        /// Transform house images using HomeDesigns.ai Perfect Redesign API
        /// Downloads image from Azure Data Lake and sends to HomeDesigns.ai
        /// POST /api/house-redesign/{twinId}
        /// Body: { "filePath": "path/to/file", "fileName": "image.jpg", "designType": "Interior", ... }
        /// </summary>
        [Function("PerfectRedesign")]
        public async Task<HttpResponseData> PerfectRedesign(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "house-redesign/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🏠 PerfectRedesign function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                // Validate TwinID
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new HouseRedesignResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                // Read and parse JSON request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var redesignRequest = JsonSerializer.Deserialize<HouseRedesignRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (redesignRequest == null)
                {
                    _logger.LogError("❌ Failed to parse redesign request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new HouseRedesignResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid redesign request data format"
                    }));
                    return badResponse;
                }

                // Validate required fields
                if (string.IsNullOrEmpty(redesignRequest.FilePath))
                {
                    _logger.LogError("❌ File path is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new HouseRedesignResponse
                    {
                        Success = false,
                        ErrorMessage = "File path is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(redesignRequest.FileName))
                {
                    _logger.LogError("❌ File name is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new HouseRedesignResponse
                    {
                        Success = false,
                        ErrorMessage = "File name is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🏠 House redesign request:");
                _logger.LogInformation("   • File Path: {FilePath}", redesignRequest.FilePath);
                _logger.LogInformation("   • File Name: {FileName}", redesignRequest.FileName);
                _logger.LogInformation("   • Design Type: {DesignType}", redesignRequest.DesignType);
                _logger.LogInformation("   • AI Intervention: {AIIntervention}", redesignRequest.AIIntervention);
                _logger.LogInformation("   • Number of Designs: {NoDesign}", redesignRequest.NoDesign);
                _logger.LogInformation("   • Design Style: {DesignStyle}", redesignRequest.DesignStyle);

                // Step 1: Download file from Azure Data Lake
                _logger.LogInformation("📥 Downloading file from Azure Data Lake...");

                // Create DataLake client factory
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                // Combine file path and filename
                var fullFilePath = string.IsNullOrEmpty(redesignRequest.FilePath)
                    ? redesignRequest.FileName
                    : $"{redesignRequest.FilePath.TrimEnd('/')}/{redesignRequest.FileName}";

                _logger.LogInformation("📂 Downloading from: {FullFilePath}", fullFilePath);

                // Download file from Data Lake
                byte[]? imageBytes = await dataLakeClient.DownloadFileAsync(fullFilePath);

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    _logger.LogError("❌ File not found or empty in Data Lake: {FilePath}", fullFilePath);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new HouseRedesignResponse
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
                        await dimensionErrorResponse.WriteStringAsync(JsonSerializer.Serialize(new HouseRedesignResponse
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

                // Step 3: Send to HomeDesigns.ai API
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
                    GetMimeTypeFromFileName(redesignRequest.FileName));
                content.Add(imageContent, "image", redesignRequest.FileName);

                // Add parameters
                content.Add(new StringContent(redesignRequest.DesignType), "design_type");
                content.Add(new StringContent(redesignRequest.AIIntervention), "ai_intervention");
                content.Add(new StringContent(redesignRequest.NoDesign.ToString()), "no_design");
                content.Add(new StringContent(redesignRequest.DesignStyle), "design_style");
                content.Add(new StringContent(redesignRequest.KeepStructural.ToString().ToLower()), "keep_structural_element");

                // room_type is REQUIRED for Interior designs
                if (redesignRequest.DesignType.Equals("Interior", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(redesignRequest.RoomType))
                    {
                        _logger.LogError("❌ room_type is required for Interior design type");
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse, req);
                        await badResponse.WriteStringAsync(JsonSerializer.Serialize(new HouseRedesignResponse
                        {
                            Success = false,
                            ErrorMessage = "room_type is required for Interior design type"
                        }));
                        return badResponse;
                    }
                    content.Add(new StringContent(redesignRequest.RoomType), "room_type");
                }
                else if (!string.IsNullOrEmpty(redesignRequest.RoomType))
                {
                    content.Add(new StringContent(redesignRequest.RoomType), "room_type");
                }

                if (!string.IsNullOrEmpty(redesignRequest.HouseAngle))
                    content.Add(new StringContent(redesignRequest.HouseAngle), "house_angle");

                if (!string.IsNullOrEmpty(redesignRequest.GardenType))
                    content.Add(new StringContent(redesignRequest.GardenType), "garden_type");

                if (!string.IsNullOrEmpty(redesignRequest.CustomInstruction))
                    content.Add(new StringContent(redesignRequest.CustomInstruction), "custom_instruction");

                _logger.LogInformation("📤 Sending request to HomeDesigns.ai API...");
                _logger.LogInformation("📋 Request parameters:");
                _logger.LogInformation("   • design_type: {DesignType}", redesignRequest.DesignType);
                _logger.LogInformation("   • ai_intervention: {AIIntervention}", redesignRequest.AIIntervention);
                _logger.LogInformation("   • no_design: {NoDesign}", redesignRequest.NoDesign);
                _logger.LogInformation("   • design_style: {DesignStyle}", redesignRequest.DesignStyle);
                _logger.LogInformation("   • room_type: {RoomType}", redesignRequest.RoomType ?? "null");
                _logger.LogInformation("   • house_angle: {HouseAngle}", redesignRequest.HouseAngle ?? "null");
                _logger.LogInformation("   • garden_type: {GardenType}", redesignRequest.GardenType ?? "null");
                _logger.LogInformation("   • keep_structural_element: {KeepStructural}", redesignRequest.KeepStructural.ToString().ToLower());

                // Send request to HomeDesigns.ai
                var apiResponse = await httpClient.PostAsync("https://homedesigns.ai/api/v2/perfect_redesign", content);
                var responseContent = await apiResponse.Content.ReadAsStringAsync();

                _logger.LogInformation("📨 API Response Status: {StatusCode}", apiResponse.StatusCode);
                _logger.LogInformation("📨 API Response Content: {Content}", responseContent);

                if (!apiResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ HomeDesigns.ai API error: {StatusCode} - {Response}", apiResponse.StatusCode, responseContent);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new HouseRedesignResponse
                    {
                        Success = false,
                        ErrorMessage = $"HomeDesigns.ai API error: {responseContent}"
                    }));
                    return errorResponse;
                }

                // Parse queue response
                var queueResponse = JsonSerializer.Deserialize<HomeDesignsQueueResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (queueResponse == null || string.IsNullOrEmpty(queueResponse.Id))
                {
                    _logger.LogError("❌ Invalid queue response from HomeDesigns.ai");
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new HouseRedesignResponse
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
                HomeDesignsStatusResponse? statusResponse = null;

                while (attempt < maxAttempts)
                {
                    await Task.Delay(5000); // Wait 5 seconds between polls
                    attempt++;

                    _logger.LogInformation("🔄 Checking status (attempt {Attempt}/{MaxAttempts})...", attempt, maxAttempts);

                    var statusUrl = $"https://homedesigns.ai/api/v2/perfect_redesign/status_check/{queueResponse.Id}";
                    var statusResult = await httpClient.GetAsync(statusUrl);
                    var statusContent = await statusResult.Content.ReadAsStringAsync();

                    // Log the raw response for debugging
                    _logger.LogInformation("📋 Raw status response: {StatusContent}", statusContent);

                    statusResponse = JsonSerializer.Deserialize<HomeDesignsStatusResponse>(statusContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (statusResponse == null)
                    {
                        _logger.LogWarning("⚠️ Failed to deserialize status response. Content: {Content}", statusContent);
                        continue;
                    }

                    _logger.LogInformation("📊 Status: {Status}", statusResponse.Status ?? "null");

                    // Check if complete - the API returns input_image and output_images when done
                    if (!string.IsNullOrEmpty(statusResponse.InputImage) &&
                        statusResponse.OutputImages != null &&
                        statusResponse.OutputImages.Count > 0)
                    {
                        _logger.LogInformation("✅ Redesign complete! Generated {Count} designs", statusResponse.OutputImages.Count);
                        break;
                    }

                    // Check for error states
                    if (statusResponse.Status?.ToLowerInvariant() == "failed" ||
                        statusResponse.Status?.ToLowerInvariant() == "error")
                    {
                        _logger.LogError("❌ HomeDesigns.ai processing failed: {Status}", statusResponse.Status);
                        break;
                    }
                }

                // Check final status
                if (statusResponse == null || statusResponse.OutputImages == null || statusResponse.OutputImages.Count == 0)
                {
                    _logger.LogError("❌ Timeout or error waiting for HomeDesigns.ai processing");
                    var errorResponse = req.CreateResponse(HttpStatusCode.RequestTimeout);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new HouseRedesignResponse
                    {
                        Success = false,
                        ErrorMessage = "Timeout waiting for redesign completion",
                        QueueId = queueResponse.Id,
                        Status = statusResponse?.Status ?? "unknown"
                    }));
                    return errorResponse;
                }

                // Step 3: Download and save output images to Data Lake
                _logger.LogInformation("💾 Saving {Count} redesigned images to Data Lake...", statusResponse.OutputImages.Count);

                var savedImageUrls = new List<string>();
                var directoryPath = redesignRequest.FilePath ?? "house-redesigns";
                var baseFileName = Path.GetFileNameWithoutExtension(redesignRequest.FileName);

                // Create fresh HTTP client for downloading images (without HomeDesigns.ai auth header)
                using var downloadClient = new HttpClient();

                for (int i = 0; i < statusResponse.OutputImages.Count; i++)
                {
                    var outputUrl = statusResponse.OutputImages[i];
                    try
                    {
                        _logger.LogInformation("📥 Downloading redesigned image {Index}/{Total} from: {Url}", i + 1, statusResponse.OutputImages.Count, outputUrl);

                        // Use clean HTTP client without authorization headers
                        var outputImageBytes = await downloadClient.GetByteArrayAsync(outputUrl);
                        _logger.LogInformation("✅ Downloaded {Size} bytes", outputImageBytes.Length);

                        // Generate filename with timestamp
                        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                        var fileName = $"{baseFileName}_redesign_{timestamp}_{i + 1}.png";

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

                            _logger.LogInformation("✅ Saved redesigned image {Index}/{Total} to: {Path}", i + 1, statusResponse.OutputImages.Count, fullPath);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to upload redesigned image {Index}/{Total}", i + 1, statusResponse.OutputImages.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error processing output image {Index}/{Total}", i + 1, statusResponse.OutputImages.Count);
                    }
                }

                var processingTime = DateTime.UtcNow - startTime;

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new HouseRedesignResponse
                {
                    Success = true,
                    TwinId = twinId,
                    QueueId = queueResponse.Id,
                    Status = statusResponse.Status,
                    InputImage = statusResponse.InputImage,
                    OutputImages = statusResponse.OutputImages,
                    SavedImageUrls = savedImageUrls,
                    DesignType = redesignRequest.DesignType,
                    DesignStyle = redesignRequest.DesignStyle,
                    AIIntervention = redesignRequest.AIIntervention,
                    NumberOfDesigns = statusResponse.OutputImages.Count,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    CreatedAt = statusResponse.CreatedAt,
                    StartedAt = statusResponse.StartedAt,
                    Message = $"Casa rediseñada exitosamente con {statusResponse.OutputImages.Count} variaciones. {savedImageUrls.Count} imágenes guardadas en Data Lake",
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ House redesign completed successfully in {Time} seconds", processingTime.TotalSeconds);
                _logger.LogInformation("   • Total redesigns: {Count}", statusResponse.OutputImages.Count);
                _logger.LogInformation("   • Saved to Data Lake: {SavedCount}", savedImageUrls.Count);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error processing house redesign after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new HouseRedesignResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al generar el rediseño de la casa"
                }));

                return errorResponse;
            }
        }
        /// <summary>
        /// Generate AI-based decor designs for interior images using HomeDesigns.ai
        /// POST /api/decor-design/{twinId}
        /// Body: { "filePath": "path/to/file", "fileName": "image.jpg", ... }
        /// </summary>
     /*   [Function("DecorDesign")]
        public async Task<HttpResponseData> DecorDesign(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "decor-design/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🎨 DecorDesign function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                // Validate TwinID
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorDesignResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                // Read and parse JSON request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var decorRequest = JsonSerializer.Deserialize<DecorDesignRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (decorRequest == null)
                {
                    _logger.LogError("❌ Failed to parse decor design request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorDesignResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid decor design request data format"
                    }));
                    return badResponse;
                }

                // Validate required fields
                if (string.IsNullOrEmpty(decorRequest.FilePath))
                {
                    _logger.LogError("❌ File path is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorDesignResponse
                    {
                        Success = false,
                        ErrorMessage = "File path is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(decorRequest.FileName))
                {
                    _logger.LogError("❌ File name is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorDesignResponse
                    {
                        Success = false,
                        ErrorMessage = "File name is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🎨 Decor design request:");
                _logger.LogInformation("   • File Path: {FilePath}", decorRequest.FilePath);
                _logger.LogInformation("   • File Name: {FileName}", decorRequest.FileName);

                // Step 1: Download file from Azure Data Lake
                _logger.LogInformation("📥 Downloading file from Azure Data Lake...");

                // Create DataLake client factory
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                var fullFilePath = string.IsNullOrEmpty(decorRequest.FilePath)
                    ? decorRequest.FileName
                    : $"{decorRequest.FilePath.TrimEnd('/')}/{decorRequest.FileName}";

                _logger.LogInformation("📂 Downloading from: {FullFilePath}", fullFilePath);

                byte[]? imageBytes = await dataLakeClient.DownloadFileAsync(fullFilePath);

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    _logger.LogError("❌ File not found or empty in Data Lake: {FilePath}", fullFilePath);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorDesignResponse
                    {
                        Success = false,
                        ErrorMessage = $"File not found in Data Lake: {fullFilePath}"
                    }));
                    return notFoundResponse;
                }

                _logger.LogInformation("✅ File downloaded successfully. Size: {Size} bytes", imageBytes.Length);

                // Step 2: Validate image dimensions (minimum 512x512 pixels)
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
                        await dimensionErrorResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorDesignResponse
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

                // Step 3: Send to HomeDesigns.ai Decor Design API
                var homeDesignsToken = _configuration["HOMEDESIGNS_AI_TOKEN"] ??
                                     _configuration["Values:HOMEDESIGNS_AI_TOKEN"] ??
                                     "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJuYW1lIjoiSm9yZuiLCJlbWFpbCI6ImpvcmdlbHVuYUBmbGF0Yml0LmNvbSJ9.rWhV_6nBGG0Oh3t2dwvNQsPAfzEBBEhPGzfmmJstEkM";

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {homeDesignsToken}");

                using var content = new MultipartFormDataContent();

                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                    GetMimeTypeFromFileName(decorRequest.FileName));
                content.Add(imageContent, "image", decorRequest.FileName);

                // Add required parameters
                content.Add(new StringContent(decorRequest.DesignType), "design_type");
                content.Add(new StringContent(decorRequest.NoDesign.ToString()), "no_design");
                content.Add(new StringContent(decorRequest.DesignStyle), "design_style");

                // room_type is REQUIRED for Interior designs
                if (decorRequest.DesignType.Equals("Interior", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(decorRequest.RoomType))
                    {
                        _logger.LogError("❌ room_type is required for Interior design type");
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse, req);
                        await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorDesignResponse
                        {
                            Success = false,
                            ErrorMessage = "room_type is required for Interior design type"
                        }));
                        return badResponse;
                    }
                    content.Add(new StringContent(decorRequest.RoomType), "room_type");
                }
                else if (!string.IsNullOrEmpty(decorRequest.RoomType))
                {
                    content.Add(new StringContent(decorRequest.RoomType), "room_type");
                }

                if (!string.IsNullOrEmpty(decorRequest.HouseAngle))
                    content.Add(new StringContent(decorRequest.HouseAngle), "house_angle");

                if (!string.IsNullOrEmpty(decorRequest.GardenType))
                    content.Add(new StringContent(decorRequest.GardenType), "garden_type");

                if (!string.IsNullOrEmpty(decorRequest.CustomInstruction))
                    content.Add(new StringContent(decorRequest.CustomInstruction), "custom_instruction");

                _logger.LogInformation("📤 Sending request to HomeDesigns.ai Decor Design API...");
                _logger.LogInformation("📋 Request parameters:");
                _logger.LogInformation("   • design_type: {DesignType}", decorRequest.DesignType);
                _logger.LogInformation("   • no_design: {NoDesign}", decorRequest.NoDesign);
                _logger.LogInformation("   • design_style: {DesignStyle}", decorRequest.DesignStyle);
                _logger.LogInformation("   • room_type: {RoomType}", decorRequest.RoomType ?? "null");
                _logger.LogInformation("   • house_angle: {HouseAngle}", decorRequest.HouseAngle ?? "null");
                _logger.LogInformation("   • garden_type: {GardenType}", decorRequest.GardenType ?? "null");
                _logger.LogInformation("   • custom_instruction: {CustomInstruction}", decorRequest.CustomInstruction ?? "null");

                var apiResponse = await httpClient.PostAsync("https://homedesigns.ai/api/v2/decor_design", content);
                var responseContent = await apiResponse.Content.ReadAsStringAsync();

                _logger.LogInformation("📨 API Response Status: {StatusCode}", apiResponse.StatusCode);
                _logger.LogInformation("📨 API Response Content: {Content}", responseContent);

                if (!apiResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ HomeDesigns.ai API error: {StatusCode} - {Response}", apiResponse.StatusCode, responseContent);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorDesignResponse
                    {
                        Success = false,
                        ErrorMessage = $"HomeDesigns.ai API error: {responseContent}"
                    }));
                    return errorResponse;
                }

                // Parse response - Decor Design returns results directly (no queue)
                var decorResponse = JsonSerializer.Deserialize<HomeDesignsDirectResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (decorResponse == null || decorResponse.OutputImages == null || decorResponse.OutputImages.Count == 0)
                {
                    _logger.LogError("❌ Invalid response from HomeDesigns.ai Decor Design API");
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorDesignResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid response from HomeDesigns.ai: No output images received"
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Decor Design complete! Generated {Count} designs", decorResponse.OutputImages.Count);

                // Step 4: Download and save output images to Data Lake
                _logger.LogInformation("💾 Saving {Count} decor design images to Data Lake...", decorResponse.OutputImages.Count);

                var savedImageUrls = new List<string>();
                var directoryPath = decorRequest.FilePath ?? "decor-designs";
                var baseFileName = Path.GetFileNameWithoutExtension(decorRequest.FileName);

                using var downloadClient = new HttpClient();

                for (int i = 0; i < decorResponse.OutputImages.Count; i++)
                {
                    var outputUrl = decorResponse.OutputImages[i];
                    try
                    {
                        _logger.LogInformation("📥 Downloading decor design image {Index}/{Total} from: {Url}", i + 1, decorResponse.OutputImages.Count, outputUrl);

                        var outputImageBytes = await downloadClient.GetByteArrayAsync(outputUrl);
                        _logger.LogInformation("✅ Downloaded {Size} bytes", outputImageBytes.Length);

                        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                        var fileName = $"{baseFileName}_decor_{timestamp}_{i + 1}.png";

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
                            var fullPath = $"{directoryPath}/{fileName}";
                            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullPath, TimeSpan.FromHours(24));
                            savedImageUrls.Add(sasUrl);

                            _logger.LogInformation("✅ Saved decor design image {Index}/{Total} to: {Path}", i + 1, decorResponse.OutputImages.Count, fullPath);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to upload decor design image {Index}/{Total}", i + 1, decorResponse.OutputImages.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error processing output image {Index}/{Total}", i + 1, decorResponse.OutputImages.Count);
                    }
                }

                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new DecorDesignResponse
                {
                    Success = true,
                    TwinId = twinId,
                    InputImage = decorResponse.InputImage,
                    OutputImages = decorResponse.OutputImages,
                    SavedImageUrls = savedImageUrls,
                    DesignType = decorRequest.DesignType,
                    DesignStyle = decorRequest.DesignStyle,
                    NumberOfDesigns = decorResponse.OutputImages.Count,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = $"Diseño de decoración completado exitosamente con {decorResponse.OutputImages.Count} variaciones. {savedImageUrls.Count} imágenes guardadas en Data Lake",
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Decor Design completed successfully in {Time} seconds", processingTime.TotalSeconds);
                _logger.LogInformation("   • Total designs: {Count}", decorResponse.OutputImages.Count);
                _logger.LogInformation("   • Saved to Data Lake: {SavedCount}", savedImageUrls.Count);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error processing decor design after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorDesignResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al procesar el diseño de decoración"
                }));

                return errorResponse;
            }
        } */

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
    }

}

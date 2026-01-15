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
    /// Azure Function for Decor Staging using HomeDesigns.ai API
    /// Stages furniture or decorative objects into real-life scenarios
    /// </summary>
    public class ImageDecorStagingFx
    {
        private readonly ILogger<ImageDecorStagingFx> _logger;
        private readonly IConfiguration _configuration;
        private readonly ILoggerFactory _loggerFactory;

        public ImageDecorStagingFx(
            ILogger<ImageDecorStagingFx> logger,
            IConfiguration configuration,
            ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _loggerFactory = loggerFactory;
        }

        /// <summary>
        /// OPTIONS handler for DecorStaging endpoint (CORS preflight)
        /// </summary>
        [Function("DecorStagingOptions")]
        public async Task<HttpResponseData> HandleDecorStagingOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "decor-staging/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for decor-staging/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Stage furniture/decor into real-life scenarios using HomeDesigns.ai Decor Staging API
        /// Downloads transparent image from Azure Data Lake and sends to HomeDesigns.ai
        /// POST /api/decor-staging/{twinId}
        /// Body: { "filePath": "path/to/file", "fileName": "image.png", "designType": "Interior", ... }
        /// </summary>
        [Function("DecorStaging")]
        public async Task<HttpResponseData> DecorStaging(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "decor-staging/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🛋️ DecorStaging function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                // Validate TwinID
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorStagingResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                // Read and parse JSON request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var decorRequest = JsonSerializer.Deserialize<DecorStagingRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (decorRequest == null)
                {
                    _logger.LogError("❌ Failed to parse decor staging request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorStagingResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid decor staging request data format"
                    }));
                    return badResponse;
                }

                // Validate required fields
                if (string.IsNullOrEmpty(decorRequest.FilePath))
                {
                    _logger.LogError("❌ File path is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorStagingResponse
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
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorStagingResponse
                    {
                        Success = false,
                        ErrorMessage = "File name is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🛋️ Decor staging parameters:");
                _logger.LogInformation("   • File Path: {FilePath}", decorRequest.FilePath);
                _logger.LogInformation("   • File Name: {FileName}", decorRequest.FileName);
                _logger.LogInformation("   • Design Type: {DesignType}", decorRequest.DesignType);
                _logger.LogInformation("   • Number of Designs: {NoDesign}", decorRequest.NoDesign);
                _logger.LogInformation("   • Design Style: {DesignStyle}", decorRequest.DesignStyle);

                // Step 1: Download file from Azure Data Lake
                _logger.LogInformation("📥 Downloading file from Azure Data Lake...");

                // Create DataLake client factory
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                // Combine file path and filename
                var fullFilePath = string.IsNullOrEmpty(decorRequest.FilePath)
                    ? decorRequest.FileName
                    : $"{decorRequest.FilePath.TrimEnd('/')}/{decorRequest.FileName}";

                _logger.LogInformation("📂 Downloading from: {FullFilePath}", fullFilePath);

                // Download file from Data Lake
                byte[]? imageBytes = await dataLakeClient.DownloadFileAsync(fullFilePath);

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    _logger.LogError("❌ File not found or empty in Data Lake: {FilePath}", fullFilePath);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorStagingResponse
                    {
                        Success = false,
                        ErrorMessage = $"File not found in Data Lake: {fullFilePath}"
                    }));
                    return notFoundResponse;
                }

                _logger.LogInformation("✅ File downloaded successfully. Size: {Size} bytes", imageBytes.Length);

                // Step 2: Validate image dimensions and transparency (HomeDesigns.ai requires minimum 512x512 pixels and transparent PNG)
                _logger.LogInformation("🔍 Validating image dimensions and transparency...");

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
                        await dimensionErrorResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorStagingResponse
                        {
                            Success = false,
                            ErrorMessage = $"Image dimensions ({image.Width}x{image.Height}) are too small. Minimum required: 512x512 pixels for optimal results."
                        }));
                        return dimensionErrorResponse;
                    }

                    // Validate PNG format and transparency
                    if (!decorRequest.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("⚠️ Image is not PNG format. Decor Staging requires transparent PNG images.");
                        // Note: We'll let the API validate this, but warn the user
                    }

                    // Check if image has transparency (alpha channel)
                    var pixelFormat = image.PixelFormat;
                    var hasAlpha = pixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb ||
                                   pixelFormat == System.Drawing.Imaging.PixelFormat.Format64bppArgb ||
                                   pixelFormat == System.Drawing.Imaging.PixelFormat.Format16bppArgb1555 ||
                                   System.Drawing.Image.IsAlphaPixelFormat(pixelFormat);

                    if (!hasAlpha)
                    {
                        _logger.LogWarning("⚠️ Image does not appear to have transparency (alpha channel). Decor Staging requires transparent PNG images with isolated objects.");

                        var transparencyWarningResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(transparencyWarningResponse, req);
                        await transparencyWarningResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorStagingResponse
                        {
                            Success = false,
                            ErrorMessage = "Image does not have transparency. Decor Staging requires transparent PNG images with isolated furniture/decor objects. Please use an image with a transparent background (alpha channel)."
                        }));
                        return transparencyWarningResponse;
                    }

                    _logger.LogInformation("✅ Image validated: PNG format with transparency (alpha channel)");
                }
                catch (Exception dimEx)
                {
                    _logger.LogWarning(dimEx, "⚠️ Could not validate image format, continuing anyway");
                }

                // Step 3: Send to HomeDesigns.ai Decor Staging API
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
                    GetMimeTypeFromFileName(decorRequest.FileName));
                content.Add(imageContent, "image", decorRequest.FileName);

                // Add required parameters
                content.Add(new StringContent(decorRequest.DesignType), "design_type");
                content.Add(new StringContent(decorRequest.NoDesign.ToString()), "no_design");
                content.Add(new StringContent(decorRequest.DesignStyle), "design_style");

                // Add conditional parameters based on design_type
                if (decorRequest.DesignType.Equals("Interior", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(decorRequest.RoomType))
                    {
                        _logger.LogError("❌ room_type is required for Interior design type");
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse, req);
                        await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorStagingResponse
                        {
                            Success = false,
                            ErrorMessage = "room_type is required for Interior design type"
                        }));
                        return badResponse;
                    }
                    content.Add(new StringContent(decorRequest.RoomType), "room_type");
                }
                else if (decorRequest.DesignType.Equals("Exterior", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(decorRequest.HouseAngle))
                    {
                        _logger.LogError("❌ house_angle is required for Exterior design type");
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse, req);
                        await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorStagingResponse
                        {
                            Success = false,
                            ErrorMessage = "house_angle is required for Exterior design type"
                        }));
                        return badResponse;
                    }
                    content.Add(new StringContent(decorRequest.HouseAngle), "house_angle");
                }
                else if (decorRequest.DesignType.Equals("Garden", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(decorRequest.GardenType))
                    {
                        _logger.LogError("❌ garden_type is required for Garden design type");
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse, req);
                        await badResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorStagingResponse
                        {
                            Success = false,
                            ErrorMessage = "garden_type is required for Garden design type"
                        }));
                        return badResponse;
                    }
                    content.Add(new StringContent(decorRequest.GardenType), "garden_type");
                }

                // Add optional prompt
                if (!string.IsNullOrEmpty(decorRequest.Prompt))
                    content.Add(new StringContent(decorRequest.Prompt), "prompt");

                _logger.LogInformation("📤 Sending request to HomeDesigns.ai Decor Staging API...");
                _logger.LogInformation("📋 Request parameters:");
                _logger.LogInformation("   • design_type: {DesignType}", decorRequest.DesignType);
                _logger.LogInformation("   • no_design: {NoDesign}", decorRequest.NoDesign);
                _logger.LogInformation("   • design_style: {DesignStyle}", decorRequest.DesignStyle);
                _logger.LogInformation("   • room_type: {RoomType}", decorRequest.RoomType ?? "N/A");
                _logger.LogInformation("   • house_angle: {HouseAngle}", decorRequest.HouseAngle ?? "N/A");
                _logger.LogInformation("   • garden_type: {GardenType}", decorRequest.GardenType ?? "N/A");
                _logger.LogInformation("   • prompt: {Prompt}", decorRequest.Prompt ?? "null");

                // Send request to HomeDesigns.ai Decor Staging endpoint (SYNCHRONOUS - returns immediately)
                var apiResponse = await httpClient.PostAsync("https://homedesigns.ai/api/v2/decor_staging", content);
                var responseContent = await apiResponse.Content.ReadAsStringAsync();

                _logger.LogInformation("📨 API Response Status: {StatusCode}", apiResponse.StatusCode);
                _logger.LogInformation("📨 API Response Content: {Content}", responseContent);

                if (!apiResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ HomeDesigns.ai API error: {StatusCode} - {Response}", apiResponse.StatusCode, responseContent);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorStagingResponse
                    {
                        Success = false,
                        ErrorMessage = $"HomeDesigns.ai API error: {responseContent}"
                    }));
                    return errorResponse;
                }

                // Parse direct response (Decor Staging returns results immediately - synchronous)
                var decorResponse = JsonSerializer.Deserialize<DecorStagingDirectResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (decorResponse == null ||
                    decorResponse.OutputImages == null ||
                    decorResponse.OutputImages.Count == 0)
                {
                    _logger.LogError("❌ Invalid response from HomeDesigns.ai Decor Staging API");
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorStagingResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid response from HomeDesigns.ai: No output images received"
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Decor staging complete! Generated {Count} designs", decorResponse.OutputImages.Count);

                // Step 4: Download and save output images to Data Lake
                _logger.LogInformation("💾 Saving {Count} decor staging images to Data Lake...", decorResponse.OutputImages.Count);

                var savedImageUrls = new List<string>();
                var directoryPath = decorRequest.FilePath ?? "decor-staging";
                var baseFileName = Path.GetFileNameWithoutExtension(decorRequest.FileName);

                // Create fresh HTTP client for downloading images (without HomeDesigns.ai auth header)
                using var downloadClient = new HttpClient();

                for (int i = 0; i < decorResponse.OutputImages.Count; i++)
                {
                    var outputUrl = decorResponse.OutputImages[i];
                    try
                    {
                        _logger.LogInformation("📥 Downloading decor staging image {Index}/{Total} from: {Url}", i + 1, decorResponse.OutputImages.Count, outputUrl);

                        // Use clean HTTP client without authorization headers
                        var outputImageBytes = await downloadClient.GetByteArrayAsync(outputUrl);
                        _logger.LogInformation("✅ Downloaded {Size} bytes", outputImageBytes.Length);

                        // Generate filename with timestamp
                        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                        var fileName = $"{baseFileName}_decor_{timestamp}_{i + 1}.png";

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

                            _logger.LogInformation("✅ Saved decor staging image {Index}/{Total} to: {Path}", i + 1, decorResponse.OutputImages.Count, fullPath);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to upload decor staging image {Index}/{Total}", i + 1, decorResponse.OutputImages.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error processing output image {Index}/{Total}", i + 1, decorResponse.OutputImages.Count);
                    }
                }

                var processingTime = DateTime.UtcNow - startTime;

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new DecorStagingResponse
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
                    Message = $"Decor staging completado exitosamente con {decorResponse.OutputImages.Count} variaciones. {savedImageUrls.Count} imágenes guardadas en Data Lake",
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Decor staging completed successfully in {Time} seconds", processingTime.TotalSeconds);
                _logger.LogInformation("   • Total designs: {Count}", decorResponse.OutputImages.Count);
                _logger.LogInformation("   • Saved to Data Lake: {SavedCount}", savedImageUrls.Count);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error processing decor staging after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new DecorStagingResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al procesar el decor staging"
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
                _ => "image/png" // Default to PNG for transparent images
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

        #region Furniture Removal Function

        /// <summary>
        /// OPTIONS handler for FurnitureRemoval endpoint (CORS preflight)
        /// </summary>
        [Function("FurnitureRemovalOptions")]
        public async Task<HttpResponseData> HandleFurnitureRemovalOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "furniture-removal/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for furniture-removal/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Remove furniture from interior/exterior spaces using HomeDesigns.ai Furniture Removal API
        /// Downloads image and masked image from Azure Data Lake and sends to HomeDesigns.ai
        /// POST /api/furniture-removal/{twinId}
        /// Body: { "filePath": "path/to/file", "fileName": "image.jpg", "maskedFilePath": "path/to/mask", "maskedFileName": "mask.jpg" }
        /// </summary>
        [Function("FurnitureRemoval")]
        public async Task<HttpResponseData> FurnitureRemoval(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "furniture-removal/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🗑️ FurnitureRemoval function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                // Validate TwinID
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new FurnitureRemovalResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                // Read and parse JSON request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var removalRequest = JsonSerializer.Deserialize<FurnitureRemovalRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (removalRequest == null)
                {
                    _logger.LogError("❌ Failed to parse furniture removal request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new FurnitureRemovalResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid furniture removal request data format"
                    }));
                    return badResponse;
                }

                // Validate required fields
                if (string.IsNullOrEmpty(removalRequest.FilePath) || string.IsNullOrEmpty(removalRequest.FileName))
                {
                    _logger.LogError("❌ File path and file name are required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new FurnitureRemovalResponse
                    {
                        Success = false,
                        ErrorMessage = "File path and file name are required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(removalRequest.MaskedFilePath) || string.IsNullOrEmpty(removalRequest.MaskedFileName))
                {
                    _logger.LogError("❌ Masked file path and masked file name are required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new FurnitureRemovalResponse
                    {
                        Success = false,
                        ErrorMessage = "Masked file path and masked file name are required for furniture removal"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🗑️ Furniture removal parameters:");
                _logger.LogInformation("   • Image File Path: {FilePath}", removalRequest.FilePath);
                _logger.LogInformation("   • Image File Name: {FileName}", removalRequest.FileName);
                _logger.LogInformation("   • Masked File Path: {MaskedFilePath}", removalRequest.MaskedFilePath);
                _logger.LogInformation("   • Masked File Name: {MaskedFileName}", removalRequest.MaskedFileName);

                // Step 1: Download original image from Azure Data Lake
                _logger.LogInformation("📥 Downloading original image from Azure Data Lake...");

                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                var fullFilePath = string.IsNullOrEmpty(removalRequest.FilePath)
                    ? removalRequest.FileName
                    : $"{removalRequest.FilePath.TrimEnd('/')}/{removalRequest.FileName}";

                _logger.LogInformation("📂 Downloading original image from: {FullFilePath}", fullFilePath);

                byte[]? imageBytes = await dataLakeClient.DownloadFileAsync(fullFilePath);

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    _logger.LogError("❌ Original image not found or empty in Data Lake: {FilePath}", fullFilePath);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new FurnitureRemovalResponse
                    {
                        Success = false,
                        ErrorMessage = $"Original image not found in Data Lake: {fullFilePath}"
                    }));
                    return notFoundResponse;
                }

                _logger.LogInformation("✅ Original image downloaded successfully. Size: {Size} bytes", imageBytes.Length);

                // Step 2: Download masked image from Azure Data Lake
                _logger.LogInformation("📥 Downloading masked image from Azure Data Lake...");

                var fullMaskedFilePath = string.IsNullOrEmpty(removalRequest.MaskedFilePath)
                    ? removalRequest.MaskedFileName
                    : $"{removalRequest.MaskedFilePath.TrimEnd('/')}/{removalRequest.MaskedFileName}";

                _logger.LogInformation("📂 Downloading masked image from: {FullMaskedFilePath}", fullMaskedFilePath);

                byte[]? maskedImageBytes = await dataLakeClient.DownloadFileAsync(fullMaskedFilePath);

                if (maskedImageBytes == null || maskedImageBytes.Length == 0)
                {
                    _logger.LogError("❌ Masked image not found or empty in Data Lake: {FilePath}", fullMaskedFilePath);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new FurnitureRemovalResponse
                    {
                        Success = false,
                        ErrorMessage = $"Masked image not found in Data Lake: {fullMaskedFilePath}"
                    }));
                    return notFoundResponse;
                }

                _logger.LogInformation("✅ Masked image downloaded successfully. Size: {Size} bytes", maskedImageBytes.Length);

                // Step 3: Validate image dimensions
                _logger.LogInformation("🔍 Validating image dimensions...");

                try
                {
                    using var imageStream = new MemoryStream(imageBytes);
                    using var image = System.Drawing.Image.FromStream(imageStream);

                    _logger.LogInformation("📐 Original image dimensions: {Width}x{Height} pixels", image.Width, image.Height);

                    if (image.Width < 512 || image.Height < 512)
                    {
                        _logger.LogError("❌ Image dimensions too small: {Width}x{Height}. Minimum required: 512x512 pixels",
                            image.Width, image.Height);

                        var dimensionErrorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(dimensionErrorResponse, req);
                        await dimensionErrorResponse.WriteStringAsync(JsonSerializer.Serialize(new FurnitureRemovalResponse
                        {
                            Success = false,
                            ErrorMessage = $"Image dimensions ({image.Width}x{image.Height}) are too small. Minimum required: 512x512 pixels."
                        }));
                        return dimensionErrorResponse;
                    }

                    _logger.LogInformation("✅ Image dimensions validated successfully");
                }
                catch (Exception dimEx)
                {
                    _logger.LogWarning(dimEx, "⚠️ Could not validate image dimensions, continuing anyway");
                }

                // Step 4: Send to HomeDesigns.ai Furniture Removal API
                var homeDesignsToken = _configuration["HOMEDESIGNS_AI_TOKEN"] ??
                                     _configuration["Values:HOMEDESIGNS_AI_TOKEN"] ??
                                     "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJuYW1lIjoiSm9yZuiLCJlbWFpbCI6ImpvcmdlbHVuYUBmbGF0Yml0LmNvb SJ9.rWhV_6nBGG0Oh3t2dwvNQsPAfzEBBEhPGzfmmJstEkM";

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {homeDesignsToken}");

                // Prepare multipart form data
                using var content = new MultipartFormDataContent();

                // Add original image
                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                    GetMimeTypeFromFileName(removalRequest.FileName));
                content.Add(imageContent, "image", removalRequest.FileName);

                // Add masked image
                var maskedImageContent = new ByteArrayContent(maskedImageBytes);
                maskedImageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                    GetMimeTypeFromFileName(removalRequest.MaskedFileName));
                content.Add(maskedImageContent, "masked_image", removalRequest.MaskedFileName);

                _logger.LogInformation("📤 Sending request to HomeDesigns.ai Furniture Removal API...");

                // Send request to HomeDesigns.ai Furniture Removal endpoint (SYNCHRONOUS - returns immediately)
                var apiResponse = await httpClient.PostAsync("https://homedesigns.ai/api/v2/furniture_removal", content);
                var responseContent = await apiResponse.Content.ReadAsStringAsync();

                _logger.LogInformation("📨 API Response Status: {StatusCode}", apiResponse.StatusCode);
                _logger.LogInformation("📨 API Response Content: {Content}", responseContent);

                if (!apiResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ HomeDesigns.ai API error: {StatusCode} - {Response}", apiResponse.StatusCode, responseContent);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new FurnitureRemovalResponse
                    {
                        Success = false,
                        ErrorMessage = $"HomeDesigns.ai API error: {responseContent}"
                    }));
                    return errorResponse;
                }

                // Parse direct response (Furniture Removal returns results immediately - synchronous)
                var removalResponse = JsonSerializer.Deserialize<FurnitureRemovalDirectResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (removalResponse == null ||
                    removalResponse.OutputImages == null ||
                    removalResponse.OutputImages.Count == 0)
                {
                    _logger.LogError("❌ Invalid response from HomeDesigns.ai Furniture Removal API");
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new FurnitureRemovalResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid response from HomeDesigns.ai: No output images received"
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Furniture removal complete! Generated {Count} result(s)", removalResponse.OutputImages.Count);

                // Step 5: Download and save output images to Data Lake
                _logger.LogInformation("💾 Saving {Count} furniture removal images to Data Lake...", removalResponse.OutputImages.Count);

                var savedImageUrls = new List<string>();
                var directoryPath = removalRequest.FilePath ?? "furniture-removal";
                var baseFileName = Path.GetFileNameWithoutExtension(removalRequest.FileName);

                using var downloadClient = new HttpClient();

                for (int i = 0; i < removalResponse.OutputImages.Count; i++)
                {
                    var outputUrl = removalResponse.OutputImages[i];
                    try
                    {
                        _logger.LogInformation("📥 Downloading furniture removal image {Index}/{Total} from: {Url}", i + 1, removalResponse.OutputImages.Count, outputUrl);

                        var outputImageBytes = await downloadClient.GetByteArrayAsync(outputUrl);
                        _logger.LogInformation("✅ Downloaded {Size} bytes", outputImageBytes.Length);

                        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                        var fileName = $"{baseFileName}_removal_{timestamp}_{i + 1}.png";

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

                            _logger.LogInformation("✅ Saved furniture removal image {Index}/{Total} to: {Path}", i + 1, removalResponse.OutputImages.Count, fullPath);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to upload furniture removal image {Index}/{Total}", i + 1, removalResponse.OutputImages.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error processing output image {Index}/{Total}", i + 1, removalResponse.OutputImages.Count);
                    }
                }

                var processingTime = DateTime.UtcNow - startTime;

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new FurnitureRemovalResponse
                {
                    Success = true,
                    TwinId = twinId,
                    InputImage = removalResponse.InputImage,
                    OutputImages = removalResponse.OutputImages,
                    SavedImageUrls = savedImageUrls,
                    NumberOfResults = removalResponse.OutputImages.Count,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = $"Furniture removal completado exitosamente. {savedImageUrls.Count} imágenes guardadas en Data Lake",
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Furniture removal completed successfully in {Time} seconds", processingTime.TotalSeconds);
                _logger.LogInformation("   • Total results: {Count}", removalResponse.OutputImages.Count);
                _logger.LogInformation("   • Saved to Data Lake: {SavedCount}", savedImageUrls.Count);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error processing furniture removal after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new FurnitureRemovalResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al procesar furniture removal"
                }));

                return errorResponse;
            }
        }

        #endregion
    }

    #region Request/Response Models

    /// <summary>
    /// Request model for decor staging from Data Lake
    /// </summary>
    public class DecorStagingRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DesignType { get; set; } = "Interior";
        public int NoDesign { get; set; } = 1;
        public string DesignStyle { get; set; } = "Modern";
        public string? RoomType { get; set; }
        public string? HouseAngle { get; set; }
        public string? GardenType { get; set; }
        public string? Prompt { get; set; }
    }

    /// <summary>
    /// Direct response from HomeDesigns.ai Decor Staging API (synchronous)
    /// </summary>
    public class DecorStagingDirectResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("input_image")]
        public string? InputImage { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("output_images")]
        public List<string>? OutputImages { get; set; }
    }

    /// <summary>
    /// Response model for decor staging
    /// </summary>
    public class DecorStagingResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string? InputImage { get; set; }
        public List<string>? OutputImages { get; set; }
        public List<string>? SavedImageUrls { get; set; }
        public string DesignType { get; set; } = string.Empty;
        public string DesignStyle { get; set; } = string.Empty;
        public int NumberOfDesigns { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #region Furniture Removal Models

    /// <summary>
    /// Request model for furniture removal from Data Lake
    /// </summary>
    public class FurnitureRemovalRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string MaskedFilePath { get; set; } = string.Empty;
        public string MaskedFileName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Direct response from HomeDesigns.ai Furniture Removal API (synchronous)
    /// </summary>
    public class FurnitureRemovalDirectResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("input_image")]
        public string? InputImage { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("output_images")]
        public List<string>? OutputImages { get; set; }
    }

    /// <summary>
    /// Response model for furniture removal
    /// </summary>
    public class FurnitureRemovalResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string? InputImage { get; set; }
        public List<string>? OutputImages { get; set; }
        public List<string>? SavedImageUrls { get; set; }
        public int NumberOfResults { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion

    #endregion
}

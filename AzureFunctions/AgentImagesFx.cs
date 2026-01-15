using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Images;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;
using System.Collections.Generic;
using System.Net.Http;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.AzureFunctions
{
    /// <summary>
    /// Azure Function for AI-powered image generation using DALL-E and Azure Data Lake storage
    /// </summary>
    public class AgentImagesFx
    {
        private readonly ILogger<AgentImagesFx> _logger;
        private readonly IConfiguration _configuration;
        private readonly ILoggerFactory _loggerFactory;

        public AgentImagesFx(
            ILogger<AgentImagesFx> logger, 
            IConfiguration configuration,
            ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _loggerFactory = loggerFactory;
        }

        /// <summary>
        /// OPTIONS handler for GenerateImage endpoint (CORS preflight)
        /// </summary>
        [Function("GenerateImageOptions")]
        public async Task<HttpResponseData> HandleGenerateImageOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "generate-image/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for generate-image/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Generate an AI image from a text prompt using DALL-E and save it to Azure Data Lake
        /// POST /api/generate-image/{twinId}
        /// </summary>
        [Function("GenerateImage")]
        public async Task<HttpResponseData> GenerateImage(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "generate-image/{microsoftOID}")] HttpRequestData req,
            string MicrosoftOID)
        {
            _logger.LogInformation("🎨 GenerateImage function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                // Validate TwinID
                if (string.IsNullOrEmpty(MicrosoftOID))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateImageResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🎨 Generating image for Twin ID: {TwinId}", MicrosoftOID);

                // Read and parse request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var imageRequest = JsonSerializer.Deserialize<GenerateImageRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (imageRequest == null)
                {
                    _logger.LogError("❌ Failed to parse image generation request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateImageResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid image generation request data format"
                    }));
                    return badResponse;
                }

                // Validate prompt
                if (string.IsNullOrWhiteSpace(imageRequest.Prompt))
                {
                    _logger.LogError("❌ Prompt is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateImageResponse
                    {
                        Success = false,
                        ErrorMessage = "Prompt is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🎨 Image generation request:");
                _logger.LogInformation("   • Prompt: {Prompt}", imageRequest.Prompt);
                _logger.LogInformation("   • Quality: {Quality}", imageRequest.Quality ?? "Standard");
                _logger.LogInformation("   • Size: {Size}", imageRequest.Size ?? "1024x1024");
                _logger.LogInformation("   • Style: {Style}", imageRequest.Style ?? "Vivid");
                _logger.LogInformation("   • File Name: {FileName}", imageRequest.FileName ?? "generated_image");
                _logger.LogInformation("   • Directory: {Directory}", imageRequest.DirectoryPath ?? "generated-images");

                // Parse quality, size, and style enums
                GeneratedImageQuality? quality = ParseQuality(imageRequest.Quality);
                GeneratedImageSize? size = ParseSize(imageRequest.Size);
                GeneratedImageStyle? style = ParseStyle(imageRequest.Style);

                // Initialize AgentCreateImages
                var imageLogger = _loggerFactory.CreateLogger<AgentCreateImages>();
                var imageAgent = new AgentCreateImages(imageLogger, _configuration, _loggerFactory);

                // Generate and save image to Data Lake
                var result = await imageAgent.GenerateAndSaveImageAsync(
                    prompt: imageRequest.Prompt,
                    twinId: MicrosoftOID,
                    fileName: imageRequest.FileName ?? $"generated_image_{DateTime.UtcNow:yyyyMMdd_HHmmss}",
                    directoryPath: imageRequest.DirectoryPath,
                    quality: quality,
                    size: size,
                    style: style
                );

                var processingTime = DateTime.UtcNow - startTime;

                if (!result.Success)
                {
                    _logger.LogWarning("⚠️ Failed to generate image: {Error}", result.ErrorMessage);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateImageResponse
                    {
                        Success = false,
                        ErrorMessage = result.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                _logger.LogInformation("✅ Image generated and saved successfully");
                _logger.LogInformation("   • File: {FullPath}", result.FullPath);
                _logger.LogInformation("   • Size: {Size} bytes", result.FileSizeBytes);
                _logger.LogInformation("   • Processing Time: {Time}ms", result.ProcessingTimeMs);

                // Create successful response
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GenerateImageResponse
                {
                    Success = true,
                    TwinId = MicrosoftOID,
                    OriginalPrompt = result.GenerationResult?.OriginalPrompt ?? imageRequest.Prompt,
                    RevisedPrompt = result.GenerationResult?.RevisedPrompt ?? imageRequest.Prompt,
                    ImageUrl = result.GenerationResult?.ImageUrl ?? string.Empty,
                    SasUrl = result.SasUrl,
                    ContainerName = result.ContainerName,
                    DirectoryPath = result.DirectoryPath,
                    FileName = result.FileName,
                    FullPath = result.FullPath,
                    FileSizeBytes = result.FileSizeBytes,
                    Quality = result.GenerationResult?.Quality ?? imageRequest.Quality ?? "Standard",
                    Size = result.GenerationResult?.Size ?? imageRequest.Size ?? "W1024xH1024",
                    Style = result.GenerationResult?.Style ?? imageRequest.Style ?? "Vivid",
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Imagen generada y guardada exitosamente en Azure Data Lake",
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
                _logger.LogError(ex, "❌ Error generating image after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateImageResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al generar la imagen"
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// OPTIONS handler for GenerateImageSimple endpoint (CORS preflight)
        /// </summary>
        [Function("GenerateImageSimpleOptions")]
        public async Task<HttpResponseData> HandleGenerateImageSimpleOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "generate-image-simple/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for generate-image-simple/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Generate an AI image with simplified parameters (standard quality, 1024x1024, vivid style)
        /// POST /api/generate-image-simple/{twinId}
        /// Body: { "prompt": "A serene mountain landscape" }
        /// </summary>
        [Function("GenerateImageSimple")]
        public async Task<HttpResponseData> GenerateImageSimple(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "generate-image-simple/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🎨 GenerateImageSimple function triggered");
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateImageResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var simpleRequest = JsonSerializer.Deserialize<SimpleImageRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (simpleRequest == null || string.IsNullOrWhiteSpace(simpleRequest.Prompt))
                {
                    _logger.LogError("❌ Prompt is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateImageResponse
                    {
                        Success = false,
                        ErrorMessage = "Prompt is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("🎨 Generating simple image with prompt: {Prompt}", simpleRequest.Prompt);

                var imageLogger = _loggerFactory.CreateLogger<AgentCreateImages>();
                var imageAgent = new AgentCreateImages(imageLogger, _configuration, _loggerFactory);

                var result = await imageAgent.GenerateAndSaveImageAsync(
                    prompt: simpleRequest.Prompt,
                    twinId: twinId,
                    fileName: $"simple_image_{DateTime.UtcNow:yyyyMMdd_HHmmss}"
                );

                var processingTime = DateTime.UtcNow - startTime;

                if (!result.Success)
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateImageResponse
                    {
                        Success = false,
                        ErrorMessage = result.ErrorMessage,
                        ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                    }));
                    return errorResponse;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new GenerateImageResponse
                {
                    Success = true,
                    TwinId = twinId,
                    OriginalPrompt = simpleRequest.Prompt,
                    RevisedPrompt = result.GenerationResult?.RevisedPrompt ?? simpleRequest.Prompt,
                    ImageUrl = result.GenerationResult?.ImageUrl ?? string.Empty,
                    SasUrl = result.SasUrl,
                    ContainerName = result.ContainerName,
                    FullPath = result.FullPath,
                    FileSizeBytes = result.FileSizeBytes,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Imagen simple generada exitosamente",
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
                _logger.LogError(ex, "❌ Error generating simple image");

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new GenerateImageResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2)
                }));

                return errorResponse;
            }
        }

        #region Helper Methods

        private GeneratedImageQuality? ParseQuality(string? quality)
        {
            if (string.IsNullOrWhiteSpace(quality)) return null;

            return quality.ToLowerInvariant() switch
            {
                "standard" => GeneratedImageQuality.Standard,
                "hd" => GeneratedImageQuality.High,
                "high" => GeneratedImageQuality.High,
                _ => null
            };
        }

        private GeneratedImageSize? ParseSize(string? size)
        {
            if (string.IsNullOrWhiteSpace(size)) return null;

            return size.ToLowerInvariant() switch
            {
                "1024x1024" => GeneratedImageSize.W1024xH1024,
                "1024x1792" => GeneratedImageSize.W1024xH1792,
                "1792x1024" => GeneratedImageSize.W1792xH1024,
                _ => null
            };
        }

        private GeneratedImageStyle? ParseStyle(string? style)
        {
            if (string.IsNullOrWhiteSpace(style)) return null;

            return style.ToLowerInvariant() switch
            {
                "vivid" => GeneratedImageStyle.Vivid,
                "natural" => GeneratedImageStyle.Natural,
                _ => null
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

        #region House Redesign - HomeDesigns.ai Integration

        /// <summary>
        /// OPTIONS handler for PerfectRedesign endpoint (CORS preflight)
        /// </summary>
        [Function("PerfectRedesignOptions")]
        public async Task<HttpResponseData> HandlePerfectRedesignOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "house-redesign/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for house-redesign/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        

        #region Beautiful Redesign - HomeDesigns.ai Integration

        /// <summary>
        /// OPTIONS handler for BeautifulRedesign endpoint (CORS preflight)
        /// </summary>
        [Function("BeautifulRedesignOptions")]
        public async Task<HttpResponseData> HandleBeautifulRedesignOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "beautiful-redesign/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for beautiful-redesign/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        /// <summary>
        /// Transform house images using HomeDesigns.ai Beautiful Redesign API
        /// Downloads image from Azure Data Lake and sends to HomeDesigns.ai
        /// POST /api/beautiful-redesign/{twinId}
        /// Body: { "filePath": "path/to/file", "fileName": "image.jpg", "designType": "Interior", ... }
        /// </summary>
        [Function("BeautifulRedesign")]
        public async Task<HttpResponseData> BeautifulRedesign(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "beautiful-redesign/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("✨ BeautifulRedesign function triggered for TwinID: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                // Validate TwinID
                if (string.IsNullOrEmpty(twinId))
                {
                    _logger.LogError("❌ Twin ID parameter is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new BeautifulRedesignResponse
                    {
                        Success = false,
                        ErrorMessage = "Twin ID parameter is required"
                    }));
                    return badResponse;
                }

                // Read and parse JSON request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("📝 Request body length: {Length} characters", requestBody.Length);

                var beautifulRequest = JsonSerializer.Deserialize<BeautifulRedesignRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (beautifulRequest == null)
                {
                    _logger.LogError("❌ Failed to parse beautiful redesign request data");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new BeautifulRedesignResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid beautiful redesign request data format"
                    }));
                    return badResponse;
                }

                // Validate required fields
                if (string.IsNullOrEmpty(beautifulRequest.FilePath))
                {
                    _logger.LogError("❌ File path is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new BeautifulRedesignResponse
                    {
                        Success = false,
                        ErrorMessage = "File path is required"
                    }));
                    return badResponse;
                }

                if (string.IsNullOrEmpty(beautifulRequest.FileName))
                {
                    _logger.LogError("❌ File name is required");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    AddCorsHeaders(badResponse, req);
                    await badResponse.WriteStringAsync(JsonSerializer.Serialize(new BeautifulRedesignResponse
                    {
                        Success = false,
                        ErrorMessage = "File name is required"
                    }));
                    return badResponse;
                }

                _logger.LogInformation("✨ Beautiful redesign request:");
                _logger.LogInformation("   • File Path: {FilePath}", beautifulRequest.FilePath);
                _logger.LogInformation("   • File Name: {FileName}", beautifulRequest.FileName);
                _logger.LogInformation("   • Design Type: {DesignType}", beautifulRequest.DesignType);
                _logger.LogInformation("   • AI Intervention: {AIIntervention}", beautifulRequest.AIIntervention);
                _logger.LogInformation("   • Number of Designs: {NoDesign}", beautifulRequest.NoDesign);
                _logger.LogInformation("   • Design Style: {DesignStyle}", beautifulRequest.DesignStyle);

                // Step 1: Download file from Azure Data Lake
                _logger.LogInformation("📥 Downloading file from Azure Data Lake...");
                
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var dataLakeFactory = _configuration.CreateDataLakeFactory(loggerFactory);
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);

                var fullFilePath = string.IsNullOrEmpty(beautifulRequest.FilePath) 
                    ? beautifulRequest.FileName 
                    : $"{beautifulRequest.FilePath.TrimEnd('/')}/{beautifulRequest.FileName}";

                _logger.LogInformation("📂 Downloading from: {FullFilePath}", fullFilePath);

                byte[]? imageBytes = await dataLakeClient.DownloadFileAsync(fullFilePath);

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    _logger.LogError("❌ File not found or empty in Data Lake: {FilePath}", fullFilePath);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    AddCorsHeaders(notFoundResponse, req);
                    await notFoundResponse.WriteStringAsync(JsonSerializer.Serialize(new BeautifulRedesignResponse
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
                        await dimensionErrorResponse.WriteStringAsync(JsonSerializer.Serialize(new BeautifulRedesignResponse
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

                // Step 3: Send to HomeDesigns.ai Beautiful Redesign API
                var homeDesignsToken = _configuration["HOMEDESIGNS_AI_TOKEN"] ?? 
                                     _configuration["Values:HOMEDESIGNS_AI_TOKEN"] ?? 
                                     "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJuYW1lIjoiSm9yZuiLCJlbWFpbCI6ImpvcmdlbHVuYUBmbGF0Yml0LmNvbSJ9.rWhV_6nBGG0Oh3t2dwvNQsPAfzEBBEhPGzfmmJstEkM";

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {homeDesignsToken}");

                using var content = new MultipartFormDataContent();
                
                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                    GetMimeTypeFromFileName(beautifulRequest.FileName));
                content.Add(imageContent, "image", beautifulRequest.FileName);

                // Add required parameters
                content.Add(new StringContent(beautifulRequest.DesignType), "design_type");
                content.Add(new StringContent(beautifulRequest.AIIntervention), "ai_intervention");
                content.Add(new StringContent(beautifulRequest.NoDesign.ToString()), "no_design");
                content.Add(new StringContent(beautifulRequest.DesignStyle), "design_style");
                content.Add(new StringContent(beautifulRequest.KeepStructural.ToString().ToLower()), "keep_structural_element");

                // room_type is REQUIRED for Interior designs
                if (beautifulRequest.DesignType.Equals("Interior", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(beautifulRequest.RoomType))
                    {
                        _logger.LogError("❌ room_type is required for Interior design type");
                        var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                        AddCorsHeaders(badResponse, req);
                        await badResponse.WriteStringAsync(JsonSerializer.Serialize(new BeautifulRedesignResponse
                        {
                            Success = false,
                            ErrorMessage = "room_type is required for Interior design type"
                        }));
                        return badResponse;
                    }
                    content.Add(new StringContent(beautifulRequest.RoomType), "room_type");
                }
                else if (!string.IsNullOrEmpty(beautifulRequest.RoomType))
                {
                    content.Add(new StringContent(beautifulRequest.RoomType), "room_type");
                }
                
                if (!string.IsNullOrEmpty(beautifulRequest.HouseAngle))
                    content.Add(new StringContent(beautifulRequest.HouseAngle), "house_angle");
                
                if (!string.IsNullOrEmpty(beautifulRequest.GardenType))
                    content.Add(new StringContent(beautifulRequest.GardenType), "garden_type");
                
                if (!string.IsNullOrEmpty(beautifulRequest.Prompt))
                    content.Add(new StringContent(beautifulRequest.Prompt), "prompt");

                _logger.LogInformation("📤 Sending request to HomeDesigns.ai Beautiful Redesign API...");
                _logger.LogInformation("📋 Request parameters:");
                _logger.LogInformation("   • design_type: {DesignType}", beautifulRequest.DesignType);
                _logger.LogInformation("   • ai_intervention: {AIIntervention}", beautifulRequest.AIIntervention);
                _logger.LogInformation("   • no_design: {NoDesign}", beautifulRequest.NoDesign);
                _logger.LogInformation("   • design_style: {DesignStyle}", beautifulRequest.DesignStyle);
                _logger.LogInformation("   • room_type: {RoomType}", beautifulRequest.RoomType ?? "null");
                _logger.LogInformation("   • house_angle: {HouseAngle}", beautifulRequest.HouseAngle ?? "null");
                _logger.LogInformation("   • garden_type: {GardenType}", beautifulRequest.GardenType ?? "null");
                _logger.LogInformation("   • prompt: {Prompt}", beautifulRequest.Prompt ?? "null");
                _logger.LogInformation("   • keep_structural_element: {KeepStructural}", beautifulRequest.KeepStructural.ToString().ToLower());

                var apiResponse = await httpClient.PostAsync("https://homedesigns.ai/api/v2/beautiful_redesign", content);
                var responseContent = await apiResponse.Content.ReadAsStringAsync();

                _logger.LogInformation("📨 API Response Status: {StatusCode}", apiResponse.StatusCode);
                _logger.LogInformation("📨 API Response Content: {Content}", responseContent);

                if (!apiResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ HomeDesigns.ai API error: {StatusCode} - {Response}", apiResponse.StatusCode, responseContent);
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new BeautifulRedesignResponse
                    {
                        Success = false,
                        ErrorMessage = $"HomeDesigns.ai API error: {responseContent}"
                    }));
                    return errorResponse;
                }

                // Parse response - Beautiful Redesign returns results with nested success object
                var beautifulApiResponse = JsonSerializer.Deserialize<BeautifulRedesignApiResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (beautifulApiResponse == null || beautifulApiResponse.Success == null || 
                    beautifulApiResponse.Success.GeneratedImage == null || beautifulApiResponse.Success.GeneratedImage.Count == 0)
                {
                    _logger.LogError("❌ Invalid response from HomeDesigns.ai Beautiful Redesign API");
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    AddCorsHeaders(errorResponse, req);
                    await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new BeautifulRedesignResponse
                    {
                        Success = false,
                        ErrorMessage = "Invalid response from HomeDesigns.ai: No output images received"
                    }));
                    return errorResponse;
                }

                var inputImage = beautifulApiResponse.Success.OriginalImage;
                var outputImages = beautifulApiResponse.Success.GeneratedImage;

                _logger.LogInformation("✅ Beautiful Redesign complete! Generated {Count} designs", outputImages.Count);

                // Step 4: Download and save output images to Data Lake
                _logger.LogInformation("💾 Saving {Count} beautiful redesign images to Data Lake...", outputImages.Count);
                
                var savedImageUrls = new List<string>();
                var directoryPath = beautifulRequest.FilePath ?? "beautiful-redesigns";
                var baseFileName = Path.GetFileNameWithoutExtension(beautifulRequest.FileName);
                
                using var downloadClient = new HttpClient();
                
                for (int i = 0; i < outputImages.Count; i++)
                {
                    var outputUrl = outputImages[i];
                    try
                    {
                        _logger.LogInformation("📥 Downloading beautiful redesign image {Index}/{Total} from: {Url}", i + 1, outputImages.Count, outputUrl);
                        
                        var outputImageBytes = await downloadClient.GetByteArrayAsync(outputUrl);
                        _logger.LogInformation("✅ Downloaded {Size} bytes", outputImageBytes.Length);
                        
                        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                        var fileName = $"{baseFileName}_beautiful_{timestamp}_{i + 1}.png";
                        
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
                            
                            _logger.LogInformation("✅ Saved beautiful redesign image {Index}/{Total} to: {Path}", i + 1, outputImages.Count, fullPath);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to upload beautiful redesign image {Index}/{Total}", i + 1, outputImages.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error processing output image {Index}/{Total}", i + 1, outputImages.Count);
                    }
                }

                var processingTime = DateTime.UtcNow - startTime;

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var responseData = new BeautifulRedesignResponse
                {
                    Success = true,
                    TwinId = twinId,
                    InputImage = inputImage,
                    OutputImages = outputImages,
                    SavedImageUrls = savedImageUrls,
                    DesignType = beautifulRequest.DesignType,
                    DesignStyle = beautifulRequest.DesignStyle,
                    AIIntervention = beautifulRequest.AIIntervention,
                    NumberOfDesigns = outputImages.Count,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = $"Beautiful Redesign completado exitosamente con {outputImages.Count} variaciones. {savedImageUrls.Count} imágenes guardadas en Data Lake",
                    Timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

                _logger.LogInformation("✅ Beautiful Redesign completed successfully in {Time} seconds", processingTime.TotalSeconds);
                _logger.LogInformation("   • Total designs: {Count}", outputImages.Count);
                _logger.LogInformation("   • Saved to Data Lake: {SavedCount}", savedImageUrls.Count);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "❌ Error processing beautiful redesign after {ProcessingTime:F2} seconds", processingTime.TotalSeconds);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                AddCorsHeaders(errorResponse, req);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new BeautifulRedesignResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = Math.Round(processingTime.TotalSeconds, 2),
                    Message = "Error al procesar el beautiful redesign"
                }));

                return errorResponse;
            }
        }

        #endregion

        #region Decor Design - HomeDesigns.ai Integration

        /// <summary>
        /// OPTIONS handler for DecorDesign endpoint (CORS preflight)
        /// </summary>
        [Function("DecorDesignOptions")]
        public async Task<HttpResponseData> HandleDecorDesignOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "decor-design/{twinId}")] HttpRequestData req,
            string twinId)
        {
            _logger.LogInformation("🔧 OPTIONS preflight request for decor-design/{TwinId}", twinId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        

        #endregion

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

        #endregion
    }

    #region Request/Response Models

    /// <summary>
    /// Request model for image generation with all options
    /// </summary>
    public class GenerateImageRequest
    {
        public string Prompt { get; set; } = string.Empty;
        public string? Quality { get; set; } // "standard" or "hd"
        public string? Size { get; set; } // "1024x1024", "1024x1792", "1792x1024"
        public string? Style { get; set; } // "vivid" or "natural"
        public string? FileName { get; set; }
        public string? DirectoryPath { get; set; }
    }

    /// <summary>
    /// Simple request model with only prompt
    /// </summary>
    public class SimpleImageRequest
    {
        public string Prompt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request model for house redesign from Data Lake
    /// </summary>
    public class HouseRedesignRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DesignType { get; set; } = "Interior";
        public string AIIntervention { get; set; } = "Mid";
        public int NoDesign { get; set; } = 1;
        public string DesignStyle { get; set; } = "Modern";
        public string? RoomType { get; set; }
        public string? HouseAngle { get; set; }
        public string? GardenType { get; set; }
        public string? CustomInstruction { get; set; }
        public bool KeepStructural { get; set; } = true;
    }

    /// <summary>
    /// Request model for decor staging from Data Lake
    /// </summary>
    public class DecorDesignRequest
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
        public string? CustomInstruction { get; set; }
    }

    /// <summary>
    /// Response model for image generation
    /// </summary>
    public class GenerateImageResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string OriginalPrompt { get; set; } = string.Empty;
        public string RevisedPrompt { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string SasUrl { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string Quality { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public string Style { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    #endregion

    #region House Redesign Models

    /// <summary>
    /// Response from HomeDesigns.ai queue endpoint
    /// </summary>
    public class HomeDesignsQueueResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response from HomeDesigns.ai status check endpoint
    /// </summary>
    public class HomeDesignsStatusResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string? Status { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("started_at")]
        public string? StartedAt { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("input_image")]
        public string? InputImage { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("output_images")]
        public List<string>? OutputImages { get; set; }
    }

    /// <summary>
    /// Direct response from HomeDesigns.ai API (Beautiful Redesign/Decor Design with nested success object)
    /// </summary>
    public class HomeDesignsDirectResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("input_image")]
        public string? InputImage { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("output_images")]
        public List<string>? OutputImages { get; set; }
    }

    /// <summary>
    /// Beautiful Redesign API response (with nested success object)
    /// </summary>
    public class BeautifulRedesignApiResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("success")]
        public BeautifulRedesignSuccessData? Success { get; set; }
    }

    /// <summary>
    /// Success data within Beautiful Redesign API response
    /// </summary>
    public class BeautifulRedesignSuccessData
    {
        [System.Text.Json.Serialization.JsonPropertyName("original_image")]
        public string? OriginalImage { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("generated_image")]
        public List<string>? GeneratedImage { get; set; }
    }

    /// <summary>
    /// Response model for house redesign
    /// </summary>
    public class HouseRedesignResponse
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
        public string? CreatedAt { get; set; }
        public string? StartedAt { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Response model for beautiful redesign
    /// </summary>
    public class BeautifulRedesignResponse
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
        public string AIIntervention { get; set; } = string.Empty;
        public int NumberOfDesigns { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Request model for beautiful redesign from Data Lake
    /// </summary>
    public class BeautifulRedesignRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DesignType { get; set; } = "Interior";
        public string AIIntervention { get; set; } = "Mid";
        public int NoDesign { get; set; } = 1;
        public string DesignStyle { get; set; } = "Modern";
        public string? RoomType { get; set; }
        public string? HouseAngle { get; set; }
        public string? GardenType { get; set; }
        public string? Prompt { get; set; }
        public bool KeepStructural { get; set; } = true;
    }

    #endregion
}

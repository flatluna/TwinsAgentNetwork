using Azure;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// Agent for transforming and editing images using Azure OpenAI Image API
    /// Supports image generation, editing, and enhancement operations
    /// </summary>
    public class AgentTwinImageDesigner
    {
        private readonly ILogger<AgentTwinImageDesigner> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly string _endpoint;
        private readonly string _deploymentName;
        private readonly string _apiVersion;
        private readonly DefaultAzureCredential _credential;

        public AgentTwinImageDesigner(ILogger<AgentTwinImageDesigner> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = new HttpClient();
            _credential = new DefaultAzureCredential();

            // Get configuration from environment or config
            _endpoint = configuration["Values:AZURE_OPENAI_IMAGE_ENDPOINT"] ??
                       configuration["AZURE_OPENAI_IMAGE_ENDPOINT"] ??
                       Environment.GetEnvironmentVariable("AZURE_OPENAI_IMAGE_ENDPOINT") ??
                       "https://jorgeluna-8105-resource.cognitiveservices.azure.com/";

            _deploymentName = configuration["Values:AZURE_OPENAI_IMAGE_DEPLOYMENT"] ??
                             configuration["AZURE_OPENAI_IMAGE_DEPLOYMENT"] ??
                             Environment.GetEnvironmentVariable("AZURE_OPENAI_IMAGE_DEPLOYMENT") ??
                             "gpt-image-1";

            _apiVersion = configuration["Values:OPENAI_API_VERSION"] ??
                         configuration["OPENAI_API_VERSION"] ??
                         Environment.GetEnvironmentVariable("OPENAI_API_VERSION") ??
                         "2025-04-01-preview";

            _logger.LogInformation("🎨 Initializing AgentTwinImageDesigner with:");
            _logger.LogInformation("   • Endpoint: {Endpoint}", _endpoint);
            _logger.LogInformation("   • Deployment: {DeploymentName}", _deploymentName);
            _logger.LogInformation("   • API Version: {ApiVersion}", _apiVersion);
        }

        /// <summary>
        /// Generates a new image from a text prompt
        /// </summary>
        /// <param name="prompt">Text description of the image to generate</param>
        /// <param name="size">Image size (e.g., "1536x1024", "1024x1024")</param>
        /// <param name="quality">Image quality ("standard" or "hd")</param>
        /// <param name="numberOfImages">Number of images to generate (1-10)</param>
        /// <returns>ImageDesignerGenerationResult with generated images as base64 strings</returns>
        public async Task<ImageDesignerGenerationResult> GenerateImageAsync(
            string prompt,
            string size = "1536x1024",
            string quality = "high",
            int numberOfImages = 1)
        {
            var startTime = DateTime.UtcNow;
            
            try
            {
                if (string.IsNullOrEmpty(prompt))
                {
                    throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));
                }

                _logger.LogInformation("🎨 Generating image with prompt: {Prompt}", prompt);

                // Get authentication token
                var tokenResponse = await _credential.GetTokenAsync(
                    new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }));

                var basePath = $"openai/deployments/{_deploymentName}/images";
                var urlParams = $"?api-version={_apiVersion}";
                var generationUrl = $"{_endpoint}{basePath}/generations{urlParams}";

                var requestBody = new
                {
                    prompt = prompt,
                    n = numberOfImages,
                    size = size,
                    quality = quality,
                    response_format = "b64_json"
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, generationUrl);
                request.Headers.Add("Authorization", $"Bearer {tokenResponse.Token}");
                
                var json = JsonConvert.SerializeObject(requestBody);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var resultJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ Image generation failed: {StatusCode} - {Response}", response.StatusCode, resultJson);
                    return new ImageDesignerGenerationResult
                    {
                        Success = false,
                        ErrorMessage = $"Image generation failed: {response.StatusCode}",
                        ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds
                    };
                }

                var images = ExtractImagesFromResponse(resultJson);
                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;

                _logger.LogInformation("✅ Generated {Count} images successfully in {Time:F2} seconds", images.Count, processingTime);

                return new ImageDesignerGenerationResult
                {
                    Success = true,
                    Images = images,
                    Prompt = prompt,
                    Size = size,
                    Quality = quality,
                    ProcessingTimeSeconds = processingTime,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error generating image");
                return new ImageDesignerGenerationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds
                };
            }
        }

        /// <summary>
        /// Edits an existing image based on a text prompt
        /// NOTE: Azure OpenAI Image Edit API requires:
        /// - Image must be PNG format with transparency (alpha channel)
        /// - Image must be square (same width and height)
        /// - Supported sizes: 256x256, 512x512, 1024x1024
        /// - Maximum file size: 4MB
        /// </summary>
        /// <param name="imageBytes">Original image as byte array (must be PNG with alpha channel)</param>
        /// <param name="prompt">Text description of the desired changes</param>
        /// <param name="maskBytes">Optional mask image to specify which areas to edit (must be PNG with alpha channel)</param>
        /// <param name="size">Output image size (must be square: "256x256", "512x512", "1024x1024")</param>
        /// <param name="quality">Image quality ("standard", "medium", or "high")</param>
        /// <param name="numberOfImages">Number of variations to generate (1-10)</param>
        /// <returns>ImageDesignerEditResult with edited images</returns>
        public async Task<ImageDesignerEditResult> EditImageAsync(
            byte[] imageBytes,
            string prompt,
            byte[]? maskBytes = null,
            string size = "1536x1024",
            string quality = "high",
            int numberOfImages = 1)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                quality = "medium";
                size = "1536x1024";
                prompt = prompt + " Asegurate de no cambiar la estructura principal de la imagen no " +
                    " cambies paredes, ni ventanas nada solo adiciona muebles tal ocmo se te pidio " +
                    " conserva las mismas paredes, escaleras, ventanas, corredores, murales, etc).";
                if (imageBytes == null || imageBytes.Length == 0)
                {
                    throw new ArgumentException("Image bytes cannot be null or empty", nameof(imageBytes));
                }

                if (string.IsNullOrEmpty(prompt))
                {
                    throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));
                }

                // Validate size parameter - must be square for edit endpoint
                var validSizes = new[] { "256x256", "512x512", "1024x1024" };
                if (!validSizes.Contains(size))
                {
                    _logger.LogWarning("⚠️ Invalid size '{Size}' for edit operation. Must be square. Using 1024x1024", size);
                    size = "1024x1024";
                }

                _logger.LogInformation("✏️ Editing image with prompt: {Prompt}", prompt);
                _logger.LogInformation("   • Size: {Size} (square required for edit API)", size);
                _logger.LogInformation("   • Quality: {Quality}", quality);
                _logger.LogInformation("   • Image size: {ImageSize} bytes", imageBytes.Length);

                size = "1536x1024";
                // Get authentication token
                var tokenResponse = await _credential.GetTokenAsync(
                    new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }));

                var basePath = $"openai/deployments/{_deploymentName}/images";
                var urlParams = $"?api-version={_apiVersion}";
                var editUrl = $"{_endpoint}{basePath}/edits{urlParams}";

                _logger.LogInformation("📤 Sending request to: {Url}", editUrl);

                using var form = new MultipartFormDataContent();
                using var editRequest = new HttpRequestMessage(HttpMethod.Post, editUrl);
                
                editRequest.Headers.Add("Authorization", $"Bearer {tokenResponse.Token}");

                // Add form fields in the same order as the working example
                form.Add(new StringContent(prompt), "prompt");
                form.Add(new StringContent(numberOfImages.ToString()), "n");
                form.Add(new StringContent(size), "size");
                form.Add(new StringContent(quality), "quality");

                // Add image (required parameter, must be PNG with alpha channel)
                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(imageContent, "image", "image.png");

                // Add mask if provided (must be PNG with alpha channel)
                if (maskBytes != null && maskBytes.Length > 0)
                {
                    var maskContent = new ByteArrayContent(maskBytes);
                    maskContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    form.Add(maskContent, "mask", "mask.png");
                    _logger.LogInformation("📍 Mask provided for targeted editing ({Size} bytes)", maskBytes.Length);
                }

                _logger.LogInformation("📋 Request parameters:");
                _logger.LogInformation("   • prompt: {Prompt}", prompt);
                _logger.LogInformation("   • n: {N}", numberOfImages);
                _logger.LogInformation("   • size: {Size}", size);
                _logger.LogInformation("   • quality: {Quality}", quality);
                _logger.LogInformation("   • mask: {MaskProvided}", maskBytes != null ? "yes" : "no");

                editRequest.Content = form;

                var response = await _httpClient.SendAsync(editRequest);
                var resultJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ Image editing failed: {StatusCode} - {Response}", response.StatusCode, resultJson);
                    
                    // Try to parse error details
                    string errorDetail = resultJson;
                    try
                    {
                        var errorJson = JObject.Parse(resultJson);
                        var errorMsg = errorJson["error"]?["message"]?.ToString();
                        if (!string.IsNullOrEmpty(errorMsg))
                        {
                            errorDetail = errorMsg;
                        }
                    }
                    catch { /* Ignore parsing errors */ }

                    return new ImageDesignerEditResult
                    {
                        Success = false,
                        ErrorMessage = $"Image editing failed ({response.StatusCode}): {errorDetail}",
                        ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds
                    };
                }

                var images = ExtractImagesFromResponse(resultJson);
                var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;

                _logger.LogInformation("✅ Edited image successfully, generated {Count} variations in {Time:F2} seconds", images.Count, processingTime);

                return new ImageDesignerEditResult
                {
                    Success = true,
                    EditedImages = images,
                    Prompt = prompt,
                    Size = size,
                    Quality = quality,
                    MaskUsed = maskBytes != null,
                    ProcessingTimeSeconds = processingTime,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error editing image");
                return new ImageDesignerEditResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeSeconds = (DateTime.UtcNow - startTime).TotalSeconds
                };
            }
        }

        /// <summary>
        /// Edits an image from a file path
        /// NOTE: Image must be PNG with transparency and square dimensions
        /// </summary>
        /// <param name="imagePath">Path to the image file</param>
        /// <param name="prompt">Text description of the desired changes</param>
        /// <param name="maskPath">Optional path to mask image</param>
        /// <param name="size">Output image size (must be square: "256x256", "512x512", "1024x1024")</param>
        /// <param name="quality">Image quality ("standard", "medium", or "high")</param>
        /// <param name="numberOfImages">Number of variations to generate</param>
        /// <returns>ImageDesignerEditResult with edited images</returns>
        public async Task<ImageDesignerEditResult> EditImageFromFileAsync(
            string imagePath,
            string prompt,
            string? maskPath = null,
            string size = "1024x1024",
            string quality = "medium",
            int numberOfImages = 1)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    throw new FileNotFoundException($"Image file not found: {imagePath}");
                }

                var imageBytes = await File.ReadAllBytesAsync(imagePath);
                byte[]? maskBytes = null;

                if (!string.IsNullOrEmpty(maskPath))
                {
                    if (!File.Exists(maskPath))
                    {
                        _logger.LogWarning("⚠️ Mask file not found: {MaskPath}, proceeding without mask", maskPath);
                    }
                    else
                    {
                        maskBytes = await File.ReadAllBytesAsync(maskPath);
                    }
                }

                return await EditImageAsync(imageBytes, prompt, maskBytes, size, quality, numberOfImages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error editing image from file");
                return new ImageDesignerEditResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Saves generated/edited images to disk
        /// </summary>
        /// <param name="images">List of base64 encoded images</param>
        /// <param name="outputDirectory">Directory to save images</param>
        /// <param name="filePrefix">Prefix for filenames</param>
        /// <returns>List of saved file paths</returns>
        public async Task<List<string>> SaveImagesToDiskAsync(
            List<string> images,
            string outputDirectory,
            string filePrefix = "image")
        {
            var savedPaths = new List<string>();

            try
            {
                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                    _logger.LogInformation("📁 Created output directory: {Directory}", outputDirectory);
                }

                for (int i = 0; i < images.Count; i++)
                {
                    var imageBytes = Convert.FromBase64String(images[i]);
                    var fileName = $"{filePrefix}_{i + 1}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
                    var filePath = Path.Combine(outputDirectory, fileName);

                    await File.WriteAllBytesAsync(filePath, imageBytes);
                    savedPaths.Add(filePath);

                    _logger.LogInformation("💾 Saved image to: {FilePath}", filePath);
                }

                _logger.LogInformation("✅ Saved {Count} images successfully", images.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving images to disk");
            }

            return savedPaths;
        }

        /// <summary>
        /// Extracts base64 encoded images from API response JSON
        /// </summary>
        private List<string> ExtractImagesFromResponse(string responseJson)
        {
            var images = new List<string>();

            try
            {
                var json = JObject.Parse(responseJson);
                var data = json["data"] as JArray;

                if (data != null)
                {
                    foreach (var item in data)
                    {
                        var b64 = item["b64_json"]?.ToString();
                        if (!string.IsNullOrEmpty(b64))
                        {
                            images.Add(b64);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error extracting images from response");
            }

            return images;
        }
    }

    #region Result Models

    /// <summary>
    /// Result of image generation operation using Azure OpenAI Image Designer
    /// </summary>
    public class ImageDesignerGenerationResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> Images { get; set; } = new();
        public string Prompt { get; set; } = "";
        public string Size { get; set; } = "";
        public string Quality { get; set; } = "";
        public double ProcessingTimeSeconds { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of image editing operation using Azure OpenAI Image Designer
    /// </summary>
    public class ImageDesignerEditResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> EditedImages { get; set; } = new();
        public string Prompt { get; set; } = "";
        public string Size { get; set; } = "";
        public string Quality { get; set; } = "";
        public bool MaskUsed { get; set; }
        public double ProcessingTimeSeconds { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}

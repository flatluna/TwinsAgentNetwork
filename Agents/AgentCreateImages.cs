using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Images;
using System;
using System.ClientModel;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using TwinAgentsLibrary.Services;
using TwinAgentsLibrary.Models;
using Microsoft.Extensions.Options;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// Agent for generating images using Azure OpenAI DALL-E and saving them to Azure Data Lake
    /// </summary>
    public class AgentCreateImages
    {
        private readonly ILogger<AgentCreateImages> _logger;
        private readonly IConfiguration _configuration;
        private readonly AzureOpenAIClient _azureClient;
        private readonly ImageClient _imageClient;
        private readonly string _deploymentName;
        private readonly HttpClient _httpClient;
        private readonly ILoggerFactory _loggerFactory;

        public AgentCreateImages(ILogger<AgentCreateImages> logger, IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _loggerFactory = loggerFactory;
            _httpClient = new HttpClient();

            try
            {
                // Get Azure OpenAI configuration
                var endpoint = configuration["Values:AzureOpenAI:Endpoint"] ??
                              configuration["AzureOpenAI:Endpoint"] ??
                              Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ??
                              throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");

                var apiKey = configuration["Values:AzureOpenAI:ApiKey"] ??
                            configuration["AzureOpenAI:ApiKey"] ??
                            Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ??
                            throw new InvalidOperationException("Azure OpenAI API key is required");

                _deploymentName = configuration["Values:AzureOpenAI:ImageDeploymentName"] ??
                                 configuration["AzureOpenAI:ImageDeploymentName"] ??
                                 Environment.GetEnvironmentVariable("DEPLOYMENT_NAME") ??
                                 "dall-e-3";

                _logger.LogInformation("?? Initializing AgentCreateImages with:");
                _logger.LogInformation("   • Endpoint: {Endpoint}", endpoint);
                _logger.LogInformation("   • Deployment: {DeploymentName}", _deploymentName);

                _azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
                _imageClient = _azureClient.GetImageClient(_deploymentName);

                _logger.LogInformation("? AgentCreateImages initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Failed to initialize AgentCreateImages");
                throw;
            }
        }

        /// <summary>
        /// Generates an image from a text prompt using Azure OpenAI DALL-E
        /// </summary>
        /// <param name="prompt">The text description of the image to generate</param>
        /// <param name="quality">Image quality (Standard or HD). Default is Standard.</param>
        /// <param name="size">Image size (1024x1024, 1024x1792, or 1792x1024). Default is 1024x1024.</param>
        /// <param name="style">Image style (Vivid or Natural). Default is Vivid.</param>
        /// <returns>Result containing the generated image URL and details</returns>
        public async Task<ImageGenerationResult> GenerateImageAsync(
            string prompt,
            GeneratedImageQuality? quality = null,
            GeneratedImageSize? size = null,
            GeneratedImageStyle? style = null)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));
                }

                // Set defaults if not provided
                var imageQuality = quality ?? GeneratedImageQuality.Standard;
                var imageSize = size ?? GeneratedImageSize.W1024xH1024;
                var imageStyle = style ?? GeneratedImageStyle.Vivid;

                _logger.LogInformation("?? Generating image with prompt: {Prompt}", prompt);
                _logger.LogInformation("   • Quality: {Quality}", imageQuality);
                _logger.LogInformation("   • Size: {Size}", imageSize);
                _logger.LogInformation("   • Style: {Style}", imageStyle);

                ClientResult<GeneratedImage> imageResult = await _imageClient.GenerateImageAsync(prompt, new()
                {
                    Quality = imageQuality,
                    Size = imageSize,
                    Style = imageStyle,
                    ResponseFormat = GeneratedImageFormat.Uri
                });

                GeneratedImage image = imageResult.Value;
                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                _logger.LogInformation("? Image generated successfully in {ProcessingTime}ms", processingTime);
                _logger.LogInformation("   • Image URL: {ImageUrl}", image.ImageUri);
                _logger.LogInformation("   • Revised Prompt: {RevisedPrompt}", image.RevisedPrompt);

                return new ImageGenerationResult
                {
                    Success = true,
                    ImageUrl = image.ImageUri?.ToString() ?? string.Empty,
                    RevisedPrompt = image.RevisedPrompt ?? prompt,
                    OriginalPrompt = prompt,
                    Quality = imageQuality.ToString(),
                    Size = imageSize.ToString(),
                    Style = imageStyle.ToString(),
                    ProcessingTimeMs = processingTime,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogError(ex, "? Error generating image after {ProcessingTime}ms", processingTime);

                return new ImageGenerationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    OriginalPrompt = prompt,
                    ProcessingTimeMs = processingTime,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Generates an image and saves it to Azure Data Lake Storage
        /// </summary>
        /// <param name="prompt">The text description of the image to generate</param>
        /// <param name="twinId">TwinID (used as container name in Data Lake)</param>
        /// <param name="fileName">File name for the saved image (without extension)</param>
        /// <param name="directoryPath">Optional directory path within the container (default: "generated-images")</param>
        /// <param name="quality">Image quality (Standard or HD)</param>
        /// <param name="size">Image size</param>
        /// <param name="style">Image style</param>
        /// <returns>Result containing the image URL, Data Lake path, and SAS URL</returns>
        public async Task<ImageGenerationWithStorageResult> GenerateAndSaveImageAsync(
            string prompt,
            string twinId,
            string fileName,
            string? directoryPath = null,
            GeneratedImageQuality? quality = null,
            GeneratedImageSize? size = null,
            GeneratedImageStyle? style = null)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrWhiteSpace(twinId))
                {
                    throw new ArgumentException("TwinID cannot be null or empty", nameof(twinId));
                }

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    throw new ArgumentException("File name cannot be null or empty", nameof(fileName));
                }

                _logger.LogInformation("?? Generating and saving image to Data Lake");
                _logger.LogInformation("   • TwinID: {TwinId}", twinId);
                _logger.LogInformation("   • File Name: {FileName}", fileName);
                _logger.LogInformation("   • Directory: {Directory}", directoryPath ?? "generated-images");

                // Step 1: Generate the image
                var generationResult = await GenerateImageAsync(prompt, quality, size, style);

                if (!generationResult.Success)
                {
                    return new ImageGenerationWithStorageResult
                    {
                        Success = false,
                        ErrorMessage = $"Image generation failed: {generationResult.ErrorMessage}",
                        GenerationResult = generationResult,
                        ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                    };
                }

                _logger.LogInformation("? Image generated, now downloading from URL: {Url}", generationResult.ImageUrl);

                // Step 2: Download the image from the URL
                byte[] imageBytes;
                try
                {
                    imageBytes = await _httpClient.GetByteArrayAsync(generationResult.ImageUrl);
                    _logger.LogInformation("? Image downloaded successfully. Size: {Size} bytes", imageBytes.Length);
                }
                catch (Exception downloadEx)
                {
                    _logger.LogError(downloadEx, "? Failed to download image from URL");
                    return new ImageGenerationWithStorageResult
                    {
                        Success = false,
                        ErrorMessage = $"Failed to download generated image: {downloadEx.Message}",
                        GenerationResult = generationResult,
                        ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                    };
                }

                // Step 3: Prepare for Data Lake upload
                var containerName = twinId.ToLowerInvariant(); // Container name must be lowercase
                var directory = directoryPath ?? "generated-images";
                var fullFileName = fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) 
                    ? fileName 
                    : $"{fileName}.png";

                _logger.LogInformation("?? Uploading image to Data Lake:");
                _logger.LogInformation("   • Container (FileSystem): {Container}", containerName);
                _logger.LogInformation("   • Directory: {Directory}", directory);
                _logger.LogInformation("   • File: {File}", fullFileName);

                // Step 4: Create DataLake client and upload to Data Lake
                var storageSettings = new AzureStorageSettings
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

                using var imageStream = new MemoryStream(imageBytes);
                var uploadSuccess = await dataLakeClient.UploadFileAsync(
                    containerName,      // fileSystemName (container)
                    directory,          // directoryName
                    fullFileName,       // fileName
                    imageStream,        // fileData as Stream
                    "image/png"         // mimeType
                );

                if (!uploadSuccess)
                {
                    _logger.LogError("? Failed to upload image to Data Lake");
                    return new ImageGenerationWithStorageResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to upload image to Data Lake storage",
                        GenerationResult = generationResult,
                        ImageBytes = imageBytes,
                        ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                    };
                }

                // Step 5: Generate SAS URL for access (24 hour expiry)
                var fullPath = $"{directory}/{fullFileName}";
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullPath, TimeSpan.FromHours(24));

                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                _logger.LogInformation("? Image saved to Data Lake successfully in {ProcessingTime}ms", processingTime);
                _logger.LogInformation("   • Full Path: {Path}", fullPath);
                _logger.LogInformation("   • SAS URL generated: {HasSasUrl}", !string.IsNullOrEmpty(sasUrl));

                return new ImageGenerationWithStorageResult
                {
                    Success = true,
                    GenerationResult = generationResult,
                    ImageBytes = imageBytes,
                    ContainerName = containerName,
                    DirectoryPath = directory,
                    FileName = fullFileName,
                    FullPath = fullPath,
                    SasUrl = sasUrl,
                    FileSizeBytes = imageBytes.Length,
                    ProcessingTimeMs = processingTime,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogError(ex, "? Error generating and saving image after {ProcessingTime}ms", processingTime);

                return new ImageGenerationWithStorageResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeMs = processingTime,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Generates an image with simplified parameters (uses default quality, size, and style)
        /// </summary>
        /// <param name="prompt">The text description of the image to generate</param>
        /// <returns>Result containing the generated image URL and details</returns>
        public async Task<ImageGenerationResult> GenerateImageSimpleAsync(string prompt)
        {
            return await GenerateImageAsync(prompt);
        }

        /// <summary>
        /// Generates a high-quality image with default settings
        /// </summary>
        /// <param name="prompt">The text description of the image to generate</param>
        /// <returns>Result containing the generated image URL and details</returns>
        public async Task<ImageGenerationResult> GenerateHDImageAsync(string prompt)
        {
            return await GenerateImageAsync(
                prompt,
                GeneratedImageQuality.High,
                GeneratedImageSize.W1024xH1024,
                GeneratedImageStyle.Vivid
            );
        }

        /// <summary>
        /// Disposes resources
        /// </summary>
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Result of image generation operation
    /// </summary>
    public class ImageGenerationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string RevisedPrompt { get; set; } = string.Empty;
        public string OriginalPrompt { get; set; } = string.Empty;
        public string Quality { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public string Style { get; set; } = string.Empty;
        public double ProcessingTimeMs { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Result of image generation with Data Lake storage operation
    /// </summary>
    public class ImageGenerationWithStorageResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public ImageGenerationResult? GenerationResult { get; set; }
        public byte[]? ImageBytes { get; set; }
        public string ContainerName { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string SasUrl { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public double ProcessingTimeMs { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using TwinAgentsNetwork.Models;
using TwinAgentsLibrary.Models;

namespace TwinAgentsNetwork.Services
{
    public class TwinDataPersonalServices
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public TwinDataPersonalServices(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _baseUrl = Environment.GetEnvironmentVariable("TWIN_API_BASE_URL") ?? "http://localhost:7011/api";
        }

        public TwinDataPersonalServices() : this(new HttpClient())
        {
        }

        /// <summary>
        /// Gets twin profile data by ID from the Azure Function
        /// </summary>
        /// <param name="twinId">The unique identifier of the twin</param>
        /// <returns>TwinProfileData object containing the twin's profile data</returns>
        public async Task<TwinProfileData?> GetTwinProfileByIdAsync(string twinId)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("Twin ID cannot be null or empty", nameof(twinId));
            }

            try
            {
                var url = $"{_baseUrl}/twin-profiles/id/{twinId}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var jsonContent = await response.Content.ReadAsStringAsync();
                    
                    // Deserialize the Azure Function response which has structure: { success, profile, twinId }
                    var apiResponse = JsonSerializer.Deserialize<AzureFunctionResponse>(jsonContent, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true
                    });

                    if (apiResponse?.Success == true)
                    {
                        return apiResponse.Profile;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Azure Function returned success=false for twin ID: {twinId}");
                    }
                }
                else
                {
                    // Log the error or handle specific status codes
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"Failed to get twin profile. Status: {response.StatusCode}, Content: {errorContent}");
                }
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Failed to deserialize twin profile data", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected error while getting twin profile for ID: {twinId}", ex);
            }
        }

        /// <summary>
        /// Gets twin profile data by ID and returns a formatted response
        /// </summary>
        /// <param name="twinId">The unique identifier of the twin</param>
        /// <returns>Formatted response object</returns>
        public async Task<TwinProfileResponse> GetTwinProfileResponseAsync(string twinId)
        {
            try
            {
                var twinProfileData = await GetTwinProfileByIdAsync(twinId);
                
                return new TwinProfileResponse
                {
                    Success = true,
                    TwinProfileData = twinProfileData,
                    ErrorMessage = null
                };
            }
            catch (Exception ex)
            {
                return new TwinProfileResponse
                {
                    Success = false,
                    TwinProfileData = null,
                    ErrorMessage = ex.Message
                };
            }
        }
    }

    /// <summary>
    /// Response model that matches the Azure Function's response structure
    /// </summary>
    public class AzureFunctionResponse
    {
        public bool Success { get; set; }
        public TwinProfileData? Profile { get; set; }
        public string? TwinId { get; set; }
    }

    /// <summary>
    /// Response wrapper for twin profile operations
    /// </summary>
    public class TwinProfileResponse
    {
        public bool Success { get; set; }
        public TwinProfileData? TwinProfileData { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
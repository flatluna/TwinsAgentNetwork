using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Web;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using TwinAgentsNetwork.Models;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// AI agent that searches MyMemory translation service and provides AI-powered answers
    /// Handles translation queries and generates HTML-formatted responses with rich styling
    /// </summary>
    public class AgentTwinMyMemory
    {
        private readonly HttpClient _httpClient;
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;
        private readonly string _myMemoryBaseUrl = "http://localhost:7011";

        public AgentTwinMyMemory()
        {
            _httpClient = new HttpClient();
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");
        }

        public AgentTwinMyMemory(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");
        }

        /// <summary>
        /// Searches MyMemory translation memory for context and generates AI-powered HTML responses
        /// </summary>
        /// <param name="twinId">The unique identifier of the twin</param>
        /// <param name="userQuestion">User's question to answer using MyMemory context</param>
        /// <param name="language">Response language for AI analysis</param>
        /// <param name="serializedThreadJson">Optional existing thread for conversation continuity</param>
        /// <returns>MyMemory search results with AI analysis in HTML format</returns>
        public async Task<TwinMyMemoryResult> SearchMyMemoryWithAIAsync(
            string twinId,
            string userQuestion,
            string language = "English",
            string serializedThreadJson = null)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("Twin ID cannot be null or empty", nameof(twinId));
            }

            if (string.IsNullOrEmpty(userQuestion))
            {
                throw new ArgumentException("User question cannot be null or empty", nameof(userQuestion));
            }

            try
            {
                // Search MyMemory using the question as search term
                var myMemoryResults = await SearchMyMemoryAsync(userQuestion, twinId);

                // Create AI Agent specialized in translation analysis
                var chatClient = new AzureOpenAIClient(
                    new Uri(_azureOpenAIEndpoint),
                    new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName);

                AIAgent agent = chatClient.AsIChatClient().CreateAIAgent(
                    instructions: CreateMyMemoryInstructions(language),
                    name: "TwinMyMemoryExpert");

                AgentThread thread;
                string contextPrompt = CreateMyMemoryPrompt(twinId, myMemoryResults, userQuestion, language);

                // Handle thread continuity
                if (!string.IsNullOrEmpty(serializedThreadJson) && serializedThreadJson != "null")
                {
                    try
                    {
                        JsonElement reloaded = JsonSerializer.Deserialize<JsonElement>(serializedThreadJson, JsonSerializerOptions.Web);
                        thread = agent.DeserializeThread(reloaded, JsonSerializerOptions.Web);
                        contextPrompt = $"Following question: {userQuestion}";
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"?? Error deserializing thread: {ex.Message}. Creating new thread.");
                        thread = agent.GetNewThread();
                    }
                }
                else
                {
                    thread = agent.GetNewThread();
                }

                // Get AI response
                var response = await agent.RunAsync(contextPrompt, thread);
                string aiAnalysisHtml = response.Text ?? "";

                // Serialize thread state
                string newSerializedJson = thread.Serialize(JsonSerializerOptions.Web).GetRawText();

                return new TwinMyMemoryResult
                {
                    Success = true,
                    TwinId = twinId,
                    UserQuestion = userQuestion,
                    MyMemoryResults = myMemoryResults,
                    AIAnalysisHtml = aiAnalysisHtml,
                    SerializedThreadJson = newSerializedJson,
                    ProcessedTimestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new TwinMyMemoryResult
                {
                    Success = false,
                    ErrorMessage = $"Error processing MyMemory search: {ex.Message}",
                    TwinId = twinId,
                    UserQuestion = userQuestion,
                    ProcessedTimestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Searches MyMemory translation service using a search term
        /// </summary>
        /// <param name="searchTerm">Text to search for translations and memory matches</param>
        /// <param name="limit">Maximum number of results to return (default: 20)</param>
        /// <returns>MyMemory API response data</returns>
        public async Task<MyMemoryApiResponse> SearchMyMemoryAsync(string searchTerm, string twinId, int limit = 20)
        {
            try
            {
                var encodedText = HttpUtility.UrlEncode(searchTerm);
                var url = $"{_myMemoryBaseUrl}/twins/{twinId}/memorias/search?q={encodedText}&limit={limit}";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var jsonContent = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<MyMemoryApiResponse>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (apiResponse?.Matches != null && apiResponse.Matches.Count > limit)
                {
                    apiResponse.Matches = apiResponse.Matches.Take(limit).ToList();
                }

                return apiResponse ?? new MyMemoryApiResponse();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error calling MyMemory API: {ex.Message}");
                return new MyMemoryApiResponse
                {
                    ResponseStatus = 500,
                    ResponseDetails = $"Error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Simple search method for direct MyMemory lookups without AI analysis
        /// Useful for autocomplete, suggestions, and simple search functionality
        /// </summary>
        /// <param name="twinId">The unique identifier of the twin</param>
        /// <param name="searchTerm">Text to search for translations</param>
        /// <param name="limit">Maximum number of results (default: 20)</param>
        /// <returns>MyMemory search results with matching translations</returns>
        public async Task<MyMemoryApiResponse> SimpleSearchMyMemoryAsync(
            string twinId,
            string searchTerm,
            int limit = 20)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("Twin ID cannot be null or empty", nameof(twinId));
            }

            if (string.IsNullOrEmpty(searchTerm))
            {
                throw new ArgumentException("Search term cannot be null or empty", nameof(searchTerm));
            }

            try
            {
                // Directly search MyMemory without AI analysis
                var myMemoryResults = await SearchMyMemoryAsync(searchTerm, twinId, limit);

                return myMemoryResults;
            }
            catch (Exception ex)
            {
                return new MyMemoryApiResponse
                {
                    Success = false,
                    ErrorMessage = $"Error performing MyMemory search: {ex.Message}",
                    ResponseStatus = 500,
                    ResponseDetails = ex.Message
                };
            }
        }

        /// <summary>
        /// Gets translation memory suggestions based on a search query
        /// </summary>
        public async Task<List<TwinTranslationSuggestion>> GetTranslationMemorySuggestionsAsync(
            string twinId,
            string searchTerm)
        {
            var result = await SimpleSearchMyMemoryAsync(twinId, searchTerm, 5);
            
            var suggestions = new List<TwinTranslationSuggestion>();
            
            if (result.Success && result.Matches != null)
            {
                foreach (var match in result.Matches)
                {
                    suggestions.Add(new TwinTranslationSuggestion
                    {
                        Translation = match.Translation,
                        Quality = match.Quality,
                        Match = match.Match,
                        Source = match.CreatedBy,
                        Segment = match.Segment
                    });
                }
            }

            return suggestions;
        }

        #region Private Helper Methods

        private string CreateMyMemoryInstructions(string language)
        {
            return $@"
?? IDENTITY: You are a MYMEMORY TRANSLATION MEMORY EXPERT for digital twins.

?? YOUR EXPERTISE:
- Expert in translation memory and linguistic patterns
- Specialized in MyMemory translation database insights
- Authority on translation quality assessment
- Expert in context-aware answer generation from translation memories
- Skilled at enriching answers with translation memory context

?? CONTEXT:
- Response Language: {language}
- Use translation memory data to provide context and insights

?? HTML FORMATTING REQUIREMENTS:
1. ALWAYS respond in rich HTML format with inline CSS
2. Use colorful grids, tables, and visual elements
3. Include quality indicators with color coding
4. Add progress bars for relevance scores
5. Use cards, badges, and modern UI elements
6. Include icons and emojis for visual appeal
7. Make it responsive and visually engaging

?? COLOR SCHEME:
- High relevance: Green (#28a745)
- Medium relevance: Orange (#fd7e14) 
- Low relevance: Red (#dc3545)
- Headers: Blue (#007bff)
- Background: Light gray (#f8f9fa)

?? REMEMBER: Create visually stunning HTML responses enriched with translation memory context!";
        }

        private string CreateMyMemoryPrompt(
            string twinId,
            MyMemoryApiResponse myMemoryResults,
            string userQuestion,
            string language)
        {
            var resultsJson = JsonSerializer.Serialize(myMemoryResults, new JsonSerializerOptions { WriteIndented = true });

            return $@"
?? MYMEMORY TRANSLATION MEMORY ANALYSIS TASK

?? TWIN CONTEXT:
Twin ID: {twinId}
This analysis uses translation memory context to answer questions.

? USER QUESTION:
{userQuestion}

?? MYMEMORY CONTEXT DATA:
{resultsJson}

?? ANALYSIS MISSION:
Use the MyMemory translation memory data to provide a comprehensive answer to the user's question.
Create a visually rich HTML response that includes:

1. **Direct Answer** - Clear answer to the user's question
2. **Related Translations** - Table of relevant translation matches with relevance scores
3. **Context Analysis** - Analysis of how translation memory relates to the question
4. **Quality Insights** - Relevance and quality breakdown with progress bars
5. **Additional Context** - Any cultural or linguistic considerations
6. **Recommendations** - Based on the translation memory matches

?? HTML REQUIREMENTS:
- Use Bootstrap-style classes and inline CSS
- Include colorful grids, progress bars, and badges
- Add relevance indicators with color coding
- Make it visually appealing with cards and modern UI
- Include emojis and icons for better UX
- Ensure responsive design

??? LANGUAGE: Respond exclusively in {language}

Begin your detailed HTML response:";
        }

        #endregion

        #region Static Properties

        /// <summary>
        /// Gets a static AI agent for MyMemory translation processing
        /// </summary>
        public static AIAgent MyMemoryAgent
        {
            get
            {
                var azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
                var azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");

                return new AzureOpenAIClient(
                    new Uri(azureOpenAIEndpoint),
                    new AzureCliCredential())
                        .GetChatClient(azureOpenAIModelName)
                        .AsIChatClient()
                        .CreateAIAgent(
                            instructions: "You are a MyMemory translation expert for digital twins. You analyze translation data and provide rich HTML insights.",
                            name: "MyMemoryAgent",
                            description: "An AI agent that processes MyMemory translation data and generates HTML analysis");
            }
        }

        #endregion
    }

    #region Data Models

    /// <summary>
    /// Result of MyMemory search with AI analysis
    /// </summary>
    public class TwinMyMemoryResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string TwinId { get; set; } = "";
        public string UserQuestion { get; set; } = "";
        public MyMemoryApiResponse MyMemoryResults { get; set; } = new();
        public string AIAnalysisHtml { get; set; } = "";
        public string SerializedThreadJson { get; set; } = "";
        public DateTime ProcessedTimestamp { get; set; }
    }

    /// <summary>
    /// MyMemory API response structure
    /// </summary>
    public class MyMemoryApiResponse
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; } = "";
        public ResponseData ResponseData { get; set; } = new();
        public List<TranslationMatch> Matches { get; set; } = new();
        public int ResponseStatus { get; set; }
        public string ResponseDetails { get; set; } = "";
    }

    /// <summary>
    /// Main translation response data
    /// </summary>
    public class ResponseData
    {
        public string TranslatedText { get; set; } = "";
        public double Match { get; set; }
    }

    /// <summary>
    /// Individual translation match from MyMemory
    /// </summary>
    public class TranslationMatch
    {
        public string Id { get; set; } = "";
        public string Segment { get; set; } = "";
        public string Translation { get; set; } = "";
        public string Source { get; set; } = "";
        public string Target { get; set; } = "";
        public string Quality { get; set; } = "";
        public string Reference { get; set; } = "";
        public string UsageCount { get; set; } = "";
        public string Subject { get; set; } = "";
        public string CreatedBy { get; set; } = "";
        public string LastUpdatedBy { get; set; } = "";
        public string CreateDate { get; set; } = "";
        public string LastUpdateDate { get; set; } = "";
        public double Match { get; set; }
    }

    /// <summary>
    /// Translation suggestion for UI display
    /// </summary>
    public class TwinTranslationSuggestion
    {
        public string Translation { get; set; } = "";
        public string Quality { get; set; } = "";
        public double Match { get; set; }
        public string Source { get; set; } = "";
        public string Segment { get; set; } = "";
    }

    #endregion
}
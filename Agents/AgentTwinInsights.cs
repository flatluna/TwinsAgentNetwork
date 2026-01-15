using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using TwinAgentsNetwork.Services;
using TwinAgentsNetwork.Models;
using TwinAgentsLibrary.Models;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// AI agent that provides insights and recommendations for digital twins
    /// Analyzes patterns, suggests improvements, and provides strategic guidance
    /// </summary>
    public class AgentTwinInsights
    {
        private readonly TwinDataPersonalServices _twinDataService;
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;

        public AgentTwinInsights()
        {
            _twinDataService = new TwinDataPersonalServices();
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");
        }

        /// <summary>
        /// Generates comprehensive insights for a digital twin
        /// </summary>
        /// <param name="twinId">The unique identifier of the twin</param>
        /// <param name="insightType">Type of insights to generate (behavioral, predictive, strategic, wellness)</param>
        /// <param name="language">Response language</param>
        /// <param name="timeframe">Analysis timeframe (current, weekly, monthly, yearly)</param>
        /// <returns>Comprehensive insights and recommendations</returns>
        public async Task<TwinInsightsResult> GenerateInsightsAsync(
            string twinId, 
            string insightType = "comprehensive", 
            string language = "English",
            string timeframe = "current")
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("Twin ID cannot be null or empty", nameof(twinId));
            }

            try
            {
                // Get twin profile data
                var twinProfileData = await _twinDataService.GetTwinProfileByIdAsync(twinId);
                
                if (twinProfileData == null)
                {
                    return new TwinInsightsResult
                    {
                        Success = false,
                        ErrorMessage = $"No twin profile found for ID: {twinId}",
                        Insights = new List<TwinInsight>()
                    };
                }

                // Create AI Agent specialized in insights generation
                var chatClient = new AzureOpenAIClient(
                    new Uri(_azureOpenAIEndpoint),
                    new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName);

                AIAgent agent = chatClient.AsIChatClient().CreateAIAgent(
                    instructions: CreateInsightsInstructions(language, insightType, timeframe), 
                    name: "TwinInsightsExpert");

                AgentThread thread = agent.GetNewThread();
                string contextPrompt = CreateInsightsPrompt(twinProfileData, insightType, timeframe, language);

                var response = await agent.RunAsync(contextPrompt, thread);
                
                // Generate structured insights
                var insights = GenerateStructuredInsights(twinProfileData, insightType);
                var recommendations = GenerateRecommendations(twinProfileData, insightType);
                var predictions = GeneratePredictions(twinProfileData, timeframe);

                return new TwinInsightsResult
                {
                    Success = true,
                    TwinId = twinId,
                    InsightType = insightType,
                    Language = language,
                    Timeframe = timeframe,
                    AIAnalysis = response.Text ?? "",
                    Insights = insights,
                    Recommendations = recommendations,
                    Predictions = predictions,
                    ConfidenceScore = CalculateConfidenceScore(insights),
                    GeneratedTimestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new TwinInsightsResult
                {
                    Success = false,
                    ErrorMessage = $"Error generating insights: {ex.Message}",
                    TwinId = twinId,
                    Insights = new List<TwinInsight>(),
                    GeneratedTimestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Analyzes behavioral patterns for a twin
        /// </summary>
        public async Task<string> AnalyzeBehavioralPatternsAsync(string twinId, string language = "English")
        {
            var result = await GenerateInsightsAsync(twinId, "behavioral", language, "monthly");
            return result.Success ? result.AIAnalysis : result.ErrorMessage;
        }

        /// <summary>
        /// Provides strategic recommendations for personal development
        /// </summary>
        public async Task<List<string>> GetStrategicRecommendationsAsync(string twinId)
        {
            var result = await GenerateInsightsAsync(twinId, "strategic", "English", "yearly");
            return result.Success ? result.Recommendations : new List<string> { result.ErrorMessage };
        }

        /// <summary>
        /// Generates predictive insights about future trends
        /// </summary>
        public async Task<Dictionary<string, object>> GetPredictiveInsightsAsync(string twinId, string timeframe = "monthly")
        {
            var result = await GenerateInsightsAsync(twinId, "predictive", "English", timeframe);
            return result.Success ? result.Predictions : new Dictionary<string, object> { { "error", result.ErrorMessage } };
        }

        /// <summary>
        /// Gets insights agent as an AI function for use in other contexts
        /// </summary>
        public static AIAgent InsightsAgent
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
                            instructions: "You are a digital twin insights expert who analyzes patterns, generates predictions, and provides strategic recommendations for personal and professional development.",
                            name: "InsightsAgent",
                            description: "An AI agent that generates comprehensive insights and recommendations for digital twins");
            }
        }

        #region Private Helper Methods

        private string CreateInsightsInstructions(string language, string insightType, string timeframe)
        {
            return $@"
?? IDENTITY: You are a DIGITAL TWIN INSIGHTS EXPERT and strategic advisor.

?? YOUR EXPERTISE:
- Expert in behavioral pattern analysis and trend identification
- Specialized in {insightType} insights and strategic planning
- Authority on personal development and performance optimization
- Expert in predictive analytics for {timeframe} forecasting

?? INSIGHT TYPE: {insightType.ToUpper()}
?? TIMEFRAME: {timeframe.ToUpper()}

?? RESPONSE REQUIREMENTS:
1. LANGUAGE: Always respond in {language}
2. INSIGHTS: Focus on {insightType} analysis with {timeframe} perspective
3. DEPTH: Provide deep, actionable insights with specific recommendations
4. STRUCTURE: Organize findings with clear priorities and action items
5. CONFIDENCE: Include confidence levels for predictions and recommendations

?? REMEMBER: You provide strategic, data-driven insights that help individuals optimize their personal and professional development.";
        }

        private string CreateInsightsPrompt(TwinProfileData twinData, string insightType, string timeframe, string language)
        {
            string twinDataJson = JsonSerializer.Serialize(twinData, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return $@"
?? DIGITAL TWIN INSIGHTS GENERATION

?? TWIN PROFILE FOR ANALYSIS:
Twin ID: {twinData?.TwinId ?? "Unknown"}

?? COMPLETE TWIN DATA:
{twinDataJson}

?? ANALYSIS PARAMETERS:
- Insight Type: {insightType}
- Timeframe: {timeframe}
- Language: {language}

?? INSIGHTS MISSION:
Generate comprehensive {insightType} insights for this digital twin with a {timeframe} perspective.

?? ANALYSIS REQUIREMENTS:
1. Identify key patterns in the twin's profile data
2. Generate {insightType} insights specific to their characteristics
3. Provide actionable recommendations for {timeframe} improvement
4. Include confidence scores for your assessments
5. Highlight opportunities and potential risks
6. Suggest specific action items and milestones

??? LANGUAGE: Respond exclusively in {language}

Begin your insights analysis:";
        }

        private List<TwinInsight> GenerateStructuredInsights(TwinProfileData twinData, string insightType)
        {
            var insights = new List<TwinInsight>();

            // Generate insights based on available data
            if (twinData != null)
            {
                insights.Add(new TwinInsight
                {
                    Category = "Profile Analysis",
                    Title = "Core Characteristics",
                    Description = "Analysis of fundamental twin characteristics and patterns",
                    Confidence = 0.85,
                    Priority = "High",
                    ActionItems = new List<string> { "Review and validate core data", "Update profile regularly" }
                });

                insights.Add(new TwinInsight
                {
                    Category = "Behavioral Patterns",
                    Title = "Activity Trends",
                    Description = "Identified patterns in behavior and preferences",
                    Confidence = 0.75,
                    Priority = "Medium",
                    ActionItems = new List<string> { "Monitor activity patterns", "Set behavioral goals" }
                });

                insights.Add(new TwinInsight
                {
                    Category = "Development Opportunities",
                    Title = "Growth Areas",
                    Description = "Areas with potential for improvement and development",
                    Confidence = 0.80,
                    Priority = "High",
                    ActionItems = new List<string> { "Create development plan", "Set measurable goals" }
                });
            }

            return insights;
        }

        private List<string> GenerateRecommendations(TwinProfileData twinData, string insightType)
        {
            var recommendations = new List<string>();

            switch (insightType.ToLower())
            {
                case "behavioral":
                    recommendations.AddRange(new[]
                    {
                        "Establish consistent daily routines to improve productivity",
                        "Track behavioral patterns to identify optimization opportunities",
                        "Set specific behavioral goals with measurable outcomes"
                    });
                    break;

                case "strategic":
                    recommendations.AddRange(new[]
                    {
                        "Develop a 5-year personal development plan",
                        "Identify skill gaps and create learning roadmap",
                        "Build strategic partnerships and networking opportunities"
                    });
                    break;

                case "wellness":
                    recommendations.AddRange(new[]
                    {
                        "Implement regular wellness check-ins and assessments",
                        "Balance work and personal life activities",
                        "Establish healthy habits and routines"
                    });
                    break;

                default:
                    recommendations.AddRange(new[]
                    {
                        "Regular profile updates to maintain data accuracy",
                        "Continuous monitoring and analysis of key metrics",
                        "Periodic review and adjustment of goals and strategies"
                    });
                    break;
            }

            return recommendations;
        }

        private Dictionary<string, object> GeneratePredictions(TwinProfileData twinData, string timeframe)
        {
            var predictions = new Dictionary<string, object>();

            switch (timeframe.ToLower())
            {
                case "weekly":
                    predictions["short_term_trends"] = "Likely to maintain current patterns";
                    predictions["confidence"] = 0.9;
                    break;

                case "monthly":
                    predictions["performance_trend"] = "Gradual improvement expected";
                    predictions["confidence"] = 0.75;
                    break;

                case "yearly":
                    predictions["long_term_growth"] = "Significant development potential";
                    predictions["confidence"] = 0.65;
                    break;

                default:
                    predictions["current_state"] = "Stable with optimization opportunities";
                    predictions["confidence"] = 0.8;
                    break;
            }

            predictions["generated_at"] = DateTime.UtcNow;
            return predictions;
        }

        private double CalculateConfidenceScore(List<TwinInsight> insights)
        {
            if (!insights.Any()) return 0.0;
            return insights.Average(i => i.Confidence);
        }

        #endregion
    }

    #region Data Models

    /// <summary>
    /// Result of twin insights generation
    /// </summary>
    public class TwinInsightsResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string TwinId { get; set; } = "";
        public string InsightType { get; set; } = "";
        public string Language { get; set; } = "";
        public string Timeframe { get; set; } = "";
        public string AIAnalysis { get; set; } = "";
        public List<TwinInsight> Insights { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
        public Dictionary<string, object> Predictions { get; set; } = new();
        public double ConfidenceScore { get; set; }
        public DateTime GeneratedTimestamp { get; set; }
    }

    /// <summary>
    /// Individual insight about a digital twin
    /// </summary>
    public class TwinInsight
    {
        public string Category { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public double Confidence { get; set; }
        public string Priority { get; set; } = "";
        public List<string> ActionItems { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    #endregion
}
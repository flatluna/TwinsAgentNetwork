using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    /// AI agent that processes and analyzes semi-structured data for digital twins
    /// Handles JSON, XML, CSV and other mixed-format data sources
    /// </summary>
    public class AgentTwinSemiStructured
    {
        private readonly TwinDataPersonalServices _twinDataService;
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;

        public AgentTwinSemiStructured()
        {
            _twinDataService = new TwinDataPersonalServices();
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");
        }

        public AgentTwinSemiStructured(TwinDataPersonalServices twinDataService)
        {
            _twinDataService = twinDataService;
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");
        }

        /// <summary>
        /// Processes semi-structured data for a twin and extracts meaningful insights
        /// </summary>
        /// <param name="twinId">The unique identifier of the twin</param>
        /// <param name="semiStructuredData">The semi-structured data to process (JSON, XML, CSV, etc.)</param>
        /// <param name="dataFormat">The format of the data (json, xml, csv, mixed)</param>
        /// <param name="language">Response language</param>
        /// <param name="analysisType">Type of analysis to perform (structure, content, patterns, insights)</param>
        /// <returns>Analysis results with extracted insights</returns>
        public async Task<TwinSemiStructuredResult> ProcessSemiStructuredDataAsync(
            string twinId, 
            string semiStructuredData, 
            string dataFormat = "json", 
            string language = "English",
            string analysisType = "insights")
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("Twin ID cannot be null or empty", nameof(twinId));
            }

            if (string.IsNullOrEmpty(semiStructuredData))
            {
                throw new ArgumentException("Semi-structured data cannot be null or empty", nameof(semiStructuredData));
            }

            try
            {
                // Get twin profile for context
                var twinProfileData = await _twinDataService.GetTwinProfileByIdAsync(twinId);
                
                // Create AI Agent specialized in data processing
                var chatClient = new AzureOpenAIClient(
                    new Uri(_azureOpenAIEndpoint),
                    new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName);

                AIAgent agent = chatClient.AsIChatClient().CreateAIAgent(
                    instructions: CreateDataProcessingInstructions(language, dataFormat, analysisType), 
                    name: "TwinSemiStructuredDataExpert");

                AgentThread thread = agent.GetNewThread();
                string contextPrompt = CreateDataAnalysisPrompt(twinProfileData, semiStructuredData, dataFormat, analysisType, language);

                var response = await agent.RunAsync(contextPrompt, thread);
                
                // Parse the structured data
                var structuredAnalysis = ParseSemiStructuredData(semiStructuredData, dataFormat);

                return new TwinSemiStructuredResult
                {
                    Success = true,
                    TwinId = twinId,
                    DataFormat = dataFormat,
                    AnalysisType = analysisType,
                    Language = language,
                    AIAnalysis = response.Text ?? "",
                    StructuredData = structuredAnalysis,
                    ExtractedFields = ExtractKeyFields(semiStructuredData, dataFormat),
                    DataQuality = AssessDataQuality(semiStructuredData, dataFormat),
                    ProcessedTimestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new TwinSemiStructuredResult
                {
                    Success = false,
                    ErrorMessage = $"Error processing semi-structured data: {ex.Message}",
                    TwinId = twinId,
                    DataFormat = dataFormat,
                    ProcessedTimestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Converts semi-structured data to structured format for a twin
        /// </summary>
        public async Task<string> ConvertToStructuredFormatAsync(string semiStructuredData, string inputFormat, string outputFormat = "json")
        {
            var chatClient = new AzureOpenAIClient(
                new Uri(_azureOpenAIEndpoint),
                new AzureCliCredential())
                .GetChatClient(_azureOpenAIModelName);

            AIAgent agent = chatClient.AsIChatClient().CreateAIAgent(
                instructions: $"You are a data conversion expert. Convert the provided {inputFormat} data to clean, well-structured {outputFormat} format. Preserve all meaningful information while organizing it logically.",
                name: "DataConverter");

            var prompt = $@"
Convert the following {inputFormat} data to {outputFormat} format:

{semiStructuredData}

Requirements:
1. Preserve all data accurately
2. Use proper {outputFormat} structure
3. Organize data logically
4. Clean up any formatting issues
5. Ensure the output is valid {outputFormat}
";

            var result = await agent.RunAsync(prompt);
            return result.Text ?? semiStructuredData;
        }

        /// <summary>
        /// Validates and cleans semi-structured data
        /// </summary>
        public async Task<DataValidationResult> ValidateAndCleanDataAsync(string semiStructuredData, string dataFormat)
        {
            try
            {
                var validation = new DataValidationResult
                {
                    IsValid = true,
                    ValidationErrors = new List<string>(),
                    CleanedData = semiStructuredData,
                    Recommendations = new List<string>()
                };

                // Format-specific validation
                switch (dataFormat.ToLower())
                {
                    case "json":
                        validation = ValidateJsonData(semiStructuredData);
                        break;
                    case "xml":
                        validation = ValidateXmlData(semiStructuredData);
                        break;
                    case "csv":
                        validation = ValidateCsvData(semiStructuredData);
                        break;
                    default:
                        validation = ValidateGenericData(semiStructuredData);
                        break;
                }

                return validation;
            }
            catch (Exception ex)
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    ValidationErrors = new List<string> { ex.Message },
                    CleanedData = semiStructuredData,
                    Recommendations = new List<string> { "Review data format and structure" }
                };
            }
        }

        /// <summary>
        /// Gets an agent for semi-structured data processing as an AI function
        /// </summary>
        public static AIAgent SemiStructuredAgent
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
                            instructions: "You are a semi-structured data processing expert for digital twins. You can analyze, convert, and extract insights from JSON, XML, CSV, and mixed-format data.",
                            name: "SemiStructuredAgent",
                            description: "An AI agent that processes and analyzes semi-structured data for digital twins");
            }
        }

        #region Private Helper Methods

        private string CreateDataProcessingInstructions(string language, string dataFormat, string analysisType)
        {
            return $@"
?? IDENTITY: You are a SEMI-STRUCTURED DATA PROCESSING EXPERT for digital twins.

?? YOUR EXPERTISE:
- Expert in processing {dataFormat.ToUpper()} and other semi-structured data formats
- Specialized in extracting meaningful insights from complex data structures
- Authority on data quality assessment and cleaning
- Expert in data transformation and normalization

?? ANALYSIS TYPE: {analysisType}

?? RESPONSE REQUIREMENTS:
1. LANGUAGE: Always respond in {language}
2. DATA FORMAT: Focus on {dataFormat} structure and patterns
3. ANALYSIS: Provide {analysisType} analysis as requested
4. STRUCTURE: Organize findings logically
5. QUALITY: Assess data completeness and accuracy

?? REMEMBER: You are specialized in turning semi-structured data into actionable insights for digital twins.";
        }

        private string CreateDataAnalysisPrompt(TwinProfileData twinData, string semiStructuredData, string dataFormat, string analysisType, string language)
        {
            return $@"
?? SEMI-STRUCTURED DATA ANALYSIS TASK

?? TWIN CONTEXT:
Twin ID: {twinData?.TwinId ?? "Unknown"}
This analysis focuses on semi-structured data related to this digital twin.

?? DATA TO ANALYZE:
Format: {dataFormat.ToUpper()}
Analysis Type: {analysisType}

{semiStructuredData}

?? ANALYSIS MISSION:
Based on the provided {dataFormat} data, perform a {analysisType} analysis.

?? ANALYSIS REQUIREMENTS:
1. Identify data structure and patterns
2. Extract key information and relationships
3. Assess data quality and completeness
4. Provide insights relevant to digital twin analysis
5. Highlight any anomalies or interesting findings
6. Suggest improvements or additional data points

??? LANGUAGE: Respond exclusively in {language}

Begin your analysis:";
        }

        private Dictionary<string, object> ParseSemiStructuredData(string data, string format)
        {
            try
            {
                switch (format.ToLower())
                {
                    case "json":
                        return JsonSerializer.Deserialize<Dictionary<string, object>>(data) ?? new Dictionary<string, object>();
                    default:
                        return new Dictionary<string, object> { { "raw_data", data } };
                }
            }
            catch
            {
                return new Dictionary<string, object> { { "parse_error", "Could not parse data" } };
            }
        }

        private List<string> ExtractKeyFields(string data, string format)
        {
            var fields = new List<string>();
            
            try
            {
                if (format.ToLower() == "json")
                {
                    var jsonDoc = JsonDocument.Parse(data);
                    ExtractJsonFields(jsonDoc.RootElement, fields, "");
                }
            }
            catch
            {
                fields.Add("extraction_error");
            }

            return fields;
        }

        private void ExtractJsonFields(JsonElement element, List<string> fields, string prefix)
        {
            foreach (var property in element.EnumerateObject())
            {
                string fieldName = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                fields.Add(fieldName);
                
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    ExtractJsonFields(property.Value, fields, fieldName);
                }
            }
        }

        private DataQualityAssessment AssessDataQuality(string data, string format)
        {
            return new DataQualityAssessment
            {
                Completeness = CalculateCompleteness(data),
                Validity = ValidateFormat(data, format),
                Consistency = AssessConsistency(data),
                Accuracy = EstimateAccuracy(data),
                OverallScore = 0.0 // Calculate based on other metrics
            };
        }

        private double CalculateCompleteness(string data)
        {
            // Simple completeness calculation
            return string.IsNullOrWhiteSpace(data) ? 0.0 : Math.Min(data.Length / 1000.0, 1.0);
        }

        private bool ValidateFormat(string data, string format)
        {
            try
            {
                switch (format.ToLower())
                {
                    case "json":
                        JsonDocument.Parse(data);
                        return true;
                    default:
                        return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private double AssessConsistency(string data)
        {
            // Basic consistency assessment
            return 0.85; // Placeholder
        }

        private double EstimateAccuracy(string data)
        {
            // Basic accuracy estimation
            return 0.90; // Placeholder
        }

        private DataValidationResult ValidateJsonData(string jsonData)
        {
            try
            {
                JsonDocument.Parse(jsonData);
                return new DataValidationResult
                {
                    IsValid = true,
                    CleanedData = jsonData,
                    ValidationErrors = new List<string>(),
                    Recommendations = new List<string> { "JSON format is valid" }
                };
            }
            catch (JsonException ex)
            {
                return new DataValidationResult
                {
                    IsValid = false,
                    CleanedData = jsonData,
                    ValidationErrors = new List<string> { $"Invalid JSON: {ex.Message}" },
                    Recommendations = new List<string> { "Fix JSON syntax errors", "Validate JSON structure" }
                };
            }
        }

        private DataValidationResult ValidateXmlData(string xmlData)
        {
            // Basic XML validation
            return new DataValidationResult
            {
                IsValid = true,
                CleanedData = xmlData,
                ValidationErrors = new List<string>(),
                Recommendations = new List<string> { "XML processing not fully implemented" }
            };
        }

        private DataValidationResult ValidateCsvData(string csvData)
        {
            // Basic CSV validation
            return new DataValidationResult
            {
                IsValid = true,
                CleanedData = csvData,
                ValidationErrors = new List<string>(),
                Recommendations = new List<string> { "CSV processing not fully implemented" }
            };
        }

        private DataValidationResult ValidateGenericData(string data)
        {
            return new DataValidationResult
            {
                IsValid = !string.IsNullOrWhiteSpace(data),
                CleanedData = data?.Trim() ?? "",
                ValidationErrors = new List<string>(),
                Recommendations = new List<string> { "Generic data validation applied" }
            };
        }

        #endregion
    }

    #region Data Models

    /// <summary>
    /// Result of semi-structured data processing
    /// </summary>
    public class TwinSemiStructuredResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string TwinId { get; set; } = "";
        public string DataFormat { get; set; } = "";
        public string AnalysisType { get; set; } = "";
        public string Language { get; set; } = "";
        public string AIAnalysis { get; set; } = "";
        public Dictionary<string, object> StructuredData { get; set; } = new();
        public List<string> ExtractedFields { get; set; } = new();
        public DataQualityAssessment DataQuality { get; set; } = new();
        public DateTime ProcessedTimestamp { get; set; }
    }

    /// <summary>
    /// Data quality assessment metrics
    /// </summary>
    public class DataQualityAssessment
    {
        public double Completeness { get; set; }
        public bool Validity { get; set; }
        public double Consistency { get; set; }
        public double Accuracy { get; set; }
        public double OverallScore { get; set; }
    }

    /// <summary>
    /// Data validation result
    /// </summary>
    public class DataValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> ValidationErrors { get; set; } = new();
        public string CleanedData { get; set; } = "";
        public List<string> Recommendations { get; set; } = new();
    }

    #endregion
}
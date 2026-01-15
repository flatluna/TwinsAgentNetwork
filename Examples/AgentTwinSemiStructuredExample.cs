using System;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;

namespace TwinAgentsNetwork.Examples
{
    /// <summary>
    /// Example usage of the AgentTwinSemiStructured for processing semi-structured data
    /// </summary>
    public class AgentTwinSemiStructuredExample
    {
        public static async Task RunExample()
        {
            try
            {
                var semiStructuredAgent = new AgentTwinSemiStructured();

                Console.WriteLine("=== TWIN SEMI-STRUCTURED DATA PROCESSING EXAMPLES ===\n");

                // Example 1: Process JSON data
                Console.WriteLine("?? Example 1: Processing JSON Twin Data");
                string jsonData = @"{
                    ""personal_info"": {
                        ""name"": ""John Doe"",
                        ""age"": 35,
                        ""occupation"": ""Software Engineer"",
                        ""skills"": [""C#"", ""Python"", ""AI"", ""Machine Learning""]
                    },
                    ""preferences"": {
                        ""work_style"": ""remote"",
                        ""communication"": ""async"",
                        ""learning_style"": ""hands-on""
                    },
                    ""metrics"": {
                        ""productivity_score"": 8.5,
                        ""collaboration_rating"": 9.2,
                        ""innovation_index"": 7.8
                    }
                }";

                var jsonResult = await semiStructuredAgent.ProcessSemiStructuredDataAsync(
                    "388a31e7-d408-40f0-844c-4d2efedaa836",
                    jsonData,
                    "json",
                    "English",
                    "insights"
                );

                Console.WriteLine($"? JSON Analysis: {jsonResult.AIAnalysis}");
                Console.WriteLine($"?? Data Quality Score: {jsonResult.DataQuality.OverallScore:F2}");
                Console.WriteLine($"?? Extracted {jsonResult.ExtractedFields.Count} fields");
                Console.WriteLine();

                // Example 2: Process CSV-like data
                Console.WriteLine("?? Example 2: Processing CSV Twin Data");
                string csvData = @"Date,Activity,Duration,Performance,Notes
2024-01-15,Coding,8h,85%,Focused on AI algorithms
2024-01-16,Meetings,3h,92%,Great collaboration session
2024-01-17,Learning,4h,78%,Python deep learning course
2024-01-18,Project Work,6h,88%,Completed milestone 2";

                var csvResult = await semiStructuredAgent.ProcessSemiStructuredDataAsync(
                    "388a31e7-d408-40f0-844c-4d2efedaa836",
                    csvData,
                    "csv",
                    "English",
                    "patterns"
                );

                Console.WriteLine($"? CSV Analysis: {csvResult.AIAnalysis}");
                Console.WriteLine();

                // Example 3: Data conversion
                Console.WriteLine("?? Example 3: Converting JSON to structured format");
                string convertedData = await semiStructuredAgent.ConvertToStructuredFormatAsync(
                    jsonData,
                    "json",
                    "xml"
                );

                Console.WriteLine("? Converted data format successfully");
                Console.WriteLine();

                // Example 4: Data validation and cleaning
                Console.WriteLine("?? Example 4: Data validation and cleaning");
                var validationResult = await semiStructuredAgent.ValidateAndCleanDataAsync(
                    jsonData,
                    "json"
                );

                Console.WriteLine($"? Data is valid: {validationResult.IsValid}");
                Console.WriteLine($"?? Validation errors: {validationResult.ValidationErrors.Count}");
                Console.WriteLine($"?? Recommendations: {string.Join(", ", validationResult.Recommendations)}");
                Console.WriteLine();

                // Example 5: Mixed format processing
                Console.WriteLine("?? Example 5: Processing mixed format data");
                string mixedData = @"
                Twin Profile Update:
                {
                    ""timestamp"": ""2024-01-20T10:30:00Z"",
                    ""updates"": {
                        ""skills_acquired"": [""Azure AI"", ""OpenAI GPT""],
                        ""performance_metrics"": ""productivity: 9.1, creativity: 8.7""
                    }
                }
                
                Additional Notes:
                - Completed AI certification
                - Leading new project team
                - Mentor for 3 junior developers
                ";

                var mixedResult = await semiStructuredAgent.ProcessSemiStructuredDataAsync(
                    "388a31e7-d408-40f0-844c-4d2efedaa836",
                    mixedData,
                    "mixed",
                    "Spanish",
                    "content"
                );

                Console.WriteLine($"? Mixed format analysis: {mixedResult.AIAnalysis}");
                Console.WriteLine();

                // Example 6: Using the static agent for integration
                Console.WriteLine("?? Example 6: Using static agent for integration");
                var staticAgent = AgentTwinSemiStructured.SemiStructuredAgent;
                var integrationResult = await staticAgent.RunAsync("Analyze this data structure and extract key insights: " + jsonData);
                
                Console.WriteLine($"?? Static agent response: {integrationResult.Text}");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Console application to test the Semi-Structured Data Agent
    /// </summary>
    public class SemiStructuredAgentTestProgram
    {
        public static async Task Main(string[] args)
        {
            await AgentTwinSemiStructuredExample.RunExample();

            Console.WriteLine("\n=== TESTING ADVANCED FEATURES ===");
            
            var semiAgent = new AgentTwinSemiStructured();

            // Test complex JSON structure
            Console.WriteLine("?? Testing complex nested JSON structure...");
            string complexJson = @"{
                ""twin_id"": ""test-123"",
                ""profile"": {
                    ""personal"": {
                        ""demographics"": {
                            ""age_group"": ""30-40"",
                            ""location"": ""US-West"",
                            ""education"": ""Masters""
                        },
                        ""characteristics"": {
                            ""personality_type"": ""INTJ"",
                            ""work_style"": ""analytical"",
                            ""communication_preference"": ""written""
                        }
                    },
                    ""professional"": {
                        ""current_role"": ""Senior Engineer"",
                        ""experience_years"": 12,
                        ""specializations"": [""AI/ML"", ""Cloud Architecture"", ""DevOps""],
                        ""certifications"": [
                            {""name"": ""Azure AI Engineer"", ""year"": 2023},
                            {""name"": ""AWS Solutions Architect"", ""year"": 2022}
                        ]
                    }
                },
                ""metrics"": {
                    ""performance"": {
                        ""current_quarter"": {
                            ""productivity"": 92,
                            ""quality"": 96,
                            ""collaboration"": 88
                        },
                        ""trend"": ""improving""
                    }
                }
            }";

            try
            {
                var complexResult = await semiAgent.ProcessSemiStructuredDataAsync(
                    "test-complex-123",
                    complexJson,
                    "json",
                    "English",
                    "structure"
                );

                Console.WriteLine($"? Complex structure analysis completed");
                Console.WriteLine($"?? Extracted {complexResult.ExtractedFields.Count} fields");
                Console.WriteLine($"?? Analysis type: {complexResult.AnalysisType}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Complex structure test failed: {ex.Message}");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TwinAgentsNetwork.Examples
{
    /// <summary>
    /// Example demonstrating how to use the Twin Family Question Azure Function API
    /// </summary>
    public class TwinFamilyApiExample
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public TwinFamilyApiExample()
        {
            _httpClient = new HttpClient();
            _baseUrl = "https://your-function-app.azurewebsites.net"; // Replace with your Azure Function URL
        }

        /// <summary>
        /// Example 1: Ask a general family question
        /// </summary>
        public async Task AskGeneralFamilyQuestion()
        {
            try
            {
                var request = new
                {
                    twinId = "388a31e7-d408-40f0-844c-4d2efedaa836",
                    language = "English",
                    question = "Who is Daniel in the family and what is his relationship?"
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Console.WriteLine("?? Asking: Who is Daniel in the family?");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/twin-family/ask", content);
                var result = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
                    if (jsonResult.GetProperty("success").GetBoolean())
                    {
                        string familyAnalysis = jsonResult.GetProperty("familyAnalysis").GetString();
                        Console.WriteLine($"? Family Analysis: {familyAnalysis}");
                    }
                    else
                    {
                        string errorMessage = jsonResult.GetProperty("errorMessage").GetString();
                        Console.WriteLine($"? Error: {errorMessage}");
                    }
                }
                else
                {
                    Console.WriteLine($"? HTTP Error: {response.StatusCode} - {result}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Example 2: Ask about parents in Spanish
        /// </summary>
        public async Task AskAboutParentsInSpanish()
        {
            try
            {
                var request = new
                {
                    twinId = "388a31e7-d408-40f0-844c-4d2efedaa836",
                    language = "Spanish",
                    question = "¿Quiénes son los padres en esta familia y qué ocupación tienen?"
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Console.WriteLine("?? Pregunta en Español: ¿Quiénes son los padres?");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/twin-family/ask", content);
                var result = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
                    if (jsonResult.GetProperty("success").GetBoolean())
                    {
                        string familyAnalysis = jsonResult.GetProperty("familyAnalysis").GetString();
                        Console.WriteLine($"? Análisis Familiar: {familyAnalysis}");
                    }
                    else
                    {
                        string errorMessage = jsonResult.GetProperty("errorMessage").GetString();
                        Console.WriteLine($"? Error: {errorMessage}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Example 3: Use GET endpoint for simple questions
        /// </summary>
        public async Task SimpleGetFamilyQuestion()
        {
            try
            {
                string twinId = "388a31e7-d408-40f0-844c-4d2efedaa836";
                string question = Uri.EscapeDataString("Tell me about family occupations");
                string language = "English";

                Console.WriteLine("?? GET Request: Family occupations");

                var response = await _httpClient.GetAsync(
                    $"{_baseUrl}/api/twin-family/ask/{twinId}?question={question}&language={language}"
                );

                if (response.IsSuccessStatusCode)
                {
                    string result = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"? Family Analysis: {result}");
                }
                else
                {
                    string error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"? Error: {response.StatusCode} - {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Example 4: Multiple family questions
        /// </summary>
        public async Task MultipleFamilyQuestions()
        {
            string[] questions = {
                "Who are the parents in the family?",
                "Tell me about the siblings",
                "What are the different occupations in the family?",
                "What languages do family members speak?",
                "Tell me about family interests and hobbies"
            };

            foreach (string question in questions)
            {
                try
                {
                    var request = new
                    {
                        twinId = "388a31e7-d408-40f0-844c-4d2efedaa836",
                        language = "English",
                        question = question
                    };

                    var json = JsonSerializer.Serialize(request);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    Console.WriteLine($"\n?? Q: {question}");

                    var response = await _httpClient.PostAsync($"{_baseUrl}/api/twin-family/ask", content);
                    var result = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
                        if (jsonResult.GetProperty("success").GetBoolean())
                        {
                            string familyAnalysis = jsonResult.GetProperty("familyAnalysis").GetString();
                            Console.WriteLine($"? A: {familyAnalysis}");
                        }
                        else
                        {
                            string errorMessage = jsonResult.GetProperty("errorMessage").GetString();
                            Console.WriteLine($"? Error: {errorMessage}");
                        }
                    }

                    // Wait 1 second between requests
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"? Exception for question '{question}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Example 5: Error handling demonstration
        /// </summary>
        public async Task DemonstrateErrorHandling()
        {
            try
            {
                Console.WriteLine("\n?? Testing Error Handling:");

                // Test with empty Twin ID
                var invalidRequest = new
                {
                    twinId = "", // Empty Twin ID should cause error
                    language = "English",
                    question = "Tell me about the family"
                };

                var json = JsonSerializer.Serialize(invalidRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/twin-family/ask", content);
                var result = await response.Content.ReadAsStringAsync();

                var jsonResult = JsonSerializer.Deserialize<JsonElement>(result);
                if (!jsonResult.GetProperty("success").GetBoolean())
                {
                    string errorMessage = jsonResult.GetProperty("errorMessage").GetString();
                    Console.WriteLine($"? Expected error handled correctly: {errorMessage}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Exception during error testing: {ex.Message}");
            }
        }

        /// <summary>
        /// Run all examples
        /// </summary>
        public async Task RunAllExamples()
        {
            Console.WriteLine("=== TWIN FAMILY API EXAMPLES ===\n");

            await AskGeneralFamilyQuestion();
            await Task.Delay(1000);

            await AskAboutParentsInSpanish();
            await Task.Delay(1000);

            await SimpleGetFamilyQuestion();
            await Task.Delay(1000);

            await MultipleFamilyQuestions();
            await Task.Delay(1000);

            await DemonstrateErrorHandling();
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Console application to test the Family API
    /// </summary>
    public class FamilyApiTestProgram
    {
        public static async Task Main(string[] args)
        {
            var example = new TwinFamilyApiExample();

            try
            {
                await example.RunAllExamples();
            }
            finally
            {
                example.Dispose();
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
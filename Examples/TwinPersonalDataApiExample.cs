using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TwinAgentsNetwork.Examples
{
    /// <summary>
    /// Example demonstrating how to call the Twin Personal Data API from client code
    /// </summary>
    public class TwinPersonalDataApiExample
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public TwinPersonalDataApiExample(string baseUrl = "http://localhost:7071")
        {
            _httpClient = new HttpClient();
            _baseUrl = baseUrl;
        }

        /// <summary>
        /// Example of calling the POST API endpoint
        /// </summary>
        public async Task<string> AskQuestionPostExample()
        {
            try
            {
                var request = new
                {
                    twinId = "388a31e7-d408-40f0-844c-4d2efedaa836",
                    language = "English",
                    question = "As my dedicated digital twin expert, please provide insights about my unique characteristics and behavioral patterns."
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/twin-personal/ask", content);

                if (response.IsSuccessStatusCode)
                {
                    var htmlResult = await response.Content.ReadAsStringAsync();
                    Console.WriteLine("? SUCCESS - POST API Response:");
                    Console.WriteLine(htmlResult);
                    return htmlResult;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"? ERROR - Status: {response.StatusCode}, Content: {errorContent}");
                    return $"Error: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? EXCEPTION: {ex.Message}");
                return $"Exception: {ex.Message}";
            }
        }

        /// <summary>
        /// Example of calling the GET API endpoint
        /// </summary>
        public async Task<string> AskQuestionGetExample()
        {
            try
            {
                var twinId = "388a31e7-d408-40f0-844c-4d2efedaa836";
                var language = "Spanish";
                var question = "¿Cuáles son mis características únicas como individuo?";

                var encodedQuestion = Uri.EscapeDataString(question);
                var url = $"{_baseUrl}/api/twin-personal/ask/{twinId}?language={language}&question={encodedQuestion}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var htmlResult = await response.Content.ReadAsStringAsync();
                    Console.WriteLine("? SUCCESS - GET API Response:");
                    Console.WriteLine(htmlResult);
                    return htmlResult;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"? ERROR - Status: {response.StatusCode}, Content: {errorContent}");
                    return $"Error: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? EXCEPTION: {ex.Message}");
                return $"Exception: {ex.Message}";
            }
        }

        /// <summary>
        /// Example of different types of questions you can ask
        /// </summary>
        public async Task RunExamplesAsync()
        {
            Console.WriteLine("=== Twin Personal Data API Examples ===\n");

            // Example 1: Behavioral Analysis
            await AskSpecificQuestion(
                "English",
                "Based on your intimate knowledge of my profile, what behavioral patterns and personality traits can you identify that make me unique?"
            );

            Console.WriteLine("\n" + new string('=', 80) + "\n");

            // Example 2: Personal Development
            await AskSpecificQuestion(
                "English",
                "As my dedicated digital twin expert, what personalized recommendations can you provide for my personal and professional development?"
            );

            Console.WriteLine("\n" + new string('=', 80) + "\n");

            // Example 3: Spanish Query
            await AskSpecificQuestion(
                "Spanish",
                "Como mi gemelo digital dedicado, ¿qué patrones únicos de comportamiento puedes identificar en mi perfil personal?"
            );

            Console.WriteLine("\n" + new string('=', 80) + "\n");

            // Example 4: French Query
            await AskSpecificQuestion(
                "French",
                "En tant que mon jumeau numérique expert dédié, quelles sont mes caractéristiques personnelles les plus distinctives?"
            );
        }

        private async Task AskSpecificQuestion(string language, string question)
        {
            try
            {
                var request = new
                {
                    twinId = "388a31e7-d408-40f0-844c-4d2efedaa836",
                    language = language,
                    question = question
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Console.WriteLine($"?? ASKING QUESTION ({language}):");
                Console.WriteLine($"Question: {question}");
                Console.WriteLine("\n?? RESPONSE:");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/twin-personal/ask", content);
                var result = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine(result);
                }
                else
                {
                    Console.WriteLine($"? Error: {response.StatusCode} - {result}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Exception: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Simple console application to test the API
    /// </summary>
    public class ApiTestProgram
    {
        public static async Task Main(string[] args)
        {
            var example = new TwinPersonalDataApiExample();

            try
            {
                await example.RunExamplesAsync();
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
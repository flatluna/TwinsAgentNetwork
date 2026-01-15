using System;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;
using TwinAgentsNetwork.Services;
using TwinAgentsLibrary.Models;

namespace TwinAgentsNetwork.Examples
{
    /// <summary>
    /// Example usage of the TwinDataPersonalServices and AgentTwinPersonalData
    /// </summary>
    public class TwinDataExample
    {
        public static async Task RunExample()
        {
            try
            {
                // Example 1: Using the service directly
                var twinService = new TwinDataPersonalServices();
                var twinProfileData = await twinService.GetTwinProfileByIdAsync("12345");
                
                if (twinProfileData != null)
                {
                    Console.WriteLine($"Twin Profile Retrieved: {twinProfileData}");
                    // Access properties as they exist in TwinProfileData
                    // Console.WriteLine($"- Property: {twinProfileData.PropertyName}");
                }
                else
                {
                    Console.WriteLine("No twin profile found");
                }

                // Example 2: Using the new AI-powered agent
                var agent = new AgentTwinPersonalData();
                
                // English analysis
                var englishResult = await agent.AgentTwinPersonal(
                    "12345", 
                    "English", 
                    "Can you provide a comprehensive analysis of this twin's personal data and characteristics?");
                
                Console.WriteLine("=== ENGLISH ANALYSIS ===");
                Console.WriteLine(englishResult);

                // Spanish analysis
                var spanishResult = await agent.AgentTwinPersonal(
                    "12345", 
                    "Spanish", 
                    "¿Puedes proporcionar un análisis detallado de los datos personales de este gemelo?");
                
                Console.WriteLine("\n=== ANÁLISIS EN ESPAÑOL ===");
                Console.WriteLine(spanishResult);

                // French analysis
                var frenchResult = await agent.AgentTwinPersonal(
                    "12345", 
                    "French", 
                    "Pouvez-vous fournir une analyse détaillée des données personnelles de ce jumeau?");
                
                Console.WriteLine("\n=== ANALYSE EN FRANÇAIS ===");
                Console.WriteLine(frenchResult);

                // Example 3: Using the response wrapper (recommended for error handling)
                var response = await agent.GetTwinPersonalDataResponse("12345");
                
                if (response.Success && response.TwinProfileData != null)
                {
                    Console.WriteLine($"\n=== RAW DATA SUCCESS ===");
                    Console.WriteLine($"Success! Retrieved twin profile: {response.TwinProfileData}");
                }
                else
                {
                    Console.WriteLine($"Failed to retrieve twin data: {response.ErrorMessage}");
                }

                // Example 4: Test with different scenarios
                Console.WriteLine("\n--- Testing different scenarios ---");
                
                // Test with empty ID
                var emptyIdResponse = await agent.GetTwinPersonalDataResponse("");
                Console.WriteLine($"Empty ID test - Success: {emptyIdResponse.Success}, Error: {emptyIdResponse.ErrorMessage}");
                
                // Test with potentially non-existent ID
                var notFoundResponse = await agent.GetTwinPersonalDataResponse("999999");
                Console.WriteLine($"Non-existent ID test - Success: {notFoundResponse.Success}");
                
                // Test AI agent with different question types
                var behaviorAnalysis = await agent.AgentTwinPersonal(
                    "12345",
                    "English",
                    "What behavioral patterns can you identify from this twin's data?");
                
                Console.WriteLine("\n=== BEHAVIORAL ANALYSIS ===");
                Console.WriteLine(behaviorAnalysis);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in example: {ex.Message}");
            }
        }
    }
}
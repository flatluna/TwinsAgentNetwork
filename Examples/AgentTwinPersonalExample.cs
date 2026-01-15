using System;
using System.Threading.Tasks;
using TwinAgentsNetwork.Agents;

namespace TwinAgentsNetwork.Examples
{
    /// <summary>
    /// Enhanced usage example showing the AI agent as a dedicated twin expert
    /// </summary>
    public class AgentTwinPersonalExample
    {
        public static async Task RunExample()
        {
            try
            {
                var agent = new AgentTwinPersonalData();

                Console.WriteLine("=== DEDICATED DIGITAL TWIN EXPERT ANALYSIS ===\n");

                // Example 1: Comprehensive personal analysis
                var personalAnalysis = await agent.AgentTwinPersonal(
                    twinId: "388a31e7-d408-40f0-844c-4d2efedaa836",
                    language: "English",
                    question: "As my dedicated digital twin expert, please provide a comprehensive analysis of my personal profile, highlighting my unique characteristics and behavioral patterns."
                );

                Console.WriteLine("?? DEDICATED TWIN EXPERT ANALYSIS:");
                Console.WriteLine(personalAnalysis);
                Console.WriteLine("\n" + new string('=', 100) + "\n");

                // Example 2: Personalized recommendations
                var recommendations = await agent.AgentTwinPersonal(
                    twinId: "388a31e7-d408-40f0-844c-4d2efedaa836",
                    language: "English",
                    question: "Based on your intimate knowledge of my profile, what personalized recommendations can you provide for my personal development and growth?"
                );

                Console.WriteLine("?? PERSONALIZED RECOMMENDATIONS:");
                Console.WriteLine(recommendations);
                Console.WriteLine("\n" + new string('=', 100) + "\n");

                // Example 3: Spanish analysis demonstrating personal connection
                var spanishAnalysis = await agent.AgentTwinPersonal(
                    twinId: "388a31e7-d408-40f0-844c-4d2efedaa836",
                    language: "Spanish",
                    question: "Como mi experto gemelo digital dedicado, ¿qué patrones únicos puedes identificar en mi perfil personal que me distinguen como individuo?"
                );

                Console.WriteLine("???? ANÁLISIS PERSONALIZADO EN ESPAÑOL:");
                Console.WriteLine(spanishAnalysis);
                Console.WriteLine("\n" + new string('=', 100) + "\n");

                // Example 4: Behavioral insights from the dedicated expert
                var behaviorInsights = await agent.AgentTwinPersonal(
                    twinId: "388a31e7-d408-40f0-844c-4d2efedaa836",
                    language: "English",
                    question: "As someone who knows my profile intimately, what behavioral insights and patterns do you observe that I might not be aware of myself?"
                );

                Console.WriteLine("?? INTIMATE BEHAVIORAL INSIGHTS:");
                Console.WriteLine(behaviorInsights);
                Console.WriteLine("\n" + new string('=', 100) + "\n");

                // Example 5: Future predictions based on personal knowledge
                var predictions = await agent.AgentTwinPersonal(
                    twinId: "388a31e7-d408-40f0-844c-4d2efedaa836",
                    language: "English",
                    question: "Given your deep understanding of my personal profile and characteristics, what predictions or trends do you foresee for my future development?"
                );

                Console.WriteLine("?? PERSONAL FUTURE PREDICTIONS:");
                Console.WriteLine(predictions);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
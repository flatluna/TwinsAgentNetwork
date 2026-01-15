using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Threading.Tasks;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// AI agent that provides exercise recommendations and fitness guidance
    /// </summary>
    public class AgentExercise
    {
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;

        public AgentExercise()
        {
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") 
                ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") 
                ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");
        }

        /// <summary>
        /// Provides exercise recommendations based on user preferences and goals
        /// </summary>
        /// <param name="exerciseType">Type of exercise (cardio, strength, flexibility, etc.)</param>
        /// <param name="duration">Desired duration in minutes</param>
        /// <param name="fitnessLevel">User's fitness level (beginner, intermediate, advanced)</param>
        /// <returns>Personalized exercise recommendations</returns>
        public async Task<string> GetExerciseRecommendationAsync(string exerciseType = "general", int duration = 30, string fitnessLevel = "beginner")
        {
            AIAgent agent = new AzureOpenAIClient(
                new Uri(_azureOpenAIEndpoint),
                new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName)
                    .AsIChatClient()
                    .CreateAIAgent(
                        instructions: "You are a certified personal trainer and fitness expert. Provide safe, effective exercise recommendations tailored to the user's fitness level and goals. Always emphasize proper form and safety. Include warm-up and cool-down suggestions.",
                        name: "ExerciseAgent");

            var prompt = $"Please provide a {duration}-minute {exerciseType} workout routine for someone at a {fitnessLevel} fitness level. Include warm-up, main exercises, and cool-down. Focus on proper form and safety.";

            var result = await agent.RunAsync(prompt);
            return result.Text ?? "Here's a simple 30-minute beginner workout: 5-min warm-up (light walking), 20-min main workout (bodyweight exercises like squats, push-ups, lunges), and 5-min cool-down (stretching). Always listen to your body and stay hydrated!";
        }

        /// <summary>
        /// Gets an exercise agent as an AI function for use in other contexts
        /// </summary>
        public static AIAgent ExerciseAgent
        {
            get
            {
                var azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") 
                    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
                var azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") 
                    ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");

                return new AzureOpenAIClient(
                    new Uri(azureOpenAIEndpoint),
                    new AzureCliCredential())
                        .GetChatClient(azureOpenAIModelName)
                        .AsIChatClient()
                        .CreateAIAgent(
                            instructions: "You are a certified personal trainer and fitness expert. Provide safe, effective exercise recommendations. Always prioritize proper form and safety.",
                            name: "ExerciseAgent",
                            description: "An AI agent that provides personalized exercise recommendations and fitness guidance");
            }
        }

        /// <summary>
        /// Provides nutrition recommendations to complement exercise routines
        /// </summary>
        /// <param name="goal">Fitness goal (weight loss, muscle gain, maintenance, etc.)</param>
        /// <param name="activityLevel">Current activity level</param>
        /// <returns>Nutrition guidance</returns>
        public async Task<string> GetNutritionGuidanceAsync(string goal = "general health", string activityLevel = "moderate")
        {
            AIAgent agent = new AzureOpenAIClient(
                new Uri(_azureOpenAIEndpoint),
                new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName)
                    .AsIChatClient()
                    .CreateAIAgent(
                        instructions: "You are a nutrition expert who provides healthy eating guidance to support fitness goals. Focus on balanced nutrition, proper hydration, and safe dietary practices. Always recommend consulting healthcare professionals for specific dietary needs.",
                        name: "NutritionAgent");

            var prompt = $"Provide general nutrition guidance for someone with a goal of {goal} and {activityLevel} activity level. Include hydration tips and remind them to consult healthcare professionals for personalized advice.";

            var result = await agent.RunAsync(prompt);
            return result.Text ?? "For general health: focus on balanced meals with lean proteins, whole grains, fruits, and vegetables. Stay hydrated with plenty of water. Always consult a healthcare professional or registered dietitian for personalized nutrition advice.";
        }

        /// <summary>
        /// Provides wellness tips and recovery guidance
        /// </summary>
        /// <param name="focus">Focus area (recovery, sleep, stress management, etc.)</param>
        /// <returns>Wellness recommendations</returns>
        public async Task<string> GetWellnessTipsAsync(string focus = "general wellness")
        {
            AIAgent agent = new AzureOpenAIClient(
                new Uri(_azureOpenAIEndpoint),
                new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName)
                    .AsIChatClient()
                    .CreateAIAgent(
                        instructions: "You are a wellness expert who provides holistic health guidance including rest, recovery, stress management, and overall well-being. Emphasize the importance of balance and listening to one's body.",
                        name: "WellnessAgent");

            var prompt = $"Provide wellness guidance focused on {focus}. Include practical tips for maintaining physical and mental well-being.";

            var result = await agent.RunAsync(prompt);
            return result.Text ?? "For general wellness: prioritize adequate sleep (7-9 hours), manage stress through relaxation techniques, stay active regularly, maintain social connections, and listen to your body's needs. Balance is key to overall well-being.";
        }
    }
}
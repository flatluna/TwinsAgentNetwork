using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Threading.Tasks;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// AI agent that tells jokes and provides humor
    /// </summary>
    public class AgentJoker
    {
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;

        public AgentJoker()
        {
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");
        }

        /// <summary>
        /// Tells a joke based on the provided topic
        /// </summary>
        /// <param name="topic">The topic for the joke (optional)</param>
        /// <returns>A funny joke</returns>
        public async Task<string> TellJokeAsync(string topic = "general")
        {
            AIAgent agent = new AzureOpenAIClient(
                new Uri(_azureOpenAIEndpoint),
                new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName)
                    .AsIChatClient()
                    .CreateAIAgent(
                        instructions: "You are a friendly comedian that tells clean, family-friendly jokes. Always respond with enthusiasm and humor. Keep jokes appropriate for all audiences.",
                        name: "JokeAgent");

            var prompt = string.IsNullOrEmpty(topic) || topic == "general" 
                ? "Tell me a funny, clean joke that would make anyone laugh!"
                : $"Tell me a funny, clean joke about {topic}!";

            var result = await agent.RunAsync(prompt);
            return result.Text ?? "Why don't scientists trust atoms? Because they make up everything! ??";
        }

        /// <summary>
        /// Gets a joke agent as an AI function for use in other contexts
        /// </summary>
        public static AIAgent JokeAgent
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
                            instructions: "You are a friendly comedian that tells clean, family-friendly jokes. When asked for a joke, respond with a funny, appropriate joke. Keep it light and entertaining!",
                            name: "JokeAgent",
                            description: "An AI agent that tells funny, family-friendly jokes on various topics");
            }
        }
    }
}
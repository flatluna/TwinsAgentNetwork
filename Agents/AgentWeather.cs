using System;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace TwinAgentsNetwork.Agents
{
    public static class AgentWeather
    {
        private static readonly string _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
        private static readonly string _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");

        public static AIAgent WeatherAgent => new AzureOpenAIClient(
            new Uri(_azureOpenAIEndpoint),
            new AzureCliCredential())
             .GetChatClient(_azureOpenAIModelName)
             .CreateAIAgent(
                instructions: "You answer questions about the weather.",
                name: "WeatherAgent",
                description: "An agent that answers questions about the weather.",
                tools: [AIFunctionFactory.Create(GlobalTools.GetWeather)]);
    }
}

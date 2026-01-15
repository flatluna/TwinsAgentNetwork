using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI;
using System;
using System.Threading.Tasks;

namespace TwinAgentsNetwork.MCPTools
{
    public class MathAgentTool
    {
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;

        public MathAgentTool()
        {
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "https://flatbitai.openai.azure.com/";
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? "gpt4mini";
        }

        public async Task<int> AddTwoNumbersAsync(int Number1, int Number2)
        {
            AIAgent agent = new AzureOpenAIClient(
                new Uri(_azureOpenAIEndpoint),
                new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName)
                    .CreateAIAgent(
                        instructions: "You are a specialized math agent that performs addition operations. When given two numbers, add them together and return only the numerical result as an integer.",
                        name: "MathAgent");

            var prompt = $"Add these two numbers: {Number1} + {Number2}. Return only the number result.";
            var result = await agent.RunAsync(prompt);
            
            // Parse the result back to int (you might want to add error handling here)
            if (int.TryParse(result.ToString().Trim(), out int sum))
            {
                return sum;
            }
            
            // Fallback to direct calculation if parsing fails
            return Number1 + Number2;
        }

        // Keep the original synchronous method for backward compatibility
        public int AddTwoNumbers(int Number1, int Number2)
        {
            return Number1 + Number2;
        }
    }
}

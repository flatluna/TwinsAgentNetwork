using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using TwinAgentsNetwork.MCPTools;
using System;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI;

namespace TwinAgentsNetwork.MCPTools
{
    public class McpProgram
    {
        public static async Task Main(string[] args)
        {
            // Create the AI agent for math operations
            var azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "https://flatbitai.openai.azure.com/";
            var azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? "gpt4mini";

            AIAgent mathAgent = new AzureOpenAIClient(
                new Uri(azureOpenAIEndpoint),
                new AzureCliCredential())
                    .GetChatClient(azureOpenAIModelName)
                    .CreateAIAgent(
                        instructions: "You are a specialized math agent that performs addition operations. When given two numbers, add them together and return only the numerical result as an integer.",
                        name: "MathAgent",
                        description: "An agent that performs mathematical addition operations");

            // Turn the agent into an MCP tool
            McpServerTool tool = McpServerTool.Create(mathAgent.AsAIFunction());
            
            HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(settings: null);
            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithTools([tool]);

            await builder.Build().RunAsync();
        }
    }
}
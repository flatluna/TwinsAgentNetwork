// This sample shows how to expose an AI agent as an MCP tool.
// Adapted to work with Azure OpenAI instead of Azure AI Foundry Persistent Agents

using System;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using OpenAI;

namespace TwinAgentsNetwork.Agents
{
    public class MCPDemo
    {
        public static async Task RunMCPDemoAsync()
        {
            // Use your existing Azure OpenAI setup instead of Azure Foundry
            var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");

            // Create an AI agent using Azure OpenAI (equivalent to Persistent Agent)
            AIAgent agent = new AzureOpenAIClient(
                new Uri(endpoint),
                new AzureCliCredential())
                    .GetChatClient(deploymentName)
                    .CreateAIAgent(
                        instructions: "You are good at telling jokes, and you always start each joke with 'Aye aye, captain!'.",
                        name: "Joker",
                        description: "An agent that tells jokes.");

            // Convert the agent to an AIFunction and then to an MCP tool.
            // The agent name and description will be used as the mcp tool name and description.
            McpServerTool tool = McpServerTool.Create(agent.AsAIFunction());

            // Register the MCP server with StdIO transport and expose the tool via the server.
            HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(settings: null);
            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithTools([tool]);

            await builder.Build().RunAsync();
        }
    }
}

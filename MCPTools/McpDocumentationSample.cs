using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System;
using System.Threading.Tasks;

namespace TwinAgentsNetwork.MCPTools
{
    /// <summary>
    /// Documentation sample for MCP server with reusable joke agent
    /// This demonstrates clean architecture with AgentJoker.JokeAgent as single source of truth
    /// </summary>
    public class McpDocumentationSample
    {
        /// <summary>
        /// Starts a sample MCP server using the reusable AgentJoker.JokeAgent
        /// This is the clean architecture approach - no duplicated logic
        /// </summary>
        public static async Task StartDocumentationSampleAsync()
        {
            try
            {
                Console.WriteLine("?? Starting MCP Documentation Sample Server...");
                Console.WriteLine("??? Architecture: Using AgentJoker.JokeAgent as single source of truth");

                // Use the reusable joke agent from AgentJoker class
                var jokeAgentTool = McpServerTool.Create(Agents.AgentJoker.JokeAgent.AsAIFunction());

                Console.WriteLine("? Created joke agent tool from reusable AgentJoker.JokeAgent");
                Console.WriteLine("?? Benefits:");
                Console.WriteLine("   • Single source of truth");
                Console.WriteLine("   • No code duplication"); 
                Console.WriteLine("   • Easy maintenance");
                Console.WriteLine("   • Consistent behavior across all usage");
                
                Console.WriteLine("\n?? Sample server ready with joke capabilities!");
                Console.WriteLine("?? Usage: This agent can tell jokes via MCP protocol");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Error starting documentation sample: {ex.Message}");
                throw;
            }
        }
    }
}
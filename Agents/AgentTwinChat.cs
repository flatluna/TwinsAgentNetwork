using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using System.IO;
using System.Text.Json;
using OpenAI;
namespace TwinAgentsNetwork.Agents
{
    public class AgentTwinChat
    {
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;

        public AgentTwinChat()
        {
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");
        }
        public async Task AgentTwinChatMethod(string Prompt)
        {
         

            AIAgent agent = new AzureOpenAIClient(
                new Uri(_azureOpenAIEndpoint),
                new AzureCliCredential())
                 .GetChatClient(_azureOpenAIModelName)
                 .CreateAIAgent(instructions: "You are a helpful assistant.", name: "Assistant");

            AgentThread thread = agent.GetNewThread();

            var esponse = await agent.RunAsync(Prompt, thread);


            // Serialize the thread state
            string serializedJson = thread.Serialize(JsonSerializerOptions.Web).GetRawText();

            // Example: save to a local file (replace with DB or blob storage in production)
            string filePath = Path.Combine(Path.GetTempPath(), "agent_thread.json");
            await File.WriteAllTextAsync(filePath, serializedJson);
            // Method implementation goes here

            // Read persisted JSON
            string loadedJson = await File.ReadAllTextAsync(filePath);
            JsonElement reloaded = JsonSerializer.Deserialize<JsonElement>(loadedJson, JsonSerializerOptions.Web);

            // Deserialize the thread into an AgentThread tied to the same agent type
            thread = agent.DeserializeThread(reloaded, JsonSerializerOptions.Web);
            Prompt = "No te entendi que dijiste?";
            var Response = await agent.RunAsync(Prompt, thread);

        }

    }
}

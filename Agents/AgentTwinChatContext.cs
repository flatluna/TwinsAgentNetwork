using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Identity.Client;
using OpenAI;
using OpenAI.Chat;
using System;
namespace TwinAgentsNetwork.Agents
{
    public  class AgentTwinChatContext
    {
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;


        public AgentTwinChatContext()
        {
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");


        }

        public async Task AgentTwinChatContextMethod()
        {
            ChatClient chatClient = new AzureOpenAIClient(
            new Uri(_azureOpenAIEndpoint),
            new AzureCliCredential())
            .GetChatClient(_azureOpenAIModelName);

            AIAgent agent = chatClient.CreateAIAgent(new ChatClientAgentOptions()
            {
                Instructions = "You are a friendly assistant. Always address the user by their name.",
                AIContextProviderFactory = ctx => new UserInfoMemory(
                    chatClient.AsIChatClient(),
                    ctx.SerializedState,
                    ctx.JsonSerializerOptions)
            });
            // Create a new thread for the conversation.
            AgentThread thread = agent.GetNewThread();

            var mathResponse = await agent.RunAsync("Hello, what is the square root of 9?", thread);
            var nameResponse = await agent.RunAsync("My name is Ruaidhrí", thread);
            var ageResponse = await agent.RunAsync("I am 20 years old", thread);



            // Access the memory component via the thread's GetService method.
            var userInfo = thread.GetService<UserInfoMemory>()?.UserInfo;
            Console.WriteLine($"MEMORY - User Name: {userInfo?.UserName}");
            Console.WriteLine($"MEMORY - User Age: {userInfo?.UserAge}");

        }
    }
}

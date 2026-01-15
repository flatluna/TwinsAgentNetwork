using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwinAgentsNetwork.Models;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat;


namespace TwinAgentsNetwork.Agents
{
    public class TwinAgentMain
    {
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;

        public TwinAgentMain()
        {
            _azureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");
            _azureOpenAIModelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");
        }

        ChatOptions chatOptions = new()
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                schema: TwinInfo.schema,
                schemaName: "TwinInfo",
                schemaDescription: "Information about a person including their name, age, and occupation")
                };
        public async Task AgentChatAuth()
        {
            var baseUrl = _azureOpenAIEndpoint;
            var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? "";
            var modelId = _azureOpenAIModelName;


            Uri uri = new Uri(baseUrl);

            var credential = new AzureKeyCredential(apiKey);
            var client = new AzureOpenAIClient(uri, credential  );

            var chatClient = client.GetChatClient(modelId);
            var agent = chatClient.CreateAIAgent(
                "You are a friendly local assistant running fully offline.",
                "LocalAssistant");

            // --- Synchronous Run ---
            var userPrompt = "Explain in one line what a local AI agent is.";
            
            var result = await agent.RunAsync(userPrompt);
            

            // --- Streaming Run ---
            userPrompt = "Now explain it in a poetic way, with a sonnet, celebrating local intelligence.";

            agent.RunStreamingAsync(userPrompt);

        }
        public async Task AgentChat()
        {

            ChatClient chatClient = new AzureOpenAIClient(
                new Uri(_azureOpenAIEndpoint),
                new AzureCliCredential())
                .GetChatClient(_azureOpenAIModelName);



            AIAgent agentThread = chatClient.CreateAIAgent(new ChatClientAgentOptions()
            {
                Instructions = "You are a friendly assistant. Always address the user by their name.",
                AIContextProviderFactory = ctx => new UserInfoMemory(
                    chatClient.AsIChatClient(),
                    ctx.SerializedState,
                    ctx.JsonSerializerOptions)
            });

            // Create a new thread for the conversation.
            AgentThread thread = agentThread.GetNewThread();

            var Responses = await agentThread.RunAsync("Hello, what is the square root of 9?", thread);
            Responses =  await agentThread.RunAsync("My name is Ruaidhrí", thread);
            Responses = await agentThread.RunAsync("I am 20 years old", thread);

            // Access the memory component via the thread's GetService method.
            var userInfo = thread.GetService<UserInfoMemory>()?.UserInfo;
            Console.WriteLine($"MEMORY - User Name: {userInfo?.UserName}");
            Console.WriteLine($"MEMORY - User Age: {userInfo?.UserAge}");



            // END TEST OF MEMORY COMPONENT

            AIAgent agent = new AzureOpenAIClient(
                new Uri(_azureOpenAIEndpoint),
                new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName)
                     .CreateAIAgent(instructions: "You are a helpful assistant who responds in French.", 
                     tools: [AgentWeather.WeatherAgent.AsAIFunction()]);
            
            var response = await agent.RunAsync("Please provide information about John Smith, who is a 35-year-old software engineer.");

            var ResultsChat = await agent.RunAsync("Tell me a joke about a pirate.");
            var ResponseWeather = await agent.RunAsync("What is the weather like in Amsterdam?");
            
            ChatMessage systemMessage = new(
               ChatRole.System,
               """
                If the user asks you to tell a joke, refuse to do so, explaining that you are not a clown.
                Offer the user an interesting fact instead.
                """);
            ChatMessage userMessage = new(ChatRole.User, "Tell me a joke about a pirate.");

            var Results = await agent.RunAsync([systemMessage, userMessage]);

            ChatMessage message = new(ChatRole.User, [
                new TextContent("What do you see in this image?"),
                new UriContent("https://upload.wikimedia.org/wikipedia/commons/1/11/Joseph_Grimaldi.jpg", "image/jpeg")
             ]);

            Results = (await agent.RunAsync(message));
        }

        public async Task ImageAgentChat()
        {
            AIAgent agent = new AzureOpenAIClient(
               new Uri(_azureOpenAIEndpoint),
                new AzureCliCredential())
                .GetChatClient(_azureOpenAIModelName)
                .CreateAIAgent(
                    name: "VisionAgent",
                    instructions: "You are a helpful agent that can analyze images");


            AgentThread thread1 = agent.GetNewThread();
            ChatMessage message = new(ChatRole.User, [
                    new TextContent("What do you see in this image and add it to the joke you made?"),
                new UriContent("https://upload.wikimedia.org/wikipedia/commons/thumb/d/dd/Gfp-wisconsin-madison-the-nature-boardwalk.jpg/2560px-Gfp-wisconsin-madison-the-nature-boardwalk.jpg", "image/jpeg")
                ]);
            var AgentResponse = await agent.RunAsync("Tell me a joke about a pirate.", thread1);
            AgentResponse = await agent.RunAsync(message, thread1);


        }

    }

}
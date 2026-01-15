using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TwinAgentsNetwork.Models;

internal sealed class UserInfoMemory : AIContextProvider
{
    private readonly IChatClient _chatClient;
    public UserInfoMemory(IChatClient chatClient, UserInfo? userInfo = null)
    {
        this._chatClient = chatClient;
        this.UserInfo = userInfo ?? new UserInfo();
    }

    public UserInfoMemory(IChatClient chatClient, JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null)
    {
        this._chatClient = chatClient;
        this.UserInfo = serializedState.ValueKind == JsonValueKind.Object ?
            serializedState.Deserialize<UserInfo>(jsonSerializerOptions)! :
            new UserInfo();
    }

    public UserInfo UserInfo { get; set; }

    public override async ValueTask InvokedAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        if ((this.UserInfo.UserName is null || this.UserInfo.UserAge is null) && context.RequestMessages.Any(x => x.Role == ChatRole.User))
        {
            var result = await this._chatClient.GetResponseAsync<UserInfo>(
                context.RequestMessages,
                new ChatOptions()
                {
                    Instructions = "Extract the user's name and age from the message if present. If not present return nulls."
                },
                cancellationToken: cancellationToken);
            this.UserInfo.UserName ??= result.Result.UserName;
            this.UserInfo.UserAge ??= result.Result.UserAge;
        }
    }

    public override ValueTask<AIContext> InvokingAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        StringBuilder instructions = new();
        instructions
            .AppendLine(
                this.UserInfo.UserName is null ?
                    "Ask the user for their name and politely decline to answer any questions until they provide it." :
                    $"The user's name is {this.UserInfo.UserName}.")
            .AppendLine(
                this.UserInfo.UserAge is null ?
                    "Ask the user for their age and politely decline to answer any questions until they provide it." :
                    $"The user's age is {this.UserInfo.UserAge}.");
        return new ValueTask<AIContext>(new AIContext
        {
            Instructions = instructions.ToString()
        });
    }

    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        return JsonSerializer.SerializeToElement(this.UserInfo, jsonSerializerOptions);
    }
}
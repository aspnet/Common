#if (IsOpenAI || (IsAzureOpenAI && !IsManagedIdentity))
using System.ClientModel;
#elif (IsAzureOpenAI && IsManagedIdentity)
using System.ClientModel.Primitives;
#endif
using System.ComponentModel;
#if (IsAzureOpenAI && IsManagedIdentity)
using Azure.Identity;
#endif
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
#if (IsOllama)
using OllamaSharp;
#endif
#if (IsAzureOpenAI)
using OpenAI;
#endif
#if (IsOpenAI || IsAzureOpenAI)
using OpenAI.Chat;
#endif

var builder = WebApplication.CreateBuilder(args);

#if (IsOpenAI)
// You will need to set the API key to your own value
// You can do this using Visual Studio's "Manage User Secrets" UI, or on the command line:
//   cd this-project-directory
//   dotnet user-secrets set "OPENAI_KEY" "your-openai-api-key-here"
var chatClient = new ChatClient(
        "gpt-4o-mini",
        new ApiKeyCredential(builder.Configuration["OPENAI_KEY"] ?? throw new InvalidOperationException("Missing configuration: OPENAI_KEY")))
    .AsIChatClient();
#elif (IsAzureOpenAI)
// You will need to set the endpoint to your own value
// You can do this using Visual Studio's "Manage User Secrets" UI, or on the command line:
//   cd this-project-directory
//   dotnet user-secrets set AzureOpenAI:Endpoint https://YOUR-DEPLOYMENT-NAME.openai.azure.com
#if (!IsManagedIdentity)
//   dotnet user-secrets set AzureOpenAI:Key YOUR-API-KEY
#endif
var azureOpenAIEndpoint = new Uri(new Uri(builder.Configuration["AzureOpenAI:Endpoint"] ?? throw new InvalidOperationException("Missing configuration: AzureOpenAI:Endpoint")), "/openai/v1");

#if (IsManagedIdentity)
#pragma warning disable OPENAI001 // The overload accepting an AuthenticationPolicy is experimental and may change or be removed in future releases.
var chatClient = new ChatClient(
        "gpt-4o-mini",
        new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default"),
        new OpenAIClientOptions { Endpoint = azureOpenAIEndpoint })
    .AsIChatClient();
#pragma warning restore OPENAI001
#else
var chatClient = new ChatClient(
        "gpt-4o-mini",
        new ApiKeyCredential(builder.Configuration["AzureOpenAI:Key"] ?? throw new InvalidOperationException("Missing configuration: AzureOpenAI:Key")),
        new OpenAIClientOptions { Endpoint = azureOpenAIEndpoint })
    .AsIChatClient();
#endif
#elif (IsOllama)
// You will need to have Ollama running locally with the llama3.2 model installed
// Visit https://ollama.com for installation instructions
var chatClient = new OllamaApiClient(new Uri("http://localhost:11434"), "llama3.2");
#endif

builder.Services.AddChatClient(chatClient);

builder.AddAIAgent("writer", "You write short stories (300 words or less) about the specified topic.");

builder.AddAIAgent("editor", (sp, key) => new ChatClientAgent(
    chatClient,
    name: key,
    instructions: "You edit short stories to improve grammar and style, ensuring the stories are less than 300 words. Once finished editing, you select a title and format the story for publishing.",
    tools: [AIFunctionFactory.Create(FormatStory)]
));

builder.AddWorkflow("publisher", (sp, key) => AgentWorkflowBuilder.BuildSequential(
    workflowName: key,
    agents:
    [
        sp.GetRequiredKeyedService<AIAgent>("writer"),
        sp.GetRequiredKeyedService<AIAgent>("editor")
    ]
)).AddAsAIAgent("publisher-agent");

// Register services for OpenAI responses and conversations (also required for DevUI)
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

var app = builder.Build();
app.UseHttpsRedirection();

// Map endpoints for OpenAI responses and conversations (also required for DevUI)
app.MapOpenAIResponses();
app.MapOpenAIConversations();

if (builder.Environment.IsDevelopment())
{
    // Map DevUI endpoint to /devui
    app.MapDevUI();
}

app.Run();

[Description("Formats the story for publication, revealing its title.")]
string FormatStory(string title, string story) => $"""
    **Title**: {title}

    {story}
    """;

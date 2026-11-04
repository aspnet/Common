#if (IsOpenAI || (IsAzureOpenAI && !IsManagedIdentity))
using System.ClientModel;
#elif (IsAzureOpenAI && IsManagedIdentity)
using System.ClientModel.Primitives;
#endif
#if (IsAzureAISearch && !IsManagedIdentity)
using Azure;
#elif (IsManagedIdentity)
using Azure.Identity;
#endif
using Microsoft.Extensions.AI;
#if (IsOllama)
using OllamaSharp;
#elif (IsOpenAI || IsAzureOpenAI)
using OpenAI;
#endif
using AIChatWeb_CSharp.Web.Components;
using AIChatWeb_CSharp.Web.Services;
using AIChatWeb_CSharp.Web.Services.Ingestion;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

#if (IsOllama)
IChatClient chatClient = new OllamaApiClient(new Uri("http://localhost:11434"),
    "llama3.2");
IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = new OllamaApiClient(new Uri("http://localhost:11434"),
    "all-minilm");
#elif (IsOpenAI)
// You will need to set the endpoint and key to your own values
// You can do this using Visual Studio's "Manage User Secrets" UI, or on the command line:
//   cd this-project-directory
//   dotnet user-secrets set OpenAI:Key YOUR-API-KEY

var openAIClient = new OpenAIClient(
    new ApiKeyCredential(builder.Configuration["OpenAI:Key"] ?? throw new InvalidOperationException("Missing configuration: OpenAI:Key. See the README for details.")));

#pragma warning disable OPENAI001 // GetResponsesClient() is experimental and subject to change or removal in future updates.
var chatClient = openAIClient.GetResponsesClient().AsIChatClient("gpt-4o-mini");
#pragma warning restore OPENAI001

var embeddingGenerator = openAIClient.GetEmbeddingClient("text-embedding-3-small").AsIEmbeddingGenerator();
#elif (IsAzureAIFoundry)

#elif (IsAzureOpenAI)
// You will need to set the endpoint and key to your own values
// You can do this using Visual Studio's "Manage User Secrets" UI, or on the command line:
//   cd this-project-directory
//   dotnet user-secrets set AzureOpenAI:Endpoint https://YOUR-DEPLOYMENT-NAME.openai.azure.com
#if (!IsManagedIdentity)
//   dotnet user-secrets set AzureOpenAI:Key YOUR-API-KEY
#endif
var azureOpenAIEndpoint = new Uri(new Uri(builder.Configuration["AzureOpenAI:Endpoint"] ?? throw new InvalidOperationException("Missing configuration: AzureOpenAi:Endpoint. See the README for details.")), "/openai/v1");
#if (IsManagedIdentity)
#pragma warning disable OPENAI001 // OpenAIClient(AuthenticationPolicy, OpenAIClientOptions) and GetResponsesClient() are experimental and subject to change or removal in future updates.
var azureOpenAi = new OpenAIClient(
    new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default"),
    new OpenAIClientOptions { Endpoint = azureOpenAIEndpoint });

#elif (!IsManagedIdentity)
var openAIOptions = new OpenAIClientOptions { Endpoint = azureOpenAIEndpoint };
var azureOpenAi = new OpenAIClient(new ApiKeyCredential(builder.Configuration["AzureOpenAI:Key"] ?? throw new InvalidOperationException("Missing configuration: AzureOpenAi:Key. See the README for details.")), openAIOptions);

#pragma warning disable OPENAI001 // GetResponsesClient() is experimental and subject to change or removal in future updates.
#endif
var chatClient = azureOpenAi.GetResponsesClient().AsIChatClient("gpt-4o-mini");
#pragma warning restore OPENAI001

var embeddingGenerator = azureOpenAi.GetEmbeddingClient("text-embedding-3-small").AsIEmbeddingGenerator();
#endif

#if (IsAzureAISearch)
// You will need to set the endpoint and key to your own values
// You can do this using Visual Studio's "Manage User Secrets" UI, or on the command line:
//   cd this-project-directory
//   dotnet user-secrets set AzureAISearch:Endpoint https://YOUR-DEPLOYMENT-NAME.search.windows.net
#if (!IsManagedIdentity)
//   dotnet user-secrets set AzureAISearch:Key YOUR-API-KEY
#endif
var azureAISearchEndpoint = new Uri(builder.Configuration["AzureAISearch:Endpoint"]
    ?? throw new InvalidOperationException("Missing configuration: AzureAISearch:Endpoint. See the README for details."));
#if (IsManagedIdentity)
var azureAISearchCredential = new DefaultAzureCredential();
#elif (!IsManagedIdentity)
var azureAISearchCredential = new AzureKeyCredential(builder.Configuration["AzureAISearch:Key"]
    ?? throw new InvalidOperationException("Missing configuration: AzureAISearch:Key. See the README for details."));
#endif
builder.Services.AddAzureAISearchVectorStore(azureAISearchEndpoint, azureAISearchCredential);
builder.Services.AddAzureAISearchCollection<IngestedChunk>(IngestedChunk.CollectionName, azureAISearchEndpoint, azureAISearchCredential);
#elif (IsLocalVectorStore)
var vectorStorePath = Path.Combine(AppContext.BaseDirectory, "vector-store.db");
var vectorStoreConnectionString = $"Data Source={vectorStorePath}";
builder.Services.AddSqliteVectorStore(_ => vectorStoreConnectionString);
builder.Services.AddSqliteCollection<Guid, IngestedChunk>(IngestedChunk.CollectionName, vectorStoreConnectionString);
#endif

builder.Services.AddSingleton<DataIngestor>();
builder.Services.AddSingleton<SemanticSearch>();
builder.Services.AddKeyedSingleton("ingestion_directory", new DirectoryInfo(Path.Combine(builder.Environment.WebRootPath, "Data")));
builder.Services.AddChatClient(chatClient).UseFunctionInvocation().UseLogging();
builder.Services.AddEmbeddingGenerator(embeddingGenerator);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseStaticFiles();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

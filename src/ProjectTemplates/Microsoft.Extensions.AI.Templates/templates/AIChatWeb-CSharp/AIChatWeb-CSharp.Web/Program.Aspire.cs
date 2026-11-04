using Microsoft.Extensions.AI;
#if (IsOpenAI)
using OpenAI;
#endif
using AIChatWeb_CSharp.Web.Components;
using AIChatWeb_CSharp.Web.Services;
using AIChatWeb_CSharp.Web.Services.Ingestion;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

#if (IsOllama)
builder.AddOllamaApiClient("chat")
    .AddChatClient()
    .UseFunctionInvocation()
    .UseOpenTelemetry(configure: c =>
        c.EnableSensitiveData = builder.Environment.IsDevelopment());
builder.AddOllamaApiClient("embeddings")
    .AddEmbeddingGenerator();
#elif (IsAzureAIFoundry)
#else // (IsOpenAI || IsAzureOpenAI)
#if (IsOpenAI)
var openai = builder.AddOpenAIClient("openai");
#else
var openai = builder.AddAzureOpenAIClient("openai");
#endif
openai.AddChatClient("gpt-4o-mini")
    .UseFunctionInvocation()
    .UseOpenTelemetry(configure: c =>
        c.EnableSensitiveData = builder.Environment.IsDevelopment());
openai.AddEmbeddingGenerator("text-embedding-3-small");
#endif

#if (IsAzureAISearch)
builder.AddAzureSearchClient("search");
builder.Services.AddAzureAISearchVectorStore();
builder.Services.AddAzureAISearchCollection<IngestedChunk>(IngestedChunk.CollectionName);
#elif (IsQdrant)
builder.AddQdrantClient("vectordb");
builder.Services.AddQdrantVectorStore();
builder.Services.AddQdrantCollection<Guid, IngestedChunk>(IngestedChunk.CollectionName);
#else // IsLocalVectorStore
var vectorStorePath = Path.Combine(AppContext.BaseDirectory, "vector-store.db");
var vectorStoreConnectionString = $"Data Source={vectorStorePath}";
builder.Services.AddSqliteVectorStore(_ => vectorStoreConnectionString);
builder.Services.AddSqliteCollection<Guid, IngestedChunk>(IngestedChunk.CollectionName, vectorStoreConnectionString);
#endif
builder.Services.AddSingleton<DataIngestor>();
builder.Services.AddSingleton<SemanticSearch>();
builder.Services.AddKeyedSingleton("ingestion_directory", new DirectoryInfo(Path.Combine(builder.Environment.WebRootPath, "Data")));
#if (IsOllama)
// Applies robust HTTP resilience settings for all HttpClients in the Web project,
// not across the entire solution. It's aimed at supporting Ollama scenarios due
// to its self-hosted nature and potentially slow responses.
// Remove this if you want to use the global or a different HTTP resilience policy instead.
builder.Services.AddOllamaResilienceHandler();
#endif

var app = builder.Build();

app.MapDefaultEndpoints();

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

# Experimental decision abstractions

The `Microsoft.Extensions.AI.Abstractions` decision contracts provide a small provider-neutral Layer 1 capability for heterogeneous binary, choice, and ordinal-score questions. Requests preserve caller-owned question and candidate IDs and order. Responses retain complete probability observations, score precision, provenance, usage, and provider evidence without applying thresholds, calibration, probability repair, retry policy, or a joint-distribution promise.

## A business example

Suppose a retailer annotates customer-support tickets. The application wants a normal .NET category, a probability that the ticket is a refund request, and complete provider evidence for reporting or human review. It is not automatically authorizing a refund, choosing a route, or treating a probability as a calibrated business decision.

The domain types remain ordinary application-owned types:

```csharp
#pragma warning disable MEAI001

using System.ComponentModel;

public sealed record SupportTicket(string Message);

public enum TicketCategory
{
    [Description("Payment, invoice, or charge concerns.")]
    Billing,

    [Description("Product defects, errors, outages, or troubleshooting.")]
    Technical,

    [Description("Sign-in, profile, or account-access concerns.")]
    Account,
}

public sealed record TicketAnalysis(
    [Description("Classify the customer's main concern.")] TicketCategory Category,
    [Description("Is the customer requesting a refund or payment reversal?")] double RefundRequestProbability);
```

The host explicitly supplies an `IDecisionClient`. It is provider-neutral; MEAI does not create a default implementation and an arbitrary `IChatClient` is not automatically a decision client. Provider acquisition is intentionally separate from platform composition: the provider owner supplies the configured client entry point, while these examples consume only the interface. Jev/OllamaSharp and the Ollaya, Laya, Julia, and Qwen adapters remain provider-owned and are not shipped, repinned, or executed by this repository. A deterministic test double is used for the platform proofs.

An external, unpublished Julia/MEAI bridge was used as historical implementation evidence. It is not a released dependency or a runnable setup promised by this README. The provider owner's desired native shape is a separate, prospective entry point such as `JuliaDecisionClient.LoadFromDirectory(assetDirectory)`; that client is not implemented or available in this package.

The reusable typed declaration is ordinary application code:

```csharp
DecisionDefinition<TicketAnalysis> definition =
    DecisionDefinition<TicketAnalysis>.Create(builder =>
    {
        builder.Choice(result => result.Category);
        builder.BinaryProbability(result => result.RefundRequestProbability);
    });

SupportTicket ticket = new(
    "I was charged twice. Please refund the duplicate payment.");

DecisionResponse<TicketAnalysis> response =
    await client.GetResponseAsync(ticket, definition);

TicketAnalysis analysis = response.Result;
DecisionResponse evidence = response.Evidence;
```

`response.Evidence` contains the complete neutral response, including every category probability and the binary observation. It is separate from any chat usage or business action. The mapped result is not a hidden threshold or calibration claim.

For an explicit JSON boundary, supply source-generated metadata to both state and result contracts:

```csharp
DecisionDefinition<TicketAnalysis> metadataDefinition =
    DecisionDefinition<TicketAnalysis>.Create(
        TicketJsonContext.Default.TicketAnalysis,
        builder =>
        {
            builder.Choice(result => result.Category);
            builder.BinaryProbability(result => result.RefundRequestProbability);
        });

DecisionResponse<TicketAnalysis> metadataResponse =
    await client.GetResponseAsync(
        ticket,
        TicketJsonContext.Default.SupportTicket,
        metadataDefinition);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SupportTicket))]
[JsonSerializable(typeof(TicketCategory))]
[JsonSerializable(typeof(TicketAnalysis))]
public partial class TicketJsonContext : JsonSerializerContext;
```

`JsonTypeInfo<T>` describes a serialized state or result shape. It does not replace decision instructions, exact IDs, complete distributions, or stale-domain checks. JSON enum naming controls candidate IDs, while decision correlation remains exact and rejects unknown or changed IDs.

## Layer 2 interoperability: two independent paths

Layer 2 keeps function composition and chat routing independent. The function examples use either the stock `AIFunctionFactory` directly or the thin definition-based `AsAIFunction` convenience member. Neither path discovers a provider or takes a client from model-supplied arguments.

### 1. Annotated application method to `AIFunctionFactory`

An ordinary annotated method can close over a host-injected client and definition. The client, definition, endpoint, model, and options are not model-supplied tool arguments:

```csharp
// Inside an existing application type, these are ordinary host-owned fields:
// IDecisionClient client;
// DecisionDefinition<TicketAnalysis> definition;
// Action<DecisionResponse>? recordEvidence;

[DisplayName("analyze_support_ticket")]
[Description("Annotate one support ticket.")]
public async Task<TicketAnalysis> AnalyzeSupportTicketAsync(
    [Description("The ticket to classify.")] SupportTicket ticket,
    CancellationToken cancellationToken)
{
    DecisionResponse<TicketAnalysis> response = await client.GetResponseAsync(
        ticket,
        definition,
        cancellationToken: cancellationToken);

    recordEvidence?.Invoke(response.Evidence);
    return response.Result;
}

AIFunction analyzeTicket = AIFunctionFactory.Create(
    AnalyzeSupportTicketAsync,
    new AIFunctionFactoryOptions
    {
        Name = "analyze_support_ticket",
        Description = "Annotate a support ticket for review.",
    });

IChatClient chatClient = configuredChatClient
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

ChatResponse chatResponse = await chatClient.GetResponseAsync(
    messages,
    new ChatOptions { Tools = [analyzeTicket] },
    cancellationToken);
```

The result-only method returns the business result by default. `recordEvidence` is an explicit host-owned per-invocation callback; it is not a shared last-response slot or automatic analytics. If selected metadata is useful, define an application DTO and return only the mapped result plus `response.Evidence.Provenance` and `response.Evidence.Usage`. Do not return raw state, provider extensions, credentials, reasoning text, or unrestricted dictionaries. Each invocation performs one logical decision operation, cancellation and provider/mapper/host failures propagate, and decision usage remains separate from chat usage. The callback must retain or process each invocation itself; the platform does not retain a last response.

`AIFunctionFactory` options can override method attributes. Its schema contains only the application input (`ticket` here); `client`, `definition`, and `CancellationToken` are infrastructure, not model-facing parameters. The middleware example above exercises normal function invocation; direct `AIFunction.InvokeAsync` is sufficient for a platform-only proof and should not be described as a chat-loop proof.

### 1a. Definition shorthand

When the application does not need a custom evidence callback, the definition can create the same ordinary `AIFunction` directly. The generic state type is explicit, while the result type remains the `TResult` from the definition:

```csharp
AIFunction analyzeTicket = definition.AsAIFunction<SupportTicket>(
    client,
    new AIFunctionFactoryOptions
    {
        Name = "analyze_support_ticket",
        Description = "Annotate one support ticket for review.",
        SerializerOptions = TicketJsonContext.Default.Options,
    });
```

The helper exposes only one model-facing `state` parameter and returns `TicketAnalysis`. It does not return `DecisionResponse<TicketAnalysis>`, evidence, provenance, usage, raw provider data, or a credentials/options object. Use the annotated-method path when the host needs an explicit per-invocation evidence callback.

For reflection-disabled or trimmed deployments, provide source-generated metadata at both boundaries. The state contract can also provide genuine generic input inference:

```csharp
AIFunction generatedAnalyzeTicket = definition.AsAIFunction(
    client,
    functionOptions: new AIFunctionFactoryOptions
    {
        SerializerOptions = TicketJsonContext.Default.Options,
    },
    stateTypeInfo: TicketJsonContext.Default.SupportTicket);
```

`TicketJsonContext.Default.Options` covers both `SupportTicket` and `TicketAnalysis`; `stateTypeInfo` supplies the `TState` contract while `TResult` comes from `definition`. This is source-generation-compatible validation, not a Native AOT certification. Standard factory options remain standard: name and description overrides, schema options, parameter binding, result marshalling, additional properties, and result-schema exclusion are passed through. `DecisionOptions`, when supplied, are copied during construction and cloned again for each invocation. The clone is deliberately shallow: the options dictionary is copied, but nested values and a raw-options factory remain caller/provider-owned.

When registering a function with DI, resolve the caller-owned decision client once while constructing the ordinary function. Do not resolve services from model-supplied arguments or from an ambient service provider during invocation:

```csharp
services.AddScoped<AIFunction>(serviceProvider =>
{
    IDecisionClient decisions =
        serviceProvider.GetRequiredService<IDecisionClient>();

    return definition.AsAIFunction<SupportTicket>(
        decisions,
        new AIFunctionFactoryOptions
        {
            Name = "analyze_support_ticket",
            SerializerOptions = TicketJsonContext.Default.Options,
        });
});
```

The registration's lifetime must be compatible with the registered client. The helper never disposes the client; the owner (for example, the DI scope) does.

### 2. Decision-driven chat routing

Routing is a separate provider/client-selection problem. Construct one provider-neutral decision client and multiple configured, caller-owned `IChatClient` instances. Project meaningful bounded request state, use a typed `DecisionDefinition`, and compose with the existing `RoutingChatClient.Create`:

```csharp
public enum RouteKind
{
    [Description("Fast: use for short, routine requests where lower latency is preferred and deep multi-step reasoning is not needed.")]
    Fast,

    [Description("Reasoning: use for multi-step analysis, comparisons, or requests that benefit from deeper deliberation.")]
    Reasoning,
}

public sealed record RoutingDecision(RouteKind Route);
public sealed record RoutingMessage(string Role, string Text);
public sealed record RoutingState(IReadOnlyList<RoutingMessage> Messages, bool HasTools);

DecisionDefinition<RoutingDecision> routeDefinition =
    DecisionDefinition<RoutingDecision>.Create(
        RoutingJsonContext.Default.RoutingDecision,
        builder => builder.Choice(
            result => result.Route,
            "Choose Fast for short, routine requests where latency is the priority. " +
            "Choose Reasoning for multi-step analysis, comparisons, or requests that benefit from deeper deliberation."));

// Each provider-owned client must enforce its route identity on every request:
// Fast -> "fast-model"; Reasoning -> "reasoning-model". A provider's default
// model ID alone is not sufficient because RoutingChatClient forwards a clone
// of caller ChatOptions, including ModelId.
IReadOnlyDictionary<RouteKind, IChatClient> clients =
    new Dictionary<RouteKind, IChatClient>
    {
        [RouteKind.Fast] = fastConfiguredClient,
        [RouteKind.Reasoning] = reasoningConfiguredClient,
    };

RoutingChatClient router = RoutingChatClient.Create(
    async (context, cancellationToken) =>
    {
        const int maxMessages = 32;
        const int maxMessageTextCharacters = 4_096;
        const int maxTotalTextCharacters = 64_000;
        List<RoutingMessage> projectedMessages = [];
        int totalTextCharacters = 0;
        foreach (ChatMessage message in context.Messages)
        {
            if (projectedMessages.Count == maxMessages)
            {
                throw new InvalidOperationException(
                    $"Route selection accepts at most {maxMessages} messages; no history is truncated.");
            }

            if (message.Role != ChatRole.System &&
                message.Role != ChatRole.User &&
                message.Role != ChatRole.Assistant &&
                message.Role != ChatRole.Tool)
            {
                throw new NotSupportedException(
                    $"Role '{message.Role}' is not supported by this route selector.");
            }

            if (message.Contents.Any(static content => content is not TextContent))
            {
                throw new NotSupportedException(
                    "This route selector accepts text content only; no content may be silently dropped.");
            }

            if (message.Text.Length > maxMessageTextCharacters ||
                totalTextCharacters > maxTotalTextCharacters - message.Text.Length)
            {
                throw new InvalidOperationException(
                    "Route selection text budgets were exceeded; no text is truncated.");
            }

            projectedMessages.Add(new(message.Role.Value, message.Text));
            totalTextCharacters += message.Text.Length;
        }

        RoutingState state = new(
            projectedMessages,
            context.ChatOptions?.Tools is not null);

        DecisionResponse<RoutingDecision> decision =
            await decisionClient.GetResponseAsync(
                state,
                RoutingJsonContext.Default.RoutingState,
                routeDefinition,
                cancellationToken: cancellationToken);

        if (!clients.TryGetValue(decision.Result.Route, out IChatClient? selected))
        {
            throw new InvalidOperationException("The decision selected an unconfigured route.");
        }

        return selected;
    });
```

Each configured client must enforce its route-specific model identity and options on every forwarded call (for example, by overwriting a caller-supplied `ChatOptions.ModelId` in the provider-owned client adapter or rejecting a conflicting value); merely setting a provider default is not enough. The router forwards the original messages and a cloned request-options object to exactly one selected client, including streaming calls. The caller owns the configured clients and remains responsible for disposing them; the router does not dispose borrowed clients. Invalid or wrong-domain decision responses, cancellation, and provider failures stop before chat forwarding. There is no retry, failover, conversation affinity, mid-stream switching, outcome learning, or automatic calibration. Routing selects before each ordinary or streaming call. If function middleware wraps the router, a later tool-loop round can re-enter the router; if a selected configured client owns its own loop, that client remains selected. This example does not add either behavior automatically. Decision probabilities are task observations, not calibrated task-success guarantees.

### Semantic ingestion proof

The MEDI proof remains consumer-owned and uses the existing document-reader -> chunker -> semantic decision chunk processor -> collecting/serializing writer flow. It preserves chunk content, document identity/context, unrelated metadata, bounded streaming, cancellation, and stable feature schema identity/version/provenance. It does not add `IngestionDocument.Metadata`, alter `ClassificationEnricher` or batching, fabricate chat responses, or add an upstream ML.NET/Arrow dependency.

## Alternative Designs

- A decision-specific `AIFunction` wrapper was removed: ordinary annotated methods plus `AIFunctionFactory.Create` already provide the safe schema, host closure, cancellation, and standard middleware composition.
- A reusable decision router was not added: typed `DecisionDefinition` plus `RoutingChatClient.Create` is enough for application-specific eligibility and client maps.
- A reusable MEDI processor was not added: the real pipeline proof does not establish a separately justified public ingestion API.

## Risks and limitations

These contracts are experimental (`MEAI001`). Hosts own client lifetime, safe output DTOs, evidence retention, route policy, and business actions. Provider adapters and external proof packages are not shipped or repinned here. No live provider/model execution, package-release availability, Native AOT certification, calibration guarantee, or universal provider accuracy is claimed.

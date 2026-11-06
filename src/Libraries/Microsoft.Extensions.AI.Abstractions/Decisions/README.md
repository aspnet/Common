# Experimental decision abstractions

The `Microsoft.Extensions.AI.Abstractions` decision contracts provide a small provider-neutral proposed Layer 1 capability for heterogeneous binary, choice, and ordinal-score questions. Requests preserve caller-owned question and candidate IDs and order; responses preserve complete probability observations, score precision, provenance, usage, and provider extensions without applying thresholds, calibration, probability repair, retry policy, or a joint-distribution promise.

## A business example

Suppose a retailer annotates customer-support tickets. The application wants a normal .NET category, a probability that the ticket is a refund request, and the complete provider evidence for reporting, human review, or downstream training. It is not automatically authorizing a refund, choosing a route, or treating a probability as a calibrated business decision.

The domain types remain ordinary application-owned types. The standard `DescriptionAttribute` on the positional record parameters supplies business instructions for the selected result properties, while the enum members supply separate candidate criteria:

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

The business call is ordinary application code. Assume the host supplies the `IDecisionClient client`; the declaration is reusable and explicit. `Choice` declares the closed enum domain and uses its configured JSON enum names as exact candidate IDs; `BinaryProbability` maps the binary answer's `TrueProbability` to a `double`. No handwritten question IDs, enum dictionary, result mapper, JSON schema, or feature schema is required for this ordinary flow:

```csharp
using Microsoft.Extensions.AI;

var definition = DecisionDefinition<TicketAnalysis>.Create(builder =>
{
    builder.Choice(result => result.Category);
    builder.BinaryProbability(result => result.RefundRequestProbability);
});

var ticket = new SupportTicket(
    "I was charged twice. Please refund the duplicate payment.");

var response = await client.GetResponseAsync(ticket, definition);

TicketAnalysis analysis = response.Result;
DecisionResponse evidence = response.Evidence;
```

When the complete choice evidence is useful for reporting or review, the typed distribution remains available:

```csharp
var categoryDistribution = response.GetDistribution(value => value.Category);
```

The same definition can include an explicitly ordered score rubric, and heterogeneous declarations remain independent:

```csharp
var definitionWithScore = DecisionDefinition<TicketAnalysis>.Create(builder =>
{
    builder.Choice(result => result.Category, "Classify the customer's main concern.");
    builder.BinaryProbability(result => result.RefundRequestProbability);
    // A score property would be declared with an explicit DecisionScoreLevel list:
    // builder.Score(result => result.Satisfaction, [new("low", "Low"), new("high", "High")]);
    // builder.ExpectedScore(result => result.Satisfaction, [new("low", "Low"), new("high", "High")]);
});
```

`Score` projects only a provider-reported native ordinal score and fails explicitly when that scalar is absent. `ExpectedScore` projects the unmodified ordinal expectation calculated from the complete observed distribution, so the two choices remain distinguishable; after probability rounding, that observed expectation can fall outside the ordinal bounds and is not repaired. Every probability is conditioned on the full request state and question set; declaring questions independently does not promise marginal invariance, independence, a joint distribution, or calibration.

Reported scalar consistency uses normalized latent feasibility within clipped rounding intervals; the observed probabilities are never normalized or rewritten. Typed binding verifies the actual CLR property values after materialization, independently of write-only JSON formatting. When options omit a resolver, the definition supplies the MEAI default resolver, while explicit caller resolvers and converters remain unchanged.

At an explicit JSON boundary, callers can use source-generated metadata for both state and result contracts. The explicit generic arguments are optional for inference, but make the boundary visible:

```csharp
var metadataDefinition =
    DecisionDefinition<TicketAnalysis>.Create(TicketJsonContext.Default.TicketAnalysis, builder =>
    {
        builder.Choice(result => result.Category);
        builder.BinaryProbability(result => result.RefundRequestProbability);
    });

var metadataResponse = await client.GetResponseAsync<SupportTicket, TicketAnalysis>(
    ticket,
    TicketJsonContext.Default.SupportTicket,
    metadataDefinition);
```

When the application already has a JSON state snapshot, the result-only overload keeps the same local binding behavior without re-serializing the state:

```csharp
using System.Text.Json;

JsonElement stateJson = JsonSerializer.SerializeToElement(
    ticket,
    TicketJsonContext.Default.SupportTicket);
var analyzedFromJson = await client.GetResponseAsync<TicketAnalysis>(
    stateJson,
    metadataDefinition);
```

Applications that only need the neutral response can keep the no-binding path:

```csharp
var evidenceOnly = await client.GetResponseAsync(
    ticket,
    TicketJsonContext.Default.SupportTicket,
    metadataDefinition.Questions);
```

The JSON metadata setup is separate from the business call:

```csharp
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(SupportTicket))]
[JsonSerializable(typeof(TicketCategory))]
[JsonSerializable(typeof(TicketAnalysis))]
public partial class TicketJsonContext : JsonSerializerContext;
```

`TicketCategory` is a normal caller-owned enum. `DecisionDefinition<TicketAnalysis>` is an explicit declaration of which result members participate; it is not an enum generator, ORM, arbitrary POCO/union inference engine, or LINQ query provider. The candidate IDs come from the configured JSON enum contract and are exact ordinal wire-correlation identities. They are stable simple strings reused through the question definition; no pipe/version syntax is required. If a domain needs dynamic IDs instead of a closed enum, use the lower-level `ChoiceDecisionQuestion` and explicit mapping APIs so stale or unknown IDs remain rejected.

Attributes are an ergonomic fallback for instructions and enum candidate descriptions. A fluent instruction supplied to `Choice`, `BinaryProbability`, or `Score` wins over an attribute. The metadata reader checks an attributed property first, then an associated positional-record constructor parameter, then a type-level fallback. JSON enum serialization controls the candidate IDs, but JSON conversion's permissive/case-insensitive behavior does not replace exact decision correlation validation.

`JsonTypeInfo<T>` describes the serialized shape of state or a result. It may come from source generation or a configured reflection resolver; it is not handwritten JSON schema and it does not replace decision instructions, exact IDs, complete distributions, or stale-domain checks. The older `DecisionResultBinding<T>` API still requires result metadata even though a delegate mapper may not use it; simplifying that requirement is an API review candidate, not an implicit behavior of this convenience layer.

The returned `Evidence` is the complete `DecisionResponse`, including every category probability and the binary observation. The mapped `TicketAnalysis` is therefore not a claim that a hidden threshold or calibration step established a business fact. Applications can retain or inspect the complete evidence for reporting, review, or later training. Feature projection is an optional follow-on: callers may define a versioned `DecisionFeatureSchema` of named native-`double` coordinates over complete observations when a downstream feature export is useful. That schema is a downstream column/coordinate contract, distinct from JSON metadata and not a prerequisite for typed classification.

## .NET platform reuse / ergonomics under review

Layer 1 intentionally reuses existing .NET and MEAI primitives rather than adding `IQueryable`, an ORM, or a new source-generator dependency:

- [`JsonSchemaExporter`](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/extract-schema) was introduced in .NET 9. The decision contracts use `JsonTypeInfo` and serializer options for explicit metadata; they do not add another schema exporter, and `AIJsonUtilities` provides the existing MEAI compatibility helpers where applicable.
- [System.Text.Json source generation](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation) is the AOT-friendly way to supply `JsonTypeInfo`. `JsonTypeInfo` itself is not synonymous with generated code; ordinary JIT reflection through a configured resolver remains a platform option.
- [`JsonStringEnumMemberNameAttribute`](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/customize-properties#custom-enum-member-names) and the generic [`JsonStringEnumConverter`](https://learn.microsoft.com/dotnet/api/system.text.json.serialization.jsonstringenumconverter-1) are useful for JSON enum representation. They do not replace decision ID validation: enum conversion is case-insensitive and permits integer values by default, while decision IDs are exact ordinal identities.
- [`AIFunctionFactory`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.aifunctionfactory) remains the existing MEAI primitive for inspecting an explicitly declared delegate, producing a tool schema, and marshalling inputs and outputs. Tool interoperability is a later layer, not a prerequisite for this decision foundation.
- [Structured output](https://learn.microsoft.com/dotnet/ai/quickstarts/structured-output) already supports enum/record-shaped chat results. If an application only needs a label or JSON result, `IChatClient.GetResponseAsync<T>` may be the simpler choice; that path does not by itself establish a provider-reported probability distribution.
- [EF Core model conventions and explicit overrides](https://learn.microsoft.com/ef/core/modeling/) are useful precedent for bounded opt-in conventions, not a reason to add an EF dependency or `DbContext`. [EF Core query providers](https://learn.microsoft.com/ef/core/querying/) likewise do not justify an `IQueryable` decision API for explicit paid asynchronous model inference; ordinary LINQ projection over returned probabilities remains appropriate.

Automated arbitrary semantic inference is out. This layer already uses bounded opt-in conventions for JSON enum naming and standard member descriptions; future conventions for known enum/member mappings remain reasonable only when explicit, source-generation/AOT-compatible, and unable to hide provider-facing identities or application-owned result mapping.

Provider evidence for the package-level contract is bounded and documented in the [upstream provider-validation report](https://github.com/luisquintanilla/typesafe-meai/blob/13adb58c7d7479bc8c73bf31960c08349b7c3185/docs/upstream-provider-validation.md): Julia/Laya provide CPU binary/choice/score proof, Qwen provides choice-only proof, and the dedicated `tev1:0.8b` Ollama/OllamaSharp path provides live binary/choice/score proof. Other adapters use native HTTP fixtures rather than live executions; this is not a claim of universal provider accuracy.

This layer intentionally does not define tools, routing composition, MEDI processors, provider adapters, ML.NET or Arrow integrations, or automatic arbitrary POCO/union semantic inference. AIFunction/tool interoperability is a Layer 2 concern and is not implied by these abstractions.

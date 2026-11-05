// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.AI;

// Shared source-generated JsonSerializerContext for the OpenTelemetry* clients.
// Registers the union of all OTel message-part types serialized by both OpenTelemetryChatClient
// and OpenTelemetryRealtimeClientSession.

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IList<object>))]

// Shared types (Common/OtelMessageParts.cs)
[JsonSerializable(typeof(OtelGenericPart))]
[JsonSerializable(typeof(OtelBlobPart))]
[JsonSerializable(typeof(OtelUriPart))]
[JsonSerializable(typeof(OtelFilePart))]
[JsonSerializable(typeof(OtelToolCallResponsePart))]
[JsonSerializable(typeof(IEnumerable<OtelFunction>))]

// Chat-specific
[JsonSerializable(typeof(OtelMessage))]
[JsonSerializable(typeof(OtelToolCallRequestPart))]
[JsonSerializable(typeof(OtelServerToolCallPart<OtelCodeInterpreterToolCall>))]
[JsonSerializable(typeof(OtelServerToolCallResponsePart<OtelCodeInterpreterToolCallResponse>))]
[JsonSerializable(typeof(OtelServerToolCallPart<OtelImageGenerationToolCall>))]
[JsonSerializable(typeof(OtelServerToolCallResponsePart<OtelImageGenerationToolCallResponse>))]
[JsonSerializable(typeof(OtelServerToolCallPart<OtelMcpToolCall>))]
[JsonSerializable(typeof(OtelServerToolCallResponsePart<OtelMcpToolCallResponse>))]
[JsonSerializable(typeof(OtelServerToolCallPart<OtelMcpApprovalRequest>))]
[JsonSerializable(typeof(OtelServerToolCallResponsePart<OtelMcpApprovalResponse>))]

// Realtime-specific
[JsonSerializable(typeof(IEnumerable<RealtimeOtelMessage>))]
[JsonSerializable(typeof(RealtimeOtelMessage))]
[JsonSerializable(typeof(RealtimeOtelToolCallPart))]
internal sealed partial class OtelContext : JsonSerializerContext;

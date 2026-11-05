// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.AI;

#pragma warning disable S4041 // Type names should not match namespaces

/// <summary>Provides constants used by various telemetry services.</summary>
internal static class OpenTelemetryConsts
{
    public const string DefaultSourceName = "Experimental.Microsoft.Extensions.AI";

    public const string SecondsUnit = "s";
    public const string TokensUnit = "{token}";

    /// <summary>Environment variable name for controlling whether sensitive content should be captured in telemetry by default.</summary>
    public const string GenAICaptureMessageContentEnvVar = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";

    public const string ToolTypeFunction = "function";

    public const string TypeText = "text";
    public const string TypeJson = "json";
    public const string TypeImage = "image";
    public const string TypeAudio = "audio";

    public const string TokenTypeInput = "input";
    public const string TokenTypeOutput = "output";
    public const string TokenTypeInputAudio = "input_audio";
    public const string TokenTypeInputText = "input_text";
    public const string TokenTypeOutputAudio = "output_audio";
    public const string TokenTypeOutputText = "output_text";

    public static class Error
    {
        public const string Type = "error.type";
    }

    public static class GenAI
    {
        public const string ChatName = "chat";
        public const string EmbeddingsName = "embeddings";
        public const string ExecuteToolName = "execute_tool";
        public const string InvokeAgentName = "invoke_agent";
        public const string InvokeWorkflowName = "invoke_workflow";
        public const string OrchestrateToolsName = "orchestrate_tools"; // Non-standard
        public const string GenerateContentName = "generate_content";

        /// <summary>
        /// Operation name for realtime sessions.
        /// </summary>
        /// <remarks>
        /// This is a custom extension that is not part of the OpenTelemetry GenAI semantic conventions.
        /// The spec allows using custom values for <c>gen_ai.operation.name</c> when standard values do not apply.
        /// </remarks>
        public const string RealtimeName = "realtime";

        public const string SystemInstructions = "gen_ai.system_instructions";

        public static class Client
        {
            public static class OperationDuration
            {
                public const string Description = "Measures the duration of a GenAI operation";
                public const string Name = "gen_ai.client.operation.duration";
                public static readonly double[] ExplicitBucketBoundaries = [0.01, 0.02, 0.04, 0.08, 0.16, 0.32, 0.64, 1.28, 2.56, 5.12, 10.24, 20.48, 40.96, 81.92];
            }

            public static class TokenUsage
            {
                public const string Description = "Measures number of input and output tokens used";
                public const string Name = "gen_ai.client.token.usage";
                public static readonly int[] ExplicitBucketBoundaries = [1, 4, 16, 64, 256, 1_024, 4_096, 16_384, 65_536, 262_144, 1_048_576, 4_194_304, 16_777_216, 67_108_864];
            }

            public static class TimeToFirstChunk
            {
                public const string Description = "Measures the time to receive the first chunk in a streaming operation";
                public const string Name = "gen_ai.client.operation.time_to_first_chunk";
                public static readonly double[] ExplicitBucketBoundaries = OperationDuration.ExplicitBucketBoundaries;
            }

            public static class TimePerOutputChunk
            {
                public const string Description = "Measures the time per output chunk in a streaming operation";
                public const string Name = "gen_ai.client.operation.time_per_output_chunk";
                public static readonly double[] ExplicitBucketBoundaries = OperationDuration.ExplicitBucketBoundaries;
            }
        }

        public static class Conversation
        {
            public const string Id = "gen_ai.conversation.id";
        }

        public static class Embeddings
        {
            public static class Dimension
            {
                public const string Count = "gen_ai.embeddings.dimension.count";
            }
        }

        public static class Input
        {
            public const string Messages = "gen_ai.input.messages";
        }

        public static class Operation
        {
            public const string Name = "gen_ai.operation.name";
        }

        public static class Output
        {
            public const string Messages = "gen_ai.output.messages";
            public const string Type = "gen_ai.output.type";
        }

        public static class Provider
        {
            public const string Name = "gen_ai.provider.name";
        }

        public static class Request
        {
            public const string ChoiceCount = "gen_ai.request.choice.count";
            public const string FrequencyPenalty = "gen_ai.request.frequency_penalty";
            public const string Model = "gen_ai.request.model";
            public const string MaxTokens = "gen_ai.request.max_tokens";
            public const string PresencePenalty = "gen_ai.request.presence_penalty";
            public const string Seed = "gen_ai.request.seed";
            public const string StopSequences = "gen_ai.request.stop_sequences";
            public const string Stream = "gen_ai.request.stream";
            public const string Temperature = "gen_ai.request.temperature";
            public const string TopK = "gen_ai.request.top_k";
            public const string TopP = "gen_ai.request.top_p";
        }

        public static class Response
        {
            public const string FinishReasons = "gen_ai.response.finish_reasons";
            public const string Id = "gen_ai.response.id";
            public const string Model = "gen_ai.response.model";
            public const string TimeToFirstChunk = "gen_ai.response.time_to_first_chunk";
        }

        public static class Token
        {
            public const string Type = "gen_ai.token.type";
        }

        public static class Tool
        {
            public const string Name = "gen_ai.tool.name";
            public const string Description = "gen_ai.tool.description";
            public const string Message = "gen_ai.tool.message";
            public const string Type = "gen_ai.tool.type";
            public const string Definitions = "gen_ai.tool.definitions";

            public static class Call
            {
                public const string Id = "gen_ai.tool.call.id";
                public const string Arguments = "gen_ai.tool.call.arguments";
                public const string Result = "gen_ai.tool.call.result";
            }
        }

        public static class Usage
        {
            public const string InputTokens = "gen_ai.usage.input_tokens";
            public const string OutputTokens = "gen_ai.usage.output_tokens";
            public const string CacheReadInputTokens = "gen_ai.usage.cache_read.input_tokens";
            public const string InputAudioTokens = "gen_ai.usage.input_audio_tokens";
            public const string InputTextTokens = "gen_ai.usage.input_text_tokens";
            public const string OutputAudioTokens = "gen_ai.usage.output_audio_tokens";
            public const string OutputTextTokens = "gen_ai.usage.output_text_tokens";
            public const string ReasoningOutputTokens = "gen_ai.usage.reasoning.output_tokens";
        }

        /// <summary>
        /// Custom attributes for realtime sessions.
        /// These attributes are NOT part of the OpenTelemetry GenAI semantic conventions (as of v1.41).
        /// They are custom extensions to capture realtime session-specific configuration.
        /// </summary>
        public static class Realtime
        {
            /// <summary>
            /// The voice used for audio output in a realtime session.
            /// </summary>
            /// <remarks>
            /// Custom attribute: <c>gen_ai.realtime.voice</c>.
            /// </remarks>
            public const string Voice = "gen_ai.realtime.voice";

            /// <summary>
            /// The output modalities configured for a realtime session (for example, <c>"Text"</c> and <c>"Audio"</c>).
            /// </summary>
            /// <remarks>
            /// Custom attribute: <c>gen_ai.realtime.output_modalities</c>.
            /// </remarks>
            public const string OutputModalities = "gen_ai.realtime.output_modalities";

            /// <summary>
            /// The kind or type of realtime session (for example, <c>"TextInTextOut"</c> and <c>"AudioInAudioOut"</c>).
            /// </summary>
            /// <remarks>
            /// Custom attribute: <c>gen_ai.realtime.session_kind</c>.
            /// </remarks>
            public const string SessionKind = "gen_ai.realtime.session_kind";

            /// <summary>
            /// The modalities actually received in a realtime response (for example, <c>"text"</c>, <c>"audio"</c>, and <c>"transcription"</c>).
            /// </summary>
            /// <remarks>
            /// Custom attribute: <c>gen_ai.realtime.received_modalities</c>.
            /// </remarks>
            public const string ReceivedModalities = "gen_ai.realtime.received_modalities";
        }
    }

    public static class File
    {
        public const string Name = "file.name";
        public const string Size = "file.size";
    }

    public static class Server
    {
        public const string Address = "server.address";
        public const string Port = "server.port";
    }
}

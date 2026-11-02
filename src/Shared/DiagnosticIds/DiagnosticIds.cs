// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma warning disable S1144 // Remove the unused internal class

#pragma warning disable CA1716
namespace Microsoft.Shared.DiagnosticIds;
#pragma warning restore CA1716

/// <summary>
///  Various diagnostic IDs reported by this repo.
/// </summary>
/// <remarks>
///  When adding a new diagnostic ID, add a corresponding suppression to the root <c>Directory.Build.targets</c> file,
///  and add a documentation entry to <c>docs/list-of-diagnostics.md</c>.
/// </remarks>
internal static class DiagnosticIds
{
#pragma warning disable S1075 // URIs should not be hardcoded
    internal const string UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}";
#pragma warning restore S1075 // URIs should not be hardcoded

    internal static class ContextualOptions
    {
        internal const string CTXOPTGEN000 = nameof(CTXOPTGEN000);
        internal const string CTXOPTGEN001 = nameof(CTXOPTGEN001);
        internal const string CTXOPTGEN002 = nameof(CTXOPTGEN002);
        internal const string CTXOPTGEN003 = nameof(CTXOPTGEN003);
    }

    /// <summary>
    ///  Experiments supported by this repo.
    /// </summary>
    internal static class Experiments
    {
        internal const string Resilience = "EXTEXP0001";
        internal const string Compliance = "EXTEXP0002";
        internal const string Telemetry = "EXTEXP0003";
        internal const string TimeProvider = "EXTEXP0004";
        internal const string AsyncState = "EXTEXP0006";
        internal const string HealthChecks = "EXTEXP0007";
        internal const string ResourceMonitoring = "EXTEXP0008";
        internal const string Hosting = "EXTEXP0009";
        internal const string ObjectPool = "EXTEXP0010";
        internal const string DocumentDb = "EXTEXP0011";
        internal const string AutoActivation = "EXTEXP0012";
        internal const string HttpLogging = "EXTEXP0013";
        internal const string HybridCache = "EXTEXP0018";

        // All AI experiments share a diagnostic ID but have different
        // constants to manage which experiment each API belongs to.
        internal const string AIImageGeneration = AIExperiments;
        internal const string AISpeechToText = AIExperiments;
        internal const string AITextToSpeech = AIExperiments;
        internal const string AIMcpServers = AIExperiments;
        internal const string AIFunctionApprovals = AIExperiments;
        internal const string AIApprovalsInvocationRequired = AIExperiments;
        internal const string AIFunctionAndParameterName = AIExperiments;

        internal const string AIChatReduction = AIExperiments;
        internal const string AIToolSearch = AIExperiments;
        internal const string AIRealTime = AIExperiments;
        internal const string AIFiles = AIExperiments;
        internal const string AIOpenAIRequestPolicies = AIExperiments;
        internal const string AIRoutingChat = AIExperiments;

        // These diagnostic IDs are defined by the OpenAI package for its experimental APIs.
        // We use the same IDs so consumers do not need to suppress additional diagnostics
        // when using the experimental OpenAI APIs.
        internal const string AIOpenAIResponses = "OPENAI001";
        internal const string AIOpenAIImageClient = "OPENAI001";
        internal const string AIOpenAIAudio = "OPENAI001";
        internal const string AIOpenAIReasoning = "OPENAI001";
        internal const string AIOpenAIRealtime = "OPENAI002";
        internal const string AIOpenAIJsonPatch = "SCME0001";

        // All Vector Data experiments share a diagnostic ID but have different
        // constants to manage which experiment each API belongs to.
        internal const string VectorDataProviderServices = VectorDataExperiments;

        private const string AIExperiments = "MEAI001";
        private const string VectorDataExperiments = "MEVD9001";
    }

    internal static class LoggerMessage
    {
        internal const string LOGGEN000 = nameof(LOGGEN000);
        internal const string LOGGEN001 = nameof(LOGGEN001);
        internal const string LOGGEN002 = nameof(LOGGEN002);
        internal const string LOGGEN003 = nameof(LOGGEN003);
        internal const string LOGGEN004 = nameof(LOGGEN004);
        internal const string LOGGEN005 = nameof(LOGGEN005);
        internal const string LOGGEN006 = nameof(LOGGEN006);
        internal const string LOGGEN007 = nameof(LOGGEN007);
        internal const string LOGGEN008 = nameof(LOGGEN008);
        internal const string LOGGEN009 = nameof(LOGGEN009);
        internal const string LOGGEN010 = nameof(LOGGEN010);
        internal const string LOGGEN011 = nameof(LOGGEN011);
        internal const string LOGGEN012 = nameof(LOGGEN012);
        internal const string LOGGEN013 = nameof(LOGGEN013);
        internal const string LOGGEN014 = nameof(LOGGEN014);
        internal const string LOGGEN015 = nameof(LOGGEN015);
        internal const string LOGGEN016 = nameof(LOGGEN016);
        internal const string LOGGEN017 = nameof(LOGGEN017);
        internal const string LOGGEN018 = nameof(LOGGEN018);
        internal const string LOGGEN019 = nameof(LOGGEN019);
        internal const string LOGGEN020 = nameof(LOGGEN020);
        internal const string LOGGEN021 = nameof(LOGGEN021);
        internal const string LOGGEN022 = nameof(LOGGEN022);
        internal const string LOGGEN023 = nameof(LOGGEN023);
        internal const string LOGGEN024 = nameof(LOGGEN024);
        internal const string LOGGEN025 = nameof(LOGGEN025);
        internal const string LOGGEN026 = nameof(LOGGEN026);
        internal const string LOGGEN027 = nameof(LOGGEN027);
        internal const string LOGGEN028 = nameof(LOGGEN028);
        internal const string LOGGEN029 = nameof(LOGGEN029);
        internal const string LOGGEN030 = nameof(LOGGEN030);
        internal const string LOGGEN031 = nameof(LOGGEN031);
        internal const string LOGGEN032 = nameof(LOGGEN032);
        internal const string LOGGEN033 = nameof(LOGGEN033);
        internal const string LOGGEN034 = nameof(LOGGEN034);
        internal const string LOGGEN035 = nameof(LOGGEN035);
        internal const string LOGGEN036 = nameof(LOGGEN036);
        internal const string LOGGEN037 = nameof(LOGGEN037);
        internal const string LOGGEN038 = nameof(LOGGEN038);
        internal const string LOGGEN039 = nameof(LOGGEN039);
    }

    internal static class Metrics
    {
        internal const string METGEN000 = nameof(METGEN000);
        internal const string METGEN001 = nameof(METGEN001);
        internal const string METGEN002 = nameof(METGEN002);
        internal const string METGEN003 = nameof(METGEN003);
        internal const string METGEN004 = nameof(METGEN004);
        internal const string METGEN005 = nameof(METGEN005);
        internal const string METGEN006 = nameof(METGEN006);
        internal const string METGEN007 = nameof(METGEN007);
        internal const string METGEN008 = nameof(METGEN008);
        internal const string METGEN009 = nameof(METGEN009);
        internal const string METGEN010 = nameof(METGEN010);
        internal const string METGEN011 = nameof(METGEN011);
        internal const string METGEN012 = nameof(METGEN012);
        internal const string METGEN013 = nameof(METGEN013);
        internal const string METGEN014 = nameof(METGEN014);
        internal const string METGEN015 = nameof(METGEN015);
        internal const string METGEN016 = nameof(METGEN016);
        internal const string METGEN017 = nameof(METGEN017);
        internal const string METGEN018 = nameof(METGEN018);
        internal const string METGEN019 = nameof(METGEN019);
    }

    internal static class AuditReports
    {
        internal const string AUDREPGEN000 = nameof(AUDREPGEN000);
        internal const string AUDREPGEN001 = nameof(AUDREPGEN001);
    }

    internal static class Obsoletions
    {
        internal const string NonObservableResourceMonitoringApiDiagId = "EXTOBS0001";
        internal const string ObsoleteTelemetryApiDiagId = "EXTOBS0002";
        internal const string HybridCacheDisableCompressionDiagId = "EXTOBS0003";
        internal const string NonObservableResourceMonitoringApiMessage =
            "This API is obsolete and will be removed in a future version. Consider using Resource Monitoring observable instruments.";
        internal const string ObsoleteTelemetryApiMessage =
            "This API is obsolete and will be removed in a future version. Instead of the AddServiceLogEnricher() methods, consider using the respective AddApplicationLogEnricher() methods.";
        internal const string HybridCacheDisableCompressionMessage =
            "HybridCache does not provide built-in compression, so this option doesn't do anything.";
    }
}

#pragma warning restore S1144

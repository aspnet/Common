// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CA1307 // Specify StringComparison
#pragma warning disable MEAI001 // OpenAIRequestPolicies is experimental

namespace Microsoft.Extensions.AI;

/// <summary>Provides utility methods for creating <see cref="RequestOptions"/>.</summary>
internal static class RequestOptionsExtensions
{
    /// <summary>Creates a <see cref="RequestOptions"/> configured for use with OpenAI.</summary>
    public static RequestOptions ToRequestOptions(this CancellationToken cancellationToken, bool streaming) =>
        ToRequestOptions(cancellationToken, streaming, policies: null);

    /// <summary>
    /// Creates a <see cref="RequestOptions"/> configured for use with OpenAI, applying any
    /// caller-registered <see cref="OpenAIRequestPolicies"/> after Microsoft.Extensions.AI's own
    /// internal policies.
    /// </summary>
    public static RequestOptions ToRequestOptions(this CancellationToken cancellationToken, bool streaming, OpenAIRequestPolicies? policies)
    {
        RequestOptions requestOptions = new()
        {
            CancellationToken = cancellationToken,
            BufferResponse = !streaming
        };

        requestOptions.AddPolicy(MeaiUserAgentPolicy.Instance, PipelinePosition.PerCall);

        policies?.ApplyTo(requestOptions);

        return requestOptions;
    }

    /// <summary>Provides a pipeline policy that adds a "MEAI/x.y.z" user-agent header.</summary>
    private sealed class MeaiUserAgentPolicy : PipelinePolicy
    {
        public static MeaiUserAgentPolicy Instance { get; } = new MeaiUserAgentPolicy();

        private static readonly string _userAgentValue = CreateUserAgentValue();

        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            AddUserAgentHeader(message);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            AddUserAgentHeader(message);
            return ProcessNextAsync(message, pipeline, currentIndex);
        }

        private static void AddUserAgentHeader(PipelineMessage message) =>
            message.Request.Headers.Add("User-Agent", _userAgentValue);

        private static string CreateUserAgentValue()
        {
            const string Name = "MEAI";

            if (typeof(MeaiUserAgentPolicy).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is string version)
            {
                int pos = version.IndexOf('+');
                if (pos >= 0)
                {
                    version = version.Substring(0, pos);
                }

                if (version.Length > 0)
                {
                    return $"{Name}/{version}";
                }
            }

            return Name;
        }
    }
}

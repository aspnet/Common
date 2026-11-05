// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ClientModel.Primitives;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Provides an extension hook for adding <see cref="PipelinePolicy"/> instances to the
/// <see cref="RequestOptions"/> built by Microsoft.Extensions.AI for every outbound OpenAI request
/// made through the owning <c>IChatClient</c> or <c>IEmbeddingGenerator</c>.
/// </summary>
/// <remarks>
/// <para>
/// Retrieve the instance via <see cref="IChatClient.GetService(System.Type, object?)"/>
/// (or the equivalent on other Microsoft.Extensions.AI client interfaces) using
/// <see cref="OpenAIRequestPolicies"/> as the service type. The instance is per-client and
/// reachable through any <c>ChatClientBuilder</c> decorator chain.
/// </para>
/// <para>
/// Customer-registered policies are appended <em>after</em> Microsoft.Extensions.AI's own internal
/// policies, so a policy that calls <c>message.Request.Headers.Set("User-Agent", ...)</c>
/// replaces the existing value, while one that calls <c>Headers.Add(...)</c> stacks an
/// additional value.
/// </para>
/// <para>
/// Registration is intended for one-time configuration at startup, but is safe to call
/// concurrently with in-flight requests.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIOpenAIRequestPolicies, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class OpenAIRequestPolicies
{
    private static readonly Entry[] _empty = Array.Empty<Entry>();

    private Entry[] _entries = _empty;

    /// <summary>Initializes a new instance of the <see cref="OpenAIRequestPolicies"/> class.</summary>
    public OpenAIRequestPolicies()
    {
    }

    /// <summary>
    /// Adds a <see cref="PipelinePolicy"/> to be applied to every <see cref="RequestOptions"/>
    /// produced for outbound OpenAI requests by the owning Microsoft.Extensions.AI client.
    /// </summary>
    /// <param name="policy">The pipeline policy to register. Must not be <see langword="null"/>.</param>
    /// <param name="position">
    /// The position in the pipeline at which to place the policy. Defaults to
    /// <see cref="PipelinePosition.PerCall"/>, which runs the policy once per logical request
    /// (for example, to stamp a User-Agent or correlation header).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> is <see langword="null"/>.</exception>
    public void AddPolicy(PipelinePolicy policy, PipelinePosition position = PipelinePosition.PerCall)
    {
        _ = Throw.IfNull(policy);

        var newEntry = new Entry(policy, position);

        // Lock-free append: copy-on-write with CAS retry.
        while (true)
        {
            var current = Volatile.Read(ref _entries);
            var updated = new Entry[current.Length + 1];
            Array.Copy(current, updated, current.Length);
            updated[current.Length] = newEntry;

            if (Interlocked.CompareExchange(ref _entries, updated, current) == current)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Applies all registered policies to the supplied <see cref="RequestOptions"/>.
    /// Called by the Microsoft.Extensions.AI OpenAI clients after their own internal policies
    /// have been registered.
    /// </summary>
    internal void ApplyTo(RequestOptions requestOptions)
    {
        var snapshot = Volatile.Read(ref _entries);
        for (int i = 0; i < snapshot.Length; i++)
        {
            var entry = snapshot[i];
            requestOptions.AddPolicy(entry.Policy, entry.Position);
        }
    }

    private readonly struct Entry
    {
        public Entry(PipelinePolicy policy, PipelinePosition position)
        {
            Policy = policy;
            Position = position;
        }

        public PipelinePolicy Policy { get; }
        public PipelinePosition Position { get; }
    }
}

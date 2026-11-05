// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading;
using Microsoft.Extensions.Diagnostics.Latency;

namespace Microsoft.Extensions.Http.Latency.Internal;

internal sealed class HttpRequestLatencyListener : EventListener
{
    private const string SocketProviderName = "System.Net.Sockets";
    private const string HttpProviderName = "System.Net.Http";
    private const string NameResolutionProviderName = "System.Net.NameResolution";

    private readonly ConcurrentDictionary<string, EventSource?> _eventSources = new()
    {
        [SocketProviderName] = null,
        [HttpProviderName] = null,
        [NameResolutionProviderName] = null
    };

    internal HttpClientLatencyContext LatencyContext { get; }

    private readonly EventToToken _eventToToken;

    private int _enabled;

    internal bool Enabled => _enabled == 1;

    public HttpRequestLatencyListener(HttpClientLatencyContext latencyContext, ILatencyContextTokenIssuer tokenIssuer)
    {
        LatencyContext = latencyContext;
        _eventToToken = new(tokenIssuer);
    }

    public void Enable()
    {
        if (Interlocked.CompareExchange(ref _enabled, 1, 0) == 0)
        {
            // Enable any already discovered event sources
            foreach (var eventSource in _eventSources)
            {
                if (eventSource.Value != null)
                {
                    EnableEventSource(eventSource.Value);
                }
            }

#if NETSTANDARD
            foreach (var eventSource in EventSource.GetSources())
            {
                OnEventSourceCreated(eventSource.Name, eventSource);
            }
#else
            // Process already existing listeners once again
            EventSourceCreated += (_, args) => OnEventSourceCreated(args.EventSource!);
#endif
        }
    }

    internal void OnEventWritten(string eventSourceName, string? eventName)
    {
        // If event of interest, add a checkpoint for it.
        CheckpointToken? token = _eventToToken.GetCheckpointToken(eventSourceName, eventName);
        if (token.HasValue)
        {
            var latencyContext = LatencyContext.Get();
            latencyContext?.AddCheckpoint(token.Value);

            // If event of interest, add a presence measure for it.
            MeasureToken? mtoken = _eventToToken.GetMeasureToken(eventSourceName, eventName);

            if (mtoken.HasValue)
            {
                latencyContext?.AddMeasure(mtoken.Value, 1L);
            }
        }
    }

    internal void OnEventSourceCreated(string eventSourceName, EventSource eventSource)
    {
        if (_eventSources.ContainsKey(eventSourceName))
        {
            _eventSources[eventSourceName] = eventSource;
            EnableEventSource(eventSource);
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        OnEventSourceCreated(eventSource.Name, eventSource);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        // .NET 8 can deliver events before Enable is called, even during construction.
        if (Enabled)
        {
            OnEventWritten(eventData.EventSource.Name, eventData.EventName);
        }
    }

    private void EnableEventSource(EventSource eventSource)
    {
        if (Enabled)
        {
            EnableEvents(eventSource, EventLevel.Informational);
        }
    }

    private sealed class EventToToken
    {
        private static readonly Dictionary<string, string> _socketMap = new()
        {
            { "ConnectStart", HttpCheckpoints.SocketConnectStart },
            { "ConnectStop", HttpCheckpoints.SocketConnectEnd }
        };

        private static readonly Dictionary<string, string> _nameResolutionMap = new()
        {
            { "ResolutionStart", HttpCheckpoints.NameResolutionStart },
            { "ResolutionStop", HttpCheckpoints.NameResolutionEnd }
        };

        private static readonly Dictionary<string, string> _httpMap = new()
        {
            { "ConnectionEstablished", HttpCheckpoints.ConnectionEstablished },
            { "RequestLeftQueue", HttpCheckpoints.RequestLeftQueue },
            { "RequestHeadersStart", HttpCheckpoints.RequestHeadersStart },
            { "RequestHeadersStop", HttpCheckpoints.RequestHeadersEnd },
            { "RequestContentStart", HttpCheckpoints.RequestContentStart },
            { "RequestContentStop", HttpCheckpoints.RequestContentEnd },
            { "ResponseHeadersStart", HttpCheckpoints.ResponseHeadersStart },
            { "ResponseHeadersStop", HttpCheckpoints.ResponseHeadersEnd },
            { "ResponseContentStart", HttpCheckpoints.ResponseContentStart },
            { "ResponseContentStop", HttpCheckpoints.ResponseContentEnd }
        };

        private static readonly Dictionary<string, string> _httpMeasureMap = new()
        {
            { "ConnectionEstablished", HttpMeasures.ConnectionInitiated }
        };

        private readonly FrozenDictionary<string, FrozenDictionary<string, CheckpointToken>> _eventToCheckpointTokenMap;
        private readonly FrozenDictionary<string, FrozenDictionary<string, MeasureToken>> _eventToMeasureTokenMap;

        public EventToToken(ILatencyContextTokenIssuer tokenIssuer)
        {
            Dictionary<string, CheckpointToken> socket = new();
            foreach (string key in _socketMap.Keys)
            {
                socket[key] = tokenIssuer.GetCheckpointToken(_socketMap[key]);
            }

            Dictionary<string, CheckpointToken> nameResolution = new();
            foreach (string key in _nameResolutionMap.Keys)
            {
                nameResolution[key] = tokenIssuer.GetCheckpointToken(_nameResolutionMap[key]);
            }

            Dictionary<string, CheckpointToken> http = new();
            foreach (string key in _httpMap.Keys)
            {
                http[key] = tokenIssuer.GetCheckpointToken(_httpMap[key]);
            }

            Dictionary<string, MeasureToken> httpMeasures = new();
            foreach (string key in _httpMeasureMap.Keys)
            {
                httpMeasures[key] = tokenIssuer.GetMeasureToken(_httpMeasureMap[key]);
            }

            _eventToCheckpointTokenMap = new Dictionary<string, FrozenDictionary<string, CheckpointToken>>
            {
                { SocketProviderName, socket.ToFrozenDictionary(StringComparer.Ordinal) },
                { NameResolutionProviderName, nameResolution.ToFrozenDictionary(StringComparer.Ordinal) },
                { HttpProviderName, http.ToFrozenDictionary(StringComparer.Ordinal) }
            }.ToFrozenDictionary(StringComparer.Ordinal);

            _eventToMeasureTokenMap = new Dictionary<string, FrozenDictionary<string, MeasureToken>>
            {
                { HttpProviderName, httpMeasures.ToFrozenDictionary(StringComparer.Ordinal) }
            }.ToFrozenDictionary(StringComparer.Ordinal);
        }

        public CheckpointToken? GetCheckpointToken(string eventSourceName, string? eventName)
        {
            if (eventName != null && _eventToCheckpointTokenMap.TryGetValue(eventSourceName, out var events))
            {
                if (events.TryGetValue(eventName, out var token))
                {
                    return token;
                }
            }

            return null;
        }

        public MeasureToken? GetMeasureToken(string eventSourceName, string? eventName)
        {
            if (eventName != null && _eventToMeasureTokenMap.TryGetValue(eventSourceName, out var events))
            {
                if (events.TryGetValue(eventName, out var token))
                {
                    return token;
                }
            }

            return null;
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if NET9_0_OR_GREATER
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.Buffering;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Diagnostics.Buffering;

internal sealed class PerRequestLogBufferingConfigureOptions : IConfigureOptions<PerRequestLogBufferingOptions>
{
    private const string ConfigSectionName = "PerIncomingRequestLogBuffering";
    private readonly IConfiguration _configuration;

    public PerRequestLogBufferingConfigureOptions(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Configure(PerRequestLogBufferingOptions options)
    {
        if (_configuration is null)
        {
            return;
        }

        IConfigurationSection section = _configuration.GetSection(ConfigSectionName);
        if (!section.Exists())
        {
            return;
        }

        var parsedOptions = section.Get<PerRequestLogBufferingOptions>();
        if (parsedOptions is null)
        {
            return;
        }

        options.MaxLogRecordSizeInBytes = parsedOptions.MaxLogRecordSizeInBytes;
        options.MaxPerRequestBufferSizeInBytes = parsedOptions.MaxPerRequestBufferSizeInBytes;
        options.AutoFlushDuration = parsedOptions.AutoFlushDuration;
        options.IncludeScopes = parsedOptions.IncludeScopes;

        foreach (LogBufferingFilterRule rule in parsedOptions.Rules)
        {
            options.Rules.Add(rule);
        }
    }
}
#endif

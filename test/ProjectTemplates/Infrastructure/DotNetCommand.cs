// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Shared.ProjectTemplates.Tests;

public class DotNetCommand : TestCommand
{
    public DotNetCommand(params ReadOnlySpan<string> args)
    {
        FileName = WellKnownPaths.RepoDotNetExePath;

        // Reusable MSBuild nodes can keep redirected output pipes open after a command exits.
        EnvironmentVariables["MSBUILDDISABLENODEREUSE"] = "1";

        foreach (var arg in args)
        {
            Arguments.Add(arg);
        }
    }
}

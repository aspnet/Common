// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Shared.ProjectTemplates.Tests;

public sealed class DotNetNewCommand : DotNetCommand
{
    private bool _customHiveSpecified;

    public DotNetNewCommand(params ReadOnlySpan<string> args)
        : base(["new", .. args])
    {
    }

    public DotNetNewCommand WithCustomHive(string path)
    {
        Arguments.Add("--debug:custom-hive");
        Arguments.Add(path);
        _customHiveSpecified = true;
        return this;
    }

    public override Task<TestCommandResult> ExecuteAsync(ITestOutputHelper outputHelper)
    {
        if (!_customHiveSpecified)
        {
            // If this exception starts getting thrown in cases where a custom hive is
            // legitimately undesirable, we can add a new 'WithoutCustomHive()' method that
            // just sets '_customHiveSpecified' to 'true'.
            throw new InvalidOperationException($"A {nameof(DotNetNewCommand)} should specify a custom hive with '{nameof(WithCustomHive)}()'.");
        }

        return base.ExecuteAsync(outputHelper);
    }
}

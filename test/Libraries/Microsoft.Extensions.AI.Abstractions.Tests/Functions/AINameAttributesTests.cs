// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Xunit;

namespace Microsoft.Extensions.AI;

public class AINameAttributesTests
{
    [Fact]
    public void AIFunctionNameAttribute_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>("name", () => new AIFunctionNameAttribute(null!));
        Assert.Throws<ArgumentException>("name", () => new AIFunctionNameAttribute(" "));
    }

    [Fact]
    public void AIParameterNameAttribute_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>("name", () => new AIParameterNameAttribute(null!));
        Assert.Throws<ArgumentException>("name", () => new AIParameterNameAttribute(" "));
    }
}

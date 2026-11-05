// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Extensions.AI;

public class RoutingContextTests
{
    [Fact]
    public void RoutingContext_CarriesRequestInputs()
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, "initial") };
        var options = new ChatOptions { ModelId = "initial" };
        var context = new RoutingContext(messages, options);

        Assert.Same(messages, context.Messages);
        Assert.NotSame(options, context.ChatOptions);
        Assert.Equal("initial", context.ChatOptions!.ModelId);
        context.ChatOptions.ModelId = "changed";
        Assert.Equal("initial", options.ModelId);
        Assert.Throws<ArgumentNullException>(() => new RoutingContext(null!, options));
    }
}

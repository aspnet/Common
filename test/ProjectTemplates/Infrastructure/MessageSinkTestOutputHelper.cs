// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.Shared.ProjectTemplates.Tests;

public sealed class MessageSinkTestOutputHelper : ITestOutputHelper
{
    private readonly IMessageSink _messageSink;
    private readonly StringBuilder _sb = new();

    public MessageSinkTestOutputHelper(IMessageSink messageSink)
    {
        _messageSink = messageSink;
    }

    public string Output => _sb.ToString();

    public void Write(string message)
    {
        _sb.Append(message);
        _messageSink.OnMessage(new Xunit.v3.DiagnosticMessage(message));
    }

    public void Write(string format, params object[] args)
    {
        _sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, format, args);
        _messageSink.OnMessage(new Xunit.v3.DiagnosticMessage(format, args));
    }

    public void WriteLine(string message)
    {
        _sb.AppendLine(message);
        _messageSink.OnMessage(new Xunit.v3.DiagnosticMessage(message));
    }

    public void WriteLine(string format, params object[] args)
    {
        _sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, format, args);
        _sb.AppendLine();
        _messageSink.OnMessage(new Xunit.v3.DiagnosticMessage(format, args));
    }
}

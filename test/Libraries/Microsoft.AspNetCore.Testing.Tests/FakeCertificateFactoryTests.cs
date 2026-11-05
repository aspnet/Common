// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Microsoft.AspNetCore.Testing.Test;

public class FakeCertificateFactoryTests
{
    [Fact]
    public void Create_CreatesCertificate()
    {
        using var certificate = FakeSslCertificateFactory.CreateSslCertificate();

        Assert.Equal("CN=dotnet-extensions-self-signed-unit-test-certificate", certificate.SubjectName.Name);
        Assert.Equal("localhost", certificate.GetNameInfo(X509NameType.DnsFromAlternativeName, false));
        Assert.True(DateTime.Now > certificate.NotBefore + TimeSpan.FromHours(1));
        Assert.True(DateTime.Now < certificate.NotAfter - TimeSpan.FromHours(1));
        Assert.False(certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single().Critical);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenerateRsa_RunsOnWindows_GeneratesRsa(bool runsOnWindows)
    {
        Assert.SkipUnless(!RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Skipped on Linux");

        Assert.NotNull(FakeSslCertificateFactory.GenerateRsa(runsOnWindows));
    }

    [Fact]
    public void GenerateRsa_DoesNotRunOnWindows_GeneratesRsa()
    {
        Assert.SkipUnless(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Skipped on Windows");

        Assert.NotNull(FakeSslCertificateFactory.GenerateRsa(runsOnWindows: false));
    }
}

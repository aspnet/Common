// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace TestClasses
{
    internal static partial class TransitiveTestExtensions
    {
        public class C0
        {
            public C1 P0 { get; set; } = new C1();
            public string P1 { get; set; } = "V1";
        }

        public class C1
        {
            public string P2 { get; set; } = "V2";
            public override string ToString() => "TS1";
        }

        public class C2
        {
            public List<string> P3 { get; set; } = [];
        }

        public class C3
        {
            public int P4 { get; set; }

            public string this[int index] => string.Empty;

            public decimal this[string index] => decimal.One;
        }

        public class C4
        {
            public Type Kind { get; set; } = typeof(C4);
        }

        public class C5
        {
            public string P5 { get; set; } = "V5";

            [LogPropertyIgnore]
            public string P6 { get; set; } = "V6";

            public Action P7 { get; set; } = () => { };
        }

        public class C6
        {
            [LogProperties(Transitive = true)]
            public C0 P8 { get; set; } = new C0();
        }

        [LoggerMessage(LogLevel.Debug)]
        public static partial void M0(ILogger logger, [LogProperties(Transitive = true)] C0 p0);

        [LoggerMessage(LogLevel.Debug)]
        public static partial void M1(ILogger logger, [LogProperties(Transitive = false)] C0 p0);

        [LoggerMessage(LogLevel.Warning)]
        public static partial void M2(ILogger logger, [LogProperties(Transitive = true)] C2 p0);

        [LoggerMessage(LogLevel.Information)]
        public static partial void M3(ILogger logger, [LogProperties(Transitive = true)] C3 p0);

        [LoggerMessage(LogLevel.Information)]
        public static partial void M4(ILogger logger, [LogProperties(Transitive = true)] C4 p0);

        [LoggerMessage(LogLevel.Information)]
        public static partial void M5(ILogger logger, [LogProperties(Transitive = true)] C5 p0);

        [LoggerMessage(LogLevel.Information)]
        public static partial void M6(ILogger logger, [LogProperties] C6 p0);
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.HeaderParsing.Parsers;

internal sealed class IPAddressListParser : HeaderParser<IReadOnlyList<IPAddress>>
{
    public static IPAddressListParser Instance { get; } = new();

    public override bool TryParse(StringValues values, [NotNullWhen(true)] out IReadOnlyList<IPAddress>? result, [NotNullWhen(false)] out string? error)
    {
        var list = new List<IPAddress>();

        foreach (var value in values)
        {
            var startIndex = 0;
            int nextSeparatorIndex;

            do
            {
                nextSeparatorIndex = value!.IndexOf(",", startIndex, StringComparison.Ordinal);
                var length = (nextSeparatorIndex >= 0 ? nextSeparatorIndex : value.Length) - startIndex;

                if (length == 0)
                {
                    error = "IP address cannot be empty.";
                    result = null;
                    return false;
                }

#if NETCOREAPP3_1_OR_GREATER
                var addressToParse = value.AsSpan(startIndex, length).Trim();
#else
                var addressToParse = value.AsSpan(startIndex, length).Trim().ToString();
#endif

                if (IPAddress.TryParse(addressToParse, out var address))
                {
                    list.Add(address);
                }
                else
                {
                    error = "Unable to parse IP address.";
                    result = null;
                    return false;
                }

                startIndex = nextSeparatorIndex + 1;
            }
            while (nextSeparatorIndex >= 0);
        }

        result = list;
        error = null;
        return true;
    }
}

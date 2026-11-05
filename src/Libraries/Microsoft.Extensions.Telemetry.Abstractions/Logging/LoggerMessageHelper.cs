// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
#if NET
using System.Globalization;
#endif
using Microsoft.Shared.Pools;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// Utility type to support generated logging methods.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class LoggerMessageHelper
{
    /// <summary>
    /// Gets a thread-local instance of this type.
    /// </summary>
    /// <remarks>
    /// If the current thread's instance is in use, a new instance is returned so that nested logging calls on the same
    /// thread don't share it.
    /// </remarks>
    [field: ThreadStatic]
    public static LoggerMessageState ThreadLocalState
    {
        get
        {
            var state = field;
            if (state is null || state.IsInUse)
            {
                state = new();
                field = state;
            }

            return state;
        }
    }

    /// <summary>
    /// Enumerates an enumerable into a string.
    /// </summary>
    /// <param name="enumerable">The enumerable object.</param>
    /// <returns>
    /// A string representation of the enumerable.
    /// </returns>
    public static string Stringify(IEnumerable? enumerable)
    {
        if (enumerable == null)
        {
            return "null";
        }

        var sb = PoolFactory.SharedStringBuilderPool.Get();
        _ = sb.Append('[');

        bool first = true;
        foreach (object? e in enumerable)
        {
            if (!first)
            {
                _ = sb.Append(',');
            }

            if (e == null)
            {
                _ = sb.Append("null");
            }
            else
            {
#if NET
                _ = sb.Append(CultureInfo.InvariantCulture, $"\"{e}\"");
#else
                _ = sb.Append(FormattableString.Invariant($"\"{e}\""));
#endif
            }

            first = false;
        }

        _ = sb.Append(']');
        var result = sb.ToString();
        PoolFactory.SharedStringBuilderPool.Return(sb);
        return result;
    }

    /// <summary>
    /// Enumerates an enumerable of key/value pairs into a string.
    /// </summary>
    /// <typeparam name="TKey">Type of keys.</typeparam>
    /// <typeparam name="TValue">Type of values.</typeparam>
    /// <param name="enumerable">The enumerable object.</param>
    /// <returns>
    /// A string representation of the enumerable.
    /// </returns>
    public static string Stringify<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>>? enumerable)
    {
        if (enumerable == null)
        {
            return "null";
        }

        var sb = PoolFactory.SharedStringBuilderPool.Get();
        _ = sb.Append('{');

        bool first = true;
        foreach (var kvp in enumerable)
        {
            if (!first)
            {
                _ = sb.Append(',');
            }

            if (typeof(TKey).IsValueType || kvp.Key is not null)
            {
#if NET
                _ = sb.Append(CultureInfo.InvariantCulture, $"\"{kvp.Key}\"=");
#else
                _ = sb.Append(FormattableString.Invariant($"\"{kvp.Key}\"="));
#endif
            }
            else
            {
                _ = sb.Append("null=");
            }

            if (typeof(TValue).IsValueType || kvp.Value is not null)
            {
#if NET
                _ = sb.Append(CultureInfo.InvariantCulture, $"\"{kvp.Value}\"");
#else
                _ = sb.Append(FormattableString.Invariant($"\"{kvp.Value}\""));
#endif
            }
            else
            {
                _ = sb.Append("null");
            }

            first = false;
        }

        _ = sb.Append('}');
        var result = sb.ToString();
        PoolFactory.SharedStringBuilderPool.Return(sb);
        return result;
    }
}

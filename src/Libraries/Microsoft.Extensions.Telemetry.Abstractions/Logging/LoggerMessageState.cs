// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Shared.Pools;

namespace Microsoft.Extensions.Logging;

/// <summary>
/// Additional state to use with <see cref="ILogger.Log"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed partial class LoggerMessageState
{
    private KeyValuePair<string, object?>[] _tags = [];
    private KeyValuePair<string, object?>[] _redactedTags = [];
    private ClassifiedTag[] _classifiedTags = [];

#pragma warning disable CA1819 // Properties should not return arrays
    /// <summary>
    /// Gets the array of tags.
    /// </summary>
    public KeyValuePair<string, object?>[] TagArray => _tags;

    /// <summary>
    /// Gets the array of tags.
    /// </summary>
    public KeyValuePair<string, object?>[] RedactedTagArray => _redactedTags;

    /// <summary>
    /// Gets the array of classified tags.
    /// </summary>
    public ClassifiedTag[] ClassifiedTagArray => _classifiedTags;
#pragma warning restore CA1819 // Properties should not return arrays

    /// <summary>
    /// Allocates some room to put some tags.
    /// </summary>
    /// <param name="count">The amount of space to allocate.</param>
    /// <returns>The index in the <see cref="TagArray"/> where to store the tags.</returns>
    public int ReserveTagSpace(int count)
    {
        int avail = _tags.Length - TagsCount;
        if (count > avail)
        {
            var need = _tags.Length + (count - avail);
            Array.Resize(ref _tags, need);
        }

        var index = TagsCount;
        TagsCount += count;
        return index;
    }

    /// <summary>
    /// Allocates some room to put some tags.
    /// </summary>
    /// <param name="count">The amount of space to allocate.</param>
    /// <returns>The index in the <see cref="ClassifiedTagArray"/> where to store the classified tags.</returns>
    public int ReserveClassifiedTagSpace(int count)
    {
        int avail = _classifiedTags.Length - ClassifiedTagsCount;
        if (count > avail)
        {
            var need = _classifiedTags.Length + (count - avail);
            Array.Resize(ref _classifiedTags, need);
            Array.Resize(ref _redactedTags, need);
        }

        var index = ClassifiedTagsCount;
        ClassifiedTagsCount += count;
        return index;
    }

    /// <summary>
    /// Adds a tag to the array.
    /// </summary>
    /// <param name="name">The name of the tag.</param>
    /// <param name="value">The value.</param>
    public void AddTag(string name, object? value)
    {
        var index = ReserveTagSpace(1);
        TagArray[index] = new(name, value);
    }

    /// <summary>
    /// Adds a classified tag to the array.
    /// </summary>
    /// <param name="name">The name of the tag.</param>
    /// <param name="value">The value.</param>
    /// <param name="classifications">The data classification of the tag.</param>
    public void AddClassifiedTag(string name, object? value, DataClassificationSet classifications)
    {
        var index = ReserveClassifiedTagSpace(1);
        ClassifiedTagArray[index] = new(name, value, classifications);
    }

    /// <summary>
    /// Resets the state of this object to its initial condition.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_tags, 0, TagsCount);
        Array.Clear(_classifiedTags, 0, ClassifiedTagsCount);
        Array.Clear(_redactedTags, 0, ClassifiedTagsCount);
        TagsCount = 0;
        ClassifiedTagsCount = 0;
        TagNamePrefix = string.Empty;
    }

    /// <summary>
    /// Gets a value indicating whether this instance has tags or a tag name prefix set.
    /// </summary>
    internal bool IsInUse => TagsCount != 0 || ClassifiedTagsCount != 0 || !string.IsNullOrEmpty(TagNamePrefix);

    /// <summary>
    /// Gets the number of unclassified tags currently in this instance.
    /// </summary>
    public int TagsCount { get; private set; }

    /// <summary>
    /// Gets the number of classified tags currently in this instance.
    /// </summary>
    public int ClassifiedTagsCount { get; private set; }

    /// <summary>
    /// Returns a string representation of this object.
    /// </summary>
    /// <returns>The string representation of this object.</returns>
    public override string ToString()
    {
        var sb = PoolFactory.SharedStringBuilderPool.Get();

        for (int i = 0; i < TagsCount; i++)
        {
            if (sb.Length > 0)
            {
                _ = sb.Append(',');
            }

            _ = sb.Append(_tags[i].Key);
            _ = sb.Append('=');
            _ = sb.Append(_tags[i].Value);
        }

        for (int i = 0; i < ClassifiedTagsCount; i++)
        {
            if (sb.Length > 0)
            {
                _ = sb.Append(',');
            }

            // note we don't emit the value here as that could lead to a privacy incident.
            _ = sb.Append(_classifiedTags[i].Name);
            _ = sb.Append("=<omitted> (");
            _ = sb.Append(_classifiedTags[i].Classifications.ToString());
            _ = sb.Append(')');
        }

        var result = sb.ToString();
        PoolFactory.SharedStringBuilderPool.Return(sb);

        return result;
    }
}


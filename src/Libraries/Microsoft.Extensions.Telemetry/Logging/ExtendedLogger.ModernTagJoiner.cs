// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.Logging;

internal sealed partial class ExtendedLogger
{
    /// <summary>
    /// Used to collect tags in the modern logging path.
    /// </summary>
    internal sealed class ModernTagJoiner : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public KeyValuePair<string, object?>[]? StaticTags;
        public Func<LoggerMessageState, Exception?, string>? Formatter;
        public LoggerMessageState? State;

        private const int TagCapacity = 4;
        private readonly List<KeyValuePair<string, object?>> _extraTags = new(TagCapacity);
        private KeyValuePair<string, object?>[]? _incomingTags;
        private KeyValuePair<string, object?>[]? _redactedTags;
        private int _incomingTagsCount;
        private int _redactedTagsCount;

        public ModernTagJoiner()
        {
            EnrichmentTagCollector = new(_extraTags);
        }

        public EnrichmentTagCollector EnrichmentTagCollector { get; }

        public void Clear()
        {
            _extraTags.Clear();
            _incomingTags = null;
            _incomingTagsCount = 0;
            _redactedTags = null;
            _redactedTagsCount = 0;
            State = null;
            Formatter = null;
        }

        [MemberNotNull(nameof(_incomingTags))]
        public void SetIncomingTags(LoggerMessageState value)
        {
            _incomingTags = value.TagArray;
            _incomingTagsCount = value.TagsCount;

            _redactedTags = value.RedactedTagArray;
            _redactedTagsCount = value.ClassifiedTagsCount;
        }

        public KeyValuePair<string, object?> this[int index]
        {
            get
            {
                int staticTagsCount = StaticTags!.Length;
                int extraTagsCount = _extraTags.Count;

                if (index < _redactedTagsCount)
                {
                    return _redactedTags![index];
                }
                else if (index < _redactedTagsCount + extraTagsCount)
                {
                    return _extraTags[index - _redactedTagsCount];
                }
                else if (index < _redactedTagsCount + extraTagsCount + staticTagsCount)
                {
                    return StaticTags[index - _redactedTagsCount - extraTagsCount];
                }

                // Iterating over "_incomingTags" at the end, because it may contain the
                // "{OriginalFormat}" property which needs to be the last tag in the list.
                else
                {
                    return _incomingTags![index - _redactedTagsCount - extraTagsCount - staticTagsCount];
                }
            }
        }

        public int Count => _incomingTagsCount + _redactedTagsCount + _extraTags.Count + StaticTags!.Length;

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            // The groups are iterated in the same order as by the indexer, so that "{OriginalFormat}" remains the last tag.
            int extraTagsCount = _extraTags.Count;

            for (int i = 0; i < _redactedTagsCount; i++)
            {
                yield return _redactedTags![i];
            }

            for (int i = 0; i < extraTagsCount; i++)
            {
                yield return _extraTags[i];
            }

            foreach (var tag in StaticTags!)
            {
                yield return tag;
            }

            for (int i = 0; i < _incomingTagsCount; i++)
            {
                yield return _incomingTags![i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

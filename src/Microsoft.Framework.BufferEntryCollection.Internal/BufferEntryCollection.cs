﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.Framework.Internal
{
    /// <summary>
    /// An ordered collection of strings with an enumerator that iterates over it.
    /// </summary>
    internal class BufferEntryCollection : IEnumerable<string>
    {
        // Specifies the maximum size we'll allow for direct conversion from
        // char arrays to string.
        private const int MaxCharToStringLength = 1024;
        private readonly List<object> _buffer = new List<object>();
        private bool _isModified = false;

        // internal for testing
        internal IReadOnlyList<object> BufferEntries
        {
            get { return _buffer; }
        }

        /// <summary>
        /// Gets a value whether the buffer entries were modified (Added to / Cleared) or not.
        /// </summary>
        public bool IsModified
        {
            get { return _isModified; }
        }

        /// <summary>
        /// Adds a string value to the buffer.
        /// </summary>
        /// <param name="value">The value to be added.</param>
        public void Add(string value)
        {
            _buffer.Add(value);
            _isModified = true;
        }

        /// <summary>
        /// Adds a array of characters to the buffer.
        /// </summary>
        /// <param name="value">The array to add.</param>
        /// <param name="index">The character position in the array at which to start copying data.</param>
        /// <param name="count">The number of characters to copy.</param>
        public void Add([NotNull] char[] value, int index, int count)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            if (count < 0 || value.Length - index < count)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            while (count > 0)
            {
                // Split large char arrays into 1KB strings.
                var currentCount = count;
                if (MaxCharToStringLength < currentCount)
                {
                    currentCount = MaxCharToStringLength;
                }

                Add(new string(value, index, currentCount));
                index += currentCount;
                count -= currentCount;
            }
            _isModified = true;
        }

        /// <summary>
        /// Adds an instance of <see cref="BufferEntryCollection"/> to the buffer.
        /// </summary>
        /// <param name="buffer">The buffer collection to add.</param>
        public void Add([NotNull] BufferEntryCollection buffer)
        {
            _buffer.Add(buffer.BufferEntries);
            _isModified = true;
        }

        /// <summary>
        /// Removes all the elements from the buffer.
        /// </summary>
        public void Clear()
        {
            _buffer.Clear();
            _isModified = true;
        }

        /// <inheritdoc />
        public IEnumerator<string> GetEnumerator()
        {
            return new BufferEntryEnumerator(_buffer);
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private sealed class BufferEntryEnumerator : IEnumerator<string>
        {
            private readonly Stack<IEnumerator<object>> _enumerators = new Stack<IEnumerator<object>>();
            private readonly List<object> _initialBuffer;

            public BufferEntryEnumerator(List<object> buffer)
            {
                _initialBuffer = buffer;
                Reset();
            }

            public IEnumerator<object> CurrentEnumerator
            {
                get
                {
                    return _enumerators.Peek();
                }
            }

            public string Current
            {
                get
                {
                    var currentEnumerator = CurrentEnumerator;
                    Debug.Assert(currentEnumerator != null);

                    return (string)currentEnumerator.Current;
                }
            }

            object IEnumerator.Current
            {
                get
                {
                    return Current;
                }
            }

            public bool MoveNext()
            {
                var currentEnumerator = CurrentEnumerator;
                if (currentEnumerator.MoveNext())
                {
                    var current = currentEnumerator.Current;
                    var buffer = current as List<object>;
                    if (buffer != null)
                    {
                        // If the next item is a collection, recursively call in to it.
                        var enumerator = buffer.GetEnumerator();
                        _enumerators.Push(enumerator);
                        return MoveNext();
                    }

                    return true;
                }
                else if (_enumerators.Count > 1)
                {
                    // The current enumerator is exhausted and we have a parent.
                    // Pop the current enumerator out and continue with it's parent.
                    var enumerator = _enumerators.Pop();
                    enumerator.Dispose();

                    return MoveNext();
                }

                // We've exactly one element in our stack which cannot move next.
                return false;
            }

            public void Reset()
            {
                DisposeEnumerators();
                _enumerators.Push(_initialBuffer.GetEnumerator());
            }

            public void Dispose()
            {
                DisposeEnumerators();
            }

            private void DisposeEnumerators()
            {
                while (_enumerators.Count > 0)
                {
                    _enumerators.Pop().Dispose();
                }
            }
        }
    }
}
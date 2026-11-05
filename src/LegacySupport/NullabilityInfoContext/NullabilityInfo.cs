// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if !NET6_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;

#pragma warning disable SA1623 // Property summary documentation should match accessors

namespace System.Reflection
{
    /// <summary>
    /// A class that represents nullability info.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal sealed class NullabilityInfo
    {
        internal NullabilityInfo(Type type, NullabilityState readState, NullabilityState writeState,
            NullabilityInfo? elementType, NullabilityInfo[] typeArguments)
        {
            Type = type;
            ReadState = readState;
            WriteState = writeState;
            ElementType = elementType;
            GenericTypeArguments = typeArguments;
        }

        /// <summary>
        /// Gets the <see cref="System.Type" /> of the member or generic parameter
        /// to which this NullabilityInfo belongs.
        /// </summary>
        public Type Type { get; }

        /// <summary>
        /// Gets the nullability read state of the member.
        /// </summary>
        public NullabilityState ReadState { get; internal set; }

        /// <summary>
        /// Gets the nullability write state of the member.
        /// </summary>
        public NullabilityState WriteState { get; internal set; }

        /// <summary>
        /// Gets the <see cref="NullabilityInfo" /> of the elements of the array if the member type is an array; otherwise, <see langword="null"/>.
        /// </summary>
        public NullabilityInfo? ElementType { get; }

        /// <summary>
        /// Gets the array of <see cref="NullabilityInfo" /> values for each type parameter if the member type is a generic type.
        /// </summary>
        public NullabilityInfo[] GenericTypeArguments { get; }
    }

    /// <summary>
    /// An enum that represents nullability state.
    /// </summary>
    internal enum NullabilityState
    {
        /// <summary>
        /// Nullability context not enabled (oblivious).
        /// </summary>
        Unknown,

        /// <summary>
        /// Non nullable value or reference type.
        /// </summary>
        NotNull,

        /// <summary>
        /// Nullable value or reference type.
        /// </summary>
        Nullable,
    }
}
#endif

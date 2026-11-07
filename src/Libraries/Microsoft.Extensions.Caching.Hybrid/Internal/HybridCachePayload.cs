// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Microsoft.Extensions.Caching.Hybrid.Internal;

// logic related to the payload that we send to IDistributedCache
internal static class HybridCachePayload
{
    // FORMAT (v1):
    // fixed-size header (so that it can be reliably broadcast) 
    // 2 bytes: sentinel+version
    // 2 bytes: entropy (this is a random, and is to help with multi-node collisions at the same time)
    // 8 bytes: creation time (UTC ticks, little-endian)

    // and the dynamic part
    // varint: flags (little-endian)
    // varint: payload size
    // varint: duration (ticks relative to creation time)
    // varint: tag count
    // varint+utf8: key
    // (for each tag): varint+utf8: tagN
    // (payload-size bytes): payload
    // 2 bytes: sentinel+version (repeated, for reliability)
    // (at this point, all bytes *must* be exhausted, or it is treated as failure)

    // the encoding for varint etc is akin to BinaryWriter, also comparable to FormatterBinaryWriter in OutputCaching

    private const int MaxVarint64Length = 10;
    private const byte SentinelPrefix = 0x03;
    private const byte ProtocolVersion = 0x01;
    private const ushort UInt16SentinelPrefixPair = (ProtocolVersion << 8) | SentinelPrefix;

    private static readonly Random _entropySource = new(); // doesn't need to be cryptographic

    [Flags]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2344:Enumeration type names should not have \"Flags\" or \"Enum\" suffixes", Justification = "Clarity")]
    internal enum PayloadFlags : uint
    {
        None = 0,
    }

    internal enum HybridCachePayloadParseResult
    {
        Success = 0,
        FormatNotRecognized = 1,
        InvalidData = 2,
        InvalidKey = 3,
        ExpiredByEntry = 4,
        ExpiredByTag = 5,
        ExpiredByWildcard = 6,
        ParseFault = 7,
    }

    internal readonly record struct ParsedPayload(
        ArraySegment<byte> Payload,
        TimeSpan RemainingTime,
        PayloadFlags Flags,
        ushort Entropy,
        TagSet PendingTags,
        long CreationTime);

    public static UTF8Encoding Encoding { get; } = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    public static int GetMaxBytes(string key, TagSet tags, int payloadSize)
    {
        int length =
            2 // sentinel+version
            + 2 // entropy
            + 8 // creation time
            + MaxVarint64Length // flags
            + MaxVarint64Length // payload size
            + MaxVarint64Length // duration
            + MaxVarint64Length // tag count
            + 2 // trailing sentinel + version
            + GetMaxStringLength(key.Length) // key
            + payloadSize; // the payload itself

        // keys
        switch (tags.Count)
        {
            case 0:
                break;
            case 1:
                length += GetMaxStringLength(tags.GetSinglePrechecked().Length);
                break;
            default:
                foreach (string tag in tags.GetSpanPrechecked())
                {
                    length += GetMaxStringLength(tag.Length);
                }

                break;
        }

        return length;

        // pay the cost to get the actual length, to avoid significant
        // over-estimate in ASCII cases
        static int GetMaxStringLength(int charCount) =>
            MaxVarint64Length + Encoding.GetMaxByteCount(charCount);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Not cryptographic")]
    public static int Write(byte[] destination,
        string key, long creationTime, TimeSpan duration, PayloadFlags flags, TagSet tags, ReadOnlySequence<byte> payload)
    {
        int payloadLength = checked((int)payload.Length);

        BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(0, 2), UInt16SentinelPrefixPair);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(2, 2), (ushort)_entropySource.Next(0, 0x010000)); // Next is exclusive at RHS
        BinaryPrimitives.WriteInt64LittleEndian(destination.AsSpan(4, 8), creationTime);
        int len = 12;

        long durationTicks = duration.Ticks;
        if (durationTicks < 0)
        {
            durationTicks = 0;
        }

        Write7BitEncodedInt64(destination, ref len, (uint)flags);
        Write7BitEncodedInt64(destination, ref len, (ulong)payloadLength);
        Write7BitEncodedInt64(destination, ref len, (ulong)durationTicks);
        Write7BitEncodedInt64(destination, ref len, (ulong)tags.Count);
        WriteString(destination, ref len, key);
        switch (tags.Count)
        {
            case 0:
                break;
            case 1:
                WriteString(destination, ref len, tags.GetSinglePrechecked());
                break;
            default:
                foreach (string tag in tags.GetSpanPrechecked())
                {
                    WriteString(destination, ref len, tag);
                }

                break;
        }

        payload.CopyTo(destination.AsSpan(len, payloadLength));
        len += payloadLength;
        BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(len, 2), UInt16SentinelPrefixPair);
        return len + 2;

        static void Write7BitEncodedInt64(byte[] target, ref int offset, ulong value)
        {
            // Write out an int 7 bits at a time. The high bit of the byte,
            // when on, tells reader to continue reading more bytes.
            //
            // Using the constants 0x7F and ~0x7F below offers smaller
            // codegen than using the constant 0x80.

            while (value > 0x7Fu)
            {
                target[offset++] = (byte)((uint)value | ~0x7Fu);
                value >>= 7;
            }

            target[offset++] = (byte)value;
        }

        static void WriteString(byte[] target, ref int offset, string value)
        {
            int len = Encoding.GetByteCount(value);
            Write7BitEncodedInt64(target, ref offset, (ulong)len);
            offset += Encoding.GetBytes(value, 0, value.Length, target, offset);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules",
        "SA1108:Block statements should not contain embedded comments", Justification = "Byte offset comments for clarity")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules",
        "SA1122:Use string.Empty for empty strings", Justification = "Subjective, but; ugly")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204:Static elements should appear before instance elements", Justification = "False positive?")]
    public static HybridCachePayloadParseResult TryParse(ArraySegment<byte> source, string key, TagSet knownTags, DefaultHybridCache cache,
        out ParsedPayload parsed, out Exception? fault)
    {
        fault = null;
        parsed = default;

        // note "cache" is used primarily for expiration checks; we don't automatically add etc
        ushort entropy = 0;
        long creationTime = 0;
        ArraySegment<byte> payload = default;
        PayloadFlags flags = 0;
        TimeSpan remainingTime = TimeSpan.Zero;
        string[] pendingTagBuffer = [];
        int pendingTagsCount = 0;

        TagSet pendingTags = TagSet.Empty;
        ReadOnlySpan<byte> bytes = new(source.Array!, source.Offset, source.Count);
        if (bytes.Length < 19) // minimum needed for empty payload and zero tags
        {
            return HybridCachePayloadParseResult.FormatNotRecognized;
        }

        long now = cache.CurrentTimestamp();
        char[] scratch = [];
        try
        {
            switch (BinaryPrimitives.ReadUInt16LittleEndian(bytes))
            {
                case UInt16SentinelPrefixPair:
                    entropy = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(2));
                    creationTime = BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(4));
                    bytes = bytes.Slice(12); // the end of the fixed part

                    if (cache.IsWildcardExpired(creationTime))
                    {
                        return HybridCachePayloadParseResult.ExpiredByWildcard;
                    }

                    if (!TryRead7BitEncodedInt64(ref bytes, out ulong u64)) // flags
                    {
                        return HybridCachePayloadParseResult.InvalidData;
                    }

                    flags = (PayloadFlags)u64;

                    if (!TryRead7BitEncodedInt64(ref bytes, out u64) || u64 > int.MaxValue) // payload length
                    {
                        return HybridCachePayloadParseResult.InvalidData;
                    }

                    int payloadLength = (int)u64;

                    if (!TryRead7BitEncodedInt64(ref bytes, out ulong duration)) // duration
                    {
                        return HybridCachePayloadParseResult.InvalidData;
                    }

                    var remainingTicks = (creationTime + (long)duration) - now;
                    if (remainingTicks <= 0)
                    {
                        return HybridCachePayloadParseResult.ExpiredByEntry;
                    }

                    remainingTime = DefaultHybridCache.TicksToTimeSpan(remainingTicks);

                    if (!TryRead7BitEncodedInt64(ref bytes, out u64) || u64 > int.MaxValue) // tag count
                    {
                        return HybridCachePayloadParseResult.InvalidData;
                    }

                    int tagCount = (int)u64;

                    if (!TryReadString(ref bytes, ref scratch, out ReadOnlySpan<char> stringSpan))
                    {
                        return HybridCachePayloadParseResult.InvalidData;
                    }

                    if (!stringSpan.SequenceEqual(key.AsSpan()))
                    {
                        return HybridCachePayloadParseResult.InvalidKey; // key must match!
                    }

                    for (int i = 0; i < tagCount; i++)
                    {
                        if (!TryReadString(ref bytes, ref scratch, out stringSpan))
                        {
                            return HybridCachePayloadParseResult.InvalidData;
                        }

                        bool isTagExpired;
                        bool isPending;
                        if (knownTags.TryFind(stringSpan, out string? tagString))
                        {
                            // prefer to re-use existing tag strings when they exist
                            isTagExpired = cache.IsTagExpired(tagString, creationTime, out isPending);
                        }
                        else
                        {
                            // if an unknown tag; we might need to juggle
                            isTagExpired = cache.IsTagExpired(stringSpan, creationTime, out isPending);
                        }

                        if (isPending)
                        {
                            // might be expired, but the operation is still in-flight
                            if (pendingTagsCount == pendingTagBuffer.Length)
                            {
                                string[] newBuffer = ArrayPool<string>.Shared.Rent(Math.Max(4, pendingTagsCount * 2));
                                pendingTagBuffer.CopyTo(newBuffer, 0);
                                ArrayPool<string>.Shared.Return(pendingTagBuffer);
                                pendingTagBuffer = newBuffer;
                            }

                            pendingTagBuffer[pendingTagsCount++] = tagString ?? stringSpan.ToString();
                        }
                        else if (isTagExpired)
                        {
                            // definitely an expired tag
                            return HybridCachePayloadParseResult.ExpiredByTag;
                        }
                    }

                    if (bytes.Length != payloadLength + 2
                        || BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(payloadLength)) != UInt16SentinelPrefixPair)
                    {
                        return HybridCachePayloadParseResult.InvalidData;
                    }

                    int start = source.Offset + source.Count - (payloadLength + 2);
                    payload = new(source.Array!, start, payloadLength);

                    // finalize the pending tag buffer (in-flight tag expirations)
                    switch (pendingTagsCount)
                    {
                        case 0:
                            break;
                        case 1:
                            pendingTags = new(pendingTagBuffer[0]);
                            break;
                        default:
                            pendingTags = new(pendingTagBuffer.AsSpan(0, pendingTagsCount).ToArray());
                            break;
                    }

                    parsed = new(payload, remainingTime, flags, entropy, pendingTags, creationTime);
                    return HybridCachePayloadParseResult.Success;
                default:
                    return HybridCachePayloadParseResult.FormatNotRecognized;
            }
        }
        catch (Exception ex)
        {
            fault = ex;
            return HybridCachePayloadParseResult.ParseFault;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(scratch);
            ArrayPool<string>.Shared.Return(pendingTagBuffer);
        }

        static bool TryReadString(ref ReadOnlySpan<byte> buffer, ref char[] scratch, out ReadOnlySpan<char> value)
        {
            int length;
            if (!TryRead7BitEncodedInt64(ref buffer, out ulong u64Length)
                || u64Length > int.MaxValue
                || buffer.Length < (length = (int)u64Length)) // note buffer is now past the prefix via "ref"
            {
                value = default;
                return false;
            }

            // make sure we have enough buffer space
            int maxChars = Encoding.GetMaxCharCount(length);
            if (scratch.Length < maxChars)
            {
                ArrayPool<char>.Shared.Return(scratch);
                scratch = ArrayPool<char>.Shared.Rent(maxChars);
            }

            // decode
#if NETCOREAPP3_1_OR_GREATER
            var charCount = Encoding.GetChars(buffer.Slice(0, length), scratch);
#else
            int charCount;
            unsafe
            {
                fixed (byte* bPtr = buffer)
                {
                    fixed (char* cPtr = scratch)
                    {
                        charCount = Encoding.GetChars(bPtr, length, cPtr, scratch.Length);
                    }
                }
            }
#endif
            value = new(scratch, 0, charCount);
            buffer = buffer.Slice(length);
            return true;
        }

        static bool TryRead7BitEncodedInt64(ref ReadOnlySpan<byte> buffer, out ulong result)
        {
            byte byteReadJustNow;

            // Read the integer 7 bits at a time. The high bit
            // of the byte when on means to continue reading more bytes.
            //
            // There are two failure cases: we've read more than 10 bytes,
            // or the tenth byte is about to cause integer overflow.
            // This means that we can read the first 9 bytes without
            // worrying about integer overflow.

            const int MaxBytesWithoutOverflow = 9;
            result = 0;
            int index = 0;
            for (int shift = 0; shift < MaxBytesWithoutOverflow * 7; shift += 7)
            {
                // ReadByte handles end of stream cases for us.
                byteReadJustNow = buffer[index++];
                result |= (byteReadJustNow & 0x7Ful) << shift;

                if (byteReadJustNow <= 0x7Fu)
                {
                    buffer = buffer.Slice(index);
                    return true; // early exit
                }
            }

            // Read the 10th byte. Since we already read 63 bits,
            // the value of this byte must fit within 1 bit (64 - 63),
            // and it must not have the high bit set.

            byteReadJustNow = buffer[index++];
            if (byteReadJustNow > 0b_1u)
            {
                throw new OverflowException();
            }

            result |= (ulong)byteReadJustNow << (MaxBytesWithoutOverflow * 7);
            buffer = buffer.Slice(index);
            return true;
        }
    }
}

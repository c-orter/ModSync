using System;
using System.Runtime.CompilerServices;

namespace ModSync.Core.Util;

/// <summary>
///     Metro Hash 128
/// </summary>
public sealed class MetroHash128
{
    private const ulong K0 = 0xC83A91E1;
    private const ulong K1 = 0x8648DBDB;
    private const ulong K2 = 0x7BDEC03B;
    private const ulong K3 = 0x2F5870A5;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ValidateInput(byte[] input, int offset, int count)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if ((uint)offset > (uint)input.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if ((uint)count > (uint)(input.Length - offset))
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BulkLoop(
        ref ulong firstState,
        ref ulong secondState,
        ref ulong thirdState,
        ref ulong fourthState,
        ref byte[] b,
        ref int offset,
        int count
    )
    {
        // Create a local copy so that it remains in the CPU register.
        int localOffset = offset; // workaround for dotnet/runtime#39349

        while (localOffset <= count - 32)
        {
            firstState += ToUlong(b, localOffset) * K0;
            localOffset += 8;
            firstState = RotateRight(firstState, 29) + thirdState;
            secondState += ToUlong(b, localOffset) * K1;
            localOffset += 8;
            secondState = RotateRight(secondState, 29) + fourthState;
            thirdState += ToUlong(b, localOffset) * K2;
            localOffset += 8;
            thirdState = RotateRight(thirdState, 29) + firstState;
            fourthState += ToUlong(b, localOffset) * K3;
            localOffset += 8;
            fourthState = RotateRight(fourthState, 29) + secondState;
        }

        // Return the final result of the local register.
        offset = localOffset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FinalizeBulkLoop(ref ulong firstState, ref ulong secondState, ref ulong thirdState, ref ulong fourthState)
    {
        thirdState ^= RotateRight((firstState + fourthState) * K0 + secondState, 21) * K1;
        fourthState ^= RotateRight((secondState + thirdState) * K1 + firstState, 21) * K0;
        firstState ^= RotateRight((firstState + thirdState) * K0 + fourthState, 21) * K1;
        secondState ^= RotateRight((secondState + fourthState) * K1 + thirdState, 21) * K0;
    }

    private static void FinalizeHash(ref ulong firstState, ref ulong secondState, ref byte[] b, ref int offset, int count)
    {
        var end = offset + (count & 31);

        if (end - offset >= 16)
        {
            firstState += ToUlong(b, offset) * K2;
            offset += 8;
            firstState = RotateRight(firstState, 33) * K3;
            secondState += ToUlong(b, offset) * K2;
            offset += 8;
            secondState = RotateRight(secondState, 33) * K3;
            firstState ^= RotateRight(firstState * K2 + secondState, 45) * K1;
            secondState ^= RotateRight(secondState * K3 + firstState, 45) * K0;
        }

        if (end - offset >= 8)
        {
            firstState += ToUlong(b, offset) * K2;
            offset += 8;
            firstState = RotateRight(firstState, 33) * K3;
            firstState ^= RotateRight(firstState * K2 + secondState, 27) * K1;
        }

        if (end - offset >= 4)
        {
            secondState += ToUint(b, offset) * K2;
            offset += 4;
            secondState = RotateRight(secondState, 33) * K3;
            secondState ^= RotateRight(secondState * K3 + firstState, 46) * K0;
        }

        if (end - offset >= 2)
        {
            firstState += ToUshort(b, offset) * K2;
            offset += 2;
            firstState = RotateRight(firstState, 33) * K3;
            firstState ^= RotateRight(firstState * K2 + secondState, 22) * K1;
        }

        if (end - offset >= 1)
        {
            secondState += b[offset] * K2;
            secondState = RotateRight(secondState, 33) * K3;
            secondState ^= RotateRight(secondState * K3 + firstState, 58) * K0;
        }

        firstState += RotateRight(firstState * K0 + secondState, 13);
        secondState += RotateRight(secondState * K1 + firstState, 37);
        firstState += RotateRight(firstState * K2 + secondState, 13);
        secondState += RotateRight(secondState * K3 + firstState, 37);
    }

    /// <summary>
    ///     MetroHash 128 hash method
    ///     Not cryptographically secure
    /// </summary>
    /// <param name="seed">Seed to initialize data</param>
    /// <param name="input">Data you want to hash</param>
    /// <param name="offset">Start of the data you want to hash</param>
    /// <param name="count">Length of the data you want to hash</param>
    /// <returns>Hash</returns>
    public static byte[] Hash(ulong seed, byte[] input, int offset, int count)
    {
        ValidateInput(input, offset, count);
        var end = offset + count;
        var state = new ulong[4];
        ref var firstState = ref state[0];
        ref var secondState = ref state[1];
        firstState = (seed - K0) * K3;
        secondState = (seed + K1) * K2;
        if (count >= 32)
        {
            var thirdState = (seed + K0) * K2;
            var fourthState = (seed - K1) * K3;
            BulkLoop(ref firstState, ref secondState, ref thirdState, ref fourthState, ref input, ref offset, end);
            FinalizeBulkLoop(ref firstState, ref secondState, ref thirdState, ref fourthState);
        }

        FinalizeHash(ref firstState, ref secondState, ref input, ref offset, count);
        return [.. BitConverter.GetBytes(state[0]), .. BitConverter.GetBytes(state[1])];
    }

    public static byte[] Hash(byte[] input)
    {
        return Hash(0, input, 0, input.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong RotateRight(ulong x, int r)
    {
        return (x >> r) | (x << (64 - r));
    }

    /// <summary>
    ///     BitConverter methods are several times slower
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ToUshort(byte[] data, int start)
    {
        return (ushort)(data[start] | (data[start + 1] << 8));
    }

    /// <summary>
    ///     BitConverter methods are several times slower
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ToUint(byte[] data, int start)
    {
        return (uint)(data[start] | (data[start + 1] << 8) | (data[start + 2] << 16) | (data[start + 3] << 24));
    }

    /// <summary>
    ///     BitConverter methods are several times slower
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ToUlong(byte[] data, int start)
    {
        var i1 = (uint)(data[start] | (data[start + 1] << 8) | (data[start + 2] << 16) | (data[start + 3] << 24));
        var i2 = (ulong)(data[start + 4] | (data[start + 5] << 8) | (data[start + 6] << 16) | (data[start + 7] << 24));
        return i1 | (i2 << 32);
    }
}

using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace AssetsTools.NET.Standard.IO
{
    public static class TempBuffer
    {
        /// <summary>
        /// Executes the specified <see langword="static"/> <see cref="Action"/> using a temporary byte buffer, 
        /// minimizing allocations by using stack or pooled memory as appropriate.
        /// </summary>
        /// <param name="bufferSize">The size, in bytes, of the buffer to provide to the action.</param>
        /// <param name="state">An object representing the state to be passed to the action.</param>
        /// <param name="action">The action to execute, which receives the state and the allocated buffer.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [SkipLocalsInit]
        public static void RunBufferedAction<TState>(int bufferSize, TState state, Action<TState, Span<byte>> action)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);
            if (bufferSize <= MemorySizes.STACKALLOC_MAX_SIZE)
            {
                Span<byte> buffer = stackalloc byte[bufferSize];
                action(state, buffer);
            }
            else
            {
                byte[] rentedArr = ArrayPool<byte>.Shared.Rent(bufferSize);
                try
                {
                    action(state, rentedArr.AsSpan(0, bufferSize));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedArr);
                }
            }
        }
    }
}

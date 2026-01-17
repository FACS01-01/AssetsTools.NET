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
                    ArrayPool<byte>.Shared.Return(rentedArr, false);
                }
            }
        }

        /// <summary>
        /// Executes the specified <see langword="static"/> <see href="Func"/> using a temporary byte buffer,
        /// minimizing allocations by using stack or pooled memory as appropriate.
        /// </summary>
        /// <typeparam name="TResult">The type of the result returned by the function.</typeparam>
        /// <param name="bufferSize">The size, in bytes, of the temporary buffer to allocate.</param>
        /// <param name="state">The state object to pass to the function.</param>
        /// <param name="func">A function that receives the state object and the temporary buffer, and returns a result.</param>
        /// <returns>The result returned by the specified function after execution.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [SkipLocalsInit]
        public static TResult RunBufferedAction<TState, TResult>(int bufferSize, TState state, Func<TState, Span<byte>, TResult> func)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);
            if (bufferSize <= MemorySizes.STACKALLOC_MAX_SIZE)
            {
                Span<byte> buffer = stackalloc byte[bufferSize];
                return func(state, buffer);
            }
            else
            {
                byte[] rentedArr = ArrayPool<byte>.Shared.Rent(bufferSize);
                try
                {
                    return func(state, rentedArr.AsSpan(0, bufferSize));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedArr, false);
                }
            }
        }

        /// <summary>
        /// Provides a high-performance, stack-only buffer for collecting and appending bytes using pooled arrays.
        /// </summary>
        /// <remarks>
        /// Remember to <see cref="Dispose"/> after use,
        /// or instantiate it with a <see langword="using"/> statement.
        /// </remarks>
        public ref struct ByteCollector
        {
            private byte[] _buffer;
            private int _count;

            public ByteCollector() : this(4) { }
            public ByteCollector(int minCapacity = 4)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minCapacity);
                if (minCapacity < 4)
                    minCapacity = 4;
                _buffer = ArrayPool<byte>.Shared.Rent(minCapacity);
                _count = 0;
            }

            /// <summary>
            /// Read-only span containing the collected bytes only. Doesn't include unused buffer space.
            /// </summary>
            public ReadOnlySpan<byte> AsSpan()
            {
                if (_count == 0)
                    return ReadOnlySpan<byte>.Empty;
                return _buffer.AsSpan(0, _count);
            }

            public void Append(scoped ReadOnlySpan<byte> data)
            {
                var dataLength = data.Length;
                if (dataLength == 0)
                    return;
                EnsureCapacity(dataLength);
                data.CopyTo(_buffer.AsSpan(_count));
                _count += dataLength;
            }

            private void EnsureCapacity(int newData)
            {
                if (_count > int.MaxValue - newData)
                    throw new OverflowException();
                int needed = _count + newData;
                var oldBufferLength = _buffer.Length;
                if (needed <= oldBufferLength)
                    return;

                int newSize = (oldBufferLength >= int.MaxValue / 2) ? int.MaxValue 
                            : Math.Max(needed, oldBufferLength * 2);

                var newBuf = ArrayPool<byte>.Shared.Rent(newSize);
                _buffer.AsSpan(0, _count).CopyTo(newBuf);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = newBuf;
            }

            public void Dispose()
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null!;
            }
        }
    }
}

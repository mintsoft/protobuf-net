using System;
using System.Threading;

namespace ProtoBuf
{
    /// <summary>
    /// Configures and provides access to information about the protobuf BufferPool
    /// </summary>
    public static class BufferPoolConfiguration
    {
        /// <summary>
        /// Number of byte[] instances in the pool
        /// </summary>
        public static int PoolSize = 20;
        /// <summary>
        /// Starting size of byte[] in the pool; sizes will double until the arrays fit the content. A power of 2 is preferred
        /// </summary>
        public static int InitialBufferSize = 1024;
        /// <summary>
        /// Outputs the current sizes of the buffers in the pool; -1 if the buffer is currently leased
        /// </summary>
        public static int[] CurrentBufferPoolSizes => BufferPool.GetBufferPoolSizes();
    }

    internal sealed class BufferPool
    {
        private BufferPool() { }

        private static readonly CachedBuffer[] Pool = new CachedBuffer[BufferPoolConfiguration.PoolSize];

        /// <remarks>
        /// https://docs.microsoft.com/en-us/dotnet/framework/configure-apps/file-schema/runtime/gcallowverylargeobjects-element
        /// </remarks>
        private const int MaxByteArraySize = int.MaxValue - 56;

        internal static void Flush()
        {
            lock (Pool)
            {
                for (var i = 0; i < Pool.Length; i++)
                    Pool[i] = null;
            }
        }

        internal static byte[] GetBuffer()
        {
            return GetBuffer(BufferPoolConfiguration.InitialBufferSize);
        }

        internal static byte[] GetBuffer(int minSize)
        {
            byte[] cachedBuff = GetCachedBuffer(minSize);
            return cachedBuff ?? new byte[minSize];
        }

        internal static byte[] GetCachedBuffer(int minSize)
        {
            lock (Pool)
            {
                var bestIndex = -1;
                byte[] bestMatch = null;
                for (var i = 0; i < Pool.Length; i++)
                {
                    var buffer = Pool[i];
                    if (buffer == null || buffer.Size < minSize)
                    {
                        continue;
                    }
                    if (bestMatch != null && bestMatch.Length < buffer.Size)
                    {
                        continue;
                    }

                    var tmp = buffer.Buffer;
                    if (tmp == null)
                    {
                        Pool[i] = null;
                    }
                    else
                    {
                        bestMatch = tmp;
                        bestIndex = i;
                    }
                }

                if (bestIndex >= 0)
                {
                    Pool[bestIndex] = null;
                }

                return bestMatch;
            }
        }

        internal static void ResizeAndFlushLeft(ref byte[] buffer, int toFitAtLeastBytes, int copyFromIndex, int copyBytes)
        {
            Helpers.DebugAssert(buffer != null);
            Helpers.DebugAssert(toFitAtLeastBytes > buffer.Length);
            Helpers.DebugAssert(copyFromIndex >= 0);
            Helpers.DebugAssert(copyBytes >= 0);

            // try doubling, else match
            int newLength = buffer.Length * 2;
            if (newLength < 0)
            {
                newLength = MaxByteArraySize;
            }

            if (newLength < toFitAtLeastBytes) newLength = toFitAtLeastBytes;

            if (copyBytes == 0)
            {
                ReleaseBufferToPool(ref buffer);
            }

            var newBuffer = GetCachedBuffer(toFitAtLeastBytes) ?? new byte[newLength];

            if (copyBytes > 0)
            {
                Helpers.BlockCopy(buffer, copyFromIndex, newBuffer, 0, copyBytes);
                ReleaseBufferToPool(ref buffer);
            }

            buffer = newBuffer;
        }

        internal static void ReleaseBufferToPool(ref byte[] buffer)
        {
            if (buffer == null)
                return;

            lock (Pool)
            {
                var minIndex = 0;
                var minSize = int.MaxValue;
                for (var i = 0; i < Pool.Length; i++)
                {
                    var tmp = Pool[i];
                    if (tmp == null || !tmp.IsAlive)
                    {
                        minIndex = 0;
                        break;
                    }
                    if (tmp.Size < minSize)
                    {
                        minIndex = i;
                        minSize = tmp.Size;
                    }
                }

                Pool[minIndex] = new CachedBuffer(buffer);
            }

            buffer = null;
        }

        public static int[] GetBufferPoolSizes()
        {
            var bufferSizes = new int[BufferPoolConfiguration.PoolSize];
            lock(Pool)
            {
                for (var i = 0; i < Pool.Length; i++)
                {
                    bufferSizes[i] = Pool[i] == null ? -1 : Pool[i].Size;
                }
            }
            return bufferSizes;
        }

        private class CachedBuffer
        {
            private readonly WeakReference _reference;

            public int Size { get; }

            public bool IsAlive => _reference.IsAlive;
            public byte[] Buffer => (byte[])_reference.Target;

            public CachedBuffer(byte[] buffer)
            {
                Size = buffer.Length;
                _reference = new WeakReference(buffer);
            }
        }
    }
}
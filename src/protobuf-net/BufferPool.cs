using System;
#if !COREFX
//using System.Diagnostics.Tracing;
#endif
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
        private static int _bufferPoolSize;
        private static int _initialBufferSize;

        private BufferPool() {
            _bufferPoolSize = BufferPoolConfiguration.PoolSize;
            _initialBufferSize = BufferPoolConfiguration.InitialBufferSize;
        }

        private static readonly CachedBuffer[] Pool = new CachedBuffer[_bufferPoolSize];

        internal static void Flush()
        {
            lock (Pool)
            {
                for (var i = 0; i < Pool.Length; i++)
                    Pool[i] = null;
            }
            BufferPoolEventSource.Log.Flushed();
        }

        internal static byte[] GetBuffer()
        {
            return GetBuffer(_initialBufferSize);
        }

        internal static byte[] GetBuffer(int minSize)
        {
            BufferPoolEventSource.Log.GetBuffer(minSize);
            byte[] cachedBuff = GetCachedBuffer(minSize);
            if(cachedBuff == null)
            {
                BufferPoolEventSource.Log.AllocatedNewBuffer(minSize);
                return new byte[minSize];
            }
            return cachedBuff;

        }

        internal static byte[] GetCachedBuffer(int minSize)
        {
            var bestIndex = -1;
            byte[] bestMatch = null;
            lock (Pool)
            {
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
            }
            BufferPoolEventSource.Log.ReturnedCachedBuffer(minSize, bestMatch.Length);
            return bestMatch;
        }

        internal static void ResizeAndFlushLeft(ref byte[] buffer, int toFitAtLeastBytes, int copyFromIndex, int copyBytes)
        {
            Helpers.DebugAssert(buffer != null);
            Helpers.DebugAssert(toFitAtLeastBytes > buffer.Length);
            Helpers.DebugAssert(copyFromIndex >= 0);
            Helpers.DebugAssert(copyBytes >= 0);

            var newLength = buffer.Length * 2;
            if (newLength < toFitAtLeastBytes)
            {
                newLength = toFitAtLeastBytes;
            }

            BufferPoolEventSource.Log.ResizeAndFlushLeft(buffer.Length, toFitAtLeastBytes, newLength);

            if (copyBytes == 0)
            {
                ReleaseBufferToPool(ref buffer);
            }

            var newBuffer = GetCachedBuffer(toFitAtLeastBytes);
            if(newBuffer == null)
            {
                newBuffer = new byte[newLength];
                BufferPoolEventSource.Log.AllocatedNewBuffer(newLength);
            }

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

            BufferPoolEventSource.Log.ReleasedBufferToPool(buffer.Length);

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
            var bufferSizes = new int[_bufferPoolSize];
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
#if COREFX
        internal class EventMonitoringSource
        {
            internal void WriteEvent(int id, object[] inputs = null) { }
            internal void WriteEvent(int id, long inputs) { }
            internal void WriteEvent(int id, long input1, long input2) { }
            internal void WriteEvent(int id, long input1, long input2, long input3) { }
        }
#else
        //[EventSource(Name = "ProtoBuf.BufferPool.EventMonitoringSource")]
        internal class EventMonitoringSource //: EventSource
        {
            internal void WriteEvent(int id, object[] inputs = null) { }
            internal void WriteEvent(int id, long inputs) { }
            internal void WriteEvent(int id, long input1, long input2) { }
            internal void WriteEvent(int id, long input1, long input2, long input3) { }
        }
#endif

        internal sealed class BufferPoolEventSource : EventMonitoringSource
        {
            internal static BufferPoolEventSource Log = new BufferPoolEventSource();
            internal void GetBuffer(long minimumRequiredBufferLength)
            {
                WriteEvent(1, minimumRequiredBufferLength);
            }
            internal void AllocatedNewBuffer(long bufferLength)
            {
                WriteEvent(2, bufferLength);
            }
            internal void ReturnedCachedBuffer(long minimumRequiredBufferLength, long returnedBufferLength)
            {
                WriteEvent(3, minimumRequiredBufferLength, returnedBufferLength);
            }
            internal void ResizeAndFlushLeft(int oldBufferLength, int minimumRequiredBufferLength, int newBufferLength)
            {
                WriteEvent(4, oldBufferLength, minimumRequiredBufferLength, newBufferLength);
            }
            internal void ReleasedBufferToPool(int bufferLength)
            {
                WriteEvent(5, bufferLength);
            }
            internal void Flushed()
            {
                WriteEvent(6);
            }
        }
    }
}
using System;
using System.Threading;

namespace Migration.Infrastructure.Implementation.Document
{
    /// <summary>
    /// Lock striping implementation to prevent memory leaks from unlimited SemaphoreSlim instances.
    ///
    /// PROBLEM: Creating one SemaphoreSlim per folder = 1M folders × 100 bytes = ~100 MB memory leak
    /// SOLUTION: Use fixed number of locks (1024) and hash folder keys to lock indices
    ///
    /// Memory savings: 100 MB → 200 KB (99.8% reduction)
    /// </summary>
    public class LockStriping
    {
        private readonly SemaphoreSlim[] _locks;
        private readonly int _lockCount;

        /// <summary>
        /// Creates a lock striping instance with the specified number of locks.
        /// </summary>
        /// <param name="lockCount">Number of locks to create (default: 1024). Must be power of 2 for optimal hashing.</param>
        public LockStriping(int lockCount = 1024)
        {
            if (lockCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(lockCount), "Lock count must be positive");

            // Ensure lockCount is power of 2 for optimal hashing
            if ((lockCount & (lockCount - 1)) != 0)
            {
                // Round up to nearest power of 2
                lockCount = GetNextPowerOfTwo(lockCount);
            }

            _lockCount = lockCount;
            _locks = new SemaphoreSlim[lockCount];

            // Initialize all locks
            for (int i = 0; i < lockCount; i++)
            {
                _locks[i] = new SemaphoreSlim(1, 1);
            }
        }

        /// <summary>
        /// Gets a lock for the given key using consistent hashing.
        /// Multiple keys may map to the same lock (that's the point!).
        /// </summary>
        /// <param name="key">The key to hash (e.g., "parentId_folderName")</param>
        /// <returns>A SemaphoreSlim that can be used to synchronize access</returns>
        public SemaphoreSlim GetLock(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            // Get hash code and map to lock index
            int hashCode = key.GetHashCode();

            // Use bitwise AND for modulo (faster than % for power of 2)
            // Example: hash & 1023 is equivalent to hash % 1024
            int index = Math.Abs(hashCode) & (_lockCount - 1);

            return _locks[index];
        }

        /// <summary>
        /// Gets the total number of locks in the striping pool.
        /// </summary>
        public int LockCount => _lockCount;

        /// <summary>
        /// Rounds up to the nearest power of 2.
        /// </summary>
        private static int GetNextPowerOfTwo(int value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value++;
            return value;
        }

        /// <summary>
        /// Disposes all locks (call this when service is shutting down).
        /// </summary>
        public void Dispose()
        {
            foreach (var lockObj in _locks)
            {
                lockObj?.Dispose();
            }
        }
    }
}

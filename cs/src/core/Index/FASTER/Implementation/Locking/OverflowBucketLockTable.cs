﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace FASTER.core
{
    internal struct OverflowBucketLockTable<TKey> : ILockTable<TKey>
    {
        private long size_mask;     // As in the main hash table

        internal long NumBuckets => size_mask + 1;

        internal bool IsEnabled => size_mask > 0;

        internal OverflowBucketLockTable(long size_mask) => this.size_mask = size_mask;

        [Conditional("DEBUG")]
        void AssertLockAllowed() => Debug.Assert(IsEnabled, "Attempt to do Manual-locking lock when locking mode is LockingMode.EphemeralOnly");

        [Conditional("DEBUG")]
        void AssertUnlockAllowed() => Debug.Assert(IsEnabled, "Attempt to do Manual-locking unlock when locking mode is LockingMode.EphemeralOnly");

        [Conditional("DEBUG")]
        void AssertQueryAllowed() => Debug.Assert(IsEnabled, "Attempt to do Manual-locking query when locking mode is LockingMode.EphemeralOnly");

        internal long GetSize<TValue>(FasterKV<TKey, TValue> fht) => fht.state[fht.resizeInfo.version].size_mask;

        public bool NeedKeyLockCode => IsEnabled;

        /// <inheritdoc/>
        public long GetLockCode(ref TKey key, long hash) => IsEnabled ? hash & size_mask : 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal unsafe HashBucket* GetBucket<TValue>(FasterKV<TKey, TValue> fht, long keyCode)
            => fht.state[fht.resizeInfo.version].tableAligned + keyCode;

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool TryLockManual(ref TKey key, ref HashEntryInfo hei, LockType lockType) 
            => TryLockManual(hei.firstBucket, lockType);

        // The KeyCode approach is only for manual locking, to prevent a session from deadlocking itself; ephemeral always uses keys.
        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool TryLockManual<TValue>(FasterKV<TKey, TValue> fht, long keyCode, LockType lockType) 
            => TryLockManual(GetBucket(fht, keyCode), lockType);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe bool TryLockManual(HashBucket* bucket, LockType lockType)
        {
            AssertLockAllowed();
            return lockType switch
            {
                LockType.Shared => HashBucket.TryAcquireSharedLatch(bucket),
                LockType.Exclusive => HashBucket.TryAcquireExclusiveLatch(bucket),
                _ => throw new FasterException("Attempt to lock with unknown LockType")
            };
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool TryLockEphemeral(ref TKey key, ref HashEntryInfo hei, LockType lockType) 
            => lockType == LockType.Shared ? TryLockEphemeralShared(ref key, ref hei) : TryLockEphemeralExclusive(ref key, ref hei);

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool TryLockEphemeralShared(ref TKey key, ref HashEntryInfo hei)
        {
            AssertLockAllowed();
            return HashBucket.TryAcquireSharedLatch(hei.firstBucket);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool TryLockEphemeralExclusive(ref TKey key, ref HashEntryInfo hei)
        {
            AssertLockAllowed();
            return HashBucket.TryAcquireExclusiveLatch(hei.firstBucket);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Unlock(ref TKey key, ref HashEntryInfo hei, LockType lockType)
        {
            AssertUnlockAllowed();
            if (lockType == LockType.Shared)
                UnlockShared(ref key, ref hei);
            else
            {
                Debug.Assert(lockType == LockType.Exclusive, "Attempt to unlock with unknown LockType");
                UnlockExclusive(ref key, ref hei);
            }
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Unlock<TValue>(FasterKV<TKey, TValue> fht, long keyCode, LockType lockType)
        {
            AssertUnlockAllowed();
            HashBucket* bucket = GetBucket(fht, keyCode);
            if (lockType == LockType.Shared)
                HashBucket.ReleaseSharedLatch(bucket);
            else
            {
                Debug.Assert(lockType == LockType.Exclusive, "Attempt to unlock with unknown LockType");
                HashBucket.ReleaseExclusiveLatch(bucket);
            }
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void UnlockShared(ref TKey key, ref HashEntryInfo hei)
        {
            AssertUnlockAllowed();
            HashBucket.ReleaseSharedLatch(ref hei);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void UnlockExclusive(ref TKey key, ref HashEntryInfo hei)
        {
            AssertUnlockAllowed();
            HashBucket.ReleaseExclusiveLatch(ref hei);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool IsLockedShared(ref TKey key, ref HashEntryInfo hei)
        {
            AssertQueryAllowed();
            return HashBucket.NumLatchedShared(hei.firstBucket) > 0;
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool IsLockedExclusive(ref TKey key, ref HashEntryInfo hei)
        {
            AssertQueryAllowed();
            return HashBucket.IsLatchedExclusive(hei.firstBucket);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool IsLocked(ref TKey key, ref HashEntryInfo hei)
        {
            AssertQueryAllowed();
            return HashBucket.IsLatched(hei.firstBucket);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe LockState GetLockState(ref TKey key, ref HashEntryInfo hei) 
        {
            AssertQueryAllowed();
            return new()
            {
                IsFound = true, // Always true for OverflowBucketLockTable
                NumLockedShared = HashBucket.NumLatchedShared(hei.firstBucket),
                IsLockedExclusive = HashBucket.IsLatchedExclusive(hei.firstBucket)
            };
        }

        private static int LockSortComparer(long code1, LockType lt1, long code2, LockType lt2)
            => (code1 != code2) ? code1.CompareTo(code2) : -lt1.CompareTo(lt2);

        /// <inheritdoc/>
        internal void SortLockCodes<TData>(TData[] keyDatas)
            where TData : ILockableKey
        {
            Debug.Assert(LockType.Exclusive > LockType.Shared, "LockType.Exclusive must be > LockType.Shared so LockSortComparer works properly");
            Array.Sort(keyDatas, (data1, data2) => LockSortComparer(data1.LockCode, data1.LockType, data2.LockCode, data2.LockType));
        }

        /// <inheritdoc/>
        public void Dispose() { }
    }
}


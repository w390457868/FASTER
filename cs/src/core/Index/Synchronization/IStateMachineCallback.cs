// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

namespace FASTER.core
{
    /// <summary>
    /// Encapsulates custom logic to be executed as part of FASTER's state machine logic
    /// </summary>
    public interface IStateMachineCallback
    {
        /// <summary>
        /// Invoked immediately before every state transition.
        /// </summary>
        /// <param name="next"> next system state </param>
        /// <param name="faster"> reference to FASTER K-V </param>
        void BeforeEnteringState<Key, Value, StoreFunctions, Allocator>(SystemState next, FasterKV<Key, Value, StoreFunctions, Allocator> faster)
            where StoreFunctions : IStoreFunctions<Key, Value>
            where Allocator : AllocatorBase<Key, Value, StoreFunctions>;
    }
}
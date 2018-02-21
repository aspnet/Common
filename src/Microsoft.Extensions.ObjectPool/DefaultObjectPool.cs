﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.Extensions.ObjectPool
{
    public class DefaultObjectPool<T> : ObjectPool<T> where T : class
    {
        private readonly ObjectWrapper[] _items;
        private readonly IPooledObjectPolicy<T> _policy;
        private readonly bool _isDefaultPolicy;
        private readonly Func<T> _create;
        private readonly Func<T, bool> _return;
        private T _firstItem;

        public DefaultObjectPool(IPooledObjectPolicy<T> policy)
            : this(policy, Environment.ProcessorCount * 2)
        {
        }

        public DefaultObjectPool(IPooledObjectPolicy<T> policy, int maximumRetained)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _isDefaultPolicy = IsDefaultPolicy();

            var compiler = new PolicyCompiler<T>();
            _create = compiler.CompileCreate(this, policy, nameof(_policy));
            _return = compiler.CompileReturn(this, policy, nameof(_policy));

            // -1 due to _firstItem
            _items = new ObjectWrapper[maximumRetained - 1];

            bool IsDefaultPolicy()
            {
                var type = policy.GetType();

                return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DefaultPooledObjectPolicy<>);
            }
        }

        public override T Get()
        {
            T item = _firstItem;

            if (item == null || Interlocked.CompareExchange(ref _firstItem, null, item) != item)
            {
                item = GetViaScan();
            }

            return item;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private T GetViaScan()
        {
            ObjectWrapper[] items = _items;
            T item = null;

            for (var i = 0; i < items.Length; i++)
            {
                item = items[i];

                if (item != null && Interlocked.CompareExchange(ref items[i].Element, null, item) == item)
                {
                    break;
                }
            }

            return item ?? _create();
        }

        public override void Return(T obj)
        {
            if (_isDefaultPolicy || _return(obj))
            {
                if (_firstItem != null || Interlocked.CompareExchange(ref _firstItem, obj, null) != null)
                {
                    ReturnViaScan(obj);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReturnViaScan(T obj)
        {
            ObjectWrapper[] items = _items;

            for (var i = 0; i < items.Length && Interlocked.CompareExchange(ref items[i].Element, obj, null) != null; ++i)
            {
            }
        }

        [DebuggerDisplay("{Element}")]
        private struct ObjectWrapper
        {
            public T Element;

            public ObjectWrapper(T item) => Element = item;

            public static implicit operator T(ObjectWrapper wrapper) => wrapper.Element;
        }
    }
}

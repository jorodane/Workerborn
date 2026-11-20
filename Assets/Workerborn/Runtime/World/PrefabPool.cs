using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Workerborn
{
    // Scene-owned instances stay inactive until their caller has set up the next use.
    internal sealed class PrefabPool<T> where T : Component
    {
        private readonly Func<T> create;
        private readonly Stack<T> available = new Stack<T>();

        public PrefabPool(T prefab, Transform parent, Action<T> prepare = null)
        {
            var staging = new GameObject(prefab.name + " pool staging");
            staging.SetActive(false);
            staging.transform.SetParent(parent, false);
            create = () =>
            {
                var instance = Object.Instantiate(prefab, staging.transform, false);
                instance.gameObject.SetActive(false);
                instance.transform.SetParent(parent, false);
                prepare?.Invoke(instance);
                return instance;
            };
        }

        public void Prewarm(int count)
        {
            while (available.Count < count)
                available.Push(create());
        }

        public T Rent()
        {
            while (available.Count > 0)
            {
                var item = available.Pop();
                if (item != null)
                    return item;
            }
            return create();
        }

        public void Return(T item)
        {
            item.gameObject.SetActive(false);
            available.Push(item);
        }
    }
}

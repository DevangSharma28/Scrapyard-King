using System.Collections.Generic;
using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Items
{
    /// <summary>Single owner of <see cref="WorldItem"/> instances. Everything that creates or consumes items goes through here.</summary>
    [DefaultExecutionOrder(-600)]
    public sealed class ItemPool : ServiceBehaviour<ItemPool>
    {
        [SerializeField] Transform root;

        readonly Dictionary<ItemDefinition, Stack<WorldItem>> pools = new();

        public Transform Root => root != null ? root : transform;

        /// <summary>Returns an active, idle item at <paramref name="position"/> with a random look variation.</summary>
        public WorldItem Get(ItemDefinition definition, Vector3 position, Quaternion rotation)
        {
            if (definition == null || definition.Prefab == null)
            {
                Debug.LogError($"[ItemPool] '{(definition != null ? definition.name : "null")}' has no prefab.", this);
                return null;
            }

            WorldItem item = null;
            if (pools.TryGetValue(definition, out var pool))
                while (item == null && pool.Count > 0) item = pool.Pop();

            if (item == null) item = Instantiate(definition.Prefab, Root);
            else item.transform.SetParent(Root, false);

            item.transform.SetPositionAndRotation(position, rotation);
            item.Initialize(definition);
            item.gameObject.SetActive(true);
            return item;
        }

        public void Release(WorldItem item)
        {
            if (item == null) return;
            item.Deactivate();
            item.transform.SetParent(Root, false);
            if (item.Definition == null) return;

            if (!pools.TryGetValue(item.Definition, out var pool))
            {
                pool = new Stack<WorldItem>();
                pools[item.Definition] = pool;
            }

            pool.Push(item);
        }
    }
}

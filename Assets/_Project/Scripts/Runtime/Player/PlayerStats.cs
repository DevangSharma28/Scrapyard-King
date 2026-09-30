using System;
using ScrapYardKing.Core;
using ScrapYardKing.Harvest;
using UnityEngine;

namespace ScrapYardKing.Player
{
    /// <summary>Upgradeable player stats (blueprint upgrade category "Player").</summary>
    public enum PlayerStat
    {
        MoveSpeed,
        CutPower,
        CutRate,
        CutRange,
        CarryCapacity,
        PickupRadius
    }

    /// <summary>
    /// Runtime stat block built from <see cref="PlayerConfig"/>. The upgrade system adds modifiers here;
    /// components read current values and never cache base numbers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStats : MonoBehaviour, IHarvesterStats
    {
        static readonly int StatCount = Enum.GetValues(typeof(PlayerStat)).Length;

        [SerializeField] PlayerConfig config;

        ModifiableStat[] stats;

        public PlayerConfig Config => config;

        public float MoveSpeed => Get(PlayerStat.MoveSpeed).Value;
        public float CutPower => Get(PlayerStat.CutPower).Value;
        public float CutRate => Get(PlayerStat.CutRate).Value;
        public float CutRange => Get(PlayerStat.CutRange).Value;
        public int CarryCapacity => Mathf.Max(0, Get(PlayerStat.CarryCapacity).IntValue);
        public float PickupRadius => Get(PlayerStat.PickupRadius).Value;

        void Awake() => EnsureBuilt();

        public ModifiableStat Get(PlayerStat stat)
        {
            EnsureBuilt();
            return stats[(int)stat];
        }

        void EnsureBuilt()
        {
            if (stats != null) return;
            if (config == null)
            {
                Debug.LogError("[PlayerStats] PlayerConfig is not assigned.", this);
                config = ScriptableObject.CreateInstance<PlayerConfig>();
            }

            stats = new ModifiableStat[StatCount];
            stats[(int)PlayerStat.MoveSpeed] = new ModifiableStat(config.MoveSpeed);
            stats[(int)PlayerStat.CutPower] = new ModifiableStat(config.CutPower);
            stats[(int)PlayerStat.CutRate] = new ModifiableStat(config.CutRate);
            stats[(int)PlayerStat.CutRange] = new ModifiableStat(config.CutRange);
            stats[(int)PlayerStat.CarryCapacity] = new ModifiableStat(config.CarryCapacity);
            stats[(int)PlayerStat.PickupRadius] = new ModifiableStat(config.PickupRadius);
        }
    }
}

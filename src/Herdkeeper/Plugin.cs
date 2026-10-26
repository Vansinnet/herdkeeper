using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace Herdkeeper
{
    [BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const float PassSeconds = 5;
        private const float DiscoverySeconds = 60;
        private const int MaxStepsPerPass = 4;

        private sealed class Species
        {
            internal string Name;
            internal int Prefab;
            internal int Offspring;
            internal bool StartsTamed;
            internal long IntervalTicks;
            internal long PregnancyTicks;
            internal float TotalRange;
            internal float PartnerRange;
            internal float PregnancyChance;
            internal int MaxCreatures;
            internal int RequiredLovePoints;
            internal int MinOffspringLevel;
            internal float SpawnOffset;
            internal float SpawnOffsetMax;
            internal bool SpawnRandomDirection;
            internal bool OffspringPersistent;
            internal bool OffspringDistant;
            internal ZDO.ObjectType OffspringType;
        }

        private sealed class Animal
        {
            internal Species Species;
            internal long LastTicks;
        }

        private static readonly SimulationDistance Neighborhood = new SimulationDistance(1, 0, classic: true);

        private readonly HashSet<int> relevantPrefabs = new HashSet<int>();
        private readonly List<Species> discoveryTypes = new List<Species>();
        private readonly Dictionary<ZDOID, Animal> animals = new Dictionary<ZDOID, Animal>();
        private readonly List<ZDOID> removed = new List<ZDOID>();
        private readonly List<ZDO> discovered = new List<ZDO>();
        private readonly List<ZDO> sectorObjects = new List<ZDO>();
        private readonly Dictionary<Vector2s, List<ZDO>> neighbors = new Dictionary<Vector2s, List<ZDO>>();
        private readonly Stack<List<ZDO>> listPool = new Stack<List<ZDO>>();
        private readonly System.Random random = new System.Random();
        private ConfigEntry<bool> modEnabled;
        private ZNet network;
        private ZNetScene scene;
        private ZDOMan manager;
        private int prefabIndex;
        private int sectorIndex;
        private bool failed;
        private float nextDiscovery;
        private float nextPass;

        private void Awake()
        {
            modEnabled = Config.Bind("General", "Enabled", true,
                "Let tamed animals breed while no player is nearby. Only the server needs this plugin. Restart after changing the configuration file.");
            Logger.LogInfo($"{ModInfo.Name} {ModInfo.Version} loaded. Server-only breeding of unloaded tamed animals; runtime-pending.");
        }

        private void Update()
        {
            if (!modEnabled.Value || !ZNet.instance || !ZNet.instance.IsServer() || !ZNetScene.instance || ZDOMan.instance == null)
            {
                ResetSession();
                return;
            }

            try
            {
                if (network != ZNet.instance || scene != ZNetScene.instance || manager != ZDOMan.instance)
                {
                    ResetSession();
                    network = ZNet.instance;
                    scene = ZNetScene.instance;
                    manager = ZDOMan.instance;
                    RegisterSpecies();
                }
                if (failed || discoveryTypes.Count == 0) return;
                float now = Time.realtimeSinceStartup;
                Discover(now);
                if (now < nextPass) return;
                nextPass = now + PassSeconds;
                Pass();
            }
            catch (Exception error)
            {
                failed = true;
                Logger.LogError($"Herdkeeper stopped for this session after an unexpected error: {error}");
            }
        }

        private void RegisterSpecies()
        {
            foreach (var prefab in scene.m_prefabs)
            {
                if (!prefab) continue;
                var procreation = prefab.GetComponent<Procreation>();
                var tameable = prefab.GetComponent<Tameable>();
                if (!procreation || !tameable || !prefab.GetComponent<BaseAI>() || !prefab.GetComponent<ZNetView>()) continue;

                var offspring = procreation.m_offspring ? scene.GetPrefab(Utils.GetPrefabName(procreation.m_offspring)) : null;
                var offspringView = offspring ? offspring.GetComponent<ZNetView>() : null;
                if (!offspringView || !offspring.GetComponent<BaseAI>() || procreation.m_seperatePartner || procreation.m_noPartnerOffspring ||
                    !(procreation.m_updateInterval > 0) || procreation.m_requiredLovePoints <= 0 || procreation.m_maxCreatures <= 0)
                {
                    Logger.LogInfo($"Not handling {prefab.name}: only live-born offspring with the standard partner rules is supported.");
                    continue;
                }

                var entry = new Species
                {
                    Name = prefab.name,
                    Prefab = scene.GetPrefabHash(prefab),
                    Offspring = scene.GetPrefabHash(offspring),
                    StartsTamed = tameable.m_startsTamed,
                    IntervalTicks = TimeSpan.FromSeconds(procreation.m_updateInterval).Ticks,
                    PregnancyTicks = TimeSpan.FromSeconds(procreation.m_pregnancyDuration).Ticks,
                    TotalRange = procreation.m_totalCheckRange,
                    PartnerRange = procreation.m_partnerCheckRange,
                    PregnancyChance = procreation.m_pregnancyChance,
                    MaxCreatures = procreation.m_maxCreatures,
                    RequiredLovePoints = procreation.m_requiredLovePoints,
                    MinOffspringLevel = procreation.m_minOffspringLevel,
                    SpawnOffset = procreation.m_spawnOffset,
                    SpawnOffsetMax = procreation.m_spawnOffsetMax,
                    SpawnRandomDirection = procreation.m_spawnRandomDirection,
                    OffspringPersistent = offspringView.m_persistent,
                    OffspringDistant = offspringView.m_distant,
                    OffspringType = offspringView.m_type
                };
                relevantPrefabs.Add(entry.Prefab);
                relevantPrefabs.Add(entry.Offspring);
                discoveryTypes.Add(entry);
                Logger.LogInfo($"Breeding {entry.Name} into {offspring.name} while unloaded.");
            }
            if (discoveryTypes.Count == 0)
                Logger.LogWarning("No supported breeding animals were found. Check the Valheim version and other mods.");
        }

        private void Discover(float now)
        {
            if (now < nextDiscovery) return;
            // One stock incremental scan step per frame (at most 400 sectors).
            var type = discoveryTypes[prefabIndex];
            bool finished = manager.GetAllZDOsWithPrefabIterative(type.Name, discovered, ref sectorIndex);
            long nowTicks = network.GetTime().Ticks;
            foreach (var zdo in discovered)
            {
                if (zdo.IsValid() && zdo.GetPrefab() == type.Prefab && IsTamed(zdo, type) && !animals.ContainsKey(zdo.m_uid))
                    animals.Add(zdo.m_uid, new Animal { Species = type, LastTicks = nowTicks });
            }
            discovered.Clear();
            if (!finished) return;
            sectorIndex = 0;
            prefabIndex++;
            if (prefabIndex < discoveryTypes.Count) return;
            prefabIndex = 0;
            nextDiscovery = now + DiscoverySeconds;
        }

        private void Pass()
        {
            long nowTicks = network.GetTime().Ticks;
            foreach (var pair in animals)
            {
                var zdo = manager.GetZDO(pair.Key);
                var animal = pair.Value;
                if (zdo == null || !zdo.IsValid() || zdo.GetPrefab() != animal.Species.Prefab)
                {
                    removed.Add(pair.Key);
                    continue;
                }
                if (IsSimulated(zdo))
                {
                    // Vanilla handles loaded animals; never credit time that a game instance already simulated.
                    animal.LastTicks = nowTicks;
                    continue;
                }
                int steps = BreedingRules.TakeDueSteps(ref animal.LastTicks, nowTicks, animal.Species.IntervalTicks, MaxStepsPerPass);
                for (int i = 0; i < steps && IsTamed(zdo, animal.Species); i++)
                    Step(zdo, animal.Species, nowTicks);
            }
            foreach (var id in removed) animals.Remove(id);
            removed.Clear();
            foreach (var list in neighbors.Values)
            {
                list.Clear();
                listPool.Push(list);
            }
            neighbors.Clear();
        }

        private void Step(ZDO zdo, Species type, long nowTicks)
        {
            long pregnant = zdo.GetLong(ZDOVars.s_pregnant, 0);
            if (pregnant != 0)
            {
                if (!BreedingRules.IsDue(pregnant, nowTicks, type.PregnancyTicks)) return;
                zdo.Set(ZDOVars.s_pregnant, 0L);
                GiveBirth(zdo, type, nowTicks);
                return;
            }

            float roll = (float)random.NextDouble();
            if (roll <= type.PregnancyChance) return;
            var position = zdo.GetPosition();
            int creatures = 0;
            int partners = 0;
            foreach (var other in Neighbors(position))
            {
                int prefab = other.GetPrefab();
                if (prefab != type.Prefab && prefab != type.Offspring) continue;
                float distance = Vector3.Distance(position, other.GetPosition());
                if (distance <= type.TotalRange) creatures++;
                // Vanilla ReadyForProcreation without the hunger check.
                if (prefab == type.Prefab && distance <= type.PartnerRange && IsTamed(other, type) &&
                    other.GetLong(ZDOVars.s_pregnant, 0) == 0) partners++;
            }
            if (!BreedingRules.CanMate(roll, type.PregnancyChance, creatures, type.MaxCreatures, partners)) return;

            int love = zdo.GetInt(ZDOVars.s_lovePoints);
            bool conceived = BreedingRules.AddLove(ref love, type.RequiredLovePoints);
            zdo.Set(ZDOVars.s_lovePoints, love);
            if (conceived) zdo.Set(ZDOVars.s_pregnant, nowTicks);
        }

        private void GiveBirth(ZDO parent, Species type, long nowTicks)
        {
            // Mirrors vanilla placement: behind the parent, facing away from it.
            var rotation = parent.GetRotation();
            var direction = rotation * Vector3.forward;
            if (type.SpawnRandomDirection)
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2);
                direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            }
            float offset = type.SpawnOffsetMax > 0
                ? type.SpawnOffset + (float)random.NextDouble() * (type.SpawnOffsetMax - type.SpawnOffset)
                : type.SpawnOffset;
            var position = parent.GetPosition() - direction * offset;

            // The same fields ZNetView.Awake, Character.SetTamed/SetLevel and BaseAI.Awake would write.
            var child = manager.CreateNewZDO(position, type.Offspring);
            child.Persistent = type.OffspringPersistent;
            child.Type = type.OffspringType;
            child.Distant = type.OffspringDistant;
            child.SetPrefab(type.Offspring);
            child.SetRotation(Quaternion.LookRotation(-(rotation * Vector3.forward), Vector3.up));
            child.Set(ZDOVars.s_tamed, true);
            child.Set(ZDOVars.s_level, Math.Max(type.MinOffspringLevel, parent.GetInt(ZDOVars.s_level, 1)));
            child.Set(ZDOVars.s_spawnTime, nowTicks);
            // Leave it unowned like its unloaded parent; the first nearby peer takes ownership as usual.
            child.SetOwner(0L);
            var zone = ZoneSystem.GetZone(parent.GetPosition());
            if (neighbors.TryGetValue(zone, out var list)) list.Add(child);
        }

        private List<ZDO> Neighbors(Vector3 position)
        {
            // 3x3 zones (64 m each) cover every vanilla check range; cached for the current pass only.
            var zone = ZoneSystem.GetZone(position);
            if (neighbors.TryGetValue(zone, out var list)) return list;
            list = listPool.Count > 0 ? listPool.Pop() : new List<ZDO>();
            manager.FindSectorObjects(zone, Neighborhood, sectorObjects);
            foreach (var zdo in sectorObjects)
                if (relevantPrefabs.Contains(zdo.GetPrefab())) list.Add(zdo);
            sectorObjects.Clear();
            neighbors.Add(zone, list);
            return list;
        }

        private bool IsSimulated(ZDO zdo)
        {
            var position = zdo.GetPosition();
            // A dedicated server also simulates the area around its own reference position.
            if (ZNetScene.InActiveArea(position, network.GetReferencePosition())) return true;
            foreach (var peer in network.GetPeers())
                if (ZNetScene.InActiveArea(position, peer.GetRefPos())) return true;
            long owner = zdo.GetOwner();
            return owner != 0 && owner != ZDOMan.GetSessionID() && network.GetPeer(owner) != null;
        }

        private static bool IsTamed(ZDO zdo, Species type) => type.StartsTamed || zdo.GetBool(ZDOVars.s_tamed);

        private void ResetSession()
        {
            relevantPrefabs.Clear();
            discoveryTypes.Clear();
            animals.Clear();
            removed.Clear();
            discovered.Clear();
            sectorObjects.Clear();
            neighbors.Clear();
            listPool.Clear();
            network = null;
            scene = null;
            manager = null;
            prefabIndex = sectorIndex = 0;
            nextDiscovery = nextPass = 0;
            failed = false;
        }

        private void OnDisable() => ResetSession();
        private void OnDestroy() => ResetSession();
    }
}

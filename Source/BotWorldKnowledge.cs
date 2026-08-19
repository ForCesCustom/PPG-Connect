using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPGTogether.BepInEx
{
    internal struct BotGridCell : IEquatable<BotGridCell>
    {
        internal int X; internal int Y;
        internal BotGridCell(int x, int y) { X = x; Y = y; }
        public bool Equals(BotGridCell other) { return X == other.X && Y == other.Y; }
        public override bool Equals(object value) { return value is BotGridCell && Equals((BotGridCell)value); }
        public override int GetHashCode() { return X * 486187739 ^ Y; }
    }

    internal sealed class BotInterestCell
    {
        internal float LastVisited; internal float Interest; internal int Observations;
    }

    // Maintains a bounded, periodically refreshed world model. It never runs a
    // scene-wide scan in Update: the expensive query is throttled and consumed
    // in chunks, while each bot only reads an already-built snapshot.
    internal sealed class BotWorldKnowledge
    {
        private const float ScanInterval = 1.35f;
        private const float EntityForgetAfter = 18f;
        private const float CellSize = 6f;
        private const int ScanBudget = 180;
        private const int MaximumTrackedEntities = 650;
        private readonly Dictionary<ulong, BotKnowledgeEntity> entities = new Dictionary<ulong, BotKnowledgeEntity>();
        private readonly Dictionary<ulong, float> firstSeen = new Dictionary<ulong, float>();
        private readonly Dictionary<BotGridCell, List<ulong>> spatial = new Dictionary<BotGridCell, List<ulong>>();
        private readonly Dictionary<BotGridCell, BotInterestCell> interest = new Dictionary<BotGridCell, BotInterestCell>();
        private float nextScanAt;
        private int scanOffset;

        internal int Count { get { return entities.Count; } }

        internal void Refresh(WorldRegistry registry, float now)
        {
            if (now < nextScanAt) return;
            nextScanAt = now + ScanInterval;
            PhysicalBehaviour[] physicals = UnityEngine.Object.FindObjectsOfType<PhysicalBehaviour>();
            if (physicals == null || physicals.Length == 0) { Prune(now); RebuildSpatial(); return; }
            int amount = Mathf.Min(ScanBudget, physicals.Length);
            int start = scanOffset % physicals.Length;
            scanOffset = (start + amount) % physicals.Length;
            for (int i = 0; i < amount; i++)
            {
                PhysicalBehaviour physical = physicals[(start + i) % physicals.Length];
                Observe(physical, registry, now);
            }
            // Network identities are a small authoritative registry. Updating
            // them on every scan prevents a recently spawned item from waiting
            // for its turn in the broad scene scan.
            foreach (PPGTogetherIdentity identity in registry.All())
                if (identity != null) Observe(identity.GetComponent<PhysicalBehaviour>(), registry, now);
            Prune(now);
            RebuildSpatial();
        }

        internal BotPerception Perceive(Vector2 point, bool canSpawn, bool canGrab, bool canActivate, bool canCleanup, float now)
        {
            BotPerception perception = new BotPerception();
            perception.Position = ToBotPoint(point);
            perception.CanSpawn = canSpawn; perception.CanGrab = canGrab; perception.CanActivate = canActivate; perception.CanCleanup = canCleanup;
            MarkVisited(perception.Position, now);
            const float range = 38f;
            float rangeSquared = range * range;
            foreach (BotKnowledgeEntity entity in entities.Values)
            {
                float distance = entity.Position.DistanceSquared(perception.Position);
                if (distance > rangeSquared) continue;
                perception.Entities.Add(entity);
                if (entity.IsLiving) perception.LivingCount++;
                if (entity.IsNetworked) perception.NetworkedCount++;
                if (entity.Kind == BotObjectKind.Debris) perception.Debris++;
                if (entity.IsDangerous) perception.LocalDanger += entity.Danger / (1f + distance * .06f);
                if (distance < 4f) perception.Crowd++;
            }
            perception.LocalDanger = Mathf.Clamp01(perception.LocalDanger);
            perception.MapInterest = CurrentInterest(perception.Position, now);
            AddFrontier(perception, now);
            return perception;
        }

        internal bool TryGet(ulong key, out BotKnowledgeEntity entity) { return entities.TryGetValue(key, out entity); }
        internal void Clear()
        {
            entities.Clear(); firstSeen.Clear(); LastSeen.Clear(); spatial.Clear(); interest.Clear(); nextScanAt = 0f; scanOffset = 0;
        }

        private void Observe(PhysicalBehaviour physical, WorldRegistry registry, float now)
        {
            if (physical == null || physical.gameObject == null || entities.Count > MaximumTrackedEntities && !HasIdentity(physical)) return;
            PPGTogetherIdentity identity = physical.GetComponent<PPGTogetherIdentity>();
            bool networked = identity != null && identity.NetId != 0;
            ulong key = networked ? identity.NetId : LocalKey(physical.gameObject.GetInstanceID());
            BotKnowledgeEntity entity;
            bool existed = entities.TryGetValue(key, out entity);
            if (!existed)
            {
                entity = new BotKnowledgeEntity(); entity.Key = key; entities.Add(key, entity); firstSeen[key] = now;
            }
            string display = networked && !string.IsNullOrEmpty(identity.SpawnKey) ? identity.SpawnKey : physical.gameObject.name;
            entity.Name = display ?? string.Empty; entity.Kind = BotObjectClassifier.Classify(entity.Name);
            entity.Position = ToBotPoint(physical.transform.position); entity.IsNetworked = networked;
            entity.CanGrab = networked && physical.Selectable && physical.rigidbody != null;
            entity.CanDelete = networked && physical.Deletable;
            entity.CanActivate = networked && BotObjectClassifier.CanActivate(entity.Kind, entity.Name);
            entity.IsLiving = physical.GetComponentInParent<PersonBehaviour>() != null || entity.Kind == BotObjectKind.Living;
            entity.IsSleeping = physical.rigidbody == null || physical.rigidbody.IsSleeping();
            entity.Velocity = physical.rigidbody == null ? new BotPoint() : ToBotPoint(physical.rigidbody.velocity);
            entity.Danger = EstimateDanger(entity, physical); entity.IsDangerous = entity.Danger >= .52f;
            entity.Value = BotObjectClassifier.Value(entity.Kind); entity.Novelty = existed ? Mathf.Max(.05f, entity.Novelty * .94f) : 1f;
            float created; entity.Age = firstSeen.TryGetValue(key, out created) ? now - created : 0f;
            entity.Cluster = Cluster(entity.Position); LastSeen[key] = now;
            TouchInterest(entity.Position, entity.IsDangerous ? .05f : .16f, now);
        }

        private readonly Dictionary<ulong, float> LastSeen = new Dictionary<ulong, float>();
        private void Prune(float now)
        {
            List<ulong> stale = null;
            foreach (KeyValuePair<ulong, float> pair in LastSeen)
            {
                if (now - pair.Value < EntityForgetAfter) continue;
                if (stale == null) stale = new List<ulong>();
                stale.Add(pair.Key);
            }
            if (stale == null) return;
            for (int i = 0; i < stale.Count; i++) { entities.Remove(stale[i]); firstSeen.Remove(stale[i]); LastSeen.Remove(stale[i]); }
        }

        private void RebuildSpatial()
        {
            spatial.Clear();
            foreach (BotKnowledgeEntity entity in entities.Values)
            {
                BotGridCell cell = Cell(entity.Position); List<ulong> bucket;
                if (!spatial.TryGetValue(cell, out bucket)) { bucket = new List<ulong>(); spatial.Add(cell, bucket); }
                bucket.Add(entity.Key);
            }
        }

        private void AddFrontier(BotPerception perception, float now)
        {
            BotGridCell centre = Cell(perception.Position);
            float best = float.MaxValue;
            BotPoint candidate = perception.Position;
            for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++)
            {
                if (x == 0 && y == 0) continue;
                BotGridCell cell = new BotGridCell(centre.X + x, centre.Y + y);
                BotInterestCell state; float score = interest.TryGetValue(cell, out state) ? state.Interest + Mathf.Max(0f, 4f - (now - state.LastVisited) * .1f) : 0f;
                BotPoint point = new BotPoint((cell.X + .5f) * CellSize, (cell.Y + .5f) * CellSize);
                if (score < best) { best = score; candidate = point; }
                if (score < 1.4f) perception.Frontier.Add(point);
            }
            if (perception.Frontier.Count == 0) perception.Frontier.Add(candidate);
        }

        private void MarkVisited(BotPoint point, float now)
        {
            BotGridCell cell = Cell(point); BotInterestCell state; if (!interest.TryGetValue(cell, out state)) { state = new BotInterestCell(); interest.Add(cell, state); }
            state.LastVisited = now; state.Interest += .9f; state.Observations++;
        }

        private void TouchInterest(BotPoint point, float value, float now)
        {
            BotGridCell cell = Cell(point); BotInterestCell state; if (!interest.TryGetValue(cell, out state)) { state = new BotInterestCell(); interest.Add(cell, state); }
            state.Interest = Mathf.Min(6f, state.Interest + value); if (state.LastVisited <= 0f) state.LastVisited = now;
        }

        private float CurrentInterest(BotPoint point, float now)
        {
            BotInterestCell state; if (!interest.TryGetValue(Cell(point), out state)) return 0f;
            return Mathf.Clamp01(state.Interest / 4f - (now - state.LastVisited) * .02f);
        }

        private static float EstimateDanger(BotKnowledgeEntity entity, PhysicalBehaviour physical)
        {
            float danger = entity.Kind == BotObjectKind.Explosive ? .85f : entity.Kind == BotObjectKind.Weapon ? .38f : 0f;
            if (physical.rigidbody != null && physical.rigidbody.velocity.sqrMagnitude > 110f) danger = Mathf.Max(danger, .60f);
            string name = entity.Name == null ? string.Empty : entity.Name.ToLowerInvariant();
            if (name.IndexOf("fire", StringComparison.Ordinal) >= 0 || name.IndexOf("acid", StringComparison.Ordinal) >= 0 || name.IndexOf("laser", StringComparison.Ordinal) >= 0) danger = Mathf.Max(danger, .7f);
            return danger;
        }

        private static bool HasIdentity(PhysicalBehaviour physical) { return physical != null && physical.GetComponent<PPGTogetherIdentity>() != null; }
        private static ulong LocalKey(int id) { return 0x8000000000000000UL | unchecked((uint)id); }
        private static BotPoint ToBotPoint(Vector2 v) { return new BotPoint(v.x, v.y); }
        private static BotPoint ToBotPoint(Vector3 v) { return new BotPoint(v.x, v.y); }
        private static BotGridCell Cell(BotPoint p) { return new BotGridCell(Mathf.FloorToInt(p.X / CellSize), Mathf.FloorToInt(p.Y / CellSize)); }
        private static int Cluster(BotPoint p) { BotGridCell cell = Cell(p); return cell.GetHashCode(); }
    }

    internal static class BotObjectClassifier
    {
        internal static BotObjectKind Classify(string raw)
        {
            string name = raw == null ? string.Empty : raw.ToLowerInvariant();
            if (Contains(name, "human", "android", "person", "zombie", "gorse")) return BotObjectKind.Living;
            if (Contains(name, "grenade", "bomb", "dynamite", "mine", "rocket", "nuke")) return BotObjectKind.Explosive;
            if (Contains(name, "pistol", "rifle", "shotgun", "revolver", "gun", "m16", "ak-", "knife", "sword")) return BotObjectKind.Weapon;
            if (Contains(name, "syringe", "defibr", "medic", "bandage", "blood")) return BotObjectKind.Medical;
            if (Contains(name, "motor", "piston", "gear", "conveyor", "rotor", "generator")) return BotObjectKind.Mechanism;
            if (Contains(name, "car", "truck", "vehicle", "bike", "wheel", "tank")) return BotObjectKind.Vehicle;
            if (Contains(name, "button", "switch", "wire", "battery", "lamp", "laser", "radio")) return BotObjectKind.Electronic;
            if (Contains(name, "brick", "plank", "beam", "wall", "frame")) return BotObjectKind.Construction;
            if (Contains(name, "crate", "container", "barrel", "box", "bucket")) return BotObjectKind.Container;
            if (Contains(name, "metal", "wood", "steel", "concrete", "rod", "ball", "plate")) return BotObjectKind.Material;
            return BotObjectKind.Debris;
        }
        internal static bool CanActivate(BotObjectKind kind, string raw)
        {
            return kind == BotObjectKind.Weapon || kind == BotObjectKind.Mechanism || kind == BotObjectKind.Electronic || kind == BotObjectKind.Medical || kind == BotObjectKind.Explosive;
        }
        internal static float Value(BotObjectKind kind)
        {
            if (kind == BotObjectKind.Mechanism || kind == BotObjectKind.Electronic) return .9f;
            if (kind == BotObjectKind.Construction || kind == BotObjectKind.Material) return .65f;
            if (kind == BotObjectKind.Medical || kind == BotObjectKind.Container) return .7f;
            if (kind == BotObjectKind.Weapon || kind == BotObjectKind.Explosive) return .55f;
            return .35f;
        }
        private static bool Contains(string value, params string[] words)
        {
            for (int i = 0; i < words.Length; i++) if (value.IndexOf(words[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }
}



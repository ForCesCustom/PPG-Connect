using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace PPGTogether.BepInEx
{
    internal sealed class BotSpawnChoice
    {
        internal SpawnableAsset Asset;
        internal string Key;
        internal BotObjectKind Kind;
    }

    // Read-only view of the game's already registered catalog. It does not add
    // definitions, mutate categories or create hidden prefabs. Reflection is
    // deliberately constrained to catalog collections so Workshop assets that
    // are already visible to the game can participate as well.
    internal sealed class BotSpawnCatalog
    {
        private static readonly string[] CuratedKeys =
        {
            "Brick", "Metal Rod", "Wooden Plank", "Ball", "Metal", "Wood", "Steel", "Concrete", "Beam", "Wheel",
            "Human", "Android", "Pistol", "Revolver", "Shotgun", "M16", "AK-47", "Knife", "Sword", "Grenade",
            "Syringe", "Defibrillator", "Bandage", "Blood Tank", "Motor", "Piston", "Generator", "Button", "Switch",
            "Wire", "Battery", "Lamp", "Crate", "Box", "Barrel", "Container", "Car", "Tank", "Hovercar"
        };
        private readonly List<BotSpawnChoice> choices = new List<BotSpawnChoice>();
        private readonly Dictionary<string, BotSpawnChoice> byKey = new Dictionary<string, BotSpawnChoice>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<BotObjectKind, int> cursor = new Dictionary<BotObjectKind, int>();
        private float nextRefreshAt;

        internal int Count { get { return choices.Count; } }

        internal void Refresh(float now, Action<string> warning)
        {
            if (now < nextRefreshAt) return;
            nextRefreshAt = now + 30f;
            choices.Clear(); byKey.Clear(); cursor.Clear();
            try
            {
                CatalogBehaviour catalog = CatalogBehaviour.Main;
                if (catalog != null) DiscoverCatalog(catalog, warning);
            }
            catch (Exception exception)
            {
                if (warning != null) warning("Catalog discovery failed: " + exception.GetType().Name);
            }
            for (int i = 0; i < CuratedKeys.Length; i++)
            {
                SpawnableAsset asset = ModAPI.FindSpawnable(CuratedKeys[i]);
                if (asset != null) Add(asset);
            }
        }

        internal BotSpawnChoice Select(BotObjectKind desired, int botIndex)
        {
            if (choices.Count == 0) return null;
            List<BotSpawnChoice> matching = new List<BotSpawnChoice>();
            for (int i = 0; i < choices.Count; i++)
            {
                BotSpawnChoice choice = choices[i];
                if (choice.Asset == null || choice.Asset.IsLocked || choice.Kind != desired) continue;
                matching.Add(choice);
            }
            // Never quietly turn an unavailable category into a Human. The
            // safe fallback deliberately skips Living and explosives.
            if (matching.Count == 0 && desired != BotObjectKind.Living)
                for (int i = 0; i < choices.Count; i++)
                    if (choices[i].Asset != null && !choices[i].Asset.IsLocked && choices[i].Kind != BotObjectKind.Living && choices[i].Kind != BotObjectKind.Explosive)
                        matching.Add(choices[i]);
            if (matching.Count == 0) return null;
            int next; if (!cursor.TryGetValue(desired, out next)) next = botIndex;
            BotSpawnChoice result = matching[Math.Abs(next) % matching.Count];
            cursor[desired] = next + 1;
            return result;
        }

        internal void Clear() { choices.Clear(); byKey.Clear(); cursor.Clear(); nextRefreshAt = 0f; }

        private void DiscoverCatalog(object catalog, Action<string> warning)
        {
            Type type = catalog.GetType();
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            FieldInfo[] fields = type.GetFields(flags);
            for (int i = 0; i < fields.Length; i++)
            {
                if (!LooksLikeCatalogMember(fields[i].Name)) continue;
                try { AddSource(fields[i].GetValue(catalog)); }
                catch (Exception exception) { if (warning != null) warning("Catalog field " + fields[i].Name + " skipped: " + exception.GetType().Name); }
            }
            PropertyInfo[] properties = type.GetProperties(flags);
            for (int i = 0; i < properties.Length; i++)
            {
                if (!properties[i].CanRead || properties[i].GetIndexParameters().Length != 0 || !LooksLikeCatalogMember(properties[i].Name)) continue;
                try { AddSource(properties[i].GetValue(catalog, null)); }
                catch (Exception exception) { if (warning != null) warning("Catalog property " + properties[i].Name + " skipped: " + exception.GetType().Name); }
            }
        }

        private void AddSource(object source)
        {
            SpawnableAsset single = source as SpawnableAsset;
            if (single != null) { Add(single); return; }
            IEnumerable collection = source as IEnumerable;
            if (collection == null) return;
            foreach (object item in collection)
            {
                SpawnableAsset asset = item as SpawnableAsset;
                if (asset != null) Add(asset);
            }
        }

        private void Add(SpawnableAsset asset)
        {
            if (asset == null || string.IsNullOrEmpty(asset.NameToOrderBy) || byKey.ContainsKey(asset.NameToOrderBy)) return;
            BotSpawnChoice choice = new BotSpawnChoice(); choice.Asset = asset; choice.Key = asset.NameToOrderBy; choice.Kind = BotObjectClassifier.Classify(choice.Key);
            choices.Add(choice); byKey.Add(choice.Key, choice);
        }

        private static bool LooksLikeCatalogMember(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            name = name.ToLowerInvariant();
            return name.IndexOf("spawn", StringComparison.Ordinal) >= 0 || name.IndexOf("catalog", StringComparison.Ordinal) >= 0;
        }
    }
}



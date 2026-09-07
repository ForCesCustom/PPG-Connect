using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPGTogether.BepInEx
{
    // Unity copies this marker with a normal vanilla copy/paste. It records a
    // locally verified catalogue identity, never data supplied by another peer.
    internal sealed class ConnectSpawnOrigin : MonoBehaviour
    {
        [SerializeField] internal string SpawnKey;
    }

    public sealed partial class PPGTogetherPlugin
    {
        private uint worldEpoch;
        private uint clientWorldEpoch;
        private bool clientForceMapReload;
        private bool clientEpochLoadCallbackObserved;
        private ulong lifecycleHighWater;
        private float nextLifecycleScanAt;
        private float nextManifestAt;
        private float nextLifecycleCatalogAt;
        private readonly Dictionary<string, string> lifecyclePrefabKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> lifecycleAmbiguousNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<ulong, float> lifecyclePeerRepairs = new Dictionary<ulong, float>();

        private void AdvanceHostWorldEpoch()
        {
            ResetObjectReplication();
            worldEpoch++;
            if (worldEpoch == 0) worldEpoch = 1;
            // Unity destroys the outgoing scene's roots at the end of the
            // frame. Do not re-register those roots into the new epoch while
            // its map-loader completion callback is still unwinding.
            nextLifecycleScanAt = Time.unscaledTime + 0.5f;
            nextManifestAt = 0f;
            lifecyclePeerRepairs.Clear();
        }

        // Called before QueueClientMapLoad. Even an identical map name needs
        // a fresh vanilla scene when the host clears/reloads that map.
        private bool PrepareClientWorldEpoch(uint epoch)
        {
            if (IsHost || epoch == 0 || (clientWorldEpoch != 0 && epoch <= clientWorldEpoch)) return false;
            EndClientGrab();
            EndClientActivations();
            DestroyRegisteredClientReplicas();
            ResetObjectReplication();
            clientWorldEpoch = epoch;
            sessionActive = false;
            clientSessionStartReceived = false;
            clientRequestedMapIdentity = string.Empty;
            clientMapLoadPending = false;
            clientMapLoadIssued = false;
            clientMapInstanceLoaded = false;
            clientMapSceneTransitionPending = false;
            clientForceMapReload = true;
            clientEpochLoadCallbackObserved = false;
            nextLifecycleScanAt = 0f;
            return true;
        }

        private void ObserveLifecycleSpawn(UserSpawnEventArgs args)
        {
            if (args == null || args.Instance == null || args.SpawnableAsset == null || suppressHostSpawnObservation > 0) return;
            string key = ResolveNetworkSpawnKey(args.SpawnableAsset);
            if (string.IsNullOrEmpty(key)) return;
            ConnectSpawnOrigin origin = args.Instance.GetComponent<ConnectSpawnOrigin>();
            if (origin == null) origin = args.Instance.AddComponent<ConnectSpawnOrigin>();
            origin.SpawnKey = key;
        }

        private void RecordLifecycleNetId(ulong id)
        {
            if (id > lifecycleHighWater) lifecycleHighWater = id;
        }

        private void PumpWorldLifecycle()
        {
            if (!sessionActive || !lobby.HasValue) return;
            float now = Time.unscaledTime;
            if (now >= nextLifecycleScanAt)
            {
                nextLifecycleScanAt = now + 1f;
                ReconcileLocalCatalogObjects();
            }
            if (!IsHost || worldEpoch == 0) return;
            foreach (PPGTogetherIdentity identity in registry.All())
                if (identity != null) RecordLifecycleNetId(identity.NetId);
            foreach (Peer peer in peers.Values)
            {
                if (!LifecyclePeerReady(peer)) continue;
                if (peer.BaselineCyclesRemaining > 0)
                {
                    lifecyclePeerRepairs[peer.SteamId] = now + 15f;
                    continue;
                }
                float next;
                if (!lifecyclePeerRepairs.TryGetValue(peer.SteamId, out next))
                {
                    lifecyclePeerRepairs[peer.SteamId] = now + 15f;
                    continue;
                }
                if (now < next) continue;
                // Never reset a pass still in progress. Large worlds may need
                // longer than the nominal retry interval to finish a baseline.
                lifecyclePeerRepairs[peer.SteamId] = now + 15f;
                BeginRegisteredWorldBaseline(peer);
            }
            if (now < nextManifestAt) return;
            nextManifestAt = now + 2f;
            List<ulong> ids = new List<ulong>();
            foreach (PPGTogetherIdentity identity in registry.All())
                if (identity != null && identity.NetId != 0) ids.Add(identity.NetId);
            // A partial manifest must never delete the omitted tail of a world.
            if (ids.Count > WorldManifestProtocol.MaximumRoots) return;
            byte[] payload = WorldManifestProtocol.Encode(worldEpoch, lifecycleHighWater, ids);
            foreach (Peer peer in peers.Values)
                if (LifecyclePeerReady(peer)) SendToConnection(peer.Connection, WireMessage.WorldManifest, WireChannel.World, peer.PeerId, payload, true);
        }

        private bool LifecyclePeerReady(Peer peer)
        {
            return peer != null && peer.Connection != null && peer.MapStatus == PeerMapStatus.Playing &&
                string.Equals(peer.MapIdentity, activeMapIdentity, StringComparison.Ordinal);
        }

        private void HandleWorldManifest(Envelope envelope)
        {
            WorldManifestData manifest;
            if (IsHost || !sessionActive || !WorldManifestProtocol.TryDecode(envelope.Payload, out manifest) || manifest.Epoch != clientWorldEpoch) return;
            List<PPGTogetherIdentity> removed = new List<PPGTogetherIdentity>();
            foreach (PPGTogetherIdentity identity in registry.All())
                if (identity != null && identity.ReplicatedSpawn && WorldManifestProtocol.IsAbsent(manifest, identity.NetId)) removed.Add(identity);
            for (int i = 0; i < removed.Count; i++) DestroyClientReplica(removed[i]);
        }

        private void DestroyClientReplica(PPGTogetherIdentity identity)
        {
            if (identity == null || !identity.ReplicatedSpawn) return;
            GameObject root = identity.gameObject;
            RemoveClientSnapshotTracking(identity.NetId);
            ReplicatedObjectState.DestroyReplicaParts(identity.NetId);
            ReplicatedObjectState.Forget(identity.NetId);
            registry.Remove(identity);
            identity.NetId = 0;
            if (root != null) { root.SetActive(false); Destroy(root); }
        }

        private void DestroyRegisteredClientReplicas()
        {
            if (IsHost) return;
            List<PPGTogetherIdentity> identities = new List<PPGTogetherIdentity>(registry.All());
            for (int i = 0; i < identities.Count; i++) DestroyClientReplica(identities[i]);
        }

        // Call at connection cleanup (before registry.Clear), not merely on a
        // map reset: epoch/high-water state must survive map transitions.
        private void ResetWorldLifecycle()
        {
            DestroyRegisteredClientReplicas();
            ResetObjectReplication();
            worldEpoch = 0;
            clientWorldEpoch = 0;
            clientForceMapReload = false;
            clientEpochLoadCallbackObserved = false;
            lifecycleHighWater = 0;
            nextLifecycleScanAt = nextManifestAt = nextLifecycleCatalogAt = 0f;
            lifecyclePeerRepairs.Clear();
            lifecyclePrefabKeys.Clear();
            lifecycleAmbiguousNames.Clear();
        }

        private void ReconcileLocalCatalogObjects()
        {
            RefreshLifecycleCatalog();
            PhysicalBehaviour[] physicals = UnityEngine.Object.FindObjectsOfType<PhysicalBehaviour>();
            HashSet<GameObject> checkedRoots = new HashSet<GameObject>();
            for (int i = 0; i < physicals.Length; i++)
            {
                PhysicalBehaviour physical = physicals[i];
                if (physical == null || physical.GetComponentInParent<MapLoaderBehaviour>() != null) continue;
                string key;
                GameObject root = FindLifecycleCatalogRoot(physical.transform, out key);
                if (root == null || !checkedRoots.Add(root)) continue;
                PPGTogetherIdentity existing;
                if (registry.TryGet(root, out existing)) continue;
                if (!IsHost)
                {
                    // A client may already have objects in its local sandbox
                    // when joining. Only locally identifiable catalogue roots
                    // are removed; loader-owned map fixtures are never touched.
                    root.SetActive(false);
                    Destroy(root);
                    continue;
                }
                if (registry.Count >= MaximumNetworkObjects()) continue;
                PPGTogetherIdentity identity = registry.RegisterHost(root, key);
                if (identity == null) continue;
                if (!ReplicatedObjectState.Prime(identity))
                    Logger.LogWarning("[Connect][World] Existing root exceeds supported replicated-state limits: " + SafeName(key) + ".");
                RecordLifecycleNetId(identity.NetId);
                BroadcastSpawn(identity, root);
                Logger.LogInfo("[Connect][World] Reconciled existing catalogue root " + SafeName(key) + ", netId=" + identity.NetId + ".");
            }
        }

        private GameObject FindLifecycleCatalogRoot(Transform start, out string key)
        {
            key = null;
            GameObject candidate = null;
            bool hasAuthoredOrigin = false;
            for (Transform current = start; current != null; current = current.parent)
            {
                PPGTogetherIdentity identity = current.GetComponent<PPGTogetherIdentity>();
                PPGTogetherIdentity registered;
                if (identity != null && registry.TryGet(identity.NetId, out registered) && registered == identity) return null;
                if (current.GetComponent<MapLoaderBehaviour>() != null) return null;
                // Vanilla Spawn writes this explicit asset reference before
                // renaming the root to asset.name (which need not be the
                // prefab's name). It also survives normal serialised copies.
                SerialiseInstructions serialise = current.GetComponent<SerialiseInstructions>();
                SpawnableAsset original = serialise == null ? null : serialise.OriginalSpawnableAsset;
                if (original != null && original.Prefab != null)
                {
                    string originalKey = ResolveNetworkSpawnKey(original);
                    SpawnableAsset resolved = string.IsNullOrEmpty(originalKey) ? null : ModAPI.FindSpawnable(originalKey);
                    if (resolved != null && resolved.Prefab == original.Prefab &&
                        resolved.Prefab.GetComponentInChildren<PhysicalBehaviour>(true) != null)
                    {
                        candidate = current.gameObject;
                        key = originalKey;
                        hasAuthoredOrigin = true;
                        continue;
                    }
                }
                ConnectSpawnOrigin origin = current.GetComponent<ConnectSpawnOrigin>();
                if (origin != null && !string.IsNullOrEmpty(origin.SpawnKey))
                {
                    SpawnableAsset asset = ModAPI.FindSpawnable(origin.SpawnKey);
                    if (asset != null)
                    {
                        candidate = current.gameObject;
                        key = origin.SpawnKey;
                        hasAuthoredOrigin = true;
                    }
                }
                string prefabKey;
                if (!hasAuthoredOrigin && lifecyclePrefabKeys.TryGetValue(NormaliseLifecycleCloneName(current.name), out prefabKey))
                {
                    candidate = current.gameObject;
                    key = prefabKey;
                }
            }
            return candidate;
        }

        private void RefreshLifecycleCatalog()
        {
            if (Time.unscaledTime < nextLifecycleCatalogAt) return;
            nextLifecycleCatalogAt = Time.unscaledTime + 10f;
            lifecyclePrefabKeys.Clear();
            lifecycleAmbiguousNames.Clear();
            SpawnableAsset[] assets = Resources.FindObjectsOfTypeAll<SpawnableAsset>();
            for (int i = 0; i < assets.Length; i++)
            {
                SpawnableAsset asset = assets[i];
                if (asset == null || asset.Prefab == null || asset.Prefab.GetComponentInChildren<PhysicalBehaviour>(true) == null) continue;
                string key = ResolveNetworkSpawnKey(asset);
                SpawnableAsset resolved = string.IsNullOrEmpty(key) ? null : ModAPI.FindSpawnable(key);
                if (resolved == null || resolved.Prefab != asset.Prefab) continue;
                string name = asset.Prefab.name;
                if (lifecycleAmbiguousNames.Contains(name)) continue;
                string previous;
                if (lifecyclePrefabKeys.TryGetValue(name, out previous) && !string.Equals(previous, key, StringComparison.Ordinal))
                {
                    lifecyclePrefabKeys.Remove(name);
                    lifecycleAmbiguousNames.Add(name);
                    continue;
                }
                lifecyclePrefabKeys[name] = key;
            }
        }

        private static string NormaliseLifecycleCloneName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            const string suffix = "(Clone)";
            while (name.EndsWith(suffix, StringComparison.Ordinal)) name = name.Substring(0, name.Length - suffix.Length).TrimEnd();
            return name;
        }

    }
}

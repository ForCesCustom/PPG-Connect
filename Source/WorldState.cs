using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPGTogether.BepInEx
{
    internal sealed class PPGTogetherIdentity : MonoBehaviour
    {
        // Kept as a callback instead of a direct plugin reference so the
        // protocol/world smoke tests can compile this small model on its own.
        internal static Action<PPGTogetherIdentity> DestroyedCallback;
        internal ulong NetId;
        internal string SpawnKey;
        internal bool ReplicatedSpawn;

        private void OnDestroy()
        {
            Action<PPGTogetherIdentity> callback = DestroyedCallback;
            if (callback != null) callback(this);
        }
    }

    internal sealed class WorldRegistry
    {
        private readonly Dictionary<ulong, PPGTogetherIdentity> byId = new Dictionary<ulong, PPGTogetherIdentity>();
        private readonly Dictionary<GameObject, PPGTogetherIdentity> byObject = new Dictionary<GameObject, PPGTogetherIdentity>();
        private ulong nextId = 1;

        internal int Count { get { return byId.Count; } }

        internal PPGTogetherIdentity RegisterHost(GameObject gameObject, string spawnKey)
        {
            if (gameObject == null)
                return null;
            PPGTogetherIdentity identity;
            if (byObject.TryGetValue(gameObject, out identity))
                return identity;
            identity = gameObject.GetComponent<PPGTogetherIdentity>();
            if (identity == null)
                identity = gameObject.AddComponent<PPGTogetherIdentity>();
            identity.NetId = nextId++;
            identity.SpawnKey = spawnKey ?? string.Empty;
            byId[identity.NetId] = identity;
            byObject[gameObject] = identity;
            return identity;
        }

        internal PPGTogetherIdentity RegisterReplica(GameObject gameObject, ulong netId, string spawnKey)
        {
            if (gameObject == null || netId == 0)
                return null;
            PPGTogetherIdentity existing;
            if (byId.TryGetValue(netId, out existing) && existing != null)
                return existing;
            PPGTogetherIdentity identity = gameObject.GetComponent<PPGTogetherIdentity>();
            if (identity == null)
                identity = gameObject.AddComponent<PPGTogetherIdentity>();
            identity.NetId = netId;
            identity.SpawnKey = spawnKey ?? string.Empty;
            identity.ReplicatedSpawn = true;
            byId[netId] = identity;
            byObject[gameObject] = identity;
            if (netId >= nextId) nextId = netId + 1;
            return identity;
        }

        internal bool TryGet(ulong netId, out PPGTogetherIdentity identity)
        {
            if (!byId.TryGetValue(netId, out identity) || identity == null)
            {
                byId.Remove(netId);
                return false;
            }
            return true;
        }

        internal bool TryGet(GameObject gameObject, out PPGTogetherIdentity identity)
        {
            if (gameObject == null || !byObject.TryGetValue(gameObject, out identity) || identity == null)
            {
                identity = null;
                return false;
            }
            return true;
        }

        internal void Remove(GameObject gameObject)
        {
            PPGTogetherIdentity identity;
            if (gameObject != null && byObject.TryGetValue(gameObject, out identity))
                Remove(identity);
        }

        internal void Remove(PPGTogetherIdentity identity)
        {
            if (ReferenceEquals(identity, null)) return;
            PPGTogetherIdentity registered;
            if (byId.TryGetValue(identity.NetId, out registered) && ReferenceEquals(registered, identity))
                byId.Remove(identity.NetId);
            List<GameObject> remove = null;
            foreach (KeyValuePair<GameObject, PPGTogetherIdentity> pair in byObject)
                if (ReferenceEquals(pair.Value, identity))
                {
                    if (remove == null) remove = new List<GameObject>();
                    remove.Add(pair.Key);
                }
            if (remove != null)
                for (int i = 0; i < remove.Count; i++) byObject.Remove(remove[i]);
        }

        internal IEnumerable<PPGTogetherIdentity> All()
        {
            List<PPGTogetherIdentity> result = new List<PPGTogetherIdentity>();
            foreach (PPGTogetherIdentity identity in byId.Values)
                if (identity != null)
                    result.Add(identity);
            return result;
        }

        internal void Clear()
        {
            byId.Clear();
            byObject.Clear();
            // IDs must remain unique for the lifetime of this relay session.
            // A late unreliable pose from the previous map must never target a
            // freshly-spawned object that happened to reuse its old ID.
        }
    }

    internal sealed class ActiveGrab
    {
        internal ulong NetId;
        internal ushort PeerId;
        internal uint Token;
        internal Rigidbody2D Body;
        internal Vector2 LocalPoint;
        internal Vector2 Target;
        internal uint ExpiresAtTick;
    }

    internal sealed class HostGrabController
    {
        private const float NetworkGrabTolerance = 1.35f;
        private readonly WorldRegistry registry;
        private readonly Dictionary<ulong, ActiveGrab> activeByNetId = new Dictionary<ulong, ActiveGrab>();
        private uint nextToken = 1;

        internal HostGrabController(WorldRegistry registry)
        {
            this.registry = registry;
        }

        internal bool TryBegin(ushort peerId, Vector2 point, uint tick, out ActiveGrab grab, out string denial)
        {
            grab = null;
            denial = string.Empty;
            if (!Finite(point)) { denial = "Invalid cursor coordinates"; return false; }
            Collider2D collider = Physics2D.OverlapPoint(point);
            if (collider == null) { denial = "No object under cursor"; return false; }
            PhysicalBehaviour physical = collider.GetComponentInParent<PhysicalBehaviour>();
            if (physical == null) { denial = "Object cannot be grabbed"; return false; }
            // People and some compound vanilla items put their colliders and
            // Rigidbody2D instances on child objects. The network identity is
            // deliberately attached to the spawned root, so a child limb must
            // resolve its parent identity before it can receive a grab lease.
            PPGTogetherIdentity identity = ReplicatedObjectState.FindIdentity(physical);
            return TryBeginPhysical(peerId, identity, physical, point, point, tick, out grab, out denial);
        }

        // Guests name the registered root they are pointing at. The host still
        // validates the local child collider, but no longer searches its whole
        // world for an unrelated object at that coordinate.
        internal bool TryBegin(ushort peerId, ulong netId, Vector2 point, uint tick, out ActiveGrab grab, out string denial)
        {
            grab = null;
            denial = string.Empty;
            if (!Finite(point)) { denial = "Invalid cursor coordinates"; return false; }
            PPGTogetherIdentity identity;
            if (netId == 0 || !registry.TryGet(netId, out identity) || identity == null)
            {
                denial = "Object no longer exists in the host world";
                return false;
            }
            PhysicalBehaviour[] candidates = ReplicatedObjectState.GetPhysicalParts(identity);
            PhysicalBehaviour nearest = null;
            Vector2 nearestAnchor = point;
            float nearestDistanceSquared = NetworkGrabTolerance * NetworkGrabTolerance;
            for (int i = 0; i < candidates.Length; i++)
            {
                PhysicalBehaviour physical = candidates[i];
                if (physical == null || !physical.gameObject.activeInHierarchy || physical.rigidbody == null || !physical.Selectable) continue;
                Collider2D[] colliders = physical.GetComponentsInChildren<Collider2D>(true);
                for (int j = 0; j < colliders.Length; j++)
                {
                    Collider2D collider = colliders[j];
                    if (collider != null && collider.enabled && collider.GetComponentInParent<PhysicalBehaviour>() == physical && collider.OverlapPoint(point))
                        return TryBeginPhysical(peerId, identity, physical, point, point, tick, out grab, out denial);
                    if (collider == null || !collider.enabled || collider.GetComponentInParent<PhysicalBehaviour>() != physical) continue;
                    Vector2 anchor = collider.ClosestPoint(point);
                    float distanceSquared = (anchor - point).sqrMagnitude;
                    if (distanceSquared < nearestDistanceSquared)
                    {
                        nearest = physical;
                        nearestAnchor = anchor;
                        nearestDistanceSquared = distanceSquared;
                    }
                }
            }
            if (nearest != null)
                return TryBeginPhysical(peerId, identity, nearest, point, nearestAnchor, tick, out grab, out denial);
            denial = "Requested object is not under the host cursor";
            return false;
        }

        private bool TryBeginPhysical(ushort peerId, PPGTogetherIdentity identity, PhysicalBehaviour physical, Vector2 target, Vector2 anchorPoint, uint tick, out ActiveGrab grab, out string denial)
        {
            grab = null;
            denial = string.Empty;
            if (physical == null || physical.rigidbody == null || !physical.Selectable) { denial = "Object cannot be grabbed"; return false; }
            PPGTogetherIdentity registered;
            if (identity == null || identity.NetId == 0 || !registry.TryGet(identity.NetId, out registered) || registered != identity)
            {
                denial = "Object was created before this network session";
                return false;
            }
            ActiveGrab existing;
            if (activeByNetId.TryGetValue(identity.NetId, out existing) && existing.ExpiresAtTick >= tick && existing.PeerId != peerId)
            {
                denial = "Object is being used by another player";
                return false;
            }
            grab = new ActiveGrab
            {
                NetId = identity.NetId,
                PeerId = peerId,
                Token = nextToken++,
                Body = physical.rigidbody,
                LocalPoint = physical.rigidbody.transform.InverseTransformPoint(anchorPoint),
                Target = target,
                ExpiresAtTick = tick + 180
            };
            activeByNetId[identity.NetId] = grab;
            return true;
        }

        internal bool Update(ushort peerId, ulong netId, uint token, Vector2 target, uint tick)
        {
            ActiveGrab grab;
            if (!Finite(target) || !activeByNetId.TryGetValue(netId, out grab) || grab.PeerId != peerId || grab.Token != token)
                return false;
            grab.Target = target;
            grab.ExpiresAtTick = tick + 180;
            return true;
        }

        internal void End(ushort peerId, ulong netId, uint token)
        {
            ActiveGrab grab;
            if (activeByNetId.TryGetValue(netId, out grab) && grab.PeerId == peerId && grab.Token == token)
                activeByNetId.Remove(netId);
        }

        internal void ReleasePeer(ushort peerId)
        {
            List<ulong> remove = new List<ulong>();
            foreach (KeyValuePair<ulong, ActiveGrab> pair in activeByNetId)
                if (pair.Value.PeerId == peerId)
                    remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++) activeByNetId.Remove(remove[i]);
        }

        // Bot cleanup never deletes an item while any human or bot holds the
        // authoritative lease.  This is also useful for future world actions.
        internal bool IsActive(ulong netId)
        {
            return activeByNetId.ContainsKey(netId);
        }

        internal void FixedUpdate(uint tick)
        {
            List<ulong> remove = null;
            foreach (KeyValuePair<ulong, ActiveGrab> pair in activeByNetId)
            {
                ActiveGrab grab = pair.Value;
                if (grab.Body == null || grab.ExpiresAtTick < tick)
                {
                    if (remove == null) remove = new List<ulong>();
                    remove.Add(pair.Key);
                    continue;
                }
                Vector2 current = grab.Body.transform.TransformPoint(grab.LocalPoint);
                Vector2 force = (grab.Target - current) * 75f - grab.Body.GetPointVelocity(current) * 14f;
                float maximum = 950f * Mathf.Max(0.1f, grab.Body.mass);
                grab.Body.AddForceAtPosition(Vector2.ClampMagnitude(force, maximum), current, ForceMode2D.Force);
            }
            if (remove != null)
                for (int i = 0; i < remove.Count; i++) activeByNetId.Remove(remove[i]);
        }

        internal void Clear() { activeByNetId.Clear(); }

        private static bool Finite(Vector2 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y);
        }
    }

    // Continuous vanilla Use is stateful: automatic firearms, buttons and a
    // number of Workshop components inspect IsBeingUsedContinuously().  A
    // client therefore owns a short, renewable use lease instead of sending
    // one "fire" packet every frame.  The host performs the actual Use and
    // continuous propagation, so a client cannot make its local physics world
    // authoritative by holding a key.
    internal sealed class ActiveActivation
    {
        internal ulong NetId;
        internal ushort PeerId;
        internal uint ExpiresAtTick;
    }

    internal sealed class HostActivationController
    {
        private readonly Dictionary<ulong, ActiveActivation> activeByNetId = new Dictionary<ulong, ActiveActivation>();

        internal bool TryBegin(ushort peerId, ulong netId, uint tick, out string denial)
        {
            denial = string.Empty;
            ActiveActivation existing;
            if (activeByNetId.TryGetValue(netId, out existing))
            {
                if (existing.ExpiresAtTick < tick)
                {
                    activeByNetId.Remove(netId);
                }
                else if (existing.PeerId != peerId)
                {
                    denial = "This object is already being used by another player.";
                    return false;
                }
                else
                {
                    existing.ExpiresAtTick = tick + 45;
                    return true;
                }
            }
            activeByNetId[netId] = new ActiveActivation { NetId = netId, PeerId = peerId, ExpiresAtTick = tick + 45 };
            return true;
        }

        internal bool Renew(ushort peerId, ulong netId, uint tick)
        {
            ActiveActivation active;
            if (!activeByNetId.TryGetValue(netId, out active) || active.PeerId != peerId || active.ExpiresAtTick < tick)
                return false;
            active.ExpiresAtTick = tick + 45;
            return true;
        }

        internal void End(ushort peerId, ulong netId)
        {
            ActiveActivation active;
            if (activeByNetId.TryGetValue(netId, out active) && active.PeerId == peerId)
                activeByNetId.Remove(netId);
        }

        internal void Remove(ulong netId)
        {
            activeByNetId.Remove(netId);
        }

        internal void ReleasePeer(ushort peerId)
        {
            List<ulong> remove = null;
            foreach (KeyValuePair<ulong, ActiveActivation> pair in activeByNetId)
            {
                if (pair.Value.PeerId != peerId) continue;
                if (remove == null) remove = new List<ulong>();
                remove.Add(pair.Key);
            }
            if (remove == null) return;
            for (int i = 0; i < remove.Count; i++) activeByNetId.Remove(remove[i]);
        }

        internal void FixedUpdate(uint tick, System.Action<ulong> continuousUse)
        {
            List<ulong> remove = null;
            foreach (KeyValuePair<ulong, ActiveActivation> pair in activeByNetId)
            {
                ActiveActivation active = pair.Value;
                if (active.ExpiresAtTick < tick)
                {
                    if (remove == null) remove = new List<ulong>();
                    remove.Add(pair.Key);
                    continue;
                }
                continuousUse(active.NetId);
            }
            if (remove == null) return;
            for (int i = 0; i < remove.Count; i++) activeByNetId.Remove(remove[i]);
        }

        internal bool IsActive(ulong netId)
        {
            return activeByNetId.ContainsKey(netId);
        }

        internal void Clear()
        {
            activeByNetId.Clear();
        }
    }
}

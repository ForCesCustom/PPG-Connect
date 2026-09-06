using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PPGTogether.BepInEx
{
    public sealed partial class PPGTogetherPlugin
    {
        private float nextSharedWorldAt;
        private uint lastGlobalTick, lastWireTick;
        private bool hasGlobalTick, hasWireTick, applyingSharedWorld;
        private readonly Dictionary<int, LineRenderer> replicaWires = new Dictionary<int, LineRenderer>();
        private Material replicaWireMaterial;
        private bool savedGuestGlobal;
        private bool guestOriginalPause, guestOriginalSlow;
        private float guestOriginalSlowScale;
        private SharedGlobalState receivedGlobalState;

        internal bool RouteSharedContextAction(string name)
        {
            NetworkInteraction action;
            switch (name)
            {
                case "FreezeAction": action = NetworkInteraction.Freeze; break;
                case "NoCollideAction": action = NetworkInteraction.NoCollide; break;
                case "WeightlessAction": action = NetworkInteraction.Weightless; break;
                case "IgniteAction": action = NetworkInteraction.Ignite; break;
                default: return false;
            }
            RequestClientContextAction(action);
            return true;
        }

        private bool ApplySharedContextAction(PPGTogetherIdentity identity, NetworkInteraction action)
        {
            SelectionController selection = SelectionController.Main;
            ContextMenuBehaviour context = UnityEngine.Object.FindObjectOfType<ContextMenuBehaviour>();
            if (context == null)
            {
                ContextMenuBehaviour[] menus = Resources.FindObjectsOfTypeAll<ContextMenuBehaviour>();
                foreach (ContextMenuBehaviour menu in menus)
                    if (menu != null && menu.gameObject.scene.IsValid()) { context = menu; break; }
            }
            if (selection == null || context == null || identity == null) return false;
            PhysicalBehaviour[] targets = identity.GetComponentsInChildren<PhysicalBehaviour>();
            if (targets.Length == 0) return false;
            var previous = new List<PhysicalBehaviour>(selection.SelectedObjects);
            try
            {
                selection.ClearSelection();
                selection.Select(targets, true);
                switch (action)
                {
                    case NetworkInteraction.Freeze: context.FreezeAction(); break;
                    case NetworkInteraction.NoCollide: context.NoCollideAction(); break;
                    case NetworkInteraction.Weightless: context.WeightlessAction(); break;
                    case NetworkInteraction.Ignite: context.IgniteAction(); break;
                    default: return false;
                }
                return true;
            }
            finally
            {
                selection.ClearSelection();
                foreach (PhysicalBehaviour physical in previous)
                    if (physical != null) selection.Select(physical, true);
            }
        }

        internal bool RouteWorldCommand(byte command)
        {
            if (applyingSharedWorld || !lobby.HasValue || IsHost) return true;
            if (!CanClientSendWorldRequest()) { SetStatus("Waiting for the shared world to finish loading."); return false; }
            Writer w = new Writer(8); w.Byte(command);
            SendToHost(WireMessage.WorldCommand, WireChannel.World, w.ToArray(), true);
            return false;
        }

        internal bool RouteEnvironmentChange(FieldInfo member, object value)
        {
            if (applyingSharedWorld || !lobby.HasValue || IsHost) return true;
            if (member == null || !CanClientSendWorldRequest()) return false;
            byte field;
            switch (member.Name)
            {
                case "Floodlights": field = 10; break;
                case "Rain": field = 11; break;
                case "Snow": field = 12; break;
                case "Fog": field = 13; break;
                case "Gravity": field = 14; break;
                case "Lightning_chance": field = 15; break;
                case "Ambient_temperature": field = 16; break;
                default: SetStatus("This environment option is not supported by the shared world."); return false;
            }
            float scalar;
            if (field <= 13) { if (!(value is bool)) return false; scalar = (bool)value ? 1f : 0f; }
            else { if (!(value is float)) return false; scalar = (float)value; }
            if (!Finite(scalar)) return false;
            Writer w = new Writer(8); w.Byte(field); w.Float(scalar);
            SendToHost(WireMessage.WorldCommand, WireChannel.World, w.ToArray(), true);
            return false;
        }

        private void HandleWorldCommand(ReceivedPacket packet, Envelope envelope)
        {
            Reader r = new Reader(envelope.Payload); byte command; float scalar = 0;
            if (!r.Byte(out command) || (command >= 10 && !r.Float(out scalar)) || r.Remaining != 0) return;
            if (command > 5 && (command < 10 || command > 16)) return;
            if (command >= 10 && command <= 13 && scalar != 0f && scalar != 1f) return;
            bool allowed = command <= 2 || command == 5 ? hostGuestsCanDeleteSetting.Value : hostGuestsCanActivateSetting.Value;
            if (!allowed || !TryConsumeGuestInteraction(packet.SteamId))
            { SendActionDenied(packet.Connection, "Host permissions or request limit blocked this world action."); return; }
            applyingSharedWorld = true;
            try
            {
                bool applied = true;
                if (command == 0) { var c = FindSceneComponent<ClearButtonBehaviour>(); if (c != null) c.ClearEverything(); else applied = false; }
                else if (command == 1) { var c = FindSceneComponent<ClearLivingBehaviour>(); if (c != null) c.Clear(); else applied = false; }
                else if (command == 2) { var c = FindSceneComponent<ClearDebrisBehaviour>(); if (c != null) c.Clear(); else applied = false; }
                else if (command == 3 && Global.main != null) Global.main.TogglePaused();
                else if (command == 4 && Global.main != null) Global.main.ToggleSlowmotion();
                // Undo operates on the host's shared history; never execute a
                // guest's local undo stack against replica GameObjects.
                else if (command == 5) { var c = FindSceneComponent<UndoControllerBehaviour>(); if (c != null) UndoControllerBehaviour.Undo(); else applied = false; }
                else if (command >= 10 && MapConfig.Instance != null && MapConfig.Instance.Settings != null)
                {
                    EnvironmentalSettings s = MapConfig.Instance.Settings;
                    switch (command)
                    {
                        case 10: s.Floodlights = scalar != 0; break;
                        case 11: s.Rain = scalar != 0; break;
                        case 12: s.Snow = scalar != 0; break;
                        case 13: s.Fog = scalar != 0; break;
                        case 14: s.Gravity = Mathf.Clamp(scalar, -100f, 100f); break;
                        case 15: s.Lightning_chance = Mathf.Clamp01(scalar); break;
                        case 16: s.Ambient_temperature = Mathf.Clamp(scalar, -273.15f, 100000f); break;
                    }
                    MapConfig.Instance.ApplySettings(s);
                }
                else applied = false;
                if (!applied) { SendActionDenied(packet.Connection, "The host cannot perform this action because the required world control is unavailable."); return; }
                nextSharedWorldAt = 0f;
            }
            catch (Exception error)
            {
                Logger.LogWarning("[Connect][World] Shared command " + command + " failed: " + error.Message);
                SendActionDenied(packet.Connection, "The host could not complete this world action.");
            }
            finally { applyingSharedWorld = false; }
        }

        private static T FindSceneComponent<T>() where T : Component
        {
            T active = UnityEngine.Object.FindObjectOfType<T>();
            if (active != null) return active;
            foreach (T component in Resources.FindObjectsOfTypeAll<T>())
                if (component != null && component.gameObject.scene.IsValid() && component.gameObject.scene.isLoaded) return component;
            return null;
        }

        private void PumpSharedWorld()
        {
            if (!sessionActive || Global.main == null) return;
            if (!IsHost) return;
            if (Time.unscaledTime < nextSharedWorldAt) return;
            nextSharedWorldAt = Time.unscaledTime + .1f;
            SharedGlobalState state = new SharedGlobalState();
            state.Paused = Global.main.Paused;
            state.Slow = Global.main.SlowMotion;
            state.SlowScale = Mathf.Clamp(Global.main.SlowmotionTimescale, .01f, 1f);
            if (MapConfig.Instance != null && MapConfig.Instance.Settings != null)
            {
                EnvironmentalSettings s = MapConfig.Instance.Settings;
                state.HasEnvironment = true; state.Lights = s.Floodlights; state.Rain = s.Rain; state.Snow = s.Snow; state.Fog = s.Fog;
                state.Gravity = s.Gravity; state.Lightning = s.Lightning_chance; state.Temperature = s.Ambient_temperature;
            }
            // Bad values from another mod must not interrupt the networking
            // update or be encoded into an invalid authoritative packet.
            if (SharedGlobalCodec.IsValid(state))
                Broadcast(WireMessage.GlobalState, WireChannel.Snapshot, SharedGlobalCodec.Encode(state), false);
            BroadcastWireVisuals();
        }

        private void HandleGlobalState(Envelope envelope)
        {
            SharedGlobalState state;
            if (!SharedGlobalCodec.TryDecode(envelope.Payload, out state) || Global.main == null ||
                (hasGlobalTick && !CursorSequence.IsNewer(envelope.Tick, lastGlobalTick))) return;
            lastGlobalTick = envelope.Tick; hasGlobalTick = true; receivedGlobalState = state;
            if (!savedGuestGlobal)
            {
                guestOriginalPause = Global.main.Paused; guestOriginalSlow = Global.main.SlowMotion;
                guestOriginalSlowScale = Global.main.SlowmotionTimescale; savedGuestGlobal = true;
            }
            ApplyReceivedGlobalState();
            if (state.HasEnvironment && MapConfig.Instance != null && MapConfig.Instance.Settings != null)
            {
                EnvironmentalSettings s = MapConfig.Instance.Settings;
                if (s.Floodlights == state.Lights && s.Rain == state.Rain && s.Snow == state.Snow && s.Fog == state.Fog &&
                    s.Gravity == state.Gravity && s.Lightning_chance == state.Lightning && s.Ambient_temperature == state.Temperature) return;
                s.Floodlights = state.Lights; s.Rain = state.Rain; s.Snow = state.Snow; s.Fog = state.Fog;
                s.Gravity = state.Gravity; s.Lightning_chance = state.Lightning; s.Ambient_temperature = state.Temperature;
                MapConfig.Instance.ApplySettings(s);
            }
        }

        private void ApplyReceivedGlobalState()
        {
            if (!sessionActive || IsHost || !hasGlobalTick || Global.main == null) return;
            applyingSharedWorld = true;
            try
            {
                Global.main.SlowmotionTimescale = receivedGlobalState.SlowScale;
                if (Global.main.Paused != receivedGlobalState.Paused) Global.main.TogglePaused();
                if (Global.main.SlowMotion != receivedGlobalState.Slow) Global.main.ToggleSlowmotion();
            }
            finally { applyingSharedWorld = false; }
        }

        private void BroadcastWireVisuals()
        {
            WireBehaviour[] wires = UnityEngine.Object.FindObjectsOfType<WireBehaviour>();
            List<WireVisualState> records = new List<WireVisualState>();
            foreach (WireBehaviour wire in wires)
            {
                if (wire == null || wire.lineRenderer == null || !wire.lineRenderer.enabled || wire.lineRenderer.positionCount < 2) continue;
                if (records.Count >= WireVisualCodec.MaximumLines) break;
                LineRenderer line = wire.lineRenderer;
                WireVisualState record = new WireVisualState();
                if (!Finite(wire.WireWidth) || !Finite(wire.WireColor.r) || !Finite(wire.WireColor.g) || !Finite(wire.WireColor.b) || !Finite(wire.WireColor.a)) continue;
                record.Id = wire.GetInstanceID(); record.Width = Mathf.Clamp(wire.WireWidth, .005f, 2f);
                record.R = Mathf.Clamp01(wire.WireColor.r); record.G = Mathf.Clamp01(wire.WireColor.g); record.B = Mathf.Clamp01(wire.WireColor.b); record.A = Mathf.Clamp01(wire.WireColor.a);
                int count = Math.Min(line.positionCount, WireVisualCodec.MaximumPoints);
                record.Points = new float[count * 3];
                bool valid = true;
                for (int i = 0; i < count; i++)
                {
                    int source = i * (line.positionCount - 1) / (count - 1);
                    Vector3 p = line.GetPosition(source); if (!line.useWorldSpace) p = line.transform.TransformPoint(p);
                    if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z) || Mathf.Abs(p.x) > 1000000f || Mathf.Abs(p.y) > 1000000f || Mathf.Abs(p.z) > 1000000f) { valid = false; break; }
                    record.Points[i * 3] = p.x; record.Points[i * 3 + 1] = p.y; record.Points[i * 3 + 2] = p.z;
                }
                if (valid) records.Add(record);
            }
            Broadcast(WireMessage.WireVisual, WireChannel.Snapshot, WireVisualCodec.Encode(records), false);
        }

        private void HandleWireVisual(Envelope envelope)
        {
            List<WireVisualState> records;
            if ((hasWireTick && !CursorSequence.IsNewer(envelope.Tick, lastWireTick)) || !WireVisualCodec.TryDecode(envelope.Payload, out records)) return;
            lastWireTick = envelope.Tick; hasWireTick = true;
            HashSet<int> present = new HashSet<int>();
            if (replicaWireMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default"); if (shader == null) return;
                replicaWireMaterial = new Material(shader);
            }
            foreach (WireVisualState record in records)
            {
                present.Add(record.Id); LineRenderer line;
                if (!replicaWires.TryGetValue(record.Id, out line) || line == null)
                {
                    line = new GameObject("Connect Wire Replica").AddComponent<LineRenderer>();
                    line.sharedMaterial = replicaWireMaterial; line.useWorldSpace = true;
                    replicaWires[record.Id] = line;
                }
                line.startWidth = line.endWidth = record.Width;
                line.startColor = line.endColor = new Color(record.R, record.G, record.B, record.A);
                line.positionCount = record.Points.Length / 3;
                for (int i = 0; i < line.positionCount; i++) line.SetPosition(i, new Vector3(record.Points[i*3], record.Points[i*3+1], record.Points[i*3+2]));
            }
            List<int> gone = new List<int>();
            foreach (var pair in replicaWires) if (!present.Contains(pair.Key)) { if (pair.Value != null) Destroy(pair.Value.gameObject); gone.Add(pair.Key); }
            foreach (int id in gone) replicaWires.Remove(id);
        }

        private void ResetSharedWorld()
        {
            foreach (var pair in replicaWires) if (pair.Value != null) Destroy(pair.Value.gameObject);
            replicaWires.Clear();
            if (replicaWireMaterial != null) Destroy(replicaWireMaterial);
            if (savedGuestGlobal && Global.main != null)
            {
                bool previousApplying = applyingSharedWorld;
                applyingSharedWorld = true;
                try
                {
                    Global.main.SlowmotionTimescale = guestOriginalSlowScale;
                    if (Global.main.Paused != guestOriginalPause) Global.main.TogglePaused();
                    if (Global.main.SlowMotion != guestOriginalSlow) Global.main.ToggleSlowmotion();
                }
                finally { applyingSharedWorld = previousApplying; }
            }
            savedGuestGlobal = false; hasGlobalTick = false; hasWireTick = false; nextSharedWorldAt = 0;
        }
    }

}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using PPGTogether.BepInEx;
using UnityEngine;

// Developer-only fixture: production receive path and real Unity LineRenderers.
// No Steam peers are faked. Never distribute this assembly in a release.
[BepInPlugin("local.connect.wire-visual-smoke", "Connect Wire Visual Smoke", "1.0.0")]
public sealed class WireVisualRuntimeSmoke : BaseUnityPlugin
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private object plugin;
    private Type pluginType;
    private int checks;
    private bool ownsFixture;
    private GameObject nativeReference;

    private void Awake()
    {
        foreach (string arg in Environment.GetCommandLineArgs())
            if (arg == "-connect-replication-smoke") { StartCoroutine(Run()); break; }
    }

    private IEnumerator Run()
    {
        while (Global.main == null || CatalogBehaviour.Main == null) yield return null;
        yield return null;
        try
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type candidate = assembly.GetType("PPGTogether.BepInEx.PPGTogetherPlugin", false);
                if (candidate == null) continue;
                object instance = candidate.GetField("Instance", Flags).GetValue(null);
                if (instance != null) { plugin = instance; pluginType = candidate; break; }
            }
            Check(plugin != null, "production plugin present");
            Check(pluginType.GetField("lobby", Flags).GetValue(plugin) == null, "refuse existing lobby");
            Check(Lines.Count == 0, "refuse existing replicated wire set");
            ownsFixture = true;
            List<WireVisualState> source = new List<WireVisualState>();
            for (int i = 0; i < 130; i++) source.Add(new WireVisualState {
                Id = i + 1, Width = .02f, R = .2f, G = .3f, B = .4f, A = 1f,
                Points = new float[] { i, 5, 0, i + .5f, 6, 0 }
            });
            List<byte[]> first = WireVisualSnapshotCodec.Encode(1, source);
            Receive(first[1]); Check(Lines.Count == 0, "second page alone creates no partial scene");
            Receive(first[0]); Check(Lines.Count == 130, "all130 host lines visible after complete revision");
            // Compare against the actual engine's set/get contract, not an
            // assumed lossless Color/gradient representation. This reference
            // never enters Connect, its codec or its replica collection.
            nativeReference = new GameObject("Connect Wire Smoke Native Reference");
            LineRenderer reference = nativeReference.AddComponent<LineRenderer>();
            reference.enabled = false;
            reference.startWidth = reference.endWidth = .02f;
            reference.startColor = reference.endColor = new Color(.2f, .3f, .4f, 1);
            Logger.LogInfo("[ConnectWireSmoke] Native readback width=" + reference.startWidth.ToString("R") +
                " RGBA=" + ColorValues(reference.startColor) + "; production width=" + Lines[1].startWidth.ToString("R") +
                " RGBA=" + ColorValues(Lines[1].startColor) + "; assigned width=0.02 RGBA=0.2,0.3,0.4,1");
            for (int i = 0; i < 130; i++)
            {
                LineRenderer line = Lines[i + 1];
                Check(line != null && line.positionCount == 2 && line.GetPosition(1) == new Vector3(i + .5f, 6, 0), "exact worldspace endpoint " + i);
                Check(line.startWidth == reference.startWidth && line.endWidth == reference.endWidth &&
                    line.startColor == reference.startColor && line.endColor == reference.endColor,
                    "width and both endpoint colors match independently assigned native renderer " + i);
            }
            source[0].Points[0] = -10;
            List<byte[]> second = WireVisualSnapshotCodec.Encode(2, source);
            Receive(second[0]); Check(Lines.Count == 130 && Lines[1].GetPosition(0).x == 0, "missing page preserves complete view");
            Receive(first[1]); Check(Lines[1].GetPosition(0).x == 0, "old page cannot complete pending revision");
            Receive(second[1]); Check(Lines[1].GetPosition(0).x == -10, "complete update applies new endpoints");
            Receive(WireVisualSnapshotCodec.Encode(3, new List<WireVisualState>())[0]);
            Check(Lines.Count == 0, "complete empty snapshot removes all replica lines");
            Receive(second[0]); Check(Lines.Count == 0, "stale update does not resurrect cleared lines");
            Logger.LogInfo("[ConnectWireSmoke] PASSED " + checks + " assertions: production atomic wire receive and130 real LineRenderers. Not a two-account test.");
        }
        catch (Exception error) { Logger.LogError("[ConnectWireSmoke] FAILED: " + error); }
        finally
        {
            if (nativeReference != null) Destroy(nativeReference);
            if (ownsFixture && plugin != null) pluginType.GetMethod("ResetSharedWorld", Flags).Invoke(plugin, null);
        }
    }

    private Dictionary<int, LineRenderer> Lines
    { get { return (Dictionary<int, LineRenderer>)pluginType.GetField("replicaWires", Flags).GetValue(plugin); } }

    private void Receive(byte[] payload)
    {
        Type envelopeType = pluginType.Assembly.GetType("PPGTogether.BepInEx.Envelope", true);
        object envelope = Activator.CreateInstance(envelopeType);
        envelopeType.GetField("Payload", Flags).SetValue(envelope, payload);
        pluginType.GetMethod("HandleWireVisual", Flags).Invoke(plugin, new object[] { envelope });
    }

    private void Check(bool condition, string label)
    { checks++; if (!condition) throw new Exception(label); }

    private static string ColorValues(Color color)
    { return color.r.ToString("R") + "," + color.g.ToString("R") + "," + color.b.ToString("R") + "," + color.a.ToString("R"); }
}

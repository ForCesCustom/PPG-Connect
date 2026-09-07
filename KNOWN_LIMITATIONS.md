# Known limitations — Connect v0.1.47

- This remains a multiplayer prototype. A successful compile, codec test or
  single-process runtime check does not establish complete two-account Steam
  synchronization. Test representative scenes on both accounts.
- Every player needs the same complete v0.1.47 package, People Playground
  1.27.17 build and installed content. There is no Workshop downloading,
  mod-set/content-hash agreement or arbitrary asset transfer.
- World discovery recognises local catalogue roots, including observed
  pre-session spawns and ordinary copied objects. Map-loader fixtures are
  excluded. Custom unnamed roots and arbitrary saved object graphs are not
  reconstructed. Catalogue-authored node paths ignore added runtime effects.
  Parts already detached before initial discovery cannot be rediscovered from
  the root; ambiguous same-name authored nodes remain unavailable rather than
  mapping state onto an unrelated part.
- Typed state is limited to **256 authored transform nodes per root**. It covers
  poses, scale, supported renderer/collider state and explicitly encoded
  physical/limb/skin values. It is not a general Unity component serializer.
  Cached references preserve existing parts after host detachment; newly
  created hierarchy nodes, arbitrary reparenting and full dynamic topology
  are not reconstructed.
- Native skin wound points (including bullet/stab marks and their age) now
  transfer separately, up to 128 points per cached skin component. The
  wound extension passed codec and single-process engine capture/apply tests.
  Complete custom wound textures, cut geometry, blood decals, projectile visuals,
  explosions, particles, audio and arbitrary Workshop scripts are not fully
  replicated. Selected health, heat/fire and skin acid/rot values do not
  imply every injury effect is identical.
- Host wire rendering is bounded to **128 lines**, with bounded points per
  line. Guest wire creation and electrical/joint simulation are not supported.
  Rendered lines are a view of host wiring, not independent guest mechanisms.
- Guest context actions implemented through the host are Activate/Delete,
  Freeze, No Collide, Weightless and Ignite. Copy/Save/Follow stay local.
  Paste/Load, resize/layer/amalgamation and arbitrary dynamic mod actions remain
  unsupported on guests. Context actions address registered roots, so a
  compound root may be affected more broadly than one selected limb.
- Guest Clear and Undo use the host's permissions and shared world/history.
  Undo is not a separate per-player history. Pause, slow motion and the listed
  environment fields are shared requests, not independent client settings.
- The host's object cap bounds Connect-controlled requests/discovery, not
  every vanilla host spawn path. Complete manifests support up to 1000 roots;
  a larger manifest is omitted rather than sending an incomplete list that
  could delete valid replicas. Large worlds update over multiple bounded
  ticks; simultaneous pixel-identical views at every instant are not promised.
- Camera, zoom, cursor appearance, local selection and menus intentionally
  remain independent. Network delay affects when each player sees an update.
- No public lobby browser or host migration is implemented. Bot intelligence
  runs only on the host, with up to three bots and bounded actions.
- The patches target the inspected 1.27.17 APIs. Revalidate targets and runtime
  behavior after a game update. Missing patch targets are logged; do not claim
  safe synchronization on a build whose required patches failed.

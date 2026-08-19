using System;
using System.Collections.Generic;

namespace PPGTogether.BepInEx
{
    // The virtual cursor never drives Unity input. This deterministic layer
    // produces intentions; PPGTogetherPlugin validates and executes them on
    // the host exactly like a connected player action.
    internal enum BotAction { Idle, Wander, Explore, Inspect, Spawn, GrabAndPlace, Activate, Cleanup, Recover }
    internal enum BotPersonality { Builder, Mover, Cleaner }
    internal enum BotObjectKind { Unknown, Living, Weapon, Medical, Mechanism, Vehicle, Construction, Material, Container, Electronic, Explosive, Debris }
    internal enum BotGoalKind { None, Survey, BuildScene, ArrangeObjects, RunExperiment, AssistLiving, CleanWorkspace, AvoidDanger, InspectNovelObject }
    internal enum BotOutcome { Success, Denied, MissingTarget, Timeout, Unsafe, Interrupted }

    internal struct BotPoint
    {
        internal float X; internal float Y;
        internal BotPoint(float x, float y) { X = x; Y = y; }
        internal float DistanceSquared(BotPoint other) { float x = X - other.X; float y = Y - other.Y; return x * x + y * y; }
        internal static BotPoint Lerp(BotPoint a, BotPoint b, float t)
        {
            if (t < 0f) t = 0f; if (t > 1f) t = 1f;
            return new BotPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }
    }

    internal sealed class BotKnowledgeEntity
    {
        internal ulong Key; internal BotObjectKind Kind; internal BotPoint Position; internal BotPoint Velocity;
        internal string Name; internal bool IsNetworked; internal bool CanGrab; internal bool CanActivate; internal bool CanDelete;
        internal bool IsLiving; internal bool IsDangerous; internal bool IsSleeping; internal float Danger; internal float Novelty;
        internal float Age; internal float Value; internal int Cluster;
    }

    internal sealed class BotPerception
    {
        internal readonly List<BotKnowledgeEntity> Entities = new List<BotKnowledgeEntity>();
        internal readonly List<BotPoint> Frontier = new List<BotPoint>();
        internal BotPoint Position; internal float LocalDanger; internal float Crowd; internal float Debris; internal float LivingCount;
        internal float NetworkedCount; internal float MapInterest; internal bool CanSpawn; internal bool CanGrab; internal bool CanActivate; internal bool CanCleanup;
    }

    internal sealed class BotDecision
    {
        internal BotAction Action; internal BotGoalKind Goal; internal ulong TargetKey; internal BotPoint Target; internal BotPoint Placement;
        internal BotObjectKind SpawnKind; internal bool ContinuousUse; internal float Utility; internal string Rationale;
    }

    internal sealed class BotGoal
    {
        internal BotGoalKind Kind; internal ulong TargetKey; internal BotPoint Point; internal BotObjectKind SpawnKind;
        internal float Utility; internal float CreatedAt; internal float ExpiresAt; internal string ClaimKey;
    }

    internal sealed class BotPlanStep
    {
        internal BotAction Action; internal ulong TargetKey; internal BotPoint Target; internal BotPoint Placement;
        internal BotObjectKind SpawnKind; internal bool ContinuousUse; internal string Label;
    }

    internal sealed class BotPlan
    {
        private readonly List<BotPlanStep> steps = new List<BotPlanStep>();
        private int next;
        internal bool Complete { get { return next >= steps.Count; } }
        internal void Add(BotPlanStep step) { if (step != null && steps.Count < 4) steps.Add(step); }
        internal BotPlanStep Current() { return Complete ? null : steps[next]; }
        internal void Advance() { if (!Complete) next++; }
        internal void Clear() { steps.Clear(); next = 0; }
    }

    internal sealed class BotPersonalityProfile
    {
        internal float Build; internal float Move; internal float Clean; internal float Explore; internal float Caution; internal float Curiosity;
        internal static BotPersonalityProfile Create(BotPersonality kind)
        {
            BotPersonalityProfile p = new BotPersonalityProfile();
            if (kind == BotPersonality.Builder) { p.Build = 1.35f; p.Move = .8f; p.Clean = .55f; p.Explore = .9f; p.Caution = .85f; p.Curiosity = 1.15f; }
            else if (kind == BotPersonality.Mover) { p.Build = .8f; p.Move = 1.35f; p.Clean = .65f; p.Explore = 1.25f; p.Caution = .8f; p.Curiosity = 1.2f; }
            else { p.Build = .65f; p.Move = .85f; p.Clean = 1.4f; p.Explore = .9f; p.Caution = 1.25f; p.Curiosity = .75f; }
            return p;
        }
    }

    internal sealed class BotMood
    {
        internal float Curiosity = .65f; internal float Confidence = .55f; internal float Stress; internal float Satisfaction = .5f;
        internal void Update(BotPerception p, float delta)
        {
            if (delta < 0f) delta = 0f; if (delta > 1f) delta = 1f;
            float danger = p == null ? 0f : Clamp01(p.LocalDanger * .65f + p.Crowd * .05f);
            Stress += (danger - Stress) * delta * .25f;
            Curiosity += ((p != null && p.MapInterest < .2f ? .85f : .48f) - Curiosity) * delta * .08f;
            Confidence += ((Stress > .65f ? .35f : .65f) - Confidence) * delta * .05f;
        }
        internal void Observe(BotOutcome outcome)
        {
            if (outcome == BotOutcome.Success) { Confidence = Clamp01(Confidence + .07f); Satisfaction = Clamp01(Satisfaction + .08f); Stress = Clamp01(Stress - .06f); }
            else if (outcome == BotOutcome.Unsafe) { Stress = Clamp01(Stress + .22f); Confidence = Clamp01(Confidence - .08f); }
            else { Confidence = Clamp01(Confidence - .025f); Curiosity = Clamp01(Curiosity + .025f); }
        }
        private static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
    }

    internal sealed class BotActionMemory
    {
        internal int Attempts; internal int Successes; internal int Failures; internal float MeanReward; internal float LastAt;
        internal float Reliability() { return Attempts == 0 ? .5f : (Successes + 1f) / (Attempts + 2f); }
        internal void Record(BotOutcome outcome, float now)
        {
            Attempts++; LastAt = now; float reward = outcome == BotOutcome.Success ? 1f : outcome == BotOutcome.Unsafe ? -1f : -.45f;
            if (outcome == BotOutcome.Success) Successes++; else Failures++;
            MeanReward += (reward - MeanReward) / Attempts;
        }
    }

    // Memory is intentionally session-local. It adapts behaviour without
    // storing player data or changing any item outside the active map.
    internal sealed class BotMemory
    {
        private readonly Dictionary<string, BotActionMemory> actions = new Dictionary<string, BotActionMemory>();
        private readonly Dictionary<ulong, float> inspectedAt = new Dictionary<ulong, float>();
        private readonly Dictionary<BotObjectKind, float> spawnedAt = new Dictionary<BotObjectKind, float>();
        private readonly List<BotGoalKind> recentGoals = new List<BotGoalKind>();
        internal float Estimate(string action) { BotActionMemory m; return actions.TryGetValue(action, out m) ? m.MeanReward * .2f + (m.Reliability() - .5f) * .25f : 0f; }
        internal float CooldownPenalty(BotObjectKind kind, float now)
        {
            float last; if (!spawnedAt.TryGetValue(kind, out last)) return 0f;
            float age = now - last; return age >= 14f ? 0f : (14f - age) / 14f;
        }
        internal float InspectionNovelty(ulong target, float now)
        {
            float last; if (!inspectedAt.TryGetValue(target, out last)) return 1f;
            float age = now - last; return age >= 22f ? 1f : age / 22f;
        }
        internal float GoalRepetitionPenalty(BotGoalKind goal)
        {
            int count = 0; for (int i = 0; i < recentGoals.Count; i++) if (recentGoals[i] == goal) count++;
            return count * .16f;
        }
        internal void RecordGoal(BotGoalKind goal) { recentGoals.Add(goal); if (recentGoals.Count > 6) recentGoals.RemoveAt(0); }
        internal void Record(BotDecision d, BotOutcome outcome, float now)
        {
            if (d == null) return; string key = d.Goal.ToString() + ":" + d.Action.ToString(); BotActionMemory memory;
            if (!actions.TryGetValue(key, out memory)) { memory = new BotActionMemory(); actions.Add(key, memory); }
            memory.Record(outcome, now); if (d.TargetKey != 0) inspectedAt[d.TargetKey] = now;
            if (d.Action == BotAction.Spawn && outcome == BotOutcome.Success) spawnedAt[d.SpawnKind] = now;
        }
        internal void Clear() { actions.Clear(); inspectedAt.Clear(); spawnedAt.Clear(); recentGoals.Clear(); }
    }

    internal sealed class BotDeterministicRandom
    {
        private uint state;
        internal BotDeterministicRandom(uint seed) { state = seed == 0 ? 0xA341316Cu : seed; }
        internal uint Next() { uint v = state; v ^= v << 13; v ^= v >> 17; v ^= v << 5; state = v; return v; }
        internal float Value() { return (Next() & 0x00FFFFFFu) / 16777215f; }
        internal int Range(int min, int max) { return max <= min ? min : min + (int)(Value() * (max - min)); }
    }

    internal sealed class BotCoordinationClaim { internal string Key; internal ushort Owner; internal BotGoalKind Goal; internal float ExpiresAt; }
    internal sealed class BotCoordinationBoard
    {
        private readonly Dictionary<string, BotCoordinationClaim> claims = new Dictionary<string, BotCoordinationClaim>();
        private readonly Dictionary<ushort, string> owners = new Dictionary<ushort, string>();
        internal bool TryClaim(ushort owner, string key, BotGoalKind goal, float now, float seconds)
        {
            if (string.IsNullOrEmpty(key)) return false; Prune(now); BotCoordinationClaim existing;
            if (claims.TryGetValue(key, out existing) && existing.Owner != owner) return false;
            string old; if (owners.TryGetValue(owner, out old) && old != key) Release(owner);
            BotCoordinationClaim claim = existing ?? new BotCoordinationClaim(); claim.Key = key; claim.Owner = owner; claim.Goal = goal; claim.ExpiresAt = now + (seconds < 1f ? 1f : seconds);
            claims[key] = claim; owners[owner] = key; return true;
        }
        internal bool IsClaimedByAnother(ushort owner, string key, float now) { Prune(now); BotCoordinationClaim claim; return claims.TryGetValue(key, out claim) && claim.Owner != owner; }
        internal void Release(ushort owner)
        {
            string key; if (!owners.TryGetValue(owner, out key)) return; owners.Remove(owner); BotCoordinationClaim claim;
            if (claims.TryGetValue(key, out claim) && claim.Owner == owner) claims.Remove(key);
        }
        internal void Prune(float now)
        {
            List<string> stale = null; foreach (KeyValuePair<string, BotCoordinationClaim> pair in claims) if (pair.Value.ExpiresAt < now) { if (stale == null) stale = new List<string>(); stale.Add(pair.Key); }
            if (stale == null) return; for (int i = 0; i < stale.Count; i++) { BotCoordinationClaim c = claims[stale[i]]; claims.Remove(stale[i]); string key; if (owners.TryGetValue(c.Owner, out key) && key == stale[i]) owners.Remove(c.Owner); }
        }
        internal void Clear() { claims.Clear(); owners.Clear(); }
    }

    internal sealed class BotMind
    {
        internal readonly BotPersonality Personality; internal readonly BotPersonalityProfile Profile; internal readonly BotMemory Memory = new BotMemory(); internal readonly BotMood Mood = new BotMood();
        private readonly BotDeterministicRandom random; private readonly BotPlan plan = new BotPlan(); private BotGoal activeGoal; private BotDecision issued; private float lastThinkAt;
        internal BotMind(BotPersonality personality, uint seed) { Personality = personality; Profile = BotPersonalityProfile.Create(personality); random = new BotDeterministicRandom(seed); }

        internal BotDecision Decide(ushort peer, BotPerception p, BotCoordinationBoard board, float now)
        {
            float delta = lastThinkAt <= 0f ? .25f : now - lastThinkAt; if (delta < 0f) delta = 0f; if (delta > 2f) delta = 2f; lastThinkAt = now; Mood.Update(p, delta); board.Prune(now);
            if (activeGoal != null && activeGoal.ExpiresAt > now && !plan.Complete && GoalStillValid(activeGoal, p, board, peer, now)) { issued = MakeDecision(plan.Current(), activeGoal); return issued; }
            board.Release(peer); plan.Clear(); activeGoal = SelectGoal(peer, p, board, now); if (activeGoal == null) activeGoal = ExploreGoal(p, now);
            if (!board.TryClaim(peer, activeGoal.ClaimKey, activeGoal.Kind, now, activeGoal.ExpiresAt - now)) { activeGoal = ExploreGoal(p, now); board.TryClaim(peer, activeGoal.ClaimKey, activeGoal.Kind, now, activeGoal.ExpiresAt - now); }
            Memory.RecordGoal(activeGoal.Kind); BuildPlan(activeGoal, p); issued = MakeDecision(plan.Current(), activeGoal); return issued;
        }

        internal void ReportOutcome(ushort peer, BotOutcome outcome, BotCoordinationBoard board, float now)
        {
            if (issued == null) return; Memory.Record(issued, outcome, now); Mood.Observe(outcome);
            if (outcome == BotOutcome.Success && !plan.Complete) { plan.Advance(); if (!plan.Complete) return; }
            board.Release(peer); activeGoal = null; issued = null; plan.Clear();
        }
        internal void Cancel(ushort peer, BotCoordinationBoard board) { board.Release(peer); activeGoal = null; issued = null; plan.Clear(); }

        private BotGoal SelectGoal(ushort peer, BotPerception p, BotCoordinationBoard board, float now)
        {
            if (p == null) return null; List<BotGoal> candidates = new List<BotGoal>();
            if (p.LocalDanger > .72f) candidates.Add(Goal(BotGoalKind.AvoidDanger, 1.9f * Profile.Caution + Mood.Stress, 8f, SafeFrontier(p), 0, BotObjectKind.Unknown, "safe:" + peer));
            BotKnowledgeEntity novel = Best(p, peer, board, now, true, false, false, BotObjectKind.Unknown);
            if (novel != null) candidates.Add(Goal(BotGoalKind.InspectNovelObject, (.45f + novel.Novelty + Memory.InspectionNovelty(novel.Key, now) * .45f) * Profile.Curiosity, 12f, novel.Position, novel.Key, BotObjectKind.Unknown, "inspect:" + novel.Key));
            BotKnowledgeEntity clutter = Cleanup(p, peer, board, now);
            if (clutter != null && p.CanCleanup) candidates.Add(Goal(BotGoalKind.CleanWorkspace, (.35f + p.Debris * .15f + clutter.Age * .02f) * Profile.Clean, 15f, clutter.Position, clutter.Key, BotObjectKind.Unknown, "clean:" + clutter.Key));
            BotKnowledgeEntity moving = Best(p, peer, board, now, true, true, false, BotObjectKind.Unknown);
            if (moving != null && p.CanGrab) candidates.Add(Goal(BotGoalKind.ArrangeObjects, (.3f + moving.Novelty * .45f) * Profile.Move, 13f, moving.Position, moving.Key, BotObjectKind.Unknown, "move:" + moving.Key));
            BotKnowledgeEntity weapon = Best(p, peer, board, now, true, false, true, BotObjectKind.Weapon);
            if (weapon != null && p.LivingCount > 0f && Mood.Stress < .68f) candidates.Add(Goal(BotGoalKind.RunExperiment, (.28f + weapon.Novelty) * Profile.Move, 10f, weapon.Position, weapon.Key, BotObjectKind.Unknown, "use:" + weapon.Key));
            if (p.CanSpawn)
            {
                BotObjectKind kind = ChooseSpawnKind(p, now); float score = (.3f + Mood.Curiosity * .35f + (1f - p.MapInterest) * .25f) * Profile.Build - Memory.CooldownPenalty(kind, now) * .65f;
                if (kind == BotObjectKind.Living && p.LivingCount >= 1f) score -= 1.1f;
                candidates.Add(Goal(BotGoalKind.BuildScene, score, 14f, SpawnPoint(p), 0, kind, "spawn:" + kind));
            }
            if (p.Frontier.Count > 0) candidates.Add(Goal(BotGoalKind.Survey, (.28f + Mood.Curiosity * .55f + (1f - p.MapInterest) * .45f) * Profile.Explore, 11f, Frontier(p), 0, BotObjectKind.Unknown, "survey:" + Cell(Frontier(p))));
            BotGoal best = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                BotGoal c = candidates[i]; c.Utility += Memory.Estimate(c.Kind.ToString()) - Memory.GoalRepetitionPenalty(c.Kind) + random.Value() * .08f;
                if (board.IsClaimedByAnother(peer, c.ClaimKey, now)) continue; if (best == null || c.Utility > best.Utility) best = c;
            }
            return best;
        }

        private bool GoalStillValid(BotGoal g, BotPerception p, BotCoordinationBoard board, ushort peer, float now)
        {
            if (g == null || p == null || board.IsClaimedByAnother(peer, g.ClaimKey, now)) return false; if (g.Kind == BotGoalKind.AvoidDanger) return p.LocalDanger > .3f; if (g.TargetKey == 0) return true;
            for (int i = 0; i < p.Entities.Count; i++) if (p.Entities[i].Key == g.TargetKey) return true; return false;
        }
        private BotGoal ExploreGoal(BotPerception p, float now) { BotPoint point = p != null && p.Frontier.Count > 0 ? Frontier(p) : new BotPoint(); return Goal(BotGoalKind.Survey, .1f, 8f, point, 0, BotObjectKind.Unknown, "survey:" + Cell(point)); }
        private BotGoal Goal(BotGoalKind kind, float utility, float life, BotPoint point, ulong target, BotObjectKind spawn, string claim)
        {
            BotGoal g = new BotGoal(); g.Kind = kind; g.Utility = utility; g.CreatedAt = lastThinkAt; g.ExpiresAt = lastThinkAt + life; g.Point = point; g.TargetKey = target; g.SpawnKind = spawn; g.ClaimKey = claim; return g;
        }
        private void BuildPlan(BotGoal g, BotPerception p)
        {
            if (g.Kind == BotGoalKind.BuildScene)
            {
                plan.Add(Step(BotAction.Spawn, g.Point, g.Point, 0, g.SpawnKind, "create " + g.SpawnKind));
                plan.Add(Step(BotAction.Explore, g.Point, g.Point, 0, BotObjectKind.Unknown, "review new scene area"));
                return;
            }
            if (g.Kind == BotGoalKind.ArrangeObjects)
            {
                plan.Add(Step(BotAction.GrabAndPlace, g.Point, Placement(g, p), g.TargetKey, BotObjectKind.Unknown, "arrange object"));
                plan.Add(Step(BotAction.Inspect, g.Point, g.Point, g.TargetKey, BotObjectKind.Unknown, "review placement"));
                return;
            }
            if (g.Kind == BotGoalKind.CleanWorkspace)
            {
                plan.Add(Step(BotAction.Inspect, g.Point, g.Point, g.TargetKey, BotObjectKind.Unknown, "check cleanup target"));
                plan.Add(Step(BotAction.Cleanup, g.Point, g.Point, g.TargetKey, BotObjectKind.Unknown, "clear safe debris"));
                return;
            }
            if (g.Kind == BotGoalKind.RunExperiment)
            {
                plan.Add(Step(BotAction.Inspect, g.Point, g.Point, g.TargetKey, BotObjectKind.Unknown, "inspect experiment tool"));
                plan.Add(Step(BotAction.Activate, g.Point, g.Point, g.TargetKey, BotObjectKind.Unknown, "use selected mechanism"));
                return;
            }
            if (g.Kind == BotGoalKind.AvoidDanger) { plan.Add(Step(BotAction.Recover, g.Point, g.Point, 0, BotObjectKind.Unknown, "leave danger")); return; }
            if (g.Kind == BotGoalKind.InspectNovelObject) { plan.Add(Step(BotAction.Inspect, g.Point, g.Point, g.TargetKey, BotObjectKind.Unknown, "inspect novel object")); return; }
            plan.Add(Step(BotAction.Explore, g.Point, g.Point, 0, BotObjectKind.Unknown, "survey map frontier"));
        }
        private static BotPlanStep Step(BotAction action, BotPoint target, BotPoint place, ulong id, BotObjectKind kind, string label)
        {
            BotPlanStep s = new BotPlanStep(); s.Action = action; s.Target = target; s.Placement = place; s.TargetKey = id; s.SpawnKind = kind; s.ContinuousUse = false; s.Label = label; return s;
        }
        private static BotDecision MakeDecision(BotPlanStep s, BotGoal g)
        {
            BotDecision d = new BotDecision(); if (s == null) { d.Action = BotAction.Wander; d.Goal = BotGoalKind.Survey; d.Rationale = "no viable plan"; return d; }
            d.Action = s.Action; d.Goal = g.Kind; d.TargetKey = s.TargetKey; d.Target = s.Target; d.Placement = s.Placement; d.SpawnKind = s.SpawnKind; d.ContinuousUse = s.ContinuousUse; d.Utility = g.Utility; d.Rationale = s.Label; return d;
        }
        private BotObjectKind ChooseSpawnKind(BotPerception p, float now)
        {
            BotObjectKind[] kinds = Personality == BotPersonality.Builder ? new[] { BotObjectKind.Construction, BotObjectKind.Material, BotObjectKind.Mechanism, BotObjectKind.Electronic, BotObjectKind.Container } : Personality == BotPersonality.Mover ? new[] { BotObjectKind.Material, BotObjectKind.Vehicle, BotObjectKind.Mechanism, BotObjectKind.Container, BotObjectKind.Weapon } : new[] { BotObjectKind.Container, BotObjectKind.Medical, BotObjectKind.Material, BotObjectKind.Construction, BotObjectKind.Electronic };
            BotObjectKind best = kinds[0]; float value = SpawnValue(best, p, now);
            for (int i = 1; i < kinds.Length; i++) { float next = SpawnValue(kinds[i], p, now); if (next > value) { best = kinds[i]; value = next; } }
            // Living subjects are supported content, not a default/fallback.
            if (p.LivingCount < .5f && random.Value() < .035f && Memory.CooldownPenalty(BotObjectKind.Living, now) <= 0f) return BotObjectKind.Living;
            return best;
        }
        private float SpawnValue(BotObjectKind kind, BotPerception p, float now)
        {
            float v = .6f - Memory.CooldownPenalty(kind, now); if (kind == BotObjectKind.Construction || kind == BotObjectKind.Material) v += Profile.Build * .3f; if (kind == BotObjectKind.Container && p.Debris > 2f) v += Profile.Clean * .35f; if (kind == BotObjectKind.Mechanism || kind == BotObjectKind.Electronic) v += Profile.Curiosity * .2f; if (kind == BotObjectKind.Weapon && Mood.Stress > .4f) v -= 1f; return v + random.Value() * .1f;
        }
        private static BotKnowledgeEntity Best(BotPerception p, ushort peer, BotCoordinationBoard board, float now, bool network, bool grab, bool activate, BotObjectKind kind)
        {
            BotKnowledgeEntity best = null; float score = float.MinValue;
            for (int i = 0; i < p.Entities.Count; i++) { BotKnowledgeEntity e = p.Entities[i]; if (network && !e.IsNetworked || grab && !e.CanGrab || activate && !e.CanActivate || kind != BotObjectKind.Unknown && e.Kind != kind || e.IsLiving && grab || e.IsDangerous && e.Danger > .8f) continue; if (board.IsClaimedByAnother(peer, "inspect:" + e.Key, now)) continue; float value = e.Novelty + e.Value - e.Position.DistanceSquared(p.Position) * .012f; if (value > score) { score = value; best = e; } }
            return best;
        }
        private static BotKnowledgeEntity Cleanup(BotPerception p, ushort peer, BotCoordinationBoard board, float now)
        {
            BotKnowledgeEntity best = null; float score = float.MinValue;
            for (int i = 0; i < p.Entities.Count; i++) { BotKnowledgeEntity e = p.Entities[i]; if (!e.IsNetworked || !e.CanDelete || e.IsLiving || e.IsDangerous || board.IsClaimedByAnother(peer, "clean:" + e.Key, now)) continue; float value = e.Age * .05f + (e.Kind == BotObjectKind.Debris ? .7f : 0f) - e.Position.DistanceSquared(p.Position) * .008f; if (value > score) { score = value; best = e; } }
            return best;
        }
        private BotPoint Placement(BotGoal g, BotPerception p) { BotPoint frontier = p.Frontier.Count > 0 ? Frontier(p) : g.Point; return BotPoint.Lerp(g.Point, frontier, .35f + random.Value() * .25f); }
        private BotPoint SpawnPoint(BotPerception p) { BotPoint point = p.Frontier.Count > 0 ? Frontier(p) : p.Position; return new BotPoint(point.X + (random.Value() - .5f) * 2.4f, point.Y + (random.Value() - .5f) * 1.8f); }
        private BotPoint Frontier(BotPerception p) { return p.Frontier[random.Range(0, p.Frontier.Count)]; }
        private static BotPoint SafeFrontier(BotPerception p) { BotPoint best = p.Position; float score = float.MinValue; for (int i = 0; i < p.Frontier.Count; i++) { float next = p.Frontier[i].DistanceSquared(p.Position); if (next > score) { score = next; best = p.Frontier[i]; } } return best; }
        private static string Cell(BotPoint p) { return ((int)Math.Floor(p.X / 6f)).ToString() + ":" + ((int)Math.Floor(p.Y / 6f)).ToString(); }
    }
}

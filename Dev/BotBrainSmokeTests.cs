using System;

namespace PPGTogether.BepInEx
{
    internal static class BotBrainSmokeTests
    {
        private static int Main()
        {
            BotPerception world = MakeWorld();
            BotCoordinationBoard board = new BotCoordinationBoard();
            BotMind builder = new BotMind(BotPersonality.Builder, 17u);
            BotMind mover = new BotMind(BotPersonality.Mover, 23u);
            BotMind cleaner = new BotMind(BotPersonality.Cleaner, 31u);

            BotDecision first = builder.Decide(60000, world, board, 1f);
            if (first == null || string.IsNullOrEmpty(first.Rationale)) return 1;
            // With an empty world the builder should create a useful scene
            // object, never rely on a Human as a default technical object.
            BotPerception empty = new BotPerception();
            empty.Position = new BotPoint(0f, 0f); empty.Frontier.Add(new BotPoint(5f, 3f)); empty.CanSpawn = true;
            BotDecision spawn = new BotMind(BotPersonality.Builder, 99u).Decide(60002, empty, new BotCoordinationBoard(), 2f);
            if (spawn.Action != BotAction.Spawn || spawn.SpawnKind == BotObjectKind.Living) return 2;

            // A lease makes the same object unavailable to another bot. This
            // verifies coordination rather than merely inspecting a return code.
            BotDecision held = mover.Decide(60001, world, board, 3f);
            if (held.TargetKey != 0 && board.IsClaimedByAnother(60000, "move:" + held.TargetKey, 3f) == false && held.Goal == BotGoalKind.ArrangeObjects) return 3;

            // Failure must be recorded and force a new plan rather than keep a
            // stale action forever; this mirrors a removed/leased game object.
            cleaner.Decide(60002, world, board, 4f);
            cleaner.ReportOutcome(60002, BotOutcome.MissingTarget, board, 4.5f);
            BotDecision recovery = cleaner.Decide(60002, world, board, 5f);
            if (recovery == null || recovery.Action == BotAction.Idle) return 4;

            // Danger takes precedence over the opportunistic spawn/move goals.
            world.LocalDanger = .95f;
            BotDecision safe = new BotMind(BotPersonality.Cleaner, 5u).Decide(60003, world, new BotCoordinationBoard(), 7f);
            if (safe.Goal != BotGoalKind.AvoidDanger || safe.Action != BotAction.Recover) return 5;
            return 0;
        }

        private static BotPerception MakeWorld()
        {
            BotPerception p = new BotPerception();
            p.Position = new BotPoint(0f, 0f); p.CanSpawn = true; p.CanGrab = true; p.CanCleanup = true; p.CanActivate = true;
            p.Frontier.Add(new BotPoint(8f, 2f)); p.Frontier.Add(new BotPoint(-6f, 4f));
            p.Entities.Add(Entity(41, BotObjectKind.Material, 2f, 0f, true, true, false, true));
            p.Entities.Add(Entity(42, BotObjectKind.Debris, 4f, 1f, true, true, false, true));
            p.Entities.Add(Entity(43, BotObjectKind.Weapon, 5f, 0f, true, true, true, false));
            p.Entities.Add(Entity(44, BotObjectKind.Living, 7f, 0f, false, false, false, false)); p.LivingCount = 1f; p.Debris = 1f;
            return p;
        }

        private static BotKnowledgeEntity Entity(ulong key, BotObjectKind kind, float x, float y, bool networked, bool grab, bool activate, bool deletable)
        {
            BotKnowledgeEntity e = new BotKnowledgeEntity(); e.Key = key; e.Kind = kind; e.Position = new BotPoint(x, y); e.IsNetworked = networked; e.CanGrab = grab; e.CanActivate = activate; e.CanDelete = deletable; e.Novelty = .75f; e.Value = .7f; e.Age = 20f; e.IsLiving = kind == BotObjectKind.Living; return e;
        }
    }
}



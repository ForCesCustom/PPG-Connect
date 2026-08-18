using System;

namespace PPGTogether.BepInEx
{
    // Every visual bot owns one of these brains.  The brain only chooses a
    // safe intention; the plugin performs the resulting action exclusively on
    // the authoritative host and uses the same grab lease controller as a
    // human player.
    internal enum BotAction
    {
        Idle,
        Wander,
        Spawn,
        GrabAndPlace,
        Cleanup
    }

    internal enum BotPersonality
    {
        Builder,
        Mover,
        Cleaner
    }

    internal sealed class BotBrain
    {
        internal readonly BotPersonality Personality;

        internal BotBrain(BotPersonality personality)
        {
            Personality = personality;
        }

        // `roll` is supplied by Unity's host-side random source.  Keeping the
        // weighting here pure makes the policy testable without Unity or Steam.
        internal BotAction Choose(int roll, bool canSpawn, bool canGrab, bool canCleanup)
        {
            int builderSpawn = Personality == BotPersonality.Builder ? 48 : 28;
            int moverGrab = Personality == BotPersonality.Mover ? 48 : 30;
            int cleanerCleanup = Personality == BotPersonality.Cleaner ? 42 : 18;
            int total = builderSpawn + moverGrab + cleanerCleanup + 20;
            int point = Math.Abs(roll % total);

            if (point < builderSpawn && canSpawn) return BotAction.Spawn;
            point -= builderSpawn;
            if (point < moverGrab && canGrab) return BotAction.GrabAndPlace;
            point -= moverGrab;
            if (point < cleanerCleanup && canCleanup) return BotAction.Cleanup;
            return BotAction.Wander;
        }
    }
}

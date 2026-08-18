using System;

namespace PPGTogether.BepInEx
{
    internal static class BotBrainSmokeTests
    {
        private static int Main()
        {
            BotPersonality[] personalities =
            {
                BotPersonality.Builder,
                BotPersonality.Mover,
                BotPersonality.Cleaner
            };
            for (int i = 0; i < personalities.Length; i++)
            {
                BotBrain brain = new BotBrain(personalities[i]);
                bool sawSpawn = false;
                bool sawGrab = false;
                bool sawCleanup = false;
                for (int roll = 0; roll < 4096; roll++)
                {
                    BotAction action = brain.Choose(roll, true, true, true);
                    if (action == BotAction.Spawn) sawSpawn = true;
                    else if (action == BotAction.GrabAndPlace) sawGrab = true;
                    else if (action == BotAction.Cleanup) sawCleanup = true;
                    else if (action != BotAction.Wander) return 1;
                }
                // Every personality retains all safe capabilities; personality
                // changes only the frequency with which it chooses them.
                if (!sawSpawn || !sawGrab || !sawCleanup) return 2 + i;
                if (brain.Choose(4, false, false, false) != BotAction.Wander) return 5 + i;
            }
            return 0;
        }
    }
}

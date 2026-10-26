namespace Herdkeeper
{
    // Vanilla Procreation.Procreate (1.0.20) decisions, independent of Unity so they can be tested.
    // Hunger and alertness are not checked: unloaded animals cannot eat and cannot be alerted.
    internal static class BreedingRules
    {
        internal static bool IsDue(long pregnantTicks, long nowTicks, long durationTicks) =>
            pregnantTicks != 0 && nowTicks - pregnantTicks > durationTicks;

        // Vanilla returns when Random.value <= chance, so the roll must exceed the chance. Partners include the animal itself.
        internal static bool CanMate(float roll, float pregnancyChance, int creaturesInRange, int maxCreatures, int readyPartners) =>
            roll > pregnancyChance && creaturesInRange < maxCreatures && readyPartners >= 2;

        // Returns true when the added love point makes the animal pregnant; lovePoints is then reset.
        internal static bool AddLove(ref int lovePoints, int required)
        {
            lovePoints++;
            if (lovePoints < required) return false;
            lovePoints = 0;
            return true;
        }

        // Number of vanilla update intervals that have elapsed since last, capped so a stalled server cannot burst.
        internal static int TakeDueSteps(ref long lastTicks, long nowTicks, long intervalTicks, int cap)
        {
            if (intervalTicks <= 0 || nowTicks <= lastTicks) return 0;
            long steps = (nowTicks - lastTicks) / intervalTicks;
            if (steps <= 0) return 0;
            if (steps > cap)
            {
                lastTicks = nowTicks;
                return cap;
            }
            lastTicks += steps * intervalTicks;
            return (int)steps;
        }
    }
}

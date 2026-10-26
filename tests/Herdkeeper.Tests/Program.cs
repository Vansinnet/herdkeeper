using System;
using System.Collections.Generic;
using Herdkeeper;

internal static class Program
{
    private static readonly long Second = TimeSpan.TicksPerSecond;

    private static int Main()
    {
        var scenarios = new List<(string Name, Action Run)>
        {
            ("PregnancyIsDueOnlyAfterDuration", PregnancyIsDueOnlyAfterDuration),
            ("MatingFollowsVanillaRollCapAndPartners", MatingFollowsVanillaRollCapAndPartners),
            ("LovePointsConceiveAndReset", LovePointsConceiveAndReset),
            ("DueStepsKeepRemainderAndCapBursts", DueStepsKeepRemainderAndCapBursts),
            ("PenOfTwoGrowsToTheCapAndStops", PenOfTwoGrowsToTheCapAndStops),
        };
        int failed = 0;
        foreach (var (name, run) in scenarios)
        {
            try { run(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
        }
        Console.WriteLine(scenarios.Count + " scenario(s), " + failed + " failed. No game runtime was exercised.");
        return failed == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void PregnancyIsDueOnlyAfterDuration()
    {
        Check(!BreedingRules.IsDue(0, 1000 * Second, 60 * Second), "Not pregnant but due.");
        Check(!BreedingRules.IsDue(100 * Second, 160 * Second, 60 * Second), "Due at exactly the duration (vanilla requires more).");
        Check(BreedingRules.IsDue(100 * Second, 160 * Second + 1, 60 * Second), "Not due after the duration.");
    }

    private static void MatingFollowsVanillaRollCapAndPartners()
    {
        Check(!BreedingRules.CanMate(0.33f, 0.33f, 2, 4, 2), "Roll equal to the chance mated (vanilla returns).");
        Check(BreedingRules.CanMate(0.34f, 0.33f, 2, 4, 2), "Pair did not mate.");
        Check(!BreedingRules.CanMate(0.9f, 0.33f, 4, 4, 2), "Mated at the creature cap.");
        Check(!BreedingRules.CanMate(0.9f, 0.33f, 1, 4, 1), "Mated alone (partner count includes itself).");
    }

    private static void LovePointsConceiveAndReset()
    {
        int love = 0;
        for (int i = 1; i < 4; i++)
        {
            Check(!BreedingRules.AddLove(ref love, 4), "Conceived early.");
            Check(love == i, "Love point not added.");
        }
        Check(BreedingRules.AddLove(ref love, 4), "Did not conceive at the required points.");
        Check(love == 0, "Love points not reset.");
    }

    private static void DueStepsKeepRemainderAndCapBursts()
    {
        long last = 0;
        Check(BreedingRules.TakeDueSteps(ref last, 29 * Second, 30 * Second, 4) == 0 && last == 0, "Stepped early.");
        Check(BreedingRules.TakeDueSteps(ref last, 75 * Second, 30 * Second, 4) == 2 && last == 60 * Second, "Remainder lost.");
        Check(BreedingRules.TakeDueSteps(ref last, 10000 * Second, 30 * Second, 4) == 4 && last == 10000 * Second, "Burst not capped.");
        Check(BreedingRules.TakeDueSteps(ref last, 5 * Second, 30 * Second, 4) == 0 && last == 10000 * Second, "Clock going back stepped.");
        Check(BreedingRules.TakeDueSteps(ref last, 20000 * Second, 0, 4) == 0, "Zero interval stepped.");
    }

    // Lox values from docs/evidence/prefabs.json, two tamed adults in range of each other and nothing else.
    private static void PenOfTwoGrowsToTheCapAndStops()
    {
        const int Max = 4;
        long interval = 30 * Second, duration = 120 * Second;
        var random = new Random(7);
        var love = new int[2];
        var pregnant = new long[2];
        var last = new long[2];
        int creatures = 2;
        int births = 0;
        for (long now = 0; now <= 6 * 3600 * Second; now += 5 * Second)
        {
            for (int a = 0; a < 2; a++)
            {
                int steps = BreedingRules.TakeDueSteps(ref last[a], now, interval, 4);
                for (int s = 0; s < steps; s++)
                {
                    if (pregnant[a] != 0)
                    {
                        if (!BreedingRules.IsDue(pregnant[a], now, duration)) continue;
                        pregnant[a] = 0;
                        creatures++;
                        births++;
                        continue;
                    }
                    float roll = (float)random.NextDouble();
                    int partners = 1 + (pregnant[1 - a] == 0 ? 1 : 0);
                    if (!BreedingRules.CanMate(roll, 0.33f, creatures, Max, partners)) continue;
                    if (BreedingRules.AddLove(ref love[a], 4)) pregnant[a] = now;
                }
            }
        }
        // A pregnant parent is not a ready partner, so the pair cannot both be pregnant and overshoot the cap.
        Check(births == 2 && creatures == Max, "Pen did not grow exactly to the cap: " + creatures);
        Check(pregnant[0] == 0 && pregnant[1] == 0, "Pregnancy left pending at the cap.");
    }
}

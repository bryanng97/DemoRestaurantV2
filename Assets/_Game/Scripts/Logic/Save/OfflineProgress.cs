using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;

namespace DemoRestaurant.Logic.Save
{
    /// <summary>What the staff did while the player was away.</summary>
    public sealed class OfflineReport
    {
        /// <summary>Real time away (before the cap).</summary>
        public double AwaySeconds;
        /// <summary>Time that counted for idle progress (capped at MaxOfflineHours).</summary>
        public double CountedSeconds;
        public int CustomersServed;
        public long Coins;
        public int Xp;
        /// <summary>Why nothing was earned, if nothing was (no chef, no waiter, ...). Null otherwise.</summary>
        public string BlockedReason;
        /// <summary>Each staff member's energy and rest state at the end of the time away.</summary>
        public readonly List<StaffEnergyResult> Staff = new List<StaffEnergyResult>();
        /// <summary>Staff below the lowest working band (worn out) when the player returns.</summary>
        public int ExhaustedStaff;

        public bool HasAnything => CustomersServed > 0;
    }

    public readonly struct StaffEnergyResult
    {
        public readonly Staff Staff;
        public readonly float Energy;
        public readonly bool Resting;

        public StaffEnergyResult(Staff staff, float energy, bool resting)
        {
            Staff = staff;
            Energy = energy;
            Resting = resting;
        }
    }

    /// <summary>
    /// Idle ("while you were away") progress. Steps through the time away a minute at a time, estimating the
    /// restaurant's throughput from staff (with their energy-dependent speed), stoves, seats, menu and the
    /// guest arrival rate (rating and level), capped at the economy sheet's offline hours and scaled by its
    /// offline rate so active play stays more rewarding. Idle counts as working:
    /// on-duty staff tire just as they do in the restaurant; resting staff recover and go back to work.
    /// Angry customers don't happen offline.
    /// </summary>
    public static class OfflineProgress
    {
        public const string NoChef = "no_chef";
        public const string NoWaiter = "no_waiter";
        public const string NoSeats = "no_seats";
        /// <summary>Walk speed the idle waiter trip time is based on.</summary>
        private const float ReferenceWalkSpeed = 3f;
        private const double StepSeconds = 60.0;

        private sealed class Worker
        {
            public Staff Staff;
            public float Energy;
            public bool Resting;
        }

        /// <summary>Works out idle progress for <paramref name="away"/> without changing the session.</summary>
        public static OfflineReport Calculate(RestaurantSession s, TimeSpan away)
        {
            var rules = s.Rules;
            var settings = s.Settings;
            var energyRules = rules.StaffEnergy;
            var report = new OfflineReport { AwaySeconds = Math.Max(0, away.TotalSeconds) };
            report.CountedSeconds = Math.Min(report.AwaySeconds, settings.offlineCapHours * 3600.0);

            var workers = new List<Worker>();
            foreach (var staff in s.Sim.Staff)
                workers.Add(new Worker { Staff = staff, Energy = staff.Energy, Resting = staff.IsResting || staff.RestRequested });

            int stoves = 0, seats = SeatFacing.DiningCapacity(s.Layout, s.Rules.NoTableWaitSeconds <= 0f);
            // Each stove's work-time effect (Phase 3), fastest first; the fastest chefs take the fastest stoves.
            var stoveFactors = new List<double>();
            foreach (var f in s.Layout.Furniture)
            {
                if (f.Def.Kind != FurnitureKind.Stove) continue;
                stoves++;
                stoveFactors.Add(Math.Max(10, Math.Min(200, f.Def.Effects.OperationTimePercent)) / 100.0);
            }
            stoveFactors.Sort();
            report.BlockedReason = BlockedReason(workers, stoves, seats);

            var menu = s.Menu.Active;
            double avgCook = 0, avgEat = 0, price = 0, xp = 0;
            foreach (var d in menu) { avgCook += d.CookSeconds; avgEat += d.EatSeconds; price += s.PriceForDish(d);
                xp += s.Progress.XpForServe(s.Recipes.LevelOf(d)); }
            avgCook /= menu.Count; avgEat /= menu.Count; price /= menu.Count;
            // Dishes a minute for one chef on a stove whose work-time factor is stoveFactor (1 = the recipe's own time).
            double ChefRate((double mult, double cookSpeed) chef, double stoveFactor) =>
                chef.mult * 60.0 / (avgCook * stoveFactor / chef.cookSpeed + rules.IdleChefOverheadSeconds);

            double arrivalsPerMinute = s.Arrivals.GuestsPerMinute;
            double customers = 0;
            var chefs = new List<(double mult, double cookSpeed)>();

            for (double t = 0; t < report.CountedSeconds; t += StepSeconds)
            {
                double step = Math.Min(StepSeconds, report.CountedSeconds - t);

                // Throughput this minute with everyone's current speed.
                chefs.Clear();
                double service = 0, cleaning = 0;
                foreach (var w in workers)
                {
                    if (w.Resting) continue;
                    float mult = energyRules.SpeedMultiplier(w.Energy);
                    var type = w.Staff.Type;
                    switch (type.Role)
                    {
                        case StaffRole.Chef:
                            chefs.Add((mult, type.CookSpeed));
                            break;
                        case StaffRole.Waiter:
                            service += mult * (type.WalkSpeed / ReferenceWalkSpeed) * 60.0 / rules.IdleWaiterTripSeconds;
                            break;
                        default:
                            cleaning += mult * 60.0 / (rules.CleanSeconds / type.CookSpeed
                                + rules.IdleCleanTripSeconds * ReferenceWalkSpeed / type.WalkSpeed);
                            break;
                    }
                }
                service = ServiceWithCleaning(service, cleaning, rules);
                // The fastest chefs get the stoves, the fastest stoves first; work time scales only the cooking.
                chefs.Sort((a, b) => ChefRate(b, 1.0).CompareTo(ChefRate(a, 1.0)));
                double kitchen = 0;
                for (int i = 0; i < chefs.Count && i < stoves; i++)
                    kitchen += ChefRate(chefs[i], stoveFactors[i]);
                double dining = seats * 60.0 / (avgEat + rules.IdleSeatOverheadSeconds);
                double perMinute = Math.Min(Math.Min(kitchen, service), Math.Min(dining, arrivalsPerMinute))
                                   * settings.offlineRate;
                customers += perMinute * step / 60.0;

                // Idle counts as working: on duty drains, resting recovers (and returns to work when full).
                foreach (var w in workers)
                {
                    if (w.Resting)
                    {
                        w.Energy = Math.Min(StaffEnergyRules.Max, w.Energy + energyRules.RecoverPerMinute * (float)(step / 60.0));
                        if (w.Energy >= energyRules.AutoReturnAt) w.Resting = false;
                    }
                    else
                    {
                        w.Energy = Math.Max(0f, w.Energy - energyRules.DrainPerMinute * (float)(step / 60.0));
                    }
                }
            }

            float exhaustedBelow = SecondLowestBand(energyRules);
            foreach (var w in workers)
            {
                report.Staff.Add(new StaffEnergyResult(w.Staff, w.Energy, w.Resting));
                if (!w.Resting && w.Energy < exhaustedBelow) report.ExhaustedStaff++;
            }

            report.CustomersServed = (int)Math.Floor(customers + 1e-6);
            report.Coins = (long)Math.Round(report.CustomersServed * price);
            report.Xp = (int)Math.Round(report.CustomersServed * xp / menu.Count);
            return report;
        }

        /// <summary>
        /// Customers per minute the floor can handle when every customer also leaves a table to clear.
        /// Cleaners clear up to <paramref name="cleaning"/> tables a minute; waiters clear the rest, and each table
        /// they clear costs them (clean + trip) / waiter-trip deliveries.
        /// </summary>
        internal static double ServiceWithCleaning(double service, double cleaning, GameRules rules)
        {
            if (rules.CleanSeconds <= 0f || service <= cleaning)
                return service;
            double cost = (rules.CleanSeconds + rules.IdleCleanTripSeconds) / rules.IdleWaiterTripSeconds;
            return (service + cost * cleaning) / (1.0 + cost);
        }

        private static string BlockedReason(List<Worker> workers, int stoves, int seats)
        {
            bool chef = false, waiter = false;
            foreach (var w in workers)
            {
                if (w.Staff.Role == StaffRole.Chef) chef = true;
                else if (w.Staff.Role == StaffRole.Waiter) waiter = true;
            }
            if (!chef || stoves == 0) return NoChef;
            if (!waiter) return NoWaiter;
            if (seats == 0) return NoSeats;
            return null;
        }

        /// <summary>Energy under the second-lowest band counts as "worn out" (the 0.05x band by default: below 20).</summary>
        private static float SecondLowestBand(StaffEnergyRules rules)
        {
            var bands = rules.Bands;
            return bands.Count >= 2 ? bands[bands.Count - 2].MinEnergy : 0f;
        }
    }
}

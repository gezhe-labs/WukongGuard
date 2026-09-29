using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WukongGuard.Core;

internal static class Program
{
    private static int checks;

    private static void Main()
    {
        var repo = FindRepoRoot();
        var root = (Dictionary<string, object>)StrictJson.Parse(File.ReadAllText(
            Path.Combine(repo, "WukongGuard", "rules.json")));
        Check(root.ContainsKey("Rules"), "sample rules parsed");
        Reject("{\"Rules\":[],\"Rules\":[]}", "duplicate JSON key");
        Reject("{\"Rules\":[1,]}", "trailing comma");

        var candidates = ((List<object>)root["Rules"]).Cast<Dictionary<string, object>>().ToList();
        var catalog = (Dictionary<string, object>)StrictJson.Parse(File.ReadAllText(
            Path.Combine(repo, "WukongGuard", "data", "ch1-ch6-missables.json")));
        var experienceSettings = (Dictionary<string, object>)StrictJson.Parse(File.ReadAllText(
            Path.Combine(repo, "WukongGuard", "experience.json")));
        Check(experienceSettings["Enabled"] is bool experienceEnabled && !experienceEnabled
            && experienceSettings["DevelopmentMode"] is bool developmentMode && !developmentMode,
            "release config keeps demos disabled");
        var missables = ((List<object>)catalog["Missables"])
            .Cast<Dictionary<string, object>>().ToList();
        Check(missables.Count == 7, "transcript has seven missable relationships");
        Check(candidates.Count == 13 && candidates.All(row => row["Enabled"] is bool enabled && enabled),
            "seven missable and six optional hidden area checkpoints configured");
        Check(candidates.Count(row => row.TryGetValue("LocationCheckOnly", out var mode)
            && mode is bool locationOnly && locationOnly) == 13,
            "all checkpoints disclose their location-only nature");
        Check(candidates.Count(row => (string)row["Category"] == "missable") == 7
            && candidates.Count(row => (string)row["Category"] == "hidden_area") == 6,
            "rules separate missable and hidden area categories");
        Check(candidates.Where(row => (string)row["Category"] == "hidden_area")
            .All(row => row.TryGetValue("MovementSecondsBeforeTrigger", out var seconds)
                && seconds is double delay && delay == 3)
            && candidates.Where(row => (string)row["Category"] == "missable")
                .All(row => !row.ContainsKey("MovementSecondsBeforeTrigger")),
            "only the six shrine-adjacent hidden hints wait for movement");
        var ids = candidates.Select(row => (string)row["Id"]).ToList();
        Check(ids.Distinct(StringComparer.Ordinal).Count() == ids.Count, "candidate ids unique");
        Check(ids.Contains("ch1_wight_before_secret_entry")
            && ids.Contains("ch2_rat_prince_spirit_order")
            && ids.Contains("ch3_bat_before_yellowbrow")
            && ids.Contains("ch4_talismans_before_final_boss")
            && ids.Contains("ch4_scorpionlord_before_duskveil")
            && ids.Contains("ch4_daoist_mi_before_duskveil")
            && ids.Contains("ch5_horse_before_keeper"),
            "every transcript relationship has an automatic checkpoint");
        Check(!ids.Contains("ch3_horse_dialogue_before_ch4"),
            "outdated per-chapter horse alert removed");
        var ratConfig = candidates.Single(row => (string)row["Id"] == "ch2_rat_prince_spirit_order");
        Check(ratConfig["LocationCheckOnly"] is bool ratLocationOnly && ratLocationOnly
            && !ratConfig.ContainsKey("Conditions"),
            "rat warning does not trust a prior-cycle quest completion flag");
        Check(candidates.All(row => ((List<object>)row["Spoilers"]).Count == 4),
            "all candidates have four spoiler levels");
        Check(candidates.Where(row => (string)row["Category"] == "missable"
            && row.TryGetValue("LocationCheckOnly", out var mode)
            && mode is bool locationOnly && locationOnly)
            .All(row => ((List<object>)row["Spoilers"])[0] is string text
                && (text.Contains("若") || text.Contains("后") || text.Contains("一旦"))),
            "missable copy names the consequence");

        var locationCheck = new MissableRule
        {
            Id = "location_check", Enabled = true, LocationCheckOnly = true,
            RawChapter = 10, MapId = 10, X = 100, Y = 200, Z = 300, Radius = 3000,
            Spoilers = new[] { "若尚未完成，请检查。", "1", "2", "3" }
        };
        Check(RuleEngine.IsValid(locationCheck), "location-only checkpoint can be active");
        Check(new RuleEngine(new[] { locationCheck }).Evaluate(new WukongGameState
        {
            RawChapter = 10, MapId = 10, X = 110, Y = 200, Z = 300
        }).Count == 1, "location-only checkpoint triggers without fabricated completion flag");
        Check(new RuleEngine(new[] { locationCheck }).Evaluate(new WukongGameState
        {
            RawChapter = 10, MapId = 10, X = 110, Y = 200, Z = 300,
            NewGamePlusCount = 2,
            QuestStateAvailable = true,
            QuestStages = new Dictionary<int, string> { [20208] = "Finished" }
        }).Count == 1, "old quest records cannot suppress location-only warning in NG+");
        Check(new RuleEngine(new[] { locationCheck }).Evaluate(new WukongGameState
        {
            RawChapter = 20, MapId = 10, X = 110, Y = 200, Z = 300
        }).Count == 0, "location-only checkpoint stays silent in other chapter");
        var returnEngine = new RuleEngine(new[] { locationCheck });
        var returnState = new WukongGameState
        {
            RawChapter = 10, MapId = 10, X = 110, Y = 200, Z = 300
        };
        Check(returnEngine.Evaluate(returnState).Count == 1, "first entry triggers");
        Check(returnEngine.Evaluate(returnState).Count == 0, "remaining inside does not repeat");
        returnState.X = 3200; // Outside the trigger, but inside the rearm margin.
        Check(returnEngine.Evaluate(returnState).Count == 0, "boundary movement does not rearm");
        returnState.X = 110;
        Check(returnEngine.Evaluate(returnState).Count == 0, "boundary movement does not repeat");
        returnState.MapId = 0;
        Check(returnEngine.Evaluate(returnState).Count == 0, "loading map does not rearm");
        returnState.MapId = 10;
        Check(returnEngine.Evaluate(returnState).Count == 0, "return from loading does not repeat");
        returnState.X = 3600;
        Check(returnEngine.Evaluate(returnState).Count == 0, "leaving the area rearms silently");
        returnState.X = 110;
        Check(returnEngine.Evaluate(returnState).Count == 1, "returning to the area triggers again");
        returnState.MapId = 20;
        Check(returnEngine.Evaluate(returnState).Count == 0, "travel to another map rearms silently");
        returnState.MapId = 10;
        Check(returnEngine.Evaluate(returnState).Count == 1, "return from another map triggers again");
        locationCheck.Category = "hidden_area";
        var hiddenState = new WukongGameState
        {
            RawChapter = 10, MapId = 10, X = 110, Y = 200, Z = 300
        };
        Check(new RuleEngine(new[] { locationCheck }).Evaluate(hiddenState).Count == 0,
            "hidden area hint is off by default");
        Check(new RuleEngine(new[] { locationCheck }).Evaluate(hiddenState, true).Count == 1,
            "settings opt-in enables hidden area hint");
        var hiddenReturnEngine = new RuleEngine(new[] { locationCheck });
        Check(hiddenReturnEngine.Evaluate(hiddenState, true).Count == 1,
            "optional hint triggers on first entry");
        hiddenState.X = 3600;
        Check(hiddenReturnEngine.Evaluate(hiddenState, false).Count == 0,
            "optional hint rearms while setting is off");
        hiddenState.X = 110;
        Check(hiddenReturnEngine.Evaluate(hiddenState, true).Count == 1,
            "optional hint triggers after return and opt-in");
        var delayedHint = new MissableRule
        {
            Id = "shrine_hint", Enabled = true, Category = "hidden_area", LocationCheckOnly = true,
            RawChapter = 10, MapId = 10, X = 0, Y = 0, Z = 0, Radius = 1500,
            MovementSecondsBeforeTrigger = 3,
            Spoilers = new[] { "0", "1", "2", "3" }
        };
        Check(RuleEngine.IsValid(delayedHint), "movement-gated hidden hint is valid");
        var delayedEngine = new RuleEngine(new[] { delayedHint });
        var delayedState = new WukongGameState
        {
            RawChapter = 10, MapId = 10, X = 0, Y = 0, Z = 0,
            ObservedAtUtc = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc)
        };
        Check(delayedEngine.Evaluate(delayedState, true).Count == 0,
            "arriving at shrine stays silent");
        delayedState.ObservedAtUtc = delayedState.ObservedAtUtc.AddSeconds(10);
        Check(delayedEngine.Evaluate(delayedState, true).Count == 0,
            "standing at shrine for ten seconds stays silent");
        for (var second = 1; second <= 2; second++)
        {
            delayedState.X += 100;
            delayedState.ObservedAtUtc = delayedState.ObservedAtUtc.AddSeconds(1);
            Check(delayedEngine.Evaluate(delayedState, true).Count == 0,
                "hidden hint waits for three seconds of walking");
        }
        delayedState.X += 100;
        delayedState.ObservedAtUtc = delayedState.ObservedAtUtc.AddSeconds(1);
        Check(delayedEngine.Evaluate(delayedState, true).Count == 1,
            "hidden hint appears after three seconds of movement");
        Check(delayedEngine.Evaluate(delayedState, true).Count == 0,
            "hidden hint does not repeat while standing in range");
        delayedState.X = 6000;
        delayedState.ObservedAtUtc = delayedState.ObservedAtUtc.AddSeconds(1);
        Check(delayedEngine.Evaluate(delayedState, true).Count == 0,
            "leaving shrine area rearms hidden hint");
        delayedState.X = 0;
        delayedState.ObservedAtUtc = delayedState.ObservedAtUtc.AddSeconds(1);
        Check(delayedEngine.Evaluate(delayedState, true).Count == 0,
            "returning to shrine restarts movement requirement");
        delayedState.X = 100;
        delayedState.ObservedAtUtc = delayedState.ObservedAtUtc.AddSeconds(1);
        Check(delayedEngine.Evaluate(delayedState, false).Count == 0,
            "disabling hidden hints clears movement progress");
        delayedState.X = 200;
        delayedState.ObservedAtUtc = delayedState.ObservedAtUtc.AddSeconds(1);
        Check(delayedEngine.Evaluate(delayedState, true).Count == 0,
            "re-enabling hidden hints still waits for walking");
        var outboundEngine = new RuleEngine(new[] { delayedHint });
        var outbound = new WukongGameState
        {
            RawChapter = 10, MapId = 10, X = 1400, Y = 0, Z = 0,
            ObservedAtUtc = new DateTime(2026, 9, 28, 1, 0, 0, DateTimeKind.Utc)
        };
        Check(outboundEngine.Evaluate(outbound, true).Count == 0,
            "shrine boundary entry starts movement window");
        for (var second = 1; second <= 2; second++)
        {
            outbound.X += 1000;
            outbound.ObservedAtUtc = outbound.ObservedAtUtc.AddSeconds(1);
            Check(outboundEngine.Evaluate(outbound, true).Count == 0,
                "walking away from shrine keeps countdown without early trigger");
        }
        outbound.X += 1000;
        outbound.ObservedAtUtc = outbound.ObservedAtUtc.AddSeconds(1);
        Check(outboundEngine.Evaluate(outbound, true).Count == 1,
            "walking beyond shrine radius still triggers after three seconds");
        delayedHint.Category = "missable";
        Check(!RuleEngine.IsValid(delayedHint), "irreversible warning cannot be movement delayed");
        locationCheck.Category = "missable";
        locationCheck.Radius = 5001;
        Check(!RuleEngine.IsValid(locationCheck), "excessive radius rejected");

        var rule = new MissableRule
        {
            Id = "calibrated_example", Enabled = true, RawChapter = 20, MapId = 10, AreaId = 4,
            QuestId = 123, QuestStage = "Activated", X = 100, Y = 200, Z = 300, Radius = 50,
            Spoilers = new[] { "0", "1", "2", "3" }
        };
        var engine = new RuleEngine(new[] { rule });
        var state = new WukongGameState
        {
            RawChapter = 20, MapId = 10, AreaId = 4, X = 110, Y = 200, Z = 300,
            NewGamePlusCount = 1,
            QuestStages = new Dictionary<int, string> { [123] = "Activated" }
        };
        Check(engine.Evaluate(state).Count == 1, "all calibrated conditions trigger");
        Check(engine.Evaluate(state).Count == 0, "deduplicated after trigger");
        engine.RearmAfterDeliveryFailure("calibrated_example", 1, TimeSpan.Zero);
        Check(engine.Evaluate(state).Count == 1, "failed delivery can be retried");
        state.NewGamePlusCount = 2;
        Check(engine.Evaluate(state).Count == 1, "new cycle can trigger the same rule");
        engine.RearmAfterDeliveryFailure("calibrated_example", 1, TimeSpan.Zero);
        Check(engine.Evaluate(state).Count == 0, "retry from old cycle does not rearm new cycle");
        state.NewGamePlusCount = 1;
        Check(engine.Evaluate(state).Count == 1, "old cycle retry retains its own key");
        Check(new RuleEngine(new[] { rule }).Evaluate(new WukongGameState
        {
            RawChapter = 20, MapId = 10, AreaId = 4, X = 110, Y = 200, Z = 300
        }).Count == 0, "missing quest fails closed");
        state.X = 200;
        Check(new RuleEngine(new[] { rule }).Evaluate(state).Count == 0, "outside radius fails closed");
        rule.Enabled = false;
        state.X = 110;
        Check(new RuleEngine(new[] { rule }).Evaluate(state).Count == 0, "disabled rule stays silent");

        var signalRule = new MissableRule
        {
            Id = "signal_example", Enabled = true, RawChapter = 20, MapId = 10, AreaId = 4,
            X = 100, Y = 200, Z = 300, Radius = 50,
            Conditions = new List<SignalCondition>
            {
                new SignalCondition { Source = "world_interaction", Key = "2003102", Operator = "absent" },
                new SignalCondition { Source = "item", Key = "4003", Operator = "at_least", Value = "1" },
                new SignalCondition { Source = "psm", Key = "2030001/3", Operator = "equals", Value = "Active" }
            },
            Spoilers = new[] { "0", "1", "2", "3" }
        };
        var signalState = new WukongGameState
        {
            RawChapter = 20, MapId = 10, AreaId = 4, X = 100, Y = 200, Z = 300,
            ItemStateAvailable = true, WorldInteractionStateAvailable = true, PsmStateAvailable = true,
            ItemCounts = new Dictionary<int, int> { [4003] = 1 },
            PsmNodeStates = new Dictionary<string, string> { ["2030001/3"] = "Active" }
        };
        Check(RuleEngine.IsValid(signalRule), "signal rule can omit quest stage");
        Check(new RuleEngine(new[] { signalRule }).Evaluate(signalState).Count == 1,
            "all calibrated signal conditions trigger");
        signalState.WorldInteractionStateAvailable = false;
        Check(new RuleEngine(new[] { signalRule }).Evaluate(signalState).Count == 0,
            "unavailable source fails closed");
        signalState.WorldInteractionStateAvailable = true;
        signalState.WorldInteractionSteps[2003102] = 1;
        Check(new RuleEngine(new[] { signalRule }).Evaluate(signalState).Count == 0,
            "completed interaction suppresses alert");
        signalState.WorldInteractionSteps.Clear();
        signalState.ItemCounts[4003] = 0;
        Check(new RuleEngine(new[] { signalRule }).Evaluate(signalState).Count == 0,
            "missing item suppresses alert");
        signalState.ItemCounts[4003] = 1;
        signalState.PsmNodeStates["2030001/3"] = "WasActive";
        Check(new RuleEngine(new[] { signalRule }).Evaluate(signalState).Count == 0,
            "changed state machine node suppresses alert");
        signalRule.Conditions[0].Operator = "unknown";
        Check(!RuleEngine.IsValid(signalRule), "unsupported operator rejected");

        var equipRule = new MissableRule
        {
            Id = "lantern_missing", Enabled = true, RawChapter = 30, MapId = 30, AreaId = 1,
            X = 0, Y = 0, Z = 0, Radius = 100,
            Conditions = new List<SignalCondition>
            {
                new SignalCondition { Source = "equip", Key = "16030", Operator = "absent" }
            },
            Spoilers = new[] { "0", "1", "2", "3" }
        };
        var equipState = new WukongGameState
        {
            RawChapter = 30, MapId = 30, AreaId = 1, X = 0, Y = 0, Z = 0,
            EquipStateAvailable = true
        };
        Check(new RuleEngine(new[] { equipRule }).Evaluate(equipState).Count == 1,
            "missing curio can trigger calibrated rule");
        equipState.EquipIds.Add(16030);
        Check(new RuleEngine(new[] { equipRule }).Evaluate(equipState).Count == 0,
            "owned curio suppresses alert");
        equipState.EquipIds.Clear();
        equipState.EquipStateAvailable = false;
        Check(new RuleEngine(new[] { equipRule }).Evaluate(equipState).Count == 0,
            "unavailable equipment source fails closed");

        var horseRule = new MissableRule
        {
            Id = "horse_at_temple", Enabled = true, RawChapter = 30, MapId = 30,
            X = -225027, Y = -112880, Z = -15394, Radius = 1000,
            Conditions = new List<SignalCondition>
            {
                new SignalCondition { Source = "quest", Key = "2222003", Operator = "equals", Value = "Finished" },
                new SignalCondition { Source = "quest", Key = "2222004", Operator = "not_equals", Value = "Finished" },
                new SignalCondition { Source = "quest", Key = "3900015", Operator = "equals", Value = "Activated" }
            },
            Spoilers = new[] { "0", "1", "2", "3" }
        };
        var horseState = new WukongGameState
        {
            RawChapter = 30, MapId = 30, AreaId = 5, X = -225027, Y = -112880,
            Z = -15394, QuestStateAvailable = true,
            QuestStages = new Dictionary<int, string>
            {
                [2222003] = "Finished", [3900015] = "Activated"
            }
        };
        Check(RuleEngine.IsValid(horseRule), "point rule valid without unverified area id");
        Check(new RuleEngine(new[] { horseRule }).Evaluate(horseState).Count == 1,
            "pending horse dialogue alerts near Great Hall shrine");
        horseState.QuestStages[2222004] = "Activated";
        Check(new RuleEngine(new[] { horseRule }).Evaluate(horseState).Count == 1,
            "dialogue still pending when quest is activated");
        horseState.QuestStages[2222004] = "Finished";
        Check(new RuleEngine(new[] { horseRule }).Evaluate(horseState).Count == 0,
            "completed horse dialogue suppresses alert");
        horseState.QuestStages.Remove(2222004);
        horseState.QuestStages[3900015] = "Finished";
        Check(new RuleEngine(new[] { horseRule }).Evaluate(horseState).Count == 0,
            "defeated chapter boss suppresses late alert");
        horseState.QuestStages[3900015] = "Activated";
        horseState.X = -220000;
        Check(new RuleEngine(new[] { horseRule }).Evaluate(horseState).Count == 0,
            "distant position remains silent");
        horseState.X = -225027;
        horseState.MapId = 25;
        Check(new RuleEngine(new[] { horseRule }).Evaluate(horseState).Count == 0,
            "other map remains silent");
        horseState.MapId = 30;
        horseState.QuestStages.Remove(2222003);
        Check(new RuleEngine(new[] { horseRule }).Evaluate(horseState).Count == 0,
            "missing previous chapter step suppresses unqualified alert");
        horseState.QuestStateAvailable = false;
        Check(new RuleEngine(new[] { horseRule }).Evaluate(horseState).Count == 0,
            "unavailable quest source suppresses area alert");

        var previewOne = new MissableRule { Id = "first", Spoilers = new[] { "0", "1", "2", "3" } };
        var previewTwo = new MissableRule { Id = "second", Spoilers = new[] { "0", "1", "2", "3" } };
        var automatic = new AutoExperienceEngine(new[] { previewOne, previewTwo }, true);
        var start = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
        Check(automatic.Evaluate(state, start) == null, "experience waits for runtime state");
        Check(automatic.Evaluate(state, start.AddSeconds(5))?.Id == "first",
            "first experience alert appears automatically");
        Check(automatic.Evaluate(state, start.AddSeconds(25)) == null,
            "standing still does not cycle experience alerts");
        state.X = 220;
        Check(automatic.Evaluate(state, start.AddSeconds(26))?.Id == "second",
            "movement and interval advance the alert");
        Check(automatic.Evaluate(state, start.AddSeconds(60)) == null,
            "experience stops after its candidates");
        automatic.Enabled = false;
        Check(automatic.Evaluate(state, start.AddSeconds(120)) == null,
            "experience switch disables alerts");
        Console.WriteLine($"All {checks} checks passed.");
    }

    private static string FindRepoRoot()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path != null)
        {
            if (File.Exists(Path.Combine(path.FullName, "WukongGuard", "rules.json")))
                return path.FullName;
            path = path.Parent;
        }
        throw new DirectoryNotFoundException("WukongGuard/rules.json not found above test output");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        checks++;
        Console.WriteLine("PASS " + name);
    }

    private static void Reject(string json, string name)
    {
        try { StrictJson.Parse(json); }
        catch (FormatException) { Check(true, name); return; }
        throw new Exception("FAILED: " + name);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WukongGuard.Core;

namespace WukongGuard
{
    internal static class RuleLoader
    {
        private static string RulesPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "CSharpLoader", "Mods", "WukongGuard", "rules.json");

        private static string ExperiencePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "CSharpLoader", "Mods", "WukongGuard", "experience.json");

        internal static bool LoadAutomaticExperienceEnabled()
        {
            return LoadExperienceFlag("DevelopmentMode") && LoadExperienceFlag("Enabled");
        }

        internal static bool LoadDevelopmentMode() => LoadExperienceFlag("DevelopmentMode");

        private static bool LoadExperienceFlag(string key)
        {
            try
            {
                if (!File.Exists(ExperiencePath)) return false;
                var settings = StrictJson.Parse(File.ReadAllText(ExperiencePath)) as Dictionary<string, object>;
                if (settings == null || !settings.TryGetValue(key, out var enabled)
                    || !(enabled is bool flag)) throw new FormatException(key + " must be boolean");
                return flag;
            }
            catch (Exception ex)
            {
                TraceLog.Write("[WukongGuard] experience settings invalid; demos disabled: " + ex);
                return false;
            }
        }

        internal static IReadOnlyList<MissableRule> Load()
        {
            // The loader may give injected assemblies a virtual Location string.
            // The game executable directory is stable for the loader's documented layout.
            if (!File.Exists(RulesPath))
            {
                TraceLog.Write("[WukongGuard] rules.json missing; automatic alerts disabled");
                return new List<MissableRule>();
            }
            try
            {
                var result = new List<MissableRule>();
                foreach (var rule in ReadAll())
                {
                    if (rule.Enabled && !RuleEngine.IsValid(rule))
                        TraceLog.Write("[WukongGuard] invalid rule disabled: " + rule.Id);
                    else if (RuleEngine.IsValid(rule)) result.Add(rule);
                }
                TraceLog.Write("[WukongGuard] active rules=" + result.Count);
                return result;
            }
            catch (Exception ex)
            {
                TraceLog.Write("[WukongGuard] rules load failed; automatic alerts disabled: " + ex);
                return new List<MissableRule>();
            }
        }

        // Manual preview is independent of the runtime trigger fields and never enables a rule.
        internal static IReadOnlyList<MissableRule> LoadPreviewRules()
        {
            try
            {
                if (!File.Exists(RulesPath)) return new List<MissableRule>();
                return ReadAll().Where(rule =>
                    rule.Spoilers != null && rule.Spoilers.Length == 4
                    && rule.Spoilers.All(s => !string.IsNullOrWhiteSpace(s))).ToList();
            }
            catch (Exception ex)
            {
                TraceLog.Write("[WukongGuard] preview rules unavailable: " + ex);
                return new List<MissableRule>();
            }
        }

        private static IReadOnlyList<MissableRule> ReadAll()
        {
            var root = StrictJson.Parse(File.ReadAllText(RulesPath)) as Dictionary<string, object>;
            if (root == null || !root.TryGetValue("Rules", out var rawRules)
                || !(rawRules is List<object> rows)) throw new FormatException("Rules array missing");
            var result = new List<MissableRule>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (!(row is Dictionary<string, object> fields)) throw new FormatException("Rule must be object");
                var rule = ReadRule(fields);
                if (!ids.Add(rule.Id ?? "")) throw new FormatException("Duplicate rule id: " + rule.Id);
                result.Add(rule);
            }
            return result;
        }

        private static MissableRule ReadRule(Dictionary<string, object> f)
        {
            return new MissableRule
            {
                Id = String(f, "Id"),
                Enabled = Bool(f, "Enabled"),
                Category = String(f, "Category") ?? "missable",
                RawChapter = Integer(f, "RawChapter"),
                MapId = Integer(f, "MapId"),
                AreaId = Integer(f, "AreaId"),
                QuestId = Integer(f, "QuestId"),
                QuestStage = String(f, "QuestStage"),
                LocationCheckOnly = OptionalBool(f, "LocationCheckOnly"),
                X = Number(f, "X"),
                Y = Number(f, "Y"),
                Z = Number(f, "Z"),
                Radius = Number(f, "Radius"),
                MovementSecondsBeforeTrigger = Integer(f, "MovementSecondsBeforeTrigger") ?? 0,
                Conditions = Conditions(f, "Conditions"),
                Spoilers = Strings(f, "Spoilers")
            };
        }

        private static object Field(Dictionary<string, object> f, string key)
            => f.TryGetValue(key, out var value) ? value : null;

        private static string String(Dictionary<string, object> f, string key)
        {
            var value = Field(f, key);
            if (value == null) return null;
            if (value is string text) return text;
            throw new FormatException(key + " must be string");
        }

        private static bool Bool(Dictionary<string, object> f, string key)
        {
            var value = Field(f, key);
            if (value is bool flag) return flag;
            throw new FormatException(key + " must be boolean");
        }

        private static bool OptionalBool(Dictionary<string, object> f, string key)
        {
            var value = Field(f, key);
            if (value == null) return false;
            if (value is bool flag) return flag;
            throw new FormatException(key + " must be boolean");
        }

        private static double? Number(Dictionary<string, object> f, string key)
        {
            var value = Field(f, key);
            if (value == null) return null;
            if (value is double number) return number;
            throw new FormatException(key + " must be number");
        }

        private static int? Integer(Dictionary<string, object> f, string key)
        {
            var number = Number(f, key);
            if (!number.HasValue) return null;
            if (number.Value < int.MinValue || number.Value > int.MaxValue
                || Math.Truncate(number.Value) != number.Value) throw new FormatException(key + " must be integer");
            return (int)number.Value;
        }

        private static string[] Strings(Dictionary<string, object> f, string key)
        {
            var value = Field(f, key);
            if (value == null) return null;
            if (!(value is List<object> items)) throw new FormatException(key + " must be array");
            if (items.Any(item => !(item is string))) throw new FormatException(key + " must contain strings");
            return items.Cast<string>().ToArray();
        }

        private static List<SignalCondition> Conditions(Dictionary<string, object> f, string key)
        {
            var value = Field(f, key);
            if (value == null) return new List<SignalCondition>();
            if (!(value is List<object> rows)) throw new FormatException(key + " must be array");
            if (rows.Count > 20) throw new FormatException(key + " has too many entries");
            var result = new List<SignalCondition>();
            foreach (var row in rows)
            {
                if (!(row is Dictionary<string, object> fields))
                    throw new FormatException(key + " entry must be object");
                result.Add(new SignalCondition
                {
                    Source = String(fields, "Source"),
                    Key = String(fields, "Key"),
                    Operator = String(fields, "Operator"),
                    Value = String(fields, "Value")
                });
            }
            return result;
        }
    }
}

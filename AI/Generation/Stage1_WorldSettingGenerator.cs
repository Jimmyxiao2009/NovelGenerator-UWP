using System;
using System.Threading.Tasks;
using novelgen.Models;
using novelgen.Pipeline;
using Windows.Data.Json;

namespace novelgen.AI.Generation
{
    /// <summary>阶段②：基于用户提示 + Genre 生成世界设定。</summary>
    public class Stage1_WorldSettingGenerator
    {
        public async Task<WorldSetting> GenerateAsync(Novel novel, StageContext ctx)
        {
            var client = new ApiClient(ctx.Profile);
            var preset = GenrePresets.FindById(novel, null);
            var user = PromptRegistry.User_WorldSetting(
                novel.UserPrompt, novel.Genre, novel.SubGenre, preset?.Name ?? "通用",
                novel.WorldCharacterCount, novel.WorldLocationCount, novel.WorldFactionCount, novel.WorldRuleCount);

            string raw = await client.ChatJsonAsync(PromptRegistry.System_WorldSetting, user, ctx.Ct);
            System.Diagnostics.Debug.WriteLine($"[Stage1] raw length={raw?.Length ?? 0}");
            if (raw != null && raw.Length < 500) System.Diagnostics.Debug.WriteLine($"[Stage1] raw='{raw}'");
            return Parse(raw, novel);
        }

        public static WorldSetting Parse(string json, Novel novel)
        {
            var ws = new WorldSetting();
            if (string.IsNullOrWhiteSpace(json))
            {
                ws.Synopsis = "（AI 返回为空。可能是网络问题、API Key 无效，或响应格式被网关修改。请在 API 配置页 ▶ 测试连接 验证）";
                ws.ParseFailed = true;
                return ws;
            }
            try
            {
                var jo = JsonObject.Parse(json);
                ws.Title = jo.ContainsKey("title") ? jo.GetNamedString("title") : "";
                ws.Synopsis = jo.ContainsKey("synopsis") ? jo.GetNamedString("synopsis") : "";
                if (jo.ContainsKey("themes"))
                {
                    var arr = jo.GetNamedArray("themes");
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var v in arr) list.Add(v.GetString());
                    ws.Themes = list.ToArray();
                }
                ws.Characters = ParseCharacters(jo);
                ws.Factions   = ParseFactions(jo);
                ws.Locations  = ParseLocations(jo);
                ws.Rules      = ParseRules(jo);
                if (jo.ContainsKey("timeline"))
                {
                    var arr = jo.GetNamedArray("timeline");
                    foreach (var v in arr)
                    {
                        var e = v.GetObject();
                        ws.Timeline.Add(new WorldEvent
                        {
                            Title = e.GetNamedString("title", ""),
                            Time = e.GetNamedString("time", ""),
                            Description = e.GetNamedString("description", ""),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                ws.Synopsis = "（解析失败：AI 返回的内容不是合法 JSON）\n" + ex.Message + "\n\n原始返回（前 500 字）：\n" + (json.Length > 500 ? json.Substring(0, 500) + "…" : json);
                ws.ParseFailed = true;
            }
            return ws;
        }

        private static System.Collections.Generic.List<Character> ParseCharacters(JsonObject jo)
        {
            var list = new System.Collections.Generic.List<Character>();
            if (!jo.ContainsKey("characters")) return list;
            foreach (var v in jo.GetNamedArray("characters"))
            {
                var o = v.GetObject();
                list.Add(new Character
                {
                    Name = o.GetNamedString("name", ""),
                    Role = o.GetNamedString("role", ""),
                    Personality = o.GetNamedString("personality", ""),
                    Background = o.GetNamedString("background", ""),
                    Appearance = o.GetNamedString("appearance", ""),
                    Ability = o.GetNamedString("ability", ""),
                    Goal = o.GetNamedString("goal", ""),
                });
            }
            return list;
        }

        private static System.Collections.Generic.List<Faction> ParseFactions(JsonObject jo)
        {
            var list = new System.Collections.Generic.List<Faction>();
            if (!jo.ContainsKey("factions")) return list;
            foreach (var v in jo.GetNamedArray("factions"))
            {
                var o = v.GetObject();
                list.Add(new Faction
                {
                    Name = o.GetNamedString("name", ""),
                    Ideology = o.GetNamedString("ideology", ""),
                    Territory = o.GetNamedString("territory", ""),
                    Influence = o.GetNamedString("influence", ""),
                    Description = o.GetNamedString("description", ""),
                });
            }
            return list;
        }

        private static System.Collections.Generic.List<Location> ParseLocations(JsonObject jo)
        {
            var list = new System.Collections.Generic.List<Location>();
            if (!jo.ContainsKey("locations")) return list;
            foreach (var v in jo.GetNamedArray("locations"))
            {
                var o = v.GetObject();
                list.Add(new Location
                {
                    Name = o.GetNamedString("name", ""),
                    Type = o.GetNamedString("type", ""),
                    Description = o.GetNamedString("description", ""),
                });
            }
            return list;
        }

        private static System.Collections.Generic.List<WorldRule> ParseRules(JsonObject jo)
        {
            var list = new System.Collections.Generic.List<WorldRule>();
            if (!jo.ContainsKey("rules")) return list;
            foreach (var v in jo.GetNamedArray("rules"))
            {
                var o = v.GetObject();
                list.Add(new WorldRule
                {
                    Category = o.GetNamedString("category", ""),
                    Description = o.GetNamedString("description", ""),
                    IsHard = o.ContainsKey("isHard") && o.GetNamedBoolean("isHard"),
                });
            }
            return list;
        }
    }
}

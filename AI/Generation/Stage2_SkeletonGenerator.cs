using System;
using System.Threading.Tasks;
using novelgen.Models;
using novelgen.Pipeline;
using Windows.Data.Json;

namespace novelgen.AI.Generation
{
    /// <summary>阶段③：基于世界设定生成 N 卷 × M 章的故事骨架。</summary>
    public class Stage2_SkeletonGenerator
    {
        public async Task<StorySkeleton> GenerateAsync(Novel novel, StageContext ctx)
        {
            var client = new ApiClient(ctx.Profile);
            var worldJson = SerializeWorld(novel.WorldSetting);
            var preset = AI.GenrePresets.FindById(novel, null);
            var user = PromptRegistry.User_Skeleton(
                worldJson, novel.TargetChapterCount, novel.TargetWordsPerChapter, preset?.Name ?? "通用", novel.TargetVolumeCount);

            string raw = await client.ChatJsonAsync(PromptRegistry.System_Skeleton, user, ctx.Ct);
            return Parse(raw);
        }

        public static StorySkeleton Parse(string json)
        {
            var sk = new StorySkeleton();
            if (string.IsNullOrWhiteSpace(json)) { sk.ParseFailed = true; return sk; }
            try
            {
                var jo = JsonObject.Parse(json);
                sk.Logline = jo.GetNamedString("logline", "");
                sk.ThreeAct = jo.GetNamedString("threeAct", "");

                if (jo.ContainsKey("volumes"))
                {
                    foreach (var vv in jo.GetNamedArray("volumes"))
                    {
                        var vo = vv.GetObject();
                        var vol = new VolumeOutline
                        {
                            Title = vo.GetNamedString("title", ""),
                            Synopsis = vo.GetNamedString("synopsis", ""),
                            PlotGoal = vo.GetNamedString("plotGoal", ""),
                            Climax = vo.GetNamedString("climax", ""),
                        };
                        if (vo.ContainsKey("chapters"))
                        {
                            foreach (var cc in vo.GetNamedArray("chapters"))
                            {
                                var co = cc.GetObject();
                                var chap = new ChapterOutline
                                {
                                    Title = co.GetNamedString("title", ""),
                                    Synopsis = co.GetNamedString("synopsis", ""),
                                    Mood = co.GetNamedString("mood", ""),
                                };
                                if (co.ContainsKey("keyBeats"))
                                {
                                    var list = new System.Collections.Generic.List<string>();
                                    foreach (var b in co.GetNamedArray("keyBeats")) list.Add(b.GetString());
                                    chap.KeyBeats = list.ToArray();
                                }
                                vol.Chapters.Add(chap);
                            }
                        }
                        sk.Volumes.Add(vol);
                    }
                }

                if (jo.ContainsKey("foreshadowings"))
                {
                    foreach (var ff in jo.GetNamedArray("foreshadowings"))
                    {
                        var fo = ff.GetObject();
                        sk.PlantedForeshadowings.Add(new Foreshadowing
                        {
                            Description = fo.GetNamedString("description", ""),
                            PlantedAtChapter = (int)fo.GetNamedNumber("plantedAtChapter", 0),
                            ExpectedPayoffChapter = (int)fo.GetNamedNumber("expectedPayoffChapter", 0),
                            Status = "Planted",
                        });
                    }
                }
            }
            catch (Exception)
            {
                sk.Logline = "（解析失败：AI 返回的内容不是合法 JSON）\n\n" + json;
                sk.ParseFailed = true;
            }
            // 即便 JSON 合法，若一卷都没解析出来也视为解析失败（避免据此生成 0 章的空书）
            if (sk.Volumes == null || sk.Volumes.Count == 0) sk.ParseFailed = true;
            return sk;
        }

        private static string SerializeWorld(WorldSetting ws)
        {
            if (ws == null) return "（无）";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("梗概：" + ws.Synopsis);
            sb.AppendLine("人物：");
            foreach (var c in ws.Characters ?? new System.Collections.Generic.List<Character>()) sb.AppendLine($"  - {c.Name}（{c.Role}）：{c.Personality} | 目标：{c.Goal}");
            sb.AppendLine("地点：");
            foreach (var l in ws.Locations ?? new System.Collections.Generic.List<Location>()) sb.AppendLine($"  - {l.Name}：{l.Description}");
            sb.AppendLine("势力：");
            foreach (var f in ws.Factions ?? new System.Collections.Generic.List<Faction>()) sb.AppendLine($"  - {f.Name}：{f.Ideology}");
            sb.AppendLine("硬规则：");
            foreach (var r in ws.Rules ?? new System.Collections.Generic.List<WorldRule>()) sb.AppendLine($"  - [{r.Category}] {r.Description}");
            return sb.ToString();
        }
    }
}

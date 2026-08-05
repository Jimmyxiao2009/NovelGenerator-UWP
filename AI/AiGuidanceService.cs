using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using novelgen.Models;
using novelgen.Pipeline;
using novelgen.AI.Generation;

namespace novelgen.AI
{
    /// <summary>
    /// 在 check 点上，让 AI 基于用户的额外提示词修改已有内容。
    /// 用于：分章预览 / 目录单章 / Gate1 世界设定 / Gate1 故事骨架。
    /// </summary>
    public static class AiGuidanceService
    {
        // ── ① 章节内容修改 ───────────────────────────────────────
        public const string System_ModifyChapter =
            "你是一位资深小说编辑。用户要在他/她额外要求的方向上修改一章正文。\n" +
            "要求：\n" +
            "1. 保留故事的连贯性、人物性格、已确立的世界观和风格；\n" +
            "2. 只在用户明确要求的方向上调整，不要无谓改写；\n" +
            "3. 保持文笔、节奏、对话风格与原章一致；\n" +
            "4. 输出整章完整正文，不要任何前缀说明或 Markdown 代码块。";

        public static string User_ModifyChapter(string chapterText, string outlineSummary, string userInstruction)
        {
            return string.Format(
                "【本章大纲】\n{0}\n\n【现有正文】\n{1}\n\n【用户修改要求】\n{2}\n\n请直接输出修改后的完整正文：",
                outlineSummary ?? "（无）",
                chapterText ?? "（无）",
                userInstruction ?? "");
        }

        public static async Task<string> ModifyChapterContentAsync(
            ApiClient client, string chapterText, string outlineSummary, string userInstruction,
            IProgress<string> chunkProgress, CancellationToken ct)
        {
            var sb = new StringBuilder();
            await client.StreamWithCallbackAsync(
                System_ModifyChapter,
                User_ModifyChapter(chapterText, outlineSummary, userInstruction),
                token =>
                {
                    sb.Append(token);
                    if (chunkProgress != null) chunkProgress.Report(token);
                }, ct);
            return sb.ToString();
        }

        // ── ② 世界设定修改（JSON 进出） ─────────────────────────
        public const string System_ModifyWorld =
            "你是世界观设计师。用户要在他/她的额外要求方向上修改已有世界设定。\n" +
            "严格按原 JSON 结构输出；可增删人物/地点/势力/规则/主题。\n" +
            "只输出合法 JSON，不要任何解释或 Markdown 代码块。";

        public static string User_ModifyWorld(string currentWorldJson, string userInstruction)
        {
            return string.Format(
                "【现有世界设定 JSON】\n{0}\n\n【用户修改要求】\n{1}\n\n请输出修改后的完整世界设定 JSON：",
                currentWorldJson, userInstruction ?? "");
        }

        public static async Task<WorldSetting> ModifyWorldSettingAsync(
            ApiClient client, WorldSetting current, string userInstruction, CancellationToken ct)
        {
            var worldJson = SerializeWorld(current);
            var user = User_ModifyWorld(worldJson, userInstruction);
            var raw = await client.ChatJsonAsync(System_ModifyWorld, user, ct);
            return Stage1_WorldSettingGenerator.Parse(raw, null);
        }

        // ── ③ 故事骨架修改（JSON 进出） ─────────────────────────
        public const string System_ModifySkeleton =
            "你是故事结构师。用户要在他/她的额外要求方向上修改已有故事骨架。\n" +
            "严格按原 JSON 结构输出；可调整卷/章顺序、增删章、修改节拍、伏笔。\n" +
            "只输出合法 JSON，不要任何解释或 Markdown 代码块。";

        public static string User_ModifySkeleton(string currentSkeletonJson, string userInstruction)
        {
            return string.Format(
                "【现有故事骨架 JSON】\n{0}\n\n【用户修改要求】\n{1}\n\n请输出修改后的完整骨架 JSON：",
                currentSkeletonJson, userInstruction ?? "");
        }

        public static async Task<StorySkeleton> ModifySkeletonAsync(
            ApiClient client, StorySkeleton current, string userInstruction, CancellationToken ct)
        {
            var skJson = SerializeSkeleton(current);
            var user = User_ModifySkeleton(skJson, userInstruction);
            var raw = await client.ChatJsonAsync(System_ModifySkeleton, user, ct);
            return Stage2_SkeletonGenerator.Parse(raw);
        }

        // ── 序列化：把现有模型压成 JSON 字串喂给 AI ─────────────
        private static string SerializeWorld(WorldSetting ws)
        {
            if (ws == null) return "{}";
            var sb = new StringBuilder();
            sb.Append("{");
            sb.AppendFormat("\"synopsis\":\"{0}\",", Esc(ws.Synopsis));
            sb.AppendFormat("\"themes\":[{0}],", JsonStrArr(ws.Themes));
            sb.Append("\"characters\":[");
            if (ws.Characters != null)
            {
                var parts = ws.Characters.Select(c => string.Format(
                    "{{\"name\":\"{0}\",\"role\":\"{1}\",\"personality\":\"{2}\"," +
                    "\"background\":\"{3}\",\"appearance\":\"{4}\",\"ability\":\"{5}\"," +
                    "\"goal\":\"{6}\"}}",
                    Esc(c.Name), Esc(c.Role), Esc(c.Personality), Esc(c.Background),
                    Esc(c.Appearance), Esc(c.Ability), Esc(c.Goal)));
                sb.Append(string.Join(",", parts));
            }
            sb.Append("],");
            sb.Append("\"locations\":[");
            if (ws.Locations != null)
            {
                var parts = ws.Locations.Select(l => string.Format(
                    "{{\"name\":\"{0}\",\"type\":\"{1}\",\"description\":\"{2}\"}}",
                    Esc(l.Name), Esc(l.Type), Esc(l.Description)));
                sb.Append(string.Join(",", parts));
            }
            sb.Append("],");
            sb.Append("\"factions\":[");
            if (ws.Factions != null)
            {
                var parts = ws.Factions.Select(f => string.Format(
                    "{{\"name\":\"{0}\",\"ideology\":\"{1}\",\"territory\":\"{2}\"," +
                    "\"influence\":\"{3}\",\"description\":\"{4}\"}}",
                    Esc(f.Name), Esc(f.Ideology), Esc(f.Territory), Esc(f.Influence), Esc(f.Description)));
                sb.Append(string.Join(",", parts));
            }
            sb.Append("],");
            sb.Append("\"rules\":[");
            if (ws.Rules != null)
            {
                var parts = ws.Rules.Select(r => string.Format(
                    "{{\"category\":\"{0}\",\"description\":\"{1}\",\"isHard\":{2}}}",
                    Esc(r.Category), Esc(r.Description), r.IsHard ? "true" : "false"));
                sb.Append(string.Join(",", parts));
            }
            sb.Append("],");
            sb.Append("\"timeline\":[");
            if (ws.Timeline != null)
            {
                var parts = ws.Timeline.Select(t => string.Format(
                    "{{\"title\":\"{0}\",\"time\":\"{1}\",\"description\":\"{2}\"}}",
                    Esc(t.Title), Esc(t.Time), Esc(t.Description)));
                sb.Append(string.Join(",", parts));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string SerializeSkeleton(StorySkeleton sk)
        {
            if (sk == null) return "{}";
            var sb = new StringBuilder();
            sb.Append("{");
            sb.AppendFormat("\"logline\":\"{0}\",", Esc(sk.Logline));
            sb.AppendFormat("\"threeAct\":\"{0}\",", Esc(sk.ThreeAct));
            sb.Append("\"volumes\":[");
            if (sk.Volumes != null)
            {
                var parts = sk.Volumes.Select(v => string.Format(
                    "{{\"title\":\"{0}\",\"synopsis\":\"{1}\",\"plotGoal\":\"{2}\",\"climax\":\"{3}\"," +
                    "\"chapters\":[{4}]}}",
                    Esc(v.Title), Esc(v.Synopsis), Esc(v.PlotGoal), Esc(v.Climax),
                    SerializeChapters(v.Chapters)));
                sb.Append(string.Join(",", parts));
            }
            sb.Append("],");
            sb.Append("\"foreshadowings\":[");
            if (sk.PlantedForeshadowings != null)
            {
                var parts = sk.PlantedForeshadowings.Select(f => string.Format(
                    "{{\"description\":\"{0}\",\"plantedAtChapter\":{1},\"expectedPayoffChapter\":{2}}}",
                    Esc(f.Description), f.PlantedAtChapter, f.ExpectedPayoffChapter));
                sb.Append(string.Join(",", parts));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string SerializeChapters(List<ChapterOutline> chs)
        {
            if (chs == null) return "";
            var parts = chs.Select(c => string.Format(
                "{{\"title\":\"{0}\",\"synopsis\":\"{1}\",\"mood\":\"{2}\"," +
                "\"keyBeats\":[{3}],\"characterIds\":[{4}],\"locationId\":\"{5}\"}}",
                Esc(c.Title), Esc(c.Synopsis), Esc(c.Mood),
                JsonStrArr(c.KeyBeats), JsonStrArr(c.CharacterIds), Esc(c.LocationId)));
            return string.Join(",", parts);
        }

        private static string JsonStrArr(string[] arr)
        {
            if (arr == null || arr.Length == 0) return "";
            return string.Join(",", arr.Select(s => "\"" + Esc(s) + "\""));
        }

        private static string Esc(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') { /* 丢弃 */ }
                else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

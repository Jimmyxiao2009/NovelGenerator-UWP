using System;
using System.Collections.Generic;
using novelgen.Models;

namespace novelgen.AI
{
    /// <summary>集中存放 6 个阶段的 prompt 模板。</summary>
    public static class PromptRegistry
    {
        // ── ② 世界设定 ─────────────────────────────────────────────
        public const string System_WorldSetting =
            "你是一位世界观设计师。基于用户的提示和指定的小说风格，输出严格的 JSON 对象。\n" +
            "要求：人物至少 3 名（主角 + 2 配角/反派），地点至少 2 个，规则至少 3 条。\n" +
            "只输出 JSON，不要任何解释。";

        public static string User_WorldSetting(string userPrompt, string genre, string subGenre, string preset,
            int charCount = 3, int locCount = 2, int factionCount = 2, int ruleCount = 3)
        {
            return string.Format(
                "【用户原始提示】\n{0}\n\n【小说风格】{1} / {2}\n【风格预设】{3}\n\n" +
                "【规模硬性要求】\n" +
                "- 至少 {4} 名人物（主角 + 配角/反派，可以多但不能少）\n" +
                "- 至少 {5} 个地点\n" +
                "- 至少 {6} 个势力\n" +
                "- 至少 {7} 条规则（其中硬规则至少 1 条）\n\n" +
                "请输出 JSON，结构：\n" +
                "{{\n" +
                "  \"title\": \"为这本书取一个有吸引力的中文书名（不超过 15 字，不要书名号、不要把简介当书名）\",\n" +
                "  \"synopsis\": \"200 字以内的故事梗概\",\n" +
                "  \"themes\": [\"主题1\", \"主题2\"],\n" +
                "  \"characters\": [{{ \"name\":\"\", \"role\":\"\", \"personality\":\"\", \"background\":\"\", \"appearance\":\"\", \"ability\":\"\", \"goal\":\"\" }}],\n" +
                "  \"factions\":  [{{ \"name\":\"\", \"ideology\":\"\", \"territory\":\"\", \"influence\":\"\" }}],\n" +
                "  \"locations\": [{{ \"name\":\"\", \"type\":\"\", \"description\":\"\" }}],\n" +
                "  \"rules\":     [{{ \"category\":\"\", \"description\":\"\", \"isHard\":true }}],\n" +
                "  \"timeline\":  [{{ \"title\":\"\", \"time\":\"\", \"description\":\"\" }}]\n" +
                "}}",
                userPrompt, genre, subGenre ?? "无", preset ?? "通用",
                Math.Max(1, charCount), Math.Max(1, locCount), Math.Max(1, factionCount), Math.Max(1, ruleCount));
        }

        // ── ③ 故事骨架 ─────────────────────────────────────────────
        public const string System_Skeleton =
            "你是故事结构师。基于世界设定，生成 N 卷 × M 章的故事骨架。\n" +
            "为每章标注 2-4 个关键节拍；识别至少 2 个伏笔并标注预期回收章节。\n" +
            "只输出 JSON。";

        public static string User_Skeleton(string worldJson, int targetChapters, int targetWords, string preset, int targetVolumes = 0)
        {
            var volumeHint = targetVolumes > 0
                ? $"严格分 {targetVolumes} 卷，平均每卷 {(int)System.Math.Round((double)targetChapters / targetVolumes)} 章。\n"
                : "卷数由你按故事节奏决定（建议 3-5 卷）。\n";
            return string.Format(
                "【世界设定】\n{0}\n\n【目标】{1} 章，每章约 {2} 字\n【风格】{3}\n{4}\n" +
                "请输出 JSON，结构：\n" +
                "{{\n" +
                "  \"logline\": \"一句话故事\",\n" +
                "  \"threeAct\": \"起承转合描述\",\n" +
                "  \"volumes\": [{{\n" +
                "    \"title\": \"\", \"synopsis\": \"\", \"plotGoal\": \"\", \"climax\": \"\",\n" +
                "    \"chapters\": [{{ \"title\":\"\", \"synopsis\":\"\", \"mood\":\"\", \"keyBeats\":[\"\"], \"characterIds\":[\"\"], \"locationId\":\"\" }}]\n" +
                "  }}],\n" +
                "  \"foreshadowings\": [{{ \"description\":\"\", \"plantedAtChapter\":1, \"expectedPayoffChapter\":5 }}]\n" +
                "}}", worldJson, targetChapters, targetWords, preset ?? "通用", volumeHint);
        }

        // ── ⑤ 章节生成 ─────────────────────────────────────────────
        public const string System_Chapter =
            "你是一位小说家。基于本章大纲和已知的世界设定，写一章完整的小说正文。\n" +
            "要求：\n" +
            "1. 对话与叙事交融，符合目标风格；\n" +
            "2. 章末必须留钩子（悬念/反转/情绪共鸣）；\n" +
            "3. 不要破坏硬规则；\n" +
            "4. 严格遵守人物性格、已确立的称呼和关系。";

        public static string User_Chapter(string worldSummary, string outlineJson, string previousSummary, string ragContext, string extra)
        {
            return string.Format(
                "【世界设定摘要】\n{0}\n\n【本章大纲】\n{1}\n\n【前文摘要】\n{2}\n\n【相关设定召回】\n{3}\n\n【用户额外要求】\n{4}\n\n请直接输出本章正文，不要任何前置说明。",
                worldSummary, outlineJson, previousSummary ?? "（无）", ragContext ?? "（无）", extra ?? "（无）");
        }

        // ── ⑥ 审查润色 ─────────────────────────────────────────────
        public const string System_Polish =
            "你是一位主编 + 润色编辑。任务：\n" +
            "1. 检查本章与【世界设定】【前文摘要】的一致性；\n" +
            "2. 改善文笔、节奏、对话；\n" +
            "3. 输出润色后的完整正文 + 审查报告。\n" +
            "先输出润色后正文（用 ===POLISHED=== 包裹），再输出审查报告（用 ===REPORT=== 包裹的 JSON）。";

        public static string User_Polish(string worldSummary, string outlineJson, string prevSummary, string draft)
        {
            return string.Format(
                "【世界设定摘要】\n{0}\n\n【本章大纲】\n{1}\n\n【前文摘要】\n{2}\n\n【本章草稿】\n{3}\n\n" +
                "输出格式：\n" +
                "===POLISHED===\n<润色后正文>\n===END===\n" +
                "===REPORT===\n{{ \"qualityScore\": 1-10, \"summary\":\"\", \"notes\":[{{ \"type\":\"Style|Pacing|Grammar|Character|Dialogue|Description|Other\", \"excerpt\":\"\", \"suggestion\":\"\", \"severity\":\"Minor|Major|Critical\" }}], \"issues\":[{{ \"type\":\"NameInconsistency|TimelineConflict|RuleBreak|LocationError|AbilityContradiction|Other\", \"severity\":\"Minor|Major|Critical\", \"description\":\"\", \"suggestion\":\"\" }}] }}\n===END===",
                worldSummary, outlineJson, prevSummary ?? "（无）", draft);
        }

        // ── 卷内连接 agent ─────────────────────────────────────────
        public const string System_VolumeConnector =
            "你是小说衔接师。任务：读取一卷内所有章节的首尾，为相邻章节生成过渡桥段。\n" +
            "要求：\n" +
            "1. 每处过渡 1-3 句即可，保持风格一致；\n" +
            "2. 修正人称、地点、时间线的不一致；\n" +
            "3. 只输出 JSON，不要任何解释。";

        public static string User_VolumeConnector(string worldSummary, string volumeContext)
        {
            return string.Format(
                "【世界设定】\n{0}\n\n【卷内容（各章首尾）】\n{1}\n\n" +
                "请输出 JSON：\n" +
                "{{\n" +
                "  \"transitions\": [\n" +
                "    {{ \"from\": 1, \"to\": 2, \"bridge\": \"过渡段落，追加到第1章末尾。可选填充时间跳跃、视角切换、悬念勾连。\" }}\n" +
                "  ]\n" +
                "}}",
                worldSummary, volumeContext);
        }

        // ── ⑧ 合并 ─────────────────────────────────────────────────
        public const string System_Compose =
            "你是出版编辑。基于已接受章节生成书简介、标签、目录。";

        public static string User_Compose(string title, string worldSynopsis, string chapterList)
        {
            return string.Format(
                "【书名】{0}\n【世界梗概】{1}\n【已接受章节】\n{2}\n\n" +
                "请输出 JSON：\n" +
                "{{ \"description\":\"200字简介\", \"tags\":[\"\"], \"tableOfContents\":\"按章节生成的目录\" }}",
                title, worldSynopsis, chapterList);
        }
    }
}

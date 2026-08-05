using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using novelgen.Models;

namespace novelgen.AI.RAG
{
    /// <summary>世界书召回：基于实体名命中 + 中文字符二元组（Dice 系数）的关键词召回。
    /// 注：嵌入向量精排（<see cref="Embedder"/>）尚未实现，当前仅用上述关键词打分。</summary>
    public class WorldbookRetriever
    {
        public class RetrievalResult
        {
            public List<Character> Characters = new List<Character>();
            public List<Location>  Locations  = new List<Location>();
            public List<Faction>   Factions   = new List<Faction>();
            public List<WorldRule> Rules      = new List<WorldRule>();
            public List<Item>      Items      = new List<Item>();
            public string ContextText;
        }

        public async Task<RetrievalResult> RetrieveAsync(WorldSetting ws, string query, int topK = 5)
        {
            if (ws == null) return new RetrievalResult();
            await Task.Yield();

            // 预计算查询的二元组集合，避免每个条目重复切分
            var queryBigrams = Bigrams(query);

            var result = new RetrievalResult();
            if (ws.Characters != null)
                result.Characters = ws.Characters.OrderByDescending(c => Score(c, query, queryBigrams)).Take(topK).ToList();
            if (ws.Locations != null)
                result.Locations = ws.Locations.OrderByDescending(l => Score(l, query, queryBigrams)).Take(topK).ToList();
            if (ws.Factions != null)
                result.Factions = ws.Factions.OrderByDescending(f => Score(f, query, queryBigrams)).Take(3).ToList();
            if (ws.Rules != null)
                result.Rules = ws.Rules.OrderByDescending(r => Score(r, query, queryBigrams)).Take(topK).ToList();

            result.ContextText = Format(result);
            return result;
        }

        /// <summary>
        /// 中文相关性打分：实体名出现在查询中给强加分；其余用「字符二元组」重叠度（Dice 系数）。
        /// （旧实现按空格/标点切「词」，中文查询切出来的是整句，几乎永不命中，召回等于随机。）
        /// </summary>
        private static double Score(object obj, string query, HashSet<string> queryBigrams)
        {
            string text = ObjectToText(obj);
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return 0;

            double score = 0;
            var name = ObjectName(obj);
            if (!string.IsNullOrEmpty(name) && query.Contains(name)) score += 10; // 名称直接命中：最强信号

            var textBigrams = Bigrams(text);
            if (queryBigrams.Count > 0 && textBigrams.Count > 0)
            {
                int common = 0;
                foreach (var g in queryBigrams)
                    if (textBigrams.Contains(g)) common++;
                score += 2.0 * common / (queryBigrams.Count + textBigrams.Count) * 10;
            }
            return score;
        }

        private static HashSet<string> Bigrams(string s)
        {
            var set = new HashSet<string>();
            if (string.IsNullOrEmpty(s)) return set;
            char prev = '\0';
            foreach (var c in s)
            {
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c)) { prev = '\0'; continue; }
                if (prev != '\0') set.Add(new string(new[] { prev, c }));
                prev = c;
            }
            return set;
        }

        private static string ObjectName(object obj)
        {
            if (obj is Character c) return c.Name;
            if (obj is Location l) return l.Name;
            if (obj is Faction f) return f.Name;
            if (obj is Item i) return i.Name;
            return null;
        }

        private static string ObjectToText(object obj)
        {
            if (obj is Character c) return c.Name + " " + c.Personality + " " + c.Background + " " + c.Goal;
            if (obj is Location l)  return l.Name + " " + l.Type + " " + l.Description;
            if (obj is Faction f)   return f.Name + " " + f.Ideology + " " + f.Description;
            if (obj is WorldRule r) return r.Category + " " + r.Description;
            if (obj is Item i)      return i.Name + " " + i.Description;
            return obj?.ToString() ?? "";
        }

        private static string Format(RetrievalResult r)
        {
            var sb = new System.Text.StringBuilder();
            if (r.Characters.Count > 0)
            {
                sb.AppendLine("【人物】");
                foreach (var c in r.Characters) sb.AppendLine($"- {c.Name}：{c.Personality} | 目标：{c.Goal}");
            }
            if (r.Locations.Count > 0)
            {
                sb.AppendLine("【地点】");
                foreach (var l in r.Locations) sb.AppendLine($"- {l.Name}（{l.Type}）：{l.Description}");
            }
            if (r.Factions.Count > 0)
            {
                sb.AppendLine("【势力】");
                foreach (var f in r.Factions) sb.AppendLine($"- {f.Name}：{f.Ideology}");
            }
            if (r.Rules.Count > 0)
            {
                sb.AppendLine("【硬规则】");
                foreach (var ru in r.Rules) sb.AppendLine($"- [{ru.Category}] {ru.Description}");
            }
            return sb.ToString();
        }
    }
}

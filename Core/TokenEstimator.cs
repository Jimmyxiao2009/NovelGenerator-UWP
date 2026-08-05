using System;

namespace novelgen.Core
{
    /// <summary>
    /// 简单 token 估算（中文 ≈ 1.5 token/字；英文 ≈ 0.25 token/字符）。
    /// 流水线用它做上下文长度估算与压缩触发判断。
    /// </summary>
    public static class TokenEstimator
    {
        /// <summary>是否为 CJK 表意文字（含扩展 A 区、兼容区）或 CJK 标点/全角字符/假名。</summary>
        public static bool IsCjk(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF)    // CJK 统一表意
                || (c >= 0x3400 && c <= 0x4DBF)    // 扩展 A
                || (c >= 0xF900 && c <= 0xFAFF)    // 兼容表意
                || (c >= 0x3000 && c <= 0x303F)    // CJK 标点（，。「」等的一部分）
                || (c >= 0xFF00 && c <= 0xFFEF)    // 全角形式（，！？等）
                || (c >= 0x3040 && c <= 0x30FF);   // 假名
        }

        public static int Estimate(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int cjk = 0, other = 0;
            foreach (char c in text)
            {
                if (IsCjk(c)) cjk++;
                else if (char.IsSurrogate(c)) cjk++;   // 扩展 B+ 表意字（代理对）按 CJK 权重
                else if (!char.IsWhiteSpace(c)) other++;
            }
            double t = cjk * 1.5 + other * 0.25;
            int words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            double w = words * 1.3;
            return (int)Math.Max(t, w);
        }

        /// <summary>
        /// 字数统计：CJK 每字记 1，非 CJK 文本按空白分隔的连续串每串记 1 词。
        /// （纯英文小说不再统计为 0。）
        /// </summary>
        public static int CountWords(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            bool inWord = false;
            foreach (char c in s)
            {
                if (c >= 0x4E00 && c <= 0x9FFF || c >= 0x3400 && c <= 0x4DBF || c >= 0xF900 && c <= 0xFAFF)
                {
                    n++;
                    inWord = false;
                }
                else if (char.IsLetterOrDigit(c))
                {
                    if (!inWord) { n++; inWord = true; }
                }
                else
                {
                    inWord = false;
                }
            }
            return n;
        }

        public static string Format(int tokens)
        {
            if (tokens < 1000) return tokens.ToString();
            if (tokens < 10000) return (tokens / 1000.0).ToString("F1") + "k";
            if (tokens < 1000000) return (tokens / 1000).ToString() + "k";
            return (tokens / 1000000.0).ToString("F1") + "M";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace novelgen.Models
{
    [DataContract]
    public class ApiProfile
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name { get; set; } = "新 API";
        [DataMember] public string Url { get; set; } = "https://api.deepseek.com/v1/chat/completions";
        /// <summary>API 密钥。仅存在于内存与 PasswordVault，不随 JSON 明文落盘。</summary>
        public string ApiKey { get; set; } = "";
        /// <summary>旧版明文落盘的密钥（仅兼容读取；DataManager 载入后迁移至 PasswordVault 并置空）。</summary>
        [DataMember(Name = "ApiKey", EmitDefaultValue = false)]
        internal string LegacyApiKey { get; set; }
        [DataMember] public string Model { get; set; } = "deepseek-chat";
        /// <summary>openai（兼容）| claude（Anthropic 原生）| plaa</summary>
        [DataMember] public string ProviderType { get; set; } = "openai";
        /// <summary>上下文窗口 token 数，0=自动检测</summary>
        [DataMember] public int MaxContextTokens { get; set; } = 65536;
        /// <summary>单次回复最大输出 token，0=默认（8192）。Claude 必填；过小会导致章节被截断</summary>
        [DataMember] public int MaxOutputTokens { get; set; } = 8192;
        [DataMember] public DateTime CreatedAt { get; set; } = DateTime.Now;

        public override string ToString() => Name;
    }

    [DataContract]
    public static class ModelContextLimits
    {
        private static readonly Dictionary<string, int> _known = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["deepseek-chat"] = 65536,
            ["deepseek-reasoner"] = 65536,
            ["gpt-4o"] = 128000,
            ["gpt-4o-mini"] = 128000,
            ["claude-sonnet-4-6"] = 200000,
            ["claude-haiku-4-5-20251001"] = 200000,
            ["gemini-2.5-pro"] = 1048576,
            ["gemini-2.5-flash"] = 1048576,
            ["qwen3-32b"] = 32768,
            ["glm-4-flash"] = 128000,
        };

        public static int GetLimit(string modelName, int defaultValue = 32000)
        {
            if (string.IsNullOrEmpty(modelName)) return defaultValue;
            int v;
            if (_known.TryGetValue(modelName, out v)) return v;
            foreach (var kv in _known)
                if (modelName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0) return kv.Value;
            return defaultValue;
        }

        public static int GetEffectiveLimit(ApiProfile p)
        {
            if (p == null) return 32000;
            return p.MaxContextTokens > 0 ? p.MaxContextTokens : GetLimit(p.Model);
        }
    }
}

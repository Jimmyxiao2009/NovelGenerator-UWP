using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace novelgen.Core
{
    /// <summary>本地设置（UI 偏好 / 默认 API / 生成参数）。存于 LocalSettings。
    /// 读取一律走类型安全的 Get*，存量值类型不符（版本迁移/手工改动）时回退默认值而非抛 InvalidCastException。</summary>
    public static class AppSettings
    {
        private static ApplicationDataContainer Local => ApplicationData.Current.LocalSettings;

        // ── 主题 / 语言 ───────────────────────────────────────────────
        public static bool IsDark
        {
            get => GetBool("DarkMode");
            set => Local.Values["DarkMode"] = value;
        }
        public static string S(string zh, string en) => IsEnglish ? en : zh;
        public static bool IsEnglish
        {
            get => GetBool("LangEn");
            set => Local.Values["LangEn"] = value;
        }

        // ── 默认 API ─────────────────────────────────────────────────
        public static string DefaultApiProfileId
        {
            get => GetString("DefaultApiId");
            set => Local.Values["DefaultApiId"] = value;
        }

        // ── 流水线参数 ───────────────────────────────────────────────
        // 注：ChapterReviewMode / DefaultTargetWords / IsEnglish / OobeCompleted 由 UI 层（novel-ui）消费，
        // 故意保留，勿删。
        /// <summary>逐章 vs 批量审查 0=逐章 1=批量</summary>
        public static int ChapterReviewMode
        {
            get => GetInt("ChapterReviewMode", 0);
            set => Local.Values["ChapterReviewMode"] = value;
        }
        /// <summary>每章目标字数</summary>
        public static int DefaultTargetWords
        {
            get => GetInt("DefaultTargetWords", 2500);
            set => Local.Values["DefaultTargetWords"] = value;
        }
        /// <summary>草稿模型 API（可与精修模型不同）</summary>
        public static string DraftApiProfileId
        {
            get => GetString("DraftApiId");
            set => Local.Values["DraftApiId"] = value;
        }
        /// <summary>精修/审查模型 API</summary>
        public static string PolishApiProfileId
        {
            get => GetString("PolishApiId");
            set => Local.Values["PolishApiId"] = value;
        }
        /// <summary>agent 并发数（卷内并行生成章节数，默认 3）</summary>
        public static int AgentConcurrency
        {
            get => GetInt("AgentConcurrency", 3);
            set => Local.Values["AgentConcurrency"] = value;
        }

        // ── OOBE ─────────────────────────────────────────────────────
        public static bool OobeCompleted
        {
            get => GetBool("OobeCompleted");
            set => Local.Values["OobeCompleted"] = value;
        }

        // ── Helpers ──────────────────────────────────────────────────
        public static void ApplyTheme(FrameworkElement element)
        {
            if (element != null) element.RequestedTheme = IsDark ? ElementTheme.Dark : ElementTheme.Light;
        }

        public static bool GetBool(string key, bool defaultValue = false)
        {
            object v;
            return Local.Values.TryGetValue(key, out v) && v is bool b ? b : defaultValue;
        }

        public static void SetBool(string key, bool value) => Local.Values[key] = value;

        public static int GetInt(string key, int defaultValue = 0)
        {
            object v;
            if (!Local.Values.TryGetValue(key, out v)) return defaultValue;
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v is string s && int.TryParse(s, out var parsed)) return parsed;
            return defaultValue;
        }

        public static string GetString(string key, string defaultValue = "")
        {
            object v;
            return Local.Values.TryGetValue(key, out v) && v is string s ? s : defaultValue;
        }

        public static void SetString(string key, string value) => Local.Values[key] = value;
    }
}

using System.Collections.Generic;
using novelgen.Models;

namespace novelgen.AI
{
    /// <summary>内置 Genre / StylePreset 目录。首次启动时拷贝到 DataManager。</summary>
    public static class GenrePresets
    {
        public static List<Genre> BuiltInGenres() => new List<Genre>
        {
            new Genre { Name = "玄幻", SubGenres = new List<string> { "东方玄幻", "异世大陆", "高武", "修仙" } },
            new Genre { Name = "都市", SubGenres = new List<string> { "都市生活", "都市异能", "商战", "职场" } },
            new Genre { Name = "科幻", SubGenres = new List<string> { "硬科幻", "软科幻", "赛博朋克", "末世" } },
            new Genre { Name = "历史", SubGenres = new List<string> { "穿越", "架空", "权谋", "军事" } },
            new Genre { Name = "武侠", SubGenres = new List<string> { "传统武侠", "新派武侠", "国术" } },
            new Genre { Name = "言情", SubGenres = new List<string> { "现代言情", "古代言情", "校园", "总裁" } },
            new Genre { Name = "悬疑", SubGenres = new List<string> { "推理", "灵异", "惊悚" } },
            new Genre { Name = "轻小说", SubGenres = new List<string> { "恋爱", "冒险", "校园" } },
            new Genre { Name = "通用", SubGenres = new List<string>() },
        };

        public static List<StylePreset> BuiltInPresets() => new List<StylePreset>
        {
            new StylePreset
            {
                Name = "玄幻·爽文", Genre = "玄幻", SubGenre = "东方玄幻",
                Description = "节奏快、爽点密、章末必留钩子。",
                SystemPrompt = "你是顶级网文作家，擅长玄幻爽文。",
                TargetWordsPerChapter = 2500, HookDensity = 5,
                DialogueStyle = "简洁有力，多短句",
            },
            new StylePreset
            {
                Name = "都市·现实", Genre = "都市", SubGenre = "都市生活",
                Description = "贴近生活、人物立体、情感细腻。",
                SystemPrompt = "你是都市小说作家，注重真实感和情感共鸣。",
                TargetWordsPerChapter = 2200, HookDensity = 3,
            },
            new StylePreset
            {
                Name = "科幻·硬核", Genre = "科幻", SubGenre = "硬科幻",
                Description = "科技逻辑严谨，设定详细，节奏沉稳。",
                SystemPrompt = "你是硬科幻作家，遵守物理法则和科学设定。",
                TargetWordsPerChapter = 2800, HookDensity = 3,
            },
            new StylePreset
            {
                Name = "言情·细腻", Genre = "言情", SubGenre = "现代言情",
                Description = "以情感线为主，环境渲染细腻。",
                SystemPrompt = "你是言情小说家，擅长细腻情感描写。",
                TargetWordsPerChapter = 2200, HookDensity = 3,
            },
            new StylePreset
            {
                Name = "轻小说", Genre = "轻小说", SubGenre = "校园",
                Description = "对话为主，叙事轻快，人物萌点突出。",
                SystemPrompt = "你是轻小说作家，对话生动有趣。",
                TargetWordsPerChapter = 2000, HookDensity = 4,
            },
            new StylePreset
            {
                Name = "通用", Genre = "通用", SubGenre = "",
                Description = "默认风格，无特殊偏好。",
                SystemPrompt = "你是一位经验丰富的小说家。",
                TargetWordsPerChapter = 2500, HookDensity = 3,
            },
        };

        public static StylePreset FindById(Novel novel, List<StylePreset> catalog)
        {
            if (novel == null) return null;
            // 未显式给目录时，用「用户自定义预设 + 内置预设」的合并目录
            //（此前传 null 直接返回 null，导致风格预设从未生效）
            if (catalog == null || catalog.Count == 0)
            {
                catalog = new List<StylePreset>();
                var custom = Core.DataManager.Data?.StylePresets;
                if (custom != null) catalog.AddRange(custom);
                catalog.AddRange(BuiltInPresets());
            }
            if (!string.IsNullOrEmpty(novel.StylePresetId))
            {
                var hit = catalog.Find(p => p.Id == novel.StylePresetId);
                if (hit != null) return hit;
            }
            return catalog.Find(p => p.Genre == novel.Genre && p.SubGenre == novel.SubGenre)
                ?? catalog.Find(p => p.Genre == novel.Genre)
                ?? catalog.Find(p => p.Name == "通用");
        }
    }
}

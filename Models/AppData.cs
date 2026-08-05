using System.Collections.Generic;
using System.Runtime.Serialization;

namespace novelgen.Models
{
    /// <summary>应用级数据根：所有小说的索引 + 全局 API 池 + 风格预设目录。</summary>
    [DataContract]
    public class AppData
    {
        [DataMember] public List<Novel> Novels { get; set; } = new List<Novel>();
        [DataMember] public List<ApiProfile> ApiProfiles { get; set; } = new List<ApiProfile>();
        [DataMember] public List<StylePreset> StylePresets { get; set; } = new List<StylePreset>();
        [DataMember] public List<Genre> Genres { get; set; } = new List<Genre>();
        [DataMember] public string DefaultApiProfileId { get; set; } = "";
        /// <summary>数据结构版本，供未来迁移用。</summary>
        [DataMember] public int SchemaVersion { get; set; } = 1;

        // DataContractJsonSerializer 用 GetUninitializedObject 构造对象，
        // 属性初始化器不会执行；旧版 JSON 缺少的成员会是 null，必须在此兜底。
        [OnDeserialized]
        private void OnDeserialized(StreamingContext ctx)
        {
            if (Novels == null) Novels = new List<Novel>();
            if (ApiProfiles == null) ApiProfiles = new List<ApiProfile>();
            if (StylePresets == null) StylePresets = new List<StylePreset>();
            if (Genres == null) Genres = new List<Genre>();
            if (DefaultApiProfileId == null) DefaultApiProfileId = "";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace novelgen.Models
{
    [DataContract]
    public class Character
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name { get; set; }
        [DataMember] public string[] Aliases { get; set; } = new string[0];
        [DataMember] public string Gender { get; set; } = "";
        [DataMember] public int Age { get; set; } = 0;
        [DataMember] public string Role { get; set; } = "";                  // 主角/配角/反派
        [DataMember] public string Personality { get; set; } = "";
        [DataMember] public string Background { get; set; } = "";
        [DataMember] public string Appearance { get; set; } = "";
        [DataMember] public string Ability { get; set; } = "";
        [DataMember] public string Goal { get; set; } = "";
        [DataMember] public string Arc { get; set; } = "";                   // 角色弧光
        [DataMember] public string[] Relationships { get; set; } = new string[0];
        [DataMember] public string Status { get; set; } = "active";          // active/dead/missing
        /// <summary>异步生成；用于 RAG 召回</summary>
        [DataMember] public float[] Embedding { get; set; }
    }

    [DataContract]
    public class Faction
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name { get; set; }
        [DataMember] public string Ideology { get; set; } = "";
        [DataMember] public string Leader { get; set; } = "";
        [DataMember] public string[] Members { get; set; } = new string[0];
        [DataMember] public string Territory { get; set; } = "";
        [DataMember] public string Influence { get; set; } = "";
        [DataMember] public string Description { get; set; } = "";
    }

    [DataContract]
    public class Location
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name { get; set; }
        [DataMember] public string Type { get; set; } = "";                  // 城市/秘境/宗门…
        [DataMember] public string Description { get; set; } = "";
        [DataMember] public string[] NotableFeatures { get; set; } = new string[0];
    }

    [DataContract]
    public class Item
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Name { get; set; }
        [DataMember] public string Type { get; set; } = "";
        [DataMember] public string Description { get; set; } = "";
        [DataMember] public string Rarity { get; set; } = "";
        [DataMember] public string Origin { get; set; } = "";
    }

    /// <summary>世界硬规则：AI 不得违反。</summary>
    [DataContract]
    public class WorldRule
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Category { get; set; } = "";             // 物理/魔法/社会…
        [DataMember] public string Description { get; set; }
        [DataMember] public bool IsHard { get; set; } = true;
    }

    /// <summary>时间线事件。</summary>
    [DataContract]
    public class WorldEvent
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string Title { get; set; }
        [DataMember] public string Time { get; set; } = "";                 // 故事内时间
        [DataMember] public string Location { get; set; } = "";
        [DataMember] public string[] Characters { get; set; } = new string[0];
        [DataMember] public string Description { get; set; } = "";
    }
}

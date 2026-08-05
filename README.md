# 小说生成器 (Novel Generator)

基于 LLM 的 **流水线式** 小说生成 UWP 应用。状态机驱动：用户提示词 + Genre → 世界设定 → 故事骨架 → 用户审查 → 分章生成 → AI 审查润色 → 用户审查 → 合并提交 → 导出 EPUB/TXT。

## 流水线一览

```
[① 用户提示词 + Genre]
        ↓
[② AI 生成 世界设定]                    ← Worldbook (人物/势力/地点/规则)
        ↓
[③ AI 生成 故事骨架]                    ← Logline + 卷/章大纲 + 伏笔
        ↓
[④ 🚦 Gate #1 — 用户审查]               ← Accept / Edit / RequestRevision / Reject
        ↓
[⑤ AI 分章生成 (流式)] + [⑥ AI 审查润色 (流式 + 报告)]
        ↓
[⑦ 🚦 Gate #2 — 章节审查（逐章或批量）] ← Accept / Edit / RequestRevision / Reject
        ↓
[⑧ 合并 → 简介/标签/TOC]                 ← Composition
        ↓
[⑨ 导出 → EPUB / TXT]                    ← ExportRecord
```

## 目录结构

```
Novel Generator/
├── Core/                # 基础设施（AppSettings / DataManager / TokenEstimator）
├── Models/              # 12 个 DataContract 数据模型
├── Pipeline/            # 状态机 + 编排
│   ├── NovelPipeline.cs          ← 状态机主类（9 个阶段入口 + 4 类事件）
│   ├── PipelineOrchestrator.cs   ← 跨阶段协调
│   ├── PipelineHost.cs           ← App 级单例
│   ├── StageContext.cs           ← 单次执行的输入/进度
│   └── PipelineEvents.cs         ← 事件契约
├── AI/
│   ├── ApiClient.cs              ← OpenAI/Claude 通用客户端
│   ├── PromptRegistry.cs         ← 6 套 prompt 模板
│   ├── GenrePresets.cs           ← 内置 Genre / StylePreset
│   ├── RAG/                      ← Embedder + WorldbookRetriever
│   └── Generation/               ← 6 个阶段生成器
├── Services/            # TxtExporter / EpubExporter
├── Pages/               # 5 个最小 UI 页
├── Themes/              # 资源字典
├── Common/              # RelayCommand / NavigationHelper
└── Package.appxmanifest # UWP 清单
```

## 复用 Lumina.Agent 的部分

| 借鉴内容 | 实现位置 |
|---|---|
| UWP csproj 结构（ARM/x86/x64） | `NovelGenerator.csproj` |
| `DataContract` + `DataContractJsonSerializer` 序列化 | `Models/*.cs` |
| `HttpClient` + SSE 流式 | `AI/ApiClient.cs` |
| OpenAI 兼容 + Claude 原生头 | `AI/ApiClient.cs::AddAuthHeaders` |
| `ApplicationData.LocalFolder` 持久化 | `Core/DataManager.cs` |
| 简单 token 估算（中文 1.5/字） | `Core/TokenEstimator.cs` |
| 命名空间隔离（`novelgen` 而非 `yanshuai`） | 全局 |

> **不复用 Lumina.Agent 项目本身**——保持独立演进，借鉴模式而非耦合。

## 当前状态：P1 骨架完成

✅ 项目可编译运行（需 Visual Studio 2017+ with UWP tools）
✅ 状态机可走通：Draft → ② → ③ → Gate1
✅ 6 个生成器接口已定，AI 调用可工作（需配置 API Profile）
✅ 数据持久化（每本书单独 JSON）
✅ EPUB / TXT 导出可工作

⏳ 待实现（按 P2/P3 计划）：
- 章节生成 / 审查 / 润色 UI（⑤⑥⑦）
- 上下文压缩（前 5 章 → 摘要）
- 伏笔追踪（自动检测未回收）
- 章节编辑器（流式显示）
- API Profiles 编辑页
- 风格预设编辑页
- 主题/深色模式

## 运行

1. 用 Visual Studio 2017/2019/2022 打开 `NovelGenerator.sln`
2. 选择 `Debug | x86` 或 `ARM`（Lumia 950）或 `x64`（桌面）
3. 首次启动 → 进入"我的书库"页 → 点 "➕ 新建小说"
4. 填写提示词 + 选 Genre/Preset → 点"开始生成"
5. 跳到"② 世界设定"页 → AI 自动生成 → 点"继续"
6. 跳到"③ 故事骨架"页 → AI 自动生成 → 点"继续"
7. 跳到"Gate #1"页 → 接受 / 重做 / 驳回

## 打包发布

### 方式 A：Visual Studio 向导（推荐）

1. 解决方案资源管理器右键 `NovelGenerator` → **发布 → 创建应用程序包**
2. 选择 **旁加载** → 下一步
3. 签名证书：保持默认（csproj 已经预先指向 `NovelGenerator_TemporaryKey.pfx`，指纹 `BA2C6CAA…`）
4. 配置勾选 **Release · x86 / x64 / ARM** 三项 → 创建
5. 输出位置：`bin\AppPackages\NovelGenerator_<版本>_Test\NovelGenerator_<版本>_x86_x64_arm.appxbundle`

### 方式 B：命令行一步出包

```powershell
powershell -ExecutionPolicy Bypass -File build_and_package.ps1
```

或

```cmd
build_and_package.bat
```

脚本会自动用 `vswhere.exe` 定位 MSBuild（兼容 VS 2017/2019/2022/Preview/18），单次 MSBuild 即可完成 编译 → 打 bundle → 签名，**不再依赖 makeappx / signtool 外部工具，也无任何写死路径**。

### 安装（首次需要授信测试证书）

```cmd
certutil -addstore -f TrustedPeople NovelGenerator_TemporaryKey.pfx
```

然后双击 `.appxbundle` 即可（系统需开启 *开发者模式* 或 *旁加载应用*）。

## 2026-06-10 健壮性大修

- **ApiClient**：真 SSE 流式（`stream:true` + `ResponseHeadersRead` 逐行解析）；SSE delta 解析修复（原 `GetObject()` 用错导致流式提取永远为空）；瞬时错误自动重试（429/5xx/超时，指数退避 + 尊重 `Retry-After`，已吐出内容后不重试）；全局共享 `HttpClient`；`max_tokens` 可配（Profile.MaxOutputTokens，默认 8192）；URL 规范化与控制字符转义
- **数据安全**：所有文件写入原子化（.tmp → 替换）；`_index.json` 轮换备份 `.bak`；索引损坏自动从 `Novels\` 单书文件重建（不再静默清空书库）；索引瘦身（不再每章保存时重写全部正文）；序列化快照与后台并发改写互斥（`DataManager.SyncRoot`）
- **API Key 安全**：迁移至 `PasswordVault`，不再随 JSON 明文落盘（旧明文首次载入自动迁移并抹除）
- **模型层**：全部模型加 `[OnDeserialized]` 兜底，旧版 JSON 缺字段不再 NRE；`AppData.SchemaVersion` 预留迁移
- **流水线**：用户取消不再误判为 Error；`Error` 状态启动时按实际数据自动恢复；卡死在 Drafting/Polishing 的章节自动修复；快照回滚真正还原数据（原来只改状态枚举）；单章失败不再中止整批（汇总报错、可断点续跑）；卷过渡段幂等（不再重复追加）
- **导出**：EPUB 改经 `StorageFile` 代理流写入（原 `FileStream(file.Path)` 在 UWP 沙箱必然失败）；EPUB 3 规范补全（nav.xhtml + `dcterms:modified` + uid 一致复用 Novel.Id）；TXT 统一行尾/无 BOM；零可导出章节快速失败；保留设备名（CON/NUL…）净化；`CachedFileManager` 保护云同步位置
- **页面**：全部 async void 处理器加 try/catch + 防重入；Gate1「重做」先快照后清数据（原先备份拍到的是已销毁状态）；编辑页增删条目前先收割未保存的输入；世界设定保存前确认（会清除骨架+章节）；生成页可取消、离开页面自动中止；`Frame.GoBack()` 全部加 `CanGoBack` 守卫
- **其它**：风格预设真正生效并注入系统提示词（原来从未生效）；RAG 中文打分修复（实体名命中 + 字符二元组重叠，原来恒为 0）；字数统计支持非中文与 CJK 扩展；崩溃时兜底保存数据并提示用户

## 后续工作

- [ ] P2：5 个章节页 + 流式显示 + 用户审查
- [ ] P3：RAG 真实嵌入模型（当前为二元组重叠打分）+ 上下文压缩
- [ ] P4：合并页 + 导出页 UI 完善
- [ ] API Profiles 编辑 UI
- [ ] 风格预设编辑 UI
- [ ] OOBE 流程
- [x] 错误重试（ApiClient 指数退避）+ 进度持久化（断点续跑）

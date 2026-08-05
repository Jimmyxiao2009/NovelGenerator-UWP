using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Security.Credentials;
using Windows.Storage;
using novelgen.Models;

namespace novelgen.Core
{
    /// <summary>
    /// 数据管理器：单一根 <see cref="AppData"/> 静态实例 + 异步持久化。
    /// 每本书单独存为 <c>LocalFolder\Novels\{id}.json</c>（完整数据）；
    /// <c>_index.json</c> 只存轻量元数据（不含章节正文），并带 <c>_index.bak</c> 轮换备份。
    /// 所有文件写入均为「写临时文件 → 原子替换」，避免崩溃/挂起时留下半截文件。
    /// API 密钥存 PasswordVault，不随 JSON 明文落盘（旧版明文会在首次载入时自动迁移）。
    /// </summary>
    public static class DataManager
    {
        private const string IndexFileName = "_index.json";
        private const string IndexBackupName = "_index.bak";
        private const string NovelsFolder = "Novels";
        private const string ApiFolder = "ApiProfiles";
        private const string GenreFolder = "GenrePresets";
        private const string VaultResource = "NovelGenerator.ApiKeys";

        private static AppData _data;
        private static readonly object _saveLock = new object();
        private static readonly SemaphoreSlim _fileWriteSem = new SemaphoreSlim(1, 1);
        private static bool _loaded;
        private static readonly SemaphoreSlim _loadSem = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 模型写入锁：后台线程并发修改 Novel（章节内容/状态等）时请 lock 此对象。
        /// 保存时的序列化快照在同一把锁内进行，二者互斥可避免
        /// DataContractJsonSerializer 遍历对象图途中数据被改写（撕裂保存/序列化异常）。
        /// </summary>
        public static object SyncRoot => _saveLock;

        public static AppData Data
        {
            get
            {
                if (_data == null) _data = new AppData();
                return _data;
            }
        }

        // ── 加载 ─────────────────────────────────────────────────────
        public static async Task LoadAsync()
        {
            if (_loaded) return;
            await _loadSem.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_loaded) return;
                bool migratedLegacyKeys = false;
                try
                {
                    var folder = ApplicationData.Current.LocalFolder;
                    await EnsureSubFolderAsync(folder, NovelsFolder);
                    await EnsureSubFolderAsync(folder, ApiFolder);
                    await EnsureSubFolderAsync(folder, GenreFolder);

                    // 索引 → 备份 → 从单书/Profile 文件重建，三级回退；
                    // 索引损坏不再清空书库（数据仍在 Novels\ 下）。
                    _data = await TryLoadIndexAsync(folder, IndexFileName).ConfigureAwait(false)
                         ?? await TryLoadIndexAsync(folder, IndexBackupName).ConfigureAwait(false)
                         ?? await RebuildFromFilesAsync().ConfigureAwait(false);

                    migratedLegacyKeys = HydrateApiKeys(_data.ApiProfiles);
                }
                catch
                {
                    _data = new AppData();
                }
                _loaded = true;

                if (migratedLegacyKeys)
                {
                    // 把旧版明文密钥从盘上抹掉（重写 Profile 文件与索引，均已不含密钥）
                    try
                    {
                        foreach (var p in _data.ApiProfiles.ToList())
                            await SaveApiProfileAsync(p).ConfigureAwait(false);
                        await SaveAsync().ConfigureAwait(false);
                    }
                    catch { }
                }
            }
            finally
            {
                _loadSem.Release();
            }
        }

        private static async Task<AppData> TryLoadIndexAsync(StorageFolder folder, string name)
        {
            try
            {
                StorageFile f;
                try { f = await folder.GetFileAsync(name).AsTask().ConfigureAwait(false); }
                catch { return null; }
                using (var s = await f.OpenStreamForReadAsync().ConfigureAwait(false))
                {
                    if (s.Length == 0) return null;
                    var ser = new DataContractJsonSerializer(typeof(AppData));
                    return (AppData)ser.ReadObject(s);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataManager] 索引 {name} 读取失败: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>索引及其备份都坏了/不存在时，从 Novels\ 与 ApiProfiles\ 的单文件重建。</summary>
        private static async Task<AppData> RebuildFromFilesAsync()
        {
            var data = new AppData();
            try
            {
                var novelsFolder = await GetSubFolderAsync(NovelsFolder).ConfigureAwait(false);
                foreach (var f in await novelsFolder.GetFilesAsync().AsTask().ConfigureAwait(false))
                {
                    if (!f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        using (var s = await f.OpenStreamForReadAsync().ConfigureAwait(false))
                        {
                            if (s.Length == 0) continue;
                            var n = (Novel)new DataContractJsonSerializer(typeof(Novel)).ReadObject(s);
                            if (n != null && !string.IsNullOrEmpty(n.Id)) data.Novels.Add(n);
                        }
                    }
                    catch { }
                }
                data.Novels.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));

                var apiFolder = await GetSubFolderAsync(ApiFolder).ConfigureAwait(false);
                foreach (var f in await apiFolder.GetFilesAsync().AsTask().ConfigureAwait(false))
                {
                    if (!f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        using (var s = await f.OpenStreamForReadAsync().ConfigureAwait(false))
                        {
                            if (s.Length == 0) continue;
                            var p = (ApiProfile)new DataContractJsonSerializer(typeof(ApiProfile)).ReadObject(s);
                            if (p != null && !string.IsNullOrEmpty(p.Id)) data.ApiProfiles.Add(p);
                        }
                    }
                    catch { }
                }
                data.DefaultApiProfileId = AppSettings.DefaultApiProfileId ?? "";
                if (data.Novels.Count > 0 || data.ApiProfiles.Count > 0)
                    System.Diagnostics.Debug.WriteLine($"[DataManager] 索引已从文件重建: {data.Novels.Count} 本书, {data.ApiProfiles.Count} 个 Profile");
            }
            catch { }
            return data;
        }

        // ── 索引保存 ─────────────────────────────────────────────────
        public static async Task SaveAsync()
        {
            if (_data == null) return;
            await _fileWriteSem.WaitAsync().ConfigureAwait(false);
            try { await SaveIndexUnsafeAsync().ConfigureAwait(false); }
            finally { _fileWriteSem.Release(); }
        }

        /// <summary>写索引（调用方必须已持有 _fileWriteSem）。</summary>
        private static async Task SaveIndexUnsafeAsync()
        {
            if (_data == null) return;
            try
            {
                byte[] bytes;
                lock (_saveLock)
                {
                    var slim = new AppData
                    {
                        DefaultApiProfileId = Data.DefaultApiProfileId,
                        ApiProfiles = Data.ApiProfiles,
                        StylePresets = Data.StylePresets,
                        Genres = Data.Genres,
                        Novels = (Data.Novels ?? new List<Novel>()).Select(SlimCopy).ToList()
                    };
                    using (var ms = new MemoryStream())
                    {
                        new DataContractJsonSerializer(typeof(AppData)).WriteObject(ms, slim);
                        bytes = ms.ToArray();
                    }
                }

                var folder = ApplicationData.Current.LocalFolder;
                // 轮换备份：当前索引 → _index.bak
                try
                {
                    var existing = await folder.GetFileAsync(IndexFileName).AsTask().ConfigureAwait(false);
                    await existing.CopyAsync(folder, IndexBackupName, NameCollisionOption.ReplaceExisting).AsTask().ConfigureAwait(false);
                }
                catch { }
                await WriteBytesAtomicAsync(folder, IndexFileName, bytes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataManager] SaveIndex 失败: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>索引用轻量副本：去掉章节正文/润色稿/AI 报告/快照载荷（完整数据在单书文件里）。</summary>
        private static Novel SlimCopy(Novel n)
        {
            if (n == null) return null;
            return new Novel
            {
                Id = n.Id,
                Title = n.Title,
                Author = n.Author,
                Genre = n.Genre,
                SubGenre = n.SubGenre,
                StylePresetId = n.StylePresetId,
                ApiProfileId = n.ApiProfileId,
                UserPrompt = n.UserPrompt,
                TargetChapterCount = n.TargetChapterCount,
                TargetWordsPerChapter = n.TargetWordsPerChapter,
                WorldCharacterCount = n.WorldCharacterCount,
                WorldLocationCount = n.WorldLocationCount,
                WorldFactionCount = n.WorldFactionCount,
                WorldRuleCount = n.WorldRuleCount,
                TargetVolumeCount = n.TargetVolumeCount,
                State = n.State,
                CreatedAt = n.CreatedAt,
                UpdatedAt = n.UpdatedAt,
                LastExport = n.LastExport,
                Snapshots = new List<PipelineSnapshot>(),
                Chapters = (n.Chapters ?? new List<Chapter>()).Select(c => new Chapter
                {
                    Id = c.Id,
                    Order = c.Order,
                    VolumeId = c.VolumeId,
                    Status = c.Status,
                    WordCount = c.WordCount,
                    BridgeApplied = c.BridgeApplied,
                    DraftedAt = c.DraftedAt,
                    PolishedAt = c.PolishedAt,
                    DecidedAt = c.DecidedAt
                }).ToList()
            };
        }

        // ── 单本小说 ─────────────────────────────────────────────────
        public static async Task SaveNovelAsync(Novel novel)
        {
            if (novel == null) return;
            try
            {
                byte[] bytes;
                lock (_saveLock)
                {
                    if (Data.Novels == null) Data.Novels = new List<Novel>();
                    var idx = Data.Novels.FindIndex(n => n.Id == novel.Id);
                    if (idx < 0) Data.Novels.Add(novel);
                    else Data.Novels[idx] = novel;
                    novel.UpdatedAt = DateTime.Now;

                    using (var ms = new MemoryStream())
                    {
                        new DataContractJsonSerializer(typeof(Novel)).WriteObject(ms, novel);
                        bytes = ms.ToArray();
                    }
                }

                await _fileWriteSem.WaitAsync().ConfigureAwait(false);
                try
                {
                    var folder = await GetSubFolderAsync(NovelsFolder).ConfigureAwait(false);
                    await WriteBytesAtomicAsync(folder, novel.Id + ".json", bytes).ConfigureAwait(false);
                    await SaveIndexUnsafeAsync().ConfigureAwait(false);
                }
                finally
                {
                    _fileWriteSem.Release();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataManager] SaveNovelAsync 失败: {ex.GetType().Name}: {ex.Message}\n{ex}");
            }
        }

        public static async Task<Novel> LoadNovelAsync(string novelId)
        {
            try
            {
                var folder = await GetSubFolderAsync(NovelsFolder);
                var file = await folder.GetFileAsync(novelId + ".json").AsTask().ConfigureAwait(false);
                using (var s = await file.OpenStreamForReadAsync().ConfigureAwait(false))
                {
                    if (s.Length == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DataManager] LoadNovelAsync 失败: 文件为空 {novelId}.json");
                        return null;
                    }
                    var ser = new DataContractJsonSerializer(typeof(Novel));
                    var result = (Novel)ser.ReadObject(s);
                    if (result == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DataManager] LoadNovelAsync 失败: 反序列化结果为 null");
                    }
                    return result;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataManager] LoadNovelAsync 失败: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        public static async Task DeleteNovelAsync(string novelId)
        {
            try
            {
                lock (_saveLock)
                {
                    if (Data.Novels != null)
                        Data.Novels.RemoveAll(n => n.Id == novelId);
                }
                var folder = await GetSubFolderAsync(NovelsFolder);
                try
                {
                    var file = await folder.GetFileAsync(novelId + ".json").AsTask().ConfigureAwait(false);
                    await file.DeleteAsync().AsTask().ConfigureAwait(false);
                }
                catch { }
                await SaveAsync().ConfigureAwait(false);
            }
            catch { }
        }

        // ── API Profile 管理 ───────────────────────────────────────────
        public static List<ApiProfile> ApiProfiles => _data?.ApiProfiles ?? new List<ApiProfile>();

        public static async Task<ApiProfile> SaveApiProfileAsync(ApiProfile profile)
        {
            if (profile == null) return null;
            try
            {
                lock (_saveLock)
                {
                    if (Data.ApiProfiles == null) Data.ApiProfiles = new List<ApiProfile>();
                    var idx = Data.ApiProfiles.FindIndex(p => p.Id == profile.Id);
                    if (idx < 0) Data.ApiProfiles.Add(profile);
                    else Data.ApiProfiles[idx] = profile;
                }

                StoreKeyInVault(profile.Id, profile.ApiKey);

                await _fileWriteSem.WaitAsync().ConfigureAwait(false);
                try
                {
                    byte[] bytes;
                    using (var ms = new MemoryStream())
                    {
                        // ApiKey 无 [DataMember]，不会写入文件
                        new DataContractJsonSerializer(typeof(ApiProfile)).WriteObject(ms, profile);
                        bytes = ms.ToArray();
                    }
                    var folder = await GetSubFolderAsync(ApiFolder).ConfigureAwait(false);
                    await WriteBytesAtomicAsync(folder, profile.Id + ".json", bytes).ConfigureAwait(false);
                    await SaveIndexUnsafeAsync().ConfigureAwait(false);
                }
                finally
                {
                    _fileWriteSem.Release();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataManager] SaveApiProfileAsync 失败: {ex.GetType().Name}: {ex.Message}");
            }
            return profile;
        }

        public static async Task DeleteApiProfileAsync(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return;
            try
            {
                lock (_saveLock)
                {
                    if (Data.ApiProfiles != null)
                        Data.ApiProfiles.RemoveAll(p => p.Id == profileId);
                }
                RemoveKeyFromVault(profileId);
                var folder = await GetSubFolderAsync(ApiFolder);
                try
                {
                    var file = await folder.GetFileAsync(profileId + ".json").AsTask().ConfigureAwait(false);
                    await file.DeleteAsync().AsTask().ConfigureAwait(false);
                }
                catch { }
                await SaveAsync().ConfigureAwait(false);
            }
            catch { }
        }

        public static ApiProfile FindApiProfile(string id)
        {
            if (string.IsNullOrEmpty(id) || _data?.ApiProfiles == null) return null;
            return _data.ApiProfiles.FirstOrDefault(p => p.Id == id);
        }

        // ── PasswordVault ────────────────────────────────────────────
        /// <summary>载入后填充内存密钥；发现旧版明文密钥则迁入 Vault。返回是否发生迁移。</summary>
        private static bool HydrateApiKeys(List<ApiProfile> profiles)
        {
            if (profiles == null) return false;
            bool migrated = false;
            try
            {
                var vault = new PasswordVault();
                foreach (var p in profiles)
                {
                    if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                    if (!string.IsNullOrEmpty(p.LegacyApiKey))
                    {
                        p.ApiKey = p.LegacyApiKey;
                        p.LegacyApiKey = null;
                        StoreKeyInVault(p.Id, p.ApiKey);
                        migrated = true;
                    }
                    else if (string.IsNullOrEmpty(p.ApiKey))
                    {
                        try
                        {
                            var cred = vault.Retrieve(VaultResource, p.Id);
                            cred.RetrievePassword();
                            p.ApiKey = cred.Password;
                        }
                        catch (Exception ex)
                        {
                            // 该 profile 在保险库里没有密钥（常见：从未保存成功或已删除）。
                            // 这是预期情况，不致命；记录以便排查「URL 为空 / 401」类问题来源。
                            System.Diagnostics.Debug.WriteLine($"[DataManager] 保险库无密钥 profile={p.Id}: {ex.Message}");
                        }
                    }
                }
            }
            catch { } // PasswordVault 不可用时维持现状（密钥仅在内存）
            return migrated;
        }

        private static void StoreKeyInVault(string profileId, string key)
        {
            try
            {
                var vault = new PasswordVault();
                try
                {
                    var old = vault.Retrieve(VaultResource, profileId);
                    vault.Remove(old);
                }
                catch { }
                if (!string.IsNullOrEmpty(key))
                    vault.Add(new PasswordCredential(VaultResource, profileId, key));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataManager] PasswordVault 写入失败: {ex.Message}");
            }
        }

        private static void RemoveKeyFromVault(string profileId)
        {
            try
            {
                var vault = new PasswordVault();
                var old = vault.Retrieve(VaultResource, profileId);
                vault.Remove(old);
            }
            catch { }
        }

        // ── Helpers ──────────────────────────────────────────────────
        /// <summary>原子写入：先写 .tmp 再替换目标文件，崩溃时不会留下截断的目标文件。</summary>
        private static async Task WriteBytesAtomicAsync(StorageFolder folder, string fileName, byte[] bytes)
        {
            var temp = await folder.CreateFileAsync(fileName + ".tmp", CreationCollisionOption.ReplaceExisting).AsTask().ConfigureAwait(false);
            await FileIO.WriteBytesAsync(temp, bytes).AsTask().ConfigureAwait(false);
            StorageFile target = null;
            try { target = await folder.GetFileAsync(fileName).AsTask().ConfigureAwait(false); } catch { }
            if (target != null)
                await temp.MoveAndReplaceAsync(target).AsTask().ConfigureAwait(false);
            else
                await temp.RenameAsync(fileName, NameCollisionOption.ReplaceExisting).AsTask().ConfigureAwait(false);
        }

        private static async Task EnsureSubFolderAsync(IStorageFolder parent, string name)
        {
            await parent.CreateFolderAsync(name, CreationCollisionOption.OpenIfExists).AsTask();
        }

        private static async Task<StorageFolder> GetSubFolderAsync(string name)
        {
            return await ApplicationData.Current.LocalFolder.CreateFolderAsync(name, CreationCollisionOption.OpenIfExists).AsTask().ConfigureAwait(false);
        }
    }
}

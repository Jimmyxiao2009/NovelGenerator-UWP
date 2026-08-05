using System;
using System.Threading.Tasks;
using novelgen.Pipeline;

namespace novelgen.AI.RAG
{
    /// <summary>本地嵌入模型占位。生产实现应加载 bge.embmodel（仿 Lumina.OnEmbedder）。
    /// 当前为占位实现，未做语义嵌入；目前无任何调用方（WorldbookRetriever 走关键词召回）。</summary>
    public class Embedder
    {
        public async Task<float[]> EmbedAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return new float[0];
            await Task.Yield();
            // TODO: 实际加载 bge.embmodel 并推理。
            // 现阶段用伪向量：长度 64、值由【稳定】hash 决定。
            // 不能用 string.GetHashCode()——UWP 下其值按进程随机化，会让向量每次运行都变，
            // 任何基于余弦的排序都将是噪声。改用 FNV-1a（UTF-8 字节）保证跨进程/跨运行确定性。
            var vec = new float[64];
            int seed = StableHash(text);
            var rng = new Random(seed);
            for (int i = 0; i < vec.Length; i++) vec[i] = (float)(rng.NextDouble() * 2 - 1);
            return vec;
        }

        /// <summary>FNV-1a 32 位哈希（对 UTF-8 字节），跨进程/跨运行确定性。</summary>
        private static int StableHash(string text)
        {
            unchecked
            {
                const uint offset = 2166136261;
                const uint prime = 16777619;
                uint hash = offset;
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                foreach (var b in bytes)
                {
                    hash ^= b;
                    hash *= prime;
                }
                return (int)hash;
            }
        }
    }
}

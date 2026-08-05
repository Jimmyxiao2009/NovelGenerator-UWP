using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml.Controls;

namespace novelgen.Common
{
    public static class SuspensionManager
    {
        private static Dictionary<string, Frame> _registeredFrames = new Dictionary<string, Frame>();
        private const string SessionStateFile = "_sessionState.json";

        public static Dictionary<string, object> SessionState { get; private set; } = new Dictionary<string, object>();

        public static void RegisterFrame(Frame frame, string key)
        {
            if (!_registeredFrames.ContainsKey(key))
            {
                _registeredFrames[key] = frame;
                frame.Navigated += (s, e) => SaveFrameState(key, frame);
            }
        }

        public static async Task SaveAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.CreateFileAsync(SessionStateFile, CreationCollisionOption.ReplaceExisting);
                using (var stream = await file.OpenStreamForWriteAsync())
                {
                    var ser = new DataContractJsonSerializer(typeof(Dictionary<string, object>));
                    ser.WriteObject(stream, SessionState);
                }
            }
            catch { }
        }

        public static async Task RestoreAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.GetFileAsync(SessionStateFile);
                using (var stream = await file.OpenStreamForReadAsync())
                {
                    var ser = new DataContractJsonSerializer(typeof(Dictionary<string, object>));
                    SessionState = (Dictionary<string, object>)ser.ReadObject(stream) ?? new Dictionary<string, object>();
                }
            }
            catch { SessionState = new Dictionary<string, object>(); }

            // 把恢复出来的导航状态重新套回已注册的 Frame，真正还原上次的导航栈
            foreach (var kv in _registeredFrames)
            {
                if (SessionState.TryGetValue(kv.Key, out var s) && s is string str && !string.IsNullOrEmpty(str))
                {
                    try { kv.Value.SetNavigationState(str); }
                    catch { /* 状态不兼容（页面类型变更等）时忽略，退回默认首页 */ }
                }
            }
        }

        private static void SaveFrameState(string key, Frame frame)
        {
            // 存完整导航状态（页面类型 + 基元参数），供下次冷启动 SetNavigationState 还原
            try { SessionState[key] = frame.GetNavigationState(); }
            catch { /* 含不可序列化参数时跳过，避免抛出影响保存 */ }
        }
    }
}

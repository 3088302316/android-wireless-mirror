using System;

internal sealed class StreamPreset
{
    internal string Name;
    internal string Description;
    internal int Size;
    internal int Fps;
    internal int Bitrate;
    internal int VideoBuffer;
    internal int AudioBuffer;

    internal bool Matches(ConnectionProfile profile)
    {
        return profile.Size == Size && profile.Fps == Fps && profile.Bitrate == Bitrate &&
            profile.VideoBuffer == VideoBuffer && profile.AudioBuffer == AudioBuffer;
    }

    internal void Apply(ConnectionProfile profile)
    {
        profile.Size = Size;
        profile.Fps = Fps;
        profile.Bitrate = Bitrate;
        profile.VideoBuffer = VideoBuffer;
        profile.AudioBuffer = AudioBuffer;
    }
}

internal static class StreamPresets
{
    internal static readonly StreamPreset Balanced = new StreamPreset {
        Name = "均衡", Description = "日常阅读、聊天和滚动列表，兼顾文字清晰度与操作响应",
        Size = 1600, Fps = 60, Bitrate = 80, VideoBuffer = 0, AudioBuffer = 80 };
    internal static readonly StreamPreset Game = new StreamPreset {
        Name = "游戏低延迟", Description = "适合高刷手机和稳定 Wi-Fi，优先跟手并缩短声音缓冲",
        Size = 1280, Fps = 90, Bitrate = 75, VideoBuffer = 0, AudioBuffer = 60 };

    internal static readonly StreamPreset[] All =
    {
        Balanced, Game,
        new StreamPreset { Name = "追剧稳定", Description = "字幕和运动画面优先，给 Wi-Fi 短时抖动留出缓冲",
            Size = 1920, Fps = 60, Bitrate = 100, VideoBuffer = 80, AudioBuffer = 120 },
        new StreamPreset { Name = "弱网省流", Description = "共享热点或带宽紧张时，降低传输量并保留操作响应",
            Size = 1024, Fps = 30, Bitrate = 22, VideoBuffer = 60, AudioBuffer = 120 }
    };
}

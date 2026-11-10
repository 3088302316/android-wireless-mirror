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
    internal static readonly StreamPreset[] All =
    {
        new StreamPreset { Name = "均衡", Description = "清晰度和延迟平衡，适合日常使用",
            Size = 1600, Fps = 60, Bitrate = 60, VideoBuffer = 40, AudioBuffer = 50 },
        new StreamPreset { Name = "游戏低延迟", Description = "0ms 视频缓冲、90fps，优先操作响应",
            Size = 1280, Fps = 90, Bitrate = 100, VideoBuffer = 0, AudioBuffer = 40 },
        new StreamPreset { Name = "追剧稳定", Description = "较高缓冲，网络短时抖动时更耐看",
            Size = 1920, Fps = 45, Bitrate = 80, VideoBuffer = 100, AudioBuffer = 100 },
        new StreamPreset { Name = "弱网省流", Description = "降低帧率、码率和分辨率，优先不断流",
            Size = 1280, Fps = 30, Bitrate = 35, VideoBuffer = 160, AudioBuffer = 150 }
    };
}

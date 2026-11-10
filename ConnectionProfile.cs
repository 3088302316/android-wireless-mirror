using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

internal enum AudioRoute
{
    Pc = 0,
    Phone = 1,
    Both = 2,
    None = 3
}

[DataContract]
internal sealed class ConnectionProfile
{
    [DataMember] public int Size = 1600;
    [DataMember] public int Fps = 60;
    [DataMember] public int Bitrate = 60;
    [DataMember] public int VideoBuffer = 0;
    [DataMember] public int AudioBuffer = 50;
    [DataMember] public bool AutoReconnect = true;
    [DataMember] public AudioRoute Audio = AudioRoute.Pc;
    [DataMember(EmitDefaultValue = false)] public bool PhoneAudio;

    private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(ConnectionProfile));

    public string Options
    {
        get
        {
            string options = String.Format(CultureInfo.InvariantCulture,
                "--max-size={0} --max-fps={1} --video-bit-rate={2} --video-buffer={3} --audio-buffer={4}",
                Size, Fps, Bitrate * 100000, VideoBuffer, AudioBuffer);
            switch (Audio)
            {
                case AudioRoute.Phone:
                    options += " --no-audio";
                    break;
                case AudioRoute.Both:
                    options += " --audio-source=playback --audio-dup";
                    break;
                case AudioRoute.None:
                    options += " --audio-source=playback --no-audio-playback";
                    break;
                default:
                    options += " --audio-source=playback";
                    break;
            }
            // Always use absolute SDK mouse input: the cursor remains a normal
            // desktop cursor and can leave the mirror window.
            options += " --mouse=sdk";
            return options;
        }
    }

    internal ConnectionProfile Copy() { return (ConnectionProfile)MemberwiseClone(); }

    public static ConnectionProfile Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                using (var stream = File.OpenRead(AppPaths.SettingsFile))
                {
                    var saved = (ConnectionProfile)Serializer.ReadObject(stream);
                    saved.Validate();
                    if (saved.PhoneAudio) saved.Audio = AudioRoute.Phone;
                    saved.PhoneAudio = false;
                    return saved;
                }
            }

            var profile = new ConnectionProfile();
            string oldPath = Path.Combine(AppPaths.DataDirectory, "connection-profile.txt");
            if (!File.Exists(oldPath)) return profile;
            switch (File.ReadAllText(oldPath).Trim())
            {
                case "low-latency": profile.Size = 1280; profile.Bitrate = 40; profile.AudioBuffer = 40; break;
                case "weak-wifi": profile.Size = 1280; profile.Fps = 30; profile.Bitrate = 30; profile.VideoBuffer = 80; profile.AudioBuffer = 120; break;
                case "sharp": profile.Size = 2160; profile.Bitrate = 100; break;
            }
            return profile;
        }
        catch (Exception error)
        {
            AppLog.Error("读取连接设置失败，使用默认值", error);
            return new ConnectionProfile();
        }
    }

    public void Save()
    {
        Validate();
        AppPaths.EnsureDataDirectory();
        string pending = AppPaths.SettingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                Serializer.WriteObject(stream, this);
            if (File.Exists(AppPaths.SettingsFile))
            {
                try { File.Replace(pending, AppPaths.SettingsFile, null); }
                catch (IOException)
                {
                    File.Copy(pending, AppPaths.SettingsFile, true);
                    File.Delete(pending);
                }
            }
            else File.Move(pending, AppPaths.SettingsFile);
        }
        catch (Exception error)
        {
            AppLog.Error("保存连接设置失败", error);
            throw;
        }
        finally
        {
            if (File.Exists(pending))
            {
                try { File.Delete(pending); }
                catch (Exception) { }
            }
        }
    }

    internal static string AudioLabel(AudioRoute route)
    {
        switch (route)
        {
            case AudioRoute.Phone: return "手机";
            case AudioRoute.Both: return "两者";
            case AudioRoute.None: return "静音";
            default: return "电脑";
        }
    }

    private void Validate()
    {
        if (Size < 480 || Size > 3840 || Fps < 15 || Fps > 120 || Bitrate < 5 || Bitrate > 300 ||
            VideoBuffer < 0 || VideoBuffer > 250 || AudioBuffer < 20 || AudioBuffer > 250)
            throw new InvalidDataException("连接设置有超出范围的数值。请检查 connection-settings.json。");
        if (Audio < AudioRoute.Pc || Audio > AudioRoute.None) Audio = AudioRoute.Pc;
    }
}

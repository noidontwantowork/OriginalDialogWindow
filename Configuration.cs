using System;
using Dalamud.Configuration;

namespace OriginalDialogWindow;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; }

    public bool ShowNativeWindow { get; set; } = true;

    public bool HasNativeWindowPosition { get; set; }

    public float NativeWindowX { get; set; }

    public float NativeWindowY { get; set; }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

using System.Collections;
using System.Reflection;
using KamiToolKit;

namespace OriginalDialogWindow.Addons;

/// <summary>
/// 绕过 KamiToolKit 的静态 addon 注册表，把已经无法安全销毁的 addon 对象摘出去。
/// </summary>
/// <remarks>
/// <see cref="NativeAddon.Dispose"/> 会调用 <c>Close()</c>，而 <c>Close()</c> 假设内部的 addon 指针仍然有效。
/// 经过返回主菜单这类会强制重建 UI 的场景后，引擎已经销毁了 addon，但 KamiToolKit 的字段还没清空，
/// 这时候再走 <c>Dispose()</c> 就是在操作悬空指针。此工具只做“从注册表移除”，不讲对象销毁。
/// </remarks>
internal static class NativeAddonRegistry
{
    private static readonly FieldInfo? CreatedAddonsField = typeof(NativeAddon).GetField(
        "CreatedAddons",
        BindingFlags.NonPublic | BindingFlags.Static);

    /// <summary>
    /// 把 addon 从 KamiToolKit 的静态注册表里移除，之后 <c>DisposeAddons()</c> 不会再碰它。
    /// </summary>
    public static void Unregister(NativeAddon addon)
    {
        if (CreatedAddonsField?.GetValue(null) is IList addons)
            addons.Remove(addon);
    }
}

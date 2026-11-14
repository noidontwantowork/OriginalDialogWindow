using System;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace OriginalDialogWindow.Addons;

/// <summary>
/// 接管原生窗口的关闭与隐藏入口，让指定窗口只能由插件自己控制。
/// </summary>
/// <remarks>
/// KamiToolKit 创建的 addon 走的是 AtkUnitBase 的虚函数表：游戏侧任何关闭
/// （关闭按钮、Esc、CloseAll、界面隐藏）最终都会落到 <c>Close</c> 或 <c>Hide</c>。
/// 这里对引擎静态虚函数表里的这两个入口挂钩，只放行本插件主动发起的关闭/隐藏。
/// </remarks>
internal sealed unsafe class AddonCloseGuard : IDisposable
{
    private readonly string addonName;
    private readonly Hook<AtkUnitBase.Delegates.Close> closeHook;
    private readonly Hook<AtkUnitBase.Delegates.Hide> hideHook;

    public AddonCloseGuard(string addonName, IGameInteropProvider interopProvider)
    {
        this.addonName = addonName;

        var virtualTable = AtkUnitBase.StaticVirtualTablePointer;
        this.closeHook = interopProvider.HookFromAddress<AtkUnitBase.Delegates.Close>((nint)virtualTable->Close, this.OnClose);
        this.hideHook = interopProvider.HookFromAddress<AtkUnitBase.Delegates.Hide>((nint)virtualTable->Hide, this.OnHide);

        this.closeHook.Enable();
        this.hideHook.Enable();
    }

    /// <summary>
    /// 允许窗口被销毁，只在插件主动关闭窗口时置为 <see langword="true"/>。
    /// </summary>
    public bool AllowClose { get; set; }

    /// <summary>
    /// 允许窗口被隐藏但保留实例，插件自己切换显示状态时置为 <see langword="true"/>。
    /// </summary>
    public bool AllowHide { get; set; }

    private bool IsTarget(AtkUnitBase* addon) => addon is not null && addon->NameString.ToString() == this.addonName;

    private bool OnClose(AtkUnitBase* addon, bool fireCallback)
    {
        if (!this.AllowClose && this.IsTarget(addon))
            return false;

        return this.closeHook.Original(addon, fireCallback);
    }

    private void OnHide(AtkUnitBase* addon, bool disableHideTransition, bool callCloseCallback, uint setShowHideFlags)
    {
        if (!this.AllowHide && !this.AllowClose && this.IsTarget(addon))
            return;

        this.hideHook.Original(addon, disableHideTransition, callCloseCallback, setShowHideFlags);
    }

    public void Dispose()
    {
        this.closeHook.Dispose();
        this.hideHook.Dispose();
    }
}

using System;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;
using Lumina.Text.ReadOnly;

namespace OriginalDialogWindow.Addons;

/// <summary>
/// 一个只有窗口外壳、没有任何功能的 FFXIV 原生窗口。
/// </summary>
/// <remarks>
/// 窗口实例从首次显示一直活到插件卸载，主界面的开关只切换显示状态：
/// 隐藏走 <see cref="AtkUnitBase.Hide"/> 且由 <see cref="AddonCloseGuard"/> 拦住随后的销毁，
/// 避免重复 <c>Open()</c> 撞上 KamiToolKit 的"已存在"分支而再也开不出来。
/// </remarks>
public sealed unsafe class NoticeWindow : NativeAddon
{
    public const string AddonInternalName = "ODWNoticeWindow";

    /// <summary>
    /// 拖动结束后等待多久再把位置写进配置，避免拖拽过程中每帧落盘。
    /// </summary>
    private const double SavePositionDelayMilliseconds = 500d;

    private readonly Configuration configuration;
    private readonly AddonCloseGuard closeGuard;

    private AtkUnitBase* addonPtr;
    private ExpandedWindowNode? windowNode;
    private Vector2 lastKnownPosition;
    private DateTime pendingSaveAtUtc;
    private bool savePending;
    private bool wantVisible;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal NoticeWindow(Configuration configuration, AddonCloseGuard closeGuard)
    {
        this.configuration = configuration;
        this.closeGuard = closeGuard;

        this.InternalName = AddonInternalName;
        this.Title = string.Empty;
        this.Subtitle = string.Empty;
        this.Size = new Vector2(180f, 100f);
        this.OpenInBounds = false;
        this.RememberClosePosition = false;
        this.EnableContextMenu = false;
        this.CreateWindowNode = () => this.windowNode = new ExpandedWindowNode
        {
            ShowCloseButton = true,
            ShowConfigButton = false,
            ShowHelpButton = false,
        };
    }

    /// <summary>
    /// addon 是否仍由本插件持有且有效。
    /// </summary>
    /// <remarks>
    /// 返回主菜单、跨区这类场景会让引擎重建整个 UI，addon 被销毁后 <see cref="OnFinalize"/> 会把指针清掉。
    /// 这个属性就是判断“能不能继续往这个 addon 上写东西”的入口。
    /// </remarks>
    public bool IsOperational => this.addonPtr is not null;

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        this.addonPtr = addon;

        // 界面隐藏模式（Del）下保持可见。
        addon->IgnoreUIDisplayMode = true;

        // 失焦时不响应 Esc 关闭。
        addon->DisableUnfocusedCloseOnEsc = true;

        // 过场加载时不因 LoadScreen 被关闭。
        addon->DisableCloseOnLoadScreen = true;

        if (this.configuration.HasNativeWindowPosition)
            this.SetWindowPosition(new Vector2(this.configuration.NativeWindowX, this.configuration.NativeWindowY));

        // 窗口节点在 Setup 之前已经创建并同步过尺寸，这里再确保一次拖动区已经外扩。
        this.windowNode?.ApplyDragArea();

        this.lastKnownPosition = new Vector2(addon->X, addon->Y);
    }

    protected override void OnShow(AtkUnitBase* addon)
    {
        this.wantVisible = true;
    }

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        addon->IgnoreUIDisplayMode = true;

        // 兜底：任何绕过 Close/Hide 虚表把窗口藏起来的路径，都在下一帧恢复显示。
        if (this.wantVisible && !addon->IsVisible)
            addon->IsVisible = true;

        // 引擎或布局重建偶尔会把拖动区缩回窗口尺寸，这里每帧校正（值没变则不写入）。
        this.windowNode?.ApplyDragArea();

        this.TrackPosition(addon);
    }

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        this.FlushPosition();

        // addon 已销毁，指针作废。
        this.addonPtr = null;

        // 真正关闭之后立刻收回放行，避免后续输入直接关掉重开的窗口。
        this.closeGuard.AllowClose = false;
    }

    /// <summary>
    /// 切换窗口显示状态。
    /// </summary>
    public void SetVisible(bool visible)
    {
        this.wantVisible = visible;

        if (!visible)
        {
            this.FlushPosition();

            if (this.addonPtr is null || !this.addonPtr->IsVisible)
                return;

            // 放行隐藏，但随后的 Close 仍被拦住，addon 实例因此得以保留。
            this.closeGuard.AllowHide = true;
            try
            {
                this.addonPtr->Hide(false, false, 0);
            }
            finally
            {
                this.closeGuard.AllowHide = false;
            }

            return;
        }

        if (this.addonPtr is null)
        {
            this.Open();
            return;
        }

        if (!this.addonPtr->IsVisible)
            this.addonPtr->Show(false, 0);
    }

    /// <summary>
    /// 把窗口移到当前游戏画面的正中央。
    /// </summary>
    public void MoveToScreenCenter()
    {
        if (this.addonPtr is null)
            return;

        var stage = AtkStage.Instance();
        if (stage is null)
            return;

        var screenSize = (Vector2)stage->ScreenSize;
        var target = (screenSize / 2f) - (this.Size / 2f);

        this.SetWindowPosition(target);
        this.lastKnownPosition = target;
        this.RecordPosition(target);
        this.FlushPosition();
    }

    /// <summary>
    /// 把待写入的位置立刻落盘。
    /// </summary>
    public void FlushPosition()
    {
        if (!this.savePending)
            return;

        this.savePending = false;
        this.configuration.Save();
    }

    public override void Dispose()
    {
        this.FlushPosition();

        this.closeGuard.AllowClose = true;
        base.Dispose();
        this.closeGuard.AllowClose = false;
        this.addonPtr = null;
    }

    /// <summary>
    /// 丢弃一个 addon 已被引擎销毁的实例，不触发任何引擎调用。
    /// </summary>
    /// <remarks>
    /// 这种实例的 <see cref="NativeAddon"/> 内部指针已经悬空，走 <see cref="Dispose"/> 会访问野指针，
    /// 所以只能把它从 KamiToolKit 的静态注册表里摘掉，让后续清理跳过它。
    /// </remarks>
    public void Discard()
    {
        this.FlushPosition();

        NativeAddonRegistry.Unregister(this);
        this.addonPtr = null;
    }

    private void TrackPosition(AtkUnitBase* addon)
    {
        var current = new Vector2(addon->X, addon->Y);
        if (current != this.lastKnownPosition)
        {
            this.lastKnownPosition = current;
            this.RecordPosition(current);
            return;
        }

        if (this.savePending && DateTime.UtcNow >= this.pendingSaveAtUtc)
            this.FlushPosition();
    }

    /// <summary>
    /// 位置先写进内存里的配置对象，落盘则等拖动停下后再做。
    /// </summary>
    private void RecordPosition(Vector2 position)
    {
        this.configuration.NativeWindowX = position.X;
        this.configuration.NativeWindowY = position.Y;
        this.configuration.HasNativeWindowPosition = true;

        this.pendingSaveAtUtc = DateTime.UtcNow.AddMilliseconds(SavePositionDelayMilliseconds);
        this.savePending = true;
    }
}

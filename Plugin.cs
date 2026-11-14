using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using KamiToolKit;
using OriginalDialogWindow.Addons;
using OriginalDialogWindow.Windows;

namespace OriginalDialogWindow;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/odw";

    /// <summary>
    /// addon 丢失后等待多久再重建，避开场景切换过程中的抖动。
    /// </summary>
    private const double RecreateDelayMilliseconds = 2000d;

    public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    private readonly ICommandManager commandManager;
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly WindowSystem windowSystem = new("OriginalDialogWindow");
    private readonly Configuration configuration;
    private readonly AddonCloseGuard closeGuard;
    private readonly MainWindow mainWindow;

    private NoticeWindow noticeWindow;
    private DateTime notOperationalSinceUtc;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IClientState clientState,
        IFramework framework,
        IGameInteropProvider interopProvider)
    {
        PluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.clientState = clientState;
        this.framework = framework;

        this.configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        KamiToolKitLibrary.Initialize(pluginInterface, string.Empty);

        // 关闭拦截钩子挂在引擎静态虚表上，与 addon 实例无关，插件生命周期内只需要一份。
        this.closeGuard = new AddonCloseGuard(NoticeWindow.AddonInternalName, interopProvider);
        this.noticeWindow = new NoticeWindow(this.configuration, this.closeGuard);
        this.mainWindow = new MainWindow(this.configuration, this);

        this.windowSystem.AddWindow(this.mainWindow);

        this.noticeWindow.SetVisible(this.configuration.ShowNativeWindow);

        pluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi += this.OpenMainWindow;
        this.framework.Update += this.OnFrameworkUpdate;

        commandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "打开 OriginalDialogWindow 主界面。",
        });
    }

    public NoticeWindow NoticeWindow => this.noticeWindow;

    public void Dispose()
    {
        this.commandManager.RemoveHandler(CommandName);

        PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= this.OpenMainWindow;
        this.framework.Update -= this.OnFrameworkUpdate;

        this.windowSystem.RemoveAllWindows();

        if (this.noticeWindow.IsOperational)
            this.noticeWindow.Dispose();
        else
            this.noticeWindow.Discard();

        this.closeGuard.Dispose();
        KamiToolKitLibrary.Dispose();
    }

    /// <summary>
    /// 切换原生窗口显示状态。addon 若已被引擎销毁，会先重建一份再显示。
    /// </summary>
    public void SetNoticeWindowVisible(bool visible)
    {
        this.configuration.ShowNativeWindow = visible;
        this.configuration.Save();

        if (!visible)
        {
            this.notOperationalSinceUtc = default;
            this.noticeWindow.SetVisible(false);
            return;
        }

        if (this.noticeWindow.IsOperational)
        {
            this.noticeWindow.SetVisible(true);
            return;
        }

        this.RecreateNoticeWindow();
    }

    private void OpenMainWindow() => this.mainWindow.IsOpen = true;

    private void OnCommand(string command, string args) => this.mainWindow.Toggle();

    /// <summary>
    /// 看住 addon 是否还活着。
    /// </summary>
    /// <remarks>
    /// 返回主菜单、选择角色、超域旅行这类场景会让引擎重建整个 UI 系统，先前的 addon 被直接销毁。
    /// 那时 <see cref="NoticeWindow.IsOperational"/> 变回 false，这里补一个新实例，让窗口自己回来。
    /// </remarks>
    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!this.configuration.ShowNativeWindow || this.noticeWindow.IsOperational)
        {
            this.notOperationalSinceUtc = default;
            return;
        }

        // 标题画面与选人界面还没有可用的 UI 系统，这时重建会失败。
        if (!this.clientState.IsLoggedIn)
        {
            this.notOperationalSinceUtc = default;
            return;
        }

        if (this.notOperationalSinceUtc == default)
        {
            this.notOperationalSinceUtc = DateTime.UtcNow;
            return;
        }

        if ((DateTime.UtcNow - this.notOperationalSinceUtc).TotalMilliseconds < RecreateDelayMilliseconds)
            return;

        this.RecreateNoticeWindow();
    }

    /// <summary>
    /// 丢弃当前实例并新建一个 <see cref="NoticeWindow"/>。
    /// </summary>
    /// <remarks>
    /// KamiToolKit 的 <c>Open()</c> 在内部指针非 null 时直接跳过分配，而 addon 被引擎强制销毁后该指针不会自动清空。
    /// 所以这里不复用旧对象：丢弃它、新建一个内部指针为 null 的实例，<c>Open()</c> 才会真正重新分配。
    /// </remarks>
    private void RecreateNoticeWindow()
    {
        this.notOperationalSinceUtc = default;

        this.noticeWindow.Discard();
        this.noticeWindow = new NoticeWindow(this.configuration, this.closeGuard);

        this.noticeWindow.SetVisible(true);
    }
}

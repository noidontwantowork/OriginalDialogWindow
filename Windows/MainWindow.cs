using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace OriginalDialogWindow.Windows;

public sealed class MainWindow : Window
{
    private readonly Configuration configuration;
    private readonly Plugin plugin;

    public MainWindow(Configuration configuration, Plugin plugin)
        : base("Original Dialog Window###OriginalDialogWindowMain")
    {
        this.configuration = configuration;
        this.plugin = plugin;

        this.Size = new Vector2(320f, 120f);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var showNativeWindow = this.configuration.ShowNativeWindow;
        if (ImGui.Checkbox("显示原生窗口", ref showNativeWindow))
            this.plugin.SetNoticeWindowVisible(showNativeWindow);

        ImGui.Separator();

        if (ImGui.Button("将原生窗口移到屏幕正中"))
            this.plugin.NoticeWindow.MoveToScreenCenter();
    }
}

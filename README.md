# Original Dialog Window

一个只显示自身、不带任何功能的 FFXIV 原生窗口(Dalamud 插件)。

- 插件名：Original Dialog Window
- 作者：Noidontwantowork
- 主界面命令：`/odw`

## 下载

编译好的插件包在 [Releases](https://github.com/noidontwantowork/OriginalDialogWindow/releases/latest)：`OriginalDialogWindow.zip`，解压到 Dalamud 的插件目录后即可加载。

## 用法

主界面里有两个控件：

1. `显示原生窗口` 开关：打开后显示原生窗口，关闭后隐藏。这是唯一能关掉该窗口的途径。
2. `将原生窗口移到屏幕正中` 按钮：把窗口移回当前游戏画面正中央。

原生窗口使用 KamiToolKit 的 `NativeAddon` / `WindowNode` 构建，与游戏原生对话框共用同一套主题、标题栏和关闭按钮外观。

## 行为约束

- 窗口不会响应游戏的隐藏窗口键(`Del`)：`AtkUnitBase.IgnoreUIDisplayMode` 在 `OnSetup` 与 `OnUpdate` 中持续置为 `true`。
- 窗口不会响应关闭窗口键(`Esc`)、关闭按钮、`CloseAll`、界面隐藏：`AddonCloseGuard` 挂接了引擎静态虚表 `AtkUnitBase.StaticVirtualTablePointer->Close` 与 `->Hide`，只对本插件的 addon(`ODWNoticeWindow`)拦截，其他 addon 原样放行。同时 `DisableUnfocusedCloseOnEsc`、`DisableCloseOnLoadScreen` 置为 `true` 作为第二层保险。
- 窗口可拖到游戏画面之外：`DisableClamping = true`(KamiToolKit 默认值)且 `OpenInBounds = false`，位置不会被钳制回屏幕内。
- 整个面板都能按住拖动，且拖动区比窗口四周各多出 25% 窗口长宽：见下节。

### 拖动区为什么比窗口大

FFXIV 原生窗口的"按住移动"由窗口的碰撞节点驱动(`AtkUnitBase.WindowCollisionNode`，KamiToolKit 里对应 `ComponentNode.CollisionNode` 与标题栏的 `WindowNode.HeaderCollisionNode`)。它们默认与窗口同尺寸，所以默认只有标题栏和面板本体能拖。

`ExpandedWindowNode` 继承 `WindowNode` 并在 `OnSizeChanged()` 里把这两个碰撞节点改成窗口的 1.5 倍尺寸、位置往左上偏移 0.25 个窗口长宽，于是窗口四周那圈透明区域同样可以按住拖动。`OnUpdate` 里每帧校正一次(值没变则不写入)，防止布局重建把尺寸缩回去。

### 开关为什么不销毁窗口

KamiToolKit 的 `NativeAddon.Open()` 在 `InternalAddon != null` 时直接跳过分配，而 addon 的 `Destructor` 只有在 deleting destructor 分支才会把该字段置空。如果开关走 `Close()` 销毁窗口，字段会变成悬空指针，之后再 `Open()` 就永远命中"已经打开"分支，窗口再也出不来。

所以开关只切换显示状态：关闭时放行一次 `Hide`（`AddonCloseGuard.AllowHide`），随后的 `Close` 仍被拦住，addon 实例保留；重新打开时直接 `Show`。

## 场景切换后窗口自己回来

返回主菜单、选择角色、超域旅行这类操作会让引擎把整个 UI 系统重建一遍，先前的 addon 被直接销毁，KamiToolKit 内部的 addon 指针却不会跟着清空——于是 `Open()` 会一直命中"已经打开"分支，无论开关还是居中按钮都救不回来。

处理方式：

- `NoticeWindow.IsOperational` 反映 addon 是否仍由插件持有（`OnSetup` 赋值、`OnFinalize` 清空）。
- `Plugin` 在 `Framework.Update` 里盯着它：一旦失效、且 `IClientState.IsLoggedIn`、且持续 2 秒仍未恢复，就丢弃旧实例并新建一个 `NoticeWindow`——新实例内部指针为 `null`，`Open()` 才会真正重新分配。
- 丢弃走 `NoticeWindow.Discard()`：只把对象从 KamiToolKit 的静态注册表里摘掉，绝不调用 `Dispose()`，因为那会对已销毁的 addon 调 `Close()`。
- 开关打开时若发现 addon 已失效，也会先重建再显示，不必重载插件。

`AddonCloseGuard` 也因此改成插件级单例（钩子挂在引擎静态虚表上，与 addon 实例无关），重建窗口时不会重复挂钩子。

## 位置记忆

- 窗口位置写入插件配置(`HasNativeWindowPosition` / `NativeWindowX` / `NativeWindowY`)。
- 拖动过程中每帧把新位置写进内存里的配置对象，停下约 500 ms 后落盘；开关关闭、插件卸载时立即落盘。
- 下次打开窗口（含重新加载插件）时在 `OnSetup` 里恢复该位置，配合 `DisableClamping`，即使位置在屏幕外也会原样恢复。

## 构建

```powershell
dotnet build -c Release
```

产物在 `bin/Release`，其中 `OriginalDialogWindow.dll`、`OriginalDialogWindow.json`、`KamiToolKit.dll`、`SixLabors.ImageSharp.dll` 与 `Assets`(KamiToolKit 贴图，仅色盘类节点使用)即为可加载的插件目录内容。

## 依赖

- `Dalamud.CN.NET.Sdk/15.0.0`(net10.0-windows，API 15)
- `KamiToolKit` 1.1.2

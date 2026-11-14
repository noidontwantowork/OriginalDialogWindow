using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace OriginalDialogWindow.Addons;

/// <summary>
/// 把窗口的拖动碰撞区扩展到整个窗口之外一圈的窗口节点。
/// </summary>
/// <remarks>
/// FFXIV 原生窗口的"按住移动"由 <see cref="AtkUnitBase.WindowCollisionNode"/> 驱动，
/// 也就是 <see cref="ComponentNode{T,TU}.CollisionNode"/> 这个 <see cref="AtkCollisionNode"/>。
/// 它默认与窗口同尺寸，因此只有标题栏和面板本体能拖。这里把它的尺寸放大到窗口的 1.5 倍、
/// 位置往左上挪出 0.25 个窗口长宽，于是窗口四周那圈"看不见"的区域同样可以按住拖动。
/// </remarks>
public sealed unsafe class ExpandedWindowNode : WindowNode
{
    /// <summary>
    /// 拖动区相对窗口每一条边向外扩张的比例。
    /// </summary>
    private const float ExpandRatio = 0.25f;

    /// <summary>
    /// 重新计算拖动区。尺寸或位置没变化时不会重复写入，可以每帧安全调用。
    /// </summary>
    public void ApplyDragArea()
    {
        var width = this.Width;
        var height = this.Height;
        if (width <= 0f || height <= 0f)
            return;

        var expandX = width * ExpandRatio;
        var expandY = height * ExpandRatio;

        var size = new Vector2(width + (expandX * 2f), height + (expandY * 2f));
        var position = new Vector2(-expandX, -expandY);

        ApplyTo(this.CollisionNode, size, position);

        if (this.HeaderCollisionNode is not null)
            ApplyTo(this.HeaderCollisionNode, size, position);

        // 组件的 UldManager 也记着窗口根节点尺寸，引擎做命中筛选时会参考它。
        var uldManager = &this.ComponentBase->UldManager;
        uldManager->RootNodeWidth = (ushort)size.X;
        uldManager->RootNodeHeight = (ushort)size.Y;
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();

        // 基类的实现会把 CollisionNode 重置回窗口尺寸，所以每次尺寸变化后都要再扩一次。
        this.ApplyDragArea();
    }

    private static void ApplyTo(KamiToolKit.Nodes.CollisionNode node, Vector2 size, Vector2 position)
    {
        if (node.Size != size)
            node.Size = size;

        if (node.Position != position)
            node.Position = position;
    }
}

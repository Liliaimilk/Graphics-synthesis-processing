using System.Drawing;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    /// <summary>
    /// 表示画布上的一条参考线。
    /// </summary>
    public class GuideLine
    {
        /// <summary>
        /// 是否为横向参考线。
        /// </summary>
        public bool IsHorizontal { get; set; }

        /// <summary>
        /// 参考线在画布中的位置。
        /// </summary>
        public float Position { get; set; }

        /// <summary>
        /// 参考线的显示颜色。
        /// </summary>
        public Color Color { get; set; }
    }

    /// <summary>
    /// 表示通道编辑区域对应的一组控件。
    /// </summary>
    public class ChannelControl
    {
        /// <summary>
        /// 通道容器面板。
        /// </summary>
        public Panel Panel { get; set; }

        /// <summary>
        /// 通道输入框。
        /// </summary>
        public TextBox TextBox { get; set; }

        /// <summary>
        /// 通道编号。
        /// </summary>
        public int ChannelNumber { get; set; }
    }
}

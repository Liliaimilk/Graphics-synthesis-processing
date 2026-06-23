using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RulerGridApp
{
    public class RulerControl : Panel
    {
        private Panel canvasPanel;
        private const int RulerSize = 30;
        private const int CornerSize = 30;

        public RulerControl()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
            this.Padding = new Padding(RulerSize, RulerSize, 0, 0);
        }

        public void SetCanvasPanel(Panel panel)
        {
            canvasPanel = panel;
            canvasPanel.Scroll += (s, e) => this.Invalidate();
            canvasPanel.Resize += (s, e) => UpdateScrollRange();
        }

        public void UpdateScrollRange()
        {
            if (canvasPanel != null && canvasPanel.Parent == this)
            {
                var autoScrollMinSize = new Size(
                    canvasPanel.Width + RulerSize,
                    canvasPanel.Height + RulerSize
                );
                this.AutoScrollMinSize = autoScrollMinSize;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            // 绘制角落区域
            g.FillRectangle(Brushes.LightGray, 0, 0, CornerSize, CornerSize);
            g.DrawRectangle(Pens.Gray, 0, 0, CornerSize, CornerSize);

            // 绘制顶部标尺
            DrawHorizontalRuler(g);

            // 绘制左侧标尺
            DrawVerticalRuler(g);

            // 绘制网格
            DrawGrid(g);
        }

        private void DrawHorizontalRuler(Graphics g)
        {
            int startX = this.AutoScrollPosition.X;
            int endX = startX + this.ClientSize.Width - RulerSize;

            // 背景
            using (var bgBrush = new SolidBrush(Color.FromArgb(240, 240, 240)))
            {
                g.FillRectangle(bgBrush, CornerSize, 0, this.ClientSize.Width - CornerSize, RulerSize);
            }

            // 刻度
            float zoom = GetViewZoom();
            int majorStep = (int)(50 * zoom);
            int minorStep = (int)(10 * zoom);

            for (int x = (startX / majorStep) * majorStep; x <= endX; x += minorStep)
            {
                int screenX = x - startX + CornerSize;
                if (screenX >= CornerSize && screenX <= this.ClientSize.Width)
                {
                    bool isMajor = (x % majorStep == 0);
                    int tickHeight = isMajor ? 15 : 8;
                    int y = isMajor ? 5 : 8;

                    g.DrawLine(Pens.Black, screenX, y, screenX, y + tickHeight);

                    if (isMajor)
                    {
                        string text = (x / zoom).ToString();
                        using (var font = new Font("Arial", 8))
                        {
                            var size = g.MeasureString(text, font);
                            g.DrawString(text, font, Brushes.Black,
                                screenX - size.Width / 2, y + tickHeight + 2);
                        }
                    }
                }
            }

            // 边框
            g.DrawLine(Pens.Gray, CornerSize, RulerSize, this.ClientSize.Width, RulerSize);
        }

        private void DrawVerticalRuler(Graphics g)
        {
            int startY = this.AutoScrollPosition.Y;
            int endY = startY + this.ClientSize.Height - RulerSize;

            // 背景
            using (var bgBrush = new SolidBrush(Color.FromArgb(240, 240, 240)))
            {
                g.FillRectangle(bgBrush, 0, CornerSize, RulerSize, this.ClientSize.Height - CornerSize);
            }

            // 刻度
            float zoom = GetViewZoom();
            int majorStep = (int)(50 * zoom);
            int minorStep = (int)(10 * zoom);

            for (int y = (startY / majorStep) * majorStep; y <= endY; y += minorStep)
            {
                int screenY = y - startY + CornerSize;
                if (screenY >= CornerSize && screenY <= this.ClientSize.Height)
                {
                    bool isMajor = (y % majorStep == 0);
                    int tickWidth = isMajor ? 15 : 8;
                    int x = isMajor ? 5 : 8;

                    g.DrawLine(Pens.Black, x, screenY, x + tickWidth, screenY);

                    if (isMajor)
                    {
                        string text = (y / zoom).ToString();
                        using (var font = new Font("Arial", 8))
                        {
                            var size = g.MeasureString(text, font);
                            g.DrawString(text, font, Brushes.Black,
                                x + tickWidth + 2, screenY - size.Height / 2);
                        }
                    }
                }
            }

            // 边框
            g.DrawLine(Pens.Gray, RulerSize, CornerSize, RulerSize, this.ClientSize.Height);
        }

        private void DrawGrid(Graphics g)
        {
            int startX = this.AutoScrollPosition.X;
            int startY = this.AutoScrollPosition.Y;
            int endX = startX + this.ClientSize.Width;
            int endY = startY + this.ClientSize.Height;

            float zoom = GetViewZoom();
            int gridSize = (int)(20 * zoom);

            if (gridSize < 2) gridSize = 2;

            using (Pen lightPen = new Pen(Color.FromArgb(220, 220, 220), 1))
            using (Pen darkPen = new Pen(Color.FromArgb(180, 180, 180), 1))
            {
                for (int x = (startX / gridSize) * gridSize; x <= endX; x += gridSize)
                {
                    int screenX = x - startX + RulerSize;
                    if (screenX >= RulerSize && screenX <= this.ClientSize.Width)
                    {
                        bool isMajor = ((x / zoom) % 100 == 0);
                        g.DrawLine(isMajor ? darkPen : lightPen,
                            screenX, RulerSize, screenX, this.ClientSize.Height);
                    }
                }

                for (int y = (startY / gridSize) * gridSize; y <= endY; y += gridSize) 
                {
                    int screenY = y - startY + RulerSize;
                    if (screenY >= RulerSize && screenY <= this.ClientSize.Height)
                    {
                        bool isMajor = ((y / zoom) % 100 == 0);
                        g.DrawLine(isMajor ? darkPen : lightPen,
                            RulerSize, screenY, this.ClientSize.Width, screenY);
                    }
                }
            }

            // 绘制中心十字线
            int centerX = this.ClientSize.Width / 2;
            int centerY = this.ClientSize.Height / 2;
            using (Pen centerPen = new Pen(Color.Red, 1) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(centerPen, centerX, RulerSize, centerX, this.ClientSize.Height);
                g.DrawLine(centerPen, RulerSize, centerY, this.ClientSize.Width, centerY);
            }
        }

        private float GetViewZoom()
        {
            // 这里可以根据实际缩放比例返回
            // 暂时返回1，实际应用中需要从主窗体获取
            return 1.0f;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollRange();
            this.Invalidate();
        }
    }
}
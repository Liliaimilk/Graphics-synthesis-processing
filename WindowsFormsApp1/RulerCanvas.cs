using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    public enum BackgroundStyle { Checkerboard, White }

    public class RulerCanvas : Control
    {
        private const int RULER_SIZE = 28;
        private const float MIN_ZOOM = 0.1f;
        private const float MAX_ZOOM = 10f;
        private const float ZOOM_STEP = 0.02f;

        private const int SCROLLBAR_SIZE = 16;

        // DPI 相关常量
        private const float DPI = 96f;
        // 像素转毫米的转换系数 (1 inch = 25.4 mm)
        private const float MM_TO_PX = DPI / 25.4f;

        private Bitmap _image = null;
        private Point _imageLocation = Point.Empty;
        private Point _imageOrigin = Point.Empty;
        private float _zoom = 1f;
        private Point _panStart = Point.Empty;
        private Point _panOffset = Point.Empty;
        private bool _isPanning = false;

        private readonly List<GuideLine> _guides = new List<GuideLine>();
        private GuideLine _draggingGuide = null;
        private bool _showRulers = true;
        private bool _showGuides = true;

        private Point _lastMousePos = Point.Empty;

        private Bitmap _checkerboard = null;

        private HScrollBar _hScroll;
        private VScrollBar _vScroll;
        private BackgroundStyle _bgStyle = BackgroundStyle.White;

        public event Action<float> ZoomChanged;

        public RulerCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Cursor = Cursors.Cross;
            AllowDrop = true;

            CreateCheckerboard();

            _guides.Add(new GuideLine { IsHorizontal = false, Position = 0.5f, Color = Color.FromArgb(100, 255, 0, 0) });
            _guides.Add(new GuideLine { IsHorizontal = true, Position = 0.5f, Color = Color.FromArgb(100, 255, 0, 0) });

            _hScroll = new HScrollBar { TabStop = false };
            _hScroll.Parent = null;
            this.Controls.Add(_hScroll);
            _hScroll.ValueChanged += HScroll_ValueChanged;

            _vScroll = new VScrollBar { TabStop = false };
            _vScroll.Parent = null;
            this.Controls.Add(_vScroll);
            _vScroll.ValueChanged += VScroll_ValueChanged;
        }

        public void SetBackgroundStyle(BackgroundStyle style)
        {
            _bgStyle = style;
            Invalidate();
        }

        public BackgroundStyle GetBackgroundStyle() => _bgStyle;

        private void HScroll_ValueChanged(object sender, EventArgs e)
        {
            if (_image == null) return;
            _panOffset.X = -_hScroll.Value;
            Invalidate();
        }

        private void VScroll_ValueChanged(object sender, EventArgs e)
        {
            if (_image == null) return;
            _panOffset.Y = -_vScroll.Value;
            Invalidate();
        }

        private void SyncScrollBarsFromOffset()
        {
            if (_hScroll.Visible)
            {
                int targetX = Math.Max(_hScroll.Minimum, Math.Min(_hScroll.Maximum, -_panOffset.X));
                if (_hScroll.Value != targetX)
                {
                    _hScroll.Value = targetX;
                }
            }
            if (_vScroll.Visible)
            {
                int targetY = Math.Max(_vScroll.Minimum, Math.Min(_vScroll.Maximum, -_panOffset.Y));
                if (_vScroll.Value != targetY)
                {
                    _vScroll.Value = targetY;
                }
            }
        }

        private void UpdateScrollBars()
        {
            if (_image == null)
            {
                _hScroll.Visible = false;
                _vScroll.Visible = false;
                return;
            }

            int viewportW = GetViewportSize().Width;
            int viewportH = GetViewportSize().Height;
            int virtualW = (int)(_image.Width * _zoom);
            int virtualH = (int)(_image.Height * _zoom);

            bool needH = virtualW > viewportW;
            bool needV = virtualH > viewportH;

            _hScroll.Visible = needH;
            _vScroll.Visible = needV;

            if (needH)
            {
                _hScroll.Location = new Point(RULER_SIZE, Height - SCROLLBAR_SIZE);
                _hScroll.Width = needV ? viewportW - SCROLLBAR_SIZE : viewportW;
                _hScroll.Minimum = 0;
                _hScroll.Maximum = Math.Max(0, virtualW - viewportW);
                _hScroll.LargeChange = Math.Max(1, viewportW);
                _hScroll.SmallChange = Math.Max(1, viewportW / 10);
            }

            if (needV)
            {
                _vScroll.Location = new Point(Width - SCROLLBAR_SIZE, RULER_SIZE);
                _vScroll.Height = needH ? viewportH - SCROLLBAR_SIZE : viewportH;
                _vScroll.Minimum = 0;
                _vScroll.Maximum = Math.Max(0, virtualH - viewportH);
                _vScroll.LargeChange = Math.Max(1, viewportH);
                _vScroll.SmallChange = Math.Max(1, viewportH / 10);
            }

            ClampPanOffset();
            SyncScrollFromOffset();
        }

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            base.OnDragEnter(drgevent);
            if (drgevent.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])drgevent.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && IsImageFile(files[0]))
                {
                    drgevent.Effect = DragDropEffects.Copy;
                    return;
                }
            }
            drgevent.Effect = DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            base.OnDragDrop(drgevent);
            if (drgevent.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])drgevent.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && IsImageFile(files[0]))
                {
                    try
                    {
                        var bmp = new Bitmap(files[0]);
                        LoadImage(bmp);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"无法加载图片: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".tif" || ext == ".tiff";
        }

        public void LoadImageFromFile(string filePath)
        {
            try
            {
                var bmp = new Bitmap(filePath);
                LoadImage(bmp);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法加载图片: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CreateCheckerboard()
        {
            _checkerboard = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(_checkerboard))
            {
                using (SolidBrush brush1 = new SolidBrush(Color.FromArgb(60, 60, 70)))
                using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(80, 80, 90)))
                {
                    g.FillRectangle(brush1, 0, 0, 8, 8);
                    g.FillRectangle(brush2, 8, 0, 8, 8);
                    g.FillRectangle(brush2, 0, 8, 8, 8);
                    g.FillRectangle(brush1, 8, 8, 8, 8);
                }
            }
        }

        public void LoadImage(Bitmap bitmap)
        {
            _image?.Dispose();
            _image = bitmap;
            _panOffset = Point.Empty;
            CenterImage();
            _imageOrigin = _imageLocation;
            UpdateScrollBars();
            Invalidate();
        }

        public void ClearImage()
        {
            _image?.Dispose();
            _image = null;
            Invalidate();
        }

        private void CenterImage()
        {
            if (_image == null) return;

            int canvasWidth = Width - RULER_SIZE;
            int canvasHeight = Height - RULER_SIZE;

            int imgW = (int)(_image.Width * _zoom);
            int imgH = (int)(_image.Height * _zoom);

            _imageLocation = new Point(
                RULER_SIZE + (canvasWidth - imgW) / 2,
                RULER_SIZE + (canvasHeight - imgH) / 2);
        }

        private Size GetViewportSize()
        {
            return new Size(
                Math.Max(0, Width - RULER_SIZE - SCROLLBAR_SIZE),
                Math.Max(0, Height - RULER_SIZE - SCROLLBAR_SIZE));
        }

        private void ClampPanOffset()
        {
            if (_image == null) return;

            Rectangle viewportBounds = GetViewportBounds();
            int scaledW = (int)(_image.Width * _zoom);
            int scaledH = (int)(_image.Height * _zoom);

            int minPanX = Math.Min(viewportBounds.Left - _imageLocation.X, viewportBounds.Right - _imageLocation.X - scaledW);
            int maxPanX = Math.Max(viewportBounds.Left - _imageLocation.X, viewportBounds.Right - _imageLocation.X - scaledW);
            int minPanY = Math.Min(viewportBounds.Top - _imageLocation.Y, viewportBounds.Bottom - _imageLocation.Y - scaledH);
            int maxPanY = Math.Max(viewportBounds.Top - _imageLocation.Y, viewportBounds.Bottom - _imageLocation.Y - scaledH);

            _panOffset.X = Math.Max(minPanX, Math.Min(maxPanX, _panOffset.X));
            _panOffset.Y = Math.Max(minPanY, Math.Min(maxPanY, _panOffset.Y));
        }

        private void SyncScrollFromOffset()
        {
            if (_image == null) return;

            int virtualW = (int)(_image.Width * _zoom);
            int virtualH = (int)(_image.Height * _zoom);
            int vpW = GetViewportSize().Width;
            int vpH = GetViewportSize().Height;
            int maxPanX = Math.Max(0, virtualW - vpW);
            int maxPanY = Math.Max(0, virtualH - vpH);

            // scrollbar 值 = panOffset + imageLocation (视口在虚拟画布中的位置)
            if (_hScroll.Visible)
            {
                _hScroll.Minimum = 0;
                _hScroll.Maximum = maxPanX;
                _hScroll.Value = Math.Max(0, Math.Min(maxPanX, _panOffset.X + _imageLocation.X));
            }
            if (_vScroll.Visible)
            {
                _vScroll.Minimum = 0;
                _vScroll.Maximum = maxPanY;
                _vScroll.Value = Math.Max(0, Math.Min(maxPanY, _panOffset.Y + _imageLocation.Y));
            }
        }

        private Rectangle GetViewportBounds()
        {
            return new Rectangle(
                RULER_SIZE,
                RULER_SIZE,
                Math.Max(0, Width - RULER_SIZE),
                Math.Max(0, Height - RULER_SIZE));
        }

        private Rectangle GetImageBounds()
        {
            if (_image == null)
            {
                return Rectangle.Empty;
            }

            int imgW = (int)(_image.Width * _zoom);
            int imgH = (int)(_image.Height * _zoom);
            return new Rectangle(_imageLocation.X + _panOffset.X, _imageLocation.Y + _panOffset.Y, imgW, imgH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;

            DrawBackground(g);
            DrawImage(g);

            if (_showRulers)
                DrawRulers(g);

            if (_showGuides)
                DrawGuides(g);

            DrawCrosshair(g);
        }

        private void DrawBackground(Graphics g)
        {
            g.Clear(Color.FromArgb(25, 25, 30));

            Rectangle viewportBounds = GetViewportBounds();
            if (viewportBounds.Width <= 0 || viewportBounds.Height <= 0)
            {
                return;
            }

            using (SolidBrush workspaceBrush = new SolidBrush(
                _bgStyle == BackgroundStyle.White
                    ? Color.FromArgb(30, 30, 35)
                    : Color.FromArgb(30, 30, 35)))
            using (Pen viewportBorderPen = new Pen(Color.FromArgb(45, 45, 50), 1))
            {
                g.FillRectangle(workspaceBrush, viewportBounds);
                g.DrawRectangle(viewportBorderPen, viewportBounds.X, viewportBounds.Y, viewportBounds.Width - 1, viewportBounds.Height - 1);
            }

            if (_image == null)
            {
                return;
            }

            Rectangle imageBounds = Rectangle.Intersect(GetImageBounds(), viewportBounds);
            if (imageBounds.Width <= 0 || imageBounds.Height <= 0)
            {
                return;
            }

            GraphicsState state = g.Save();
            g.SetClip(viewportBounds);

            if (_bgStyle == BackgroundStyle.Checkerboard)
            {
                using (TextureBrush brush = new TextureBrush(_checkerboard, WrapMode.Tile))
                {
                    brush.TranslateTransform(imageBounds.X, imageBounds.Y);
                    g.FillRectangle(brush, imageBounds);
                }
            }
            else
            {
                using (SolidBrush whiteBg = new SolidBrush(Color.White))
                {
                    g.FillRectangle(whiteBg, imageBounds);
                    
                }
            }

            g.Restore(state);
        }

        private void DrawImage(Graphics g)
        {
            if (_image == null)
            {
                return;
            }

            Rectangle viewportBounds = GetViewportBounds();
            Rectangle imageBounds = GetImageBounds();
            if (viewportBounds.Width <= 0 || viewportBounds.Height <= 0 || imageBounds.Width <= 0 || imageBounds.Height <= 0)
            {
                return;
            }

            GraphicsState state = g.Save();
            g.SetClip(viewportBounds);

            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            {
                Rectangle shadowBounds = new Rectangle(imageBounds.X + 6, imageBounds.Y + 6, imageBounds.Width, imageBounds.Height);
                g.FillRectangle(shadowBrush, shadowBounds);
            }

            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(_image, imageBounds);

            using (Pen imageBorderPen = new Pen(Color.FromArgb(210, 210, 215)))
            {
                g.DrawRectangle(imageBorderPen, imageBounds.X, imageBounds.Y, imageBounds.Width - 1, imageBounds.Height - 1);
            }

            g.Restore(state);
        }

        private void DrawRulers(Graphics g)
        {
            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(50, 50, 55)))
            using (SolidBrush cornerBrush = new SolidBrush(Color.FromArgb(55, 55, 60)))
            using (SolidBrush textBrush = new SolidBrush(Color.White))
            using (Pen linePen = new Pen(Color.White, 1f))
            using (Pen borderPen = new Pen(Color.FromArgb(24, 24, 28), 1f))
            using (Font tickFont = new Font("Consolas", 7.5F, FontStyle.Regular))
            {
                g.FillRectangle(bgBrush, RULER_SIZE, 0, Width - RULER_SIZE, RULER_SIZE);
                g.FillRectangle(bgBrush, 0, RULER_SIZE, RULER_SIZE, Height - RULER_SIZE);
                g.FillRectangle(cornerBrush, 0, 0, RULER_SIZE, RULER_SIZE);

                g.DrawLine(borderPen, 0, RULER_SIZE - 1, Width, RULER_SIZE - 1);
                g.DrawLine(borderPen, RULER_SIZE - 1, 0, RULER_SIZE - 1, RULER_SIZE);

                DrawXAxis(g, textBrush, linePen, tickFont);
                DrawYAxis(g, textBrush, linePen, tickFont);
            }
        }

        private void DrawXAxis(Graphics g, SolidBrush textBrush, Pen linePen, Font tickFont)
        {
            float pixelPerMm = MM_TO_PX * _zoom;
            int endMm = (int)Math.Ceiling((Width - RULER_SIZE) / pixelPerMm);

            // 自适应刻度间隔：保证主要刻度之间至少占 30px
            int interval = GetAdaptiveInterval(pixelPerMm);
            int subInterval = interval / 5;

            for (int mm = 0; mm <= endMm; mm++)
            {
                float x = RULER_SIZE + mm * pixelPerMm;
                if (x > Width) break;

                if (mm % interval == 0)
                {
                    int tickH = interval >= 50 ? 8 : interval >= 10 ? 12 : 16;
                    g.DrawLine(linePen, x, RULER_SIZE - tickH, x, RULER_SIZE - 1);
                    if (pixelPerMm * interval >= 40)
                    {
                        string label = interval >= 100 ? (mm / 100).ToString() : (mm / 10).ToString();
                        g.DrawString(label, tickFont, textBrush, new RectangleF(x + 2, 2, 32, 12));
                    }
                }
                else if (subInterval > 0 && mm % subInterval == 0)
                {
                    g.DrawLine(linePen, x, RULER_SIZE - 8, x, RULER_SIZE - 1);
                }
            }
        }

        private void DrawYAxis(Graphics g, SolidBrush textBrush, Pen linePen, Font tickFont)
        {
            float pixelPerMm = MM_TO_PX * _zoom;
            int endMm = (int)Math.Ceiling((Height - RULER_SIZE) / pixelPerMm);

            int interval = GetAdaptiveInterval(pixelPerMm);
            int subInterval = interval / 5;

            for (int mm = 0; mm <= endMm; mm++)
            {
                float y = RULER_SIZE + mm * pixelPerMm;
                if (y > Height) break;

                if (mm % interval == 0)
                {
                    int tickW = interval >= 50 ? 8 : interval >= 10 ? 12 : 16;
                    g.DrawLine(linePen, RULER_SIZE - tickW, y, RULER_SIZE - 1, y);
                    if (pixelPerMm * interval >= 40)
                    {
                        string label = interval >= 100 ? (mm / 100).ToString() : (mm / 10).ToString();
                        g.DrawString(label, tickFont, textBrush, new RectangleF(2, y - 7, RULER_SIZE - 4, 14));
                    }
                }
                else if (subInterval > 0 && mm % subInterval == 0)
                {
                    g.DrawLine(linePen, RULER_SIZE - 8, y, RULER_SIZE - 1, y);
                }
            }
        }

        private int GetAdaptiveInterval(float pixelPerMm)
        {
            // 动态选择刻度间隔，保证主要刻度间距 ≥ 30px
            // interval 单位为 mm
            if (pixelPerMm * 1 >= 30) return 1;
            if (pixelPerMm * 5 >= 30) return 5;
            if (pixelPerMm * 10 >= 30) return 10;
            if (pixelPerMm * 50 >= 30) return 50;
            return 100;
        }

        private void DrawGuides(Graphics g)
        {
            if (_image == null)
            {
                return;
            }

            int imgW = (int)(_image.Width * _zoom);
            int imgH = (int)(_image.Height * _zoom);

            using (Pen guidePen = new Pen(Color.FromArgb(200, 255, 0, 0), 1f))
            {
                guidePen.DashStyle = DashStyle.Dash;

                foreach (var guide in _guides)
                {
                    if (guide.IsHorizontal)
                    {
                        int y = _imageLocation.Y + _panOffset.Y + (int)(imgH * guide.Position);
                        if (y >= RULER_SIZE && y <= Height)
                        {
                            g.DrawLine(guidePen, RULER_SIZE, y, Width, y);
                        }
                    }
                    else
                    {
                        int x = _imageLocation.X + _panOffset.X + (int)(imgW * guide.Position);
                        if (x >= RULER_SIZE && x <= Width)
                        {
                            g.DrawLine(guidePen, x, RULER_SIZE, x, Height);
                        }
                    }
                }
            }
        }

        private void DrawCrosshair(Graphics g)
        {
            if (_lastMousePos.X < RULER_SIZE || _lastMousePos.Y < RULER_SIZE) return;

            using (Pen crossPen = new Pen(Color.FromArgb(100, 255, 255, 255), 1) { DashStyle = DashStyle.Dot })
            {
                g.DrawLine(crossPen, RULER_SIZE, _lastMousePos.Y, Width, _lastMousePos.Y);
                g.DrawLine(crossPen, _lastMousePos.X, RULER_SIZE, _lastMousePos.X, Height);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            if (_image == null) return;

            // 按住 Alt 键时为放大缩小
            if ((Control.ModifierKeys & Keys.Alt) != 0)
            {
                Point mousePos = e.Location;
                Point beforeZoom = ScreenToImage(mousePos);

                float newZoom = _zoom + (e.Delta > 0 ? ZOOM_STEP : -ZOOM_STEP);
                newZoom = Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, newZoom));

                if (Math.Abs(newZoom - _zoom) < 0.001f) return;

                _zoom = newZoom;

                CenterImage();

                Point afterZoom = ScreenToImage(mousePos);
                _panOffset.Offset(afterZoom.X - beforeZoom.X, afterZoom.Y - beforeZoom.Y);

                ClampPanOffset();
                UpdateScrollBars();
                ZoomChanged?.Invoke(_zoom);
                Invalidate();
            }
            else
            {
                // 否则滚动滚动条（平移）
                int scrollDelta = e.Delta / 120 * _vScroll.SmallChange;
                if (_vScroll.Visible)
                {
                    int newVal = Math.Max(_vScroll.Minimum, Math.Min(_vScroll.Maximum, _vScroll.Value - scrollDelta));
                    if (_vScroll.Value != newVal)
                    {
                        _vScroll.Value = newVal;
                    }
                }
                else if (_hScroll.Visible)
                {
                    int newVal = Math.Max(_hScroll.Minimum, Math.Min(_hScroll.Maximum, _hScroll.Value - scrollDelta));
                    if (_hScroll.Value != newVal)
                    {
                        _hScroll.Value = newVal;
                    }
                }
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                _draggingGuide = GetGuideAtPoint(e.Location);
                if (_draggingGuide != null) return;

                _isPanning = true;
                _panStart = e.Location;
                Cursor = Cursors.SizeAll;
            }
            else if (e.Button == MouseButtons.Right && e.Clicks == 2)
            {
                ResetView();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _lastMousePos = e.Location;

            if (_draggingGuide != null && _image != null)
            {
                int imgW = (int)(_image.Width * _zoom);
                int imgH = (int)(_image.Height * _zoom);
                int imgY = _imageLocation.Y + _panOffset.Y;
                int imgX = _imageLocation.X + _panOffset.X;

                if (_draggingGuide.IsHorizontal)
                {
                    int localY = e.Location.Y - imgY;
                    if (localY >= 0 && localY <= imgH)
                        _draggingGuide.Position = (float)localY / imgH;
                }
                else
                {
                    int localX = e.Location.X - imgX;
                    if (localX >= 0 && localX <= imgW)
                        _draggingGuide.Position = (float)localX / imgW;
                }
                Invalidate();
                return;
            }

            if (_isPanning)
            {
                _panOffset.Offset(e.Location.X - _panStart.X, e.Location.Y - _panStart.Y);
                _panStart = e.Location;
                ClampPanOffset();
                SyncScrollBarsFromOffset();
                Invalidate();
            }
            else
            {
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isPanning = false;
            _draggingGuide = null;
            Cursor = Cursors.Cross;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            CenterImage();
            UpdateScrollBars();
            Invalidate();
        }

        private Point ScreenToImage(Point screen)
        {
            return new Point(
                (int)((screen.X - _imageLocation.X - _panOffset.X) / _zoom),
                (int)((screen.Y - _imageLocation.Y - _panOffset.Y) / _zoom));
        }

        private GuideLine GetGuideAtPoint(Point screen)
        {
            if (_image == null) return null;

            int imgW = (int)(_image.Width * _zoom);
            int imgH = (int)(_image.Height * _zoom);
            int hitRadius = 5;

            foreach (var guide in _guides)
            {
                if (guide.IsHorizontal)
                {
                    int y = _imageLocation.Y + _panOffset.Y + (int)(imgH * guide.Position);
                    if (Math.Abs(screen.Y - y) <= hitRadius) return guide;
                }
                else
                {
                    int x = _imageLocation.X + _panOffset.X + (int)(imgW * guide.Position);
                    if (Math.Abs(screen.X - x) <= hitRadius) return guide;
                }
            }
            return null;
        }

        public void ResetView()
        {
            _zoom = 1f;
            _panOffset = Point.Empty;
            CenterImage();
            ZoomChanged?.Invoke(_zoom);
            Invalidate();
        }

        public void SetZoom(float zoom)
        {
            _zoom = Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, zoom));
            CenterImage();
            ZoomChanged?.Invoke(_zoom);
            Invalidate();
        }

        public float Zoom => _zoom;
    }
}

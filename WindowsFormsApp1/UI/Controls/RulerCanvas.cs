using BitMiracle.LibTiff.Classic;
using ImageMagick;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    public enum BackgroundStyle { Checkerboard, White }
    public enum CanvasTool { None, Move }

    public class RulerCanvas : Control
    {
        private sealed class CanvasImageItem
        {
            public CanvasImageItem(Bitmap image, PointF worldLocation)
            {
                Image = image;
                WorldLocation = worldLocation;
            }

            public Bitmap Image { get; }
            public PointF WorldLocation { get; set; }
            public bool IsSelected { get; set; }
        }

        private const int RULER_SIZE = 28;
        private const float MIN_ZOOM = 0.1f;
        private const float MAX_ZOOM = 10f;
        private const float ZOOM_STEP = 0.02f;
        private const int SCROLLBAR_SIZE = 16;
        private const float DPI = 300f;
        private const float MM_TO_PX = DPI / 25.4f;

        private readonly List<CanvasImageItem> _images = new List<CanvasImageItem>();
        private readonly List<GuideLine> _guides = new List<GuideLine>();

        private CanvasImageItem _selectedImage;
        private GuideLine _draggingGuide;
        private Bitmap _checkerboard;
        private readonly HScrollBar _hScroll;
        private readonly VScrollBar _vScroll;
        private readonly ContextMenuStrip _imageContextMenu;
        private readonly ContextMenuStrip _canvasContextMenu;
        private readonly ToolStripMenuItem _deleteImageMenuItem;
        private readonly ToolStripMenuItem _clearCanvasMenuItem;
        private readonly ToolStripMenuItem _resetViewMenuItem;
        private static bool _magickInitialized;

        private float _zoom = 1f;
        private PointF _panOffset = PointF.Empty;
        private Point _panStart = Point.Empty;
        private PointF _dragImageStartWorld = PointF.Empty;
        private bool _isPanning;
        private bool _isDraggingImage;
        private bool _showRulers = true;
        private bool _showGuides = true;
        private Point _lastMousePos = Point.Empty;
        private BackgroundStyle _bgStyle = BackgroundStyle.White;
        private CanvasTool _activeTool = CanvasTool.Move;

        public event Action<float> ZoomChanged;

        public RulerCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Cursor = Cursors.Default;
            AllowDrop = true;

            CreateCheckerboard();

            _guides.Add(new GuideLine { IsHorizontal = false, Position = 0.5f, Color = Color.FromArgb(100, 255, 0, 0) });
            _guides.Add(new GuideLine { IsHorizontal = true, Position = 0.5f, Color = Color.FromArgb(100, 255, 0, 0) });

            _hScroll = new HScrollBar { TabStop = false };
            Controls.Add(_hScroll);
            _hScroll.ValueChanged += HScroll_ValueChanged;

            _vScroll = new VScrollBar { TabStop = false };
            Controls.Add(_vScroll);
            _vScroll.ValueChanged += VScroll_ValueChanged;

            _deleteImageMenuItem = new ToolStripMenuItem("删除选中图片");
            _deleteImageMenuItem.Click += DeleteImageMenuItem_Click;

            _imageContextMenu = new ContextMenuStrip();
            _imageContextMenu.Items.Add(_deleteImageMenuItem);

            _clearCanvasMenuItem = new ToolStripMenuItem("清空画布");
            _clearCanvasMenuItem.Click += ClearCanvasMenuItem_Click;

            _resetViewMenuItem = new ToolStripMenuItem("重置视图");
            _resetViewMenuItem.Click += ResetViewMenuItem_Click;

            _canvasContextMenu = new ContextMenuStrip();
            _canvasContextMenu.Items.Add(_clearCanvasMenuItem);
            _canvasContextMenu.Items.Add(_resetViewMenuItem);
        }

        public void SetBackgroundStyle(BackgroundStyle style)
        {
            _bgStyle = style;
            Invalidate();
        }

        public BackgroundStyle GetBackgroundStyle() => _bgStyle;

        public CanvasTool ActiveTool
        {
            get => _activeTool;
            set
            {
                _activeTool = value;
                if (!_isDraggingImage && !_isPanning)
                {
                    UpdateIdleCursor(_lastMousePos);
                }
            }
        }

        public void LoadImage(Bitmap bitmap)
        {
            ClearScene();
            AddImage(bitmap);
        }

        public void AddImage(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                return;
            }

            PointF worldLocation = GetDefaultImageLocation(bitmap.Size);
            CanvasImageItem item = new CanvasImageItem(bitmap, worldLocation);
            _images.Add(item);
            SelectImage(item, true);

            if (_images.Count == 1)
            {
                FitSceneToViewport();
            }
            else
            {
                ClampSelectedImageToViewport(item);
            }

            UpdateScrollBars();
            Invalidate();
        }

        public void ClearScene()
        {
            foreach (CanvasImageItem item in _images)
            {
                item.Image.Dispose();
            }

            _images.Clear();
            _selectedImage = null;
            _zoom = 1f;
            _panOffset = PointF.Empty;
            _hScroll.Visible = false;
            _vScroll.Visible = false;
            ZoomChanged?.Invoke(_zoom);
            Invalidate();
        }

        public void ClearImage()
        {
            ClearScene();
        }

        public void ResetView()
        {
            FitSceneToViewport();
            UpdateScrollBars();
            ZoomChanged?.Invoke(_zoom);
            Invalidate();
        }

        public void SetZoom(float zoom)
        {
            if (_images.Count == 0)
            {
                _zoom = Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, zoom));
                ZoomChanged?.Invoke(_zoom);
                Invalidate();
                return;
            }

            Rectangle viewport = GetViewportBounds();
            PointF centerScreen = new PointF(viewport.Left + viewport.Width / 2f, viewport.Top + viewport.Height / 2f);
            PointF centerWorld = ScreenToWorld(centerScreen);

            _zoom = Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, zoom));
            _panOffset = new PointF(
                centerScreen.X - centerWorld.X * _zoom,
                centerScreen.Y - centerWorld.Y * _zoom);

            ClampPanOffset();
            UpdateScrollBars();
            ZoomChanged?.Invoke(_zoom);
            Invalidate();
        }

        public float Zoom => _zoom;

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            base.OnDragEnter(drgevent);
            if (drgevent.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])drgevent.Data.GetData(DataFormats.FileDrop);
                if (files.Any(IsImageFile))
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
            if (!drgevent.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            string[] files = (string[])drgevent.Data.GetData(DataFormats.FileDrop);
            string[] imageFiles = files.Where(IsImageFile).ToArray();
            if (imageFiles.Length == 0)
            {
                return;
            }

            List<Bitmap> loadedBitmaps = new List<Bitmap>();
            try
            {
                foreach (string file in imageFiles)
                {
                    loadedBitmaps.Add(LoadBitmapFromFile(file));
                }

                foreach (Bitmap bmp in loadedBitmaps)
                {
                    AddImage(bmp);
                }

                loadedBitmaps.Clear();
            }
            catch (Exception ex)
            {
                foreach (Bitmap bmp in loadedBitmaps)
                {
                    bmp.Dispose();
                }

                MessageBox.Show($"无法加载图片: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void LoadImageFromFile(string filePath)
        {
            try
            {
                AddImage(LoadBitmapFromFile(filePath));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法加载图片: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        // 水平加载多张图片
        public void LoadImagesHorizontally(IEnumerable<string> filePaths)
        {
            List<string> paths = (filePaths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList();

            if (paths.Count == 0)
            {
                ClearScene();
                return;
            }

            List<Bitmap> loadedBitmaps = new List<Bitmap>();
            try
            {
                foreach (string path in paths)
                {
                    loadedBitmaps.Add(LoadBitmapFromFile(path));
                }

                ClearScene();
                AddImagesHorizontally(loadedBitmaps);
                loadedBitmaps.Clear();
            }
            catch (Exception ex)
            {
                foreach (Bitmap bitmap in loadedBitmaps)
                {
                    bitmap.Dispose();
                }

                throw new InvalidOperationException($"无法加载图片: {ex.Message}", ex);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;

            DrawBackground(g);
            DrawImages(g);

            if (_showRulers)
            {
                DrawRulers(g);
            }

            if (_showGuides)
            {
                DrawGuides(g);
            }

            DrawCrosshair(g);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            if (_images.Count == 0)
            {
                return;
            }

            if ((Control.ModifierKeys & Keys.Alt) != 0)
            {
                PointF beforeZoomWorld = ScreenToWorld(e.Location);
                float newZoom = Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, _zoom + (e.Delta > 0 ? ZOOM_STEP : -ZOOM_STEP)));
                if (Math.Abs(newZoom - _zoom) < 0.001f)
                {
                    return;
                }

                _zoom = newZoom;
                _panOffset = new PointF(
                    e.Location.X - beforeZoomWorld.X * _zoom,
                    e.Location.Y - beforeZoomWorld.Y * _zoom);

                ClampPanOffset();
                UpdateScrollBars();
                ZoomChanged?.Invoke(_zoom);
                Invalidate();
                return;
            }

            int scrollDelta = e.Delta / 120;
            if (_vScroll.Visible)
            {
                int newValue = Math.Max(_vScroll.Minimum, Math.Min(_vScroll.Maximum, _vScroll.Value - scrollDelta * _vScroll.SmallChange));
                if (_vScroll.Value != newValue)
                {
                    _vScroll.Value = newValue;
                }
            }
            else if (_hScroll.Visible)
            {
                int newValue = Math.Max(_hScroll.Minimum, Math.Min(_hScroll.Maximum, _hScroll.Value - scrollDelta * _hScroll.SmallChange));
                if (_hScroll.Value != newValue)
                {
                    _hScroll.Value = newValue;
                }
            }

            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _lastMousePos = e.Location;

            if (e.Button != MouseButtons.Left)
            {
                if (e.Button == MouseButtons.Right && e.Clicks == 2)
                {
                    ResetView();
                    return;
                }

                HandleRightClick(e);
                return;
            }

            _draggingGuide = GetGuideAtPoint(e.Location);
            if (_draggingGuide != null)
            {
                return;
            }

            CanvasImageItem hitImage = HitTestImage(e.Location);
            if (hitImage != null)
            {
                SelectImage(hitImage, true);
                if (_activeTool == CanvasTool.Move)
                {
                    _isDraggingImage = true;
                    _panStart = e.Location;
                    _dragImageStartWorld = hitImage.WorldLocation;
                    Cursor = Cursors.SizeAll;
                }
                else
                {
                    UpdateIdleCursor(e.Location);
                    Invalidate();
                }
                return;
            }

            SelectImage(null, false);
            _isPanning = true;
            _panStart = e.Location;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _lastMousePos = e.Location;

            if (_draggingGuide != null)
            {
                UpdateGuidePosition(_draggingGuide, e.Location);
                Invalidate();
                return;
            }

            if (_isDraggingImage && _selectedImage != null)
            {
                float worldDx = (e.Location.X - _panStart.X) / _zoom;
                float worldDy = (e.Location.Y - _panStart.Y) / _zoom;
                _selectedImage.WorldLocation = new PointF(_dragImageStartWorld.X + worldDx, _dragImageStartWorld.Y + worldDy);
                ClampSelectedImageToViewport(_selectedImage);
                UpdateScrollBars();
                Invalidate();
                return;
            }

            if (_isPanning)
            {
                _panOffset = new PointF(_panOffset.X + e.Location.X - _panStart.X, _panOffset.Y + e.Location.Y - _panStart.Y);
                _panStart = e.Location;
                ClampPanOffset();
                SyncScrollBarsFromOffset();
                Invalidate();
                return;
            }

            UpdateIdleCursor(e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isPanning = false;
            _isDraggingImage = false;
            _draggingGuide = null;
            UpdateIdleCursor(e.Location);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampPanOffset();
            UpdateScrollBars();
            Invalidate();
        }

        private void HScroll_ValueChanged(object sender, EventArgs e)
        {
            if (_images.Count == 0)
            {
                return;
            }

            RectangleF sceneBounds = GetSceneBounds();
            float maxPanX = GetHorizontalPanForScrollValue(0, sceneBounds);
            _panOffset = new PointF(maxPanX - _hScroll.Value, _panOffset.Y);
            Invalidate();
        }

        private void VScroll_ValueChanged(object sender, EventArgs e)
        {
            if (_images.Count == 0)
            {
                return;
            }

            RectangleF sceneBounds = GetSceneBounds();
            float maxPanY = GetVerticalPanForScrollValue(0, sceneBounds);
            _panOffset = new PointF(_panOffset.X, maxPanY - _vScroll.Value);
            Invalidate();
        }

        private void SyncScrollBarsFromOffset()
        {
            if (_images.Count == 0)
            {
                return;
            }

            RectangleF sceneBounds = GetSceneBounds();

            if (_hScroll.Visible)
            {
                int target = (int)Math.Round(GetHorizontalPanForScrollValue(0, sceneBounds) - _panOffset.X);
                target = Math.Max(_hScroll.Minimum, Math.Min(_hScroll.Maximum, target));
                if (_hScroll.Value != target)
                {
                    _hScroll.Value = target;
                }
            }

            if (_vScroll.Visible)
            {
                int target = (int)Math.Round(GetVerticalPanForScrollValue(0, sceneBounds) - _panOffset.Y);
                target = Math.Max(_vScroll.Minimum, Math.Min(_vScroll.Maximum, target));
                if (_vScroll.Value != target)
                {
                    _vScroll.Value = target;
                }
            }
        }

        /// <summary>
        /// 处理画布图片的右键菜单逻辑。
        /// </summary>
        private void HandleRightClick(MouseEventArgs e)
        {
            CanvasImageItem hitImage = HitTestImage(e.Location);
            if (hitImage == null)
            {
                SelectImage(null, false);
                _imageContextMenu.Close();
                _clearCanvasMenuItem.Enabled = _images.Count > 0;
                _resetViewMenuItem.Enabled = _images.Count > 0;
                Invalidate();
                _canvasContextMenu.Show(this, e.Location);
                return;
            }

            _canvasContextMenu.Close();
            SelectImage(hitImage, true);
            _deleteImageMenuItem.Enabled = _selectedImage != null;
            Invalidate();
            _imageContextMenu.Show(this, e.Location);
        }

        /// <summary>
        /// 删除当前选中的图片，并同步刷新画布状态。
        /// </summary>
        private void DeleteSelectedImage()
        {
            if (_selectedImage == null)
            {
                return;
            }

            CanvasImageItem imageToRemove = _selectedImage;
            int removedIndex = _images.IndexOf(imageToRemove);

            _images.Remove(imageToRemove);
            _selectedImage = null;
            imageToRemove.Image.Dispose();

            if (_images.Count == 0)
            {
                _zoom = 1f;
                _panOffset = PointF.Empty;
                _hScroll.Visible = false;
                _vScroll.Visible = false;
                ZoomChanged?.Invoke(_zoom);
                Invalidate();
                return;
            }

            int nextIndex = Math.Min(removedIndex, _images.Count - 1);
            SelectImage(_images[nextIndex], false);
            ClampPanOffset();
            UpdateScrollBars();
            Invalidate();
        }

        /// <summary>
        /// 响应右键菜单中的删除操作。
        /// </summary>
        private void DeleteImageMenuItem_Click(object sender, EventArgs e)
        {
            DeleteSelectedImage();
        }

        /// <summary>
        /// 响应空白区域菜单中的清空画布操作。
        /// </summary>
        private void ClearCanvasMenuItem_Click(object sender, EventArgs e)
        {
            ClearScene();
        }

        /// <summary>
        /// 响应空白区域菜单中的重置视图操作。
        /// </summary>
        private void ResetViewMenuItem_Click(object sender, EventArgs e)
        {
            ResetView();
        }

        private void UpdateScrollBars()
        {
            if (_images.Count == 0)
            {
                _hScroll.Visible = false;
                _vScroll.Visible = false;
                return;
            }

            RectangleF sceneBounds = GetSceneBounds();
            Size viewportSize = CalculateViewportSize(false, false);
            bool needH = sceneBounds.Width * _zoom > viewportSize.Width;
            bool needV = sceneBounds.Height * _zoom > viewportSize.Height;

            viewportSize = CalculateViewportSize(needH, needV);
            needH = sceneBounds.Width * _zoom > viewportSize.Width;
            needV = sceneBounds.Height * _zoom > viewportSize.Height;
            viewportSize = CalculateViewportSize(needH, needV);

            _hScroll.Visible = needH;
            _vScroll.Visible = needV;

            if (_hScroll.Visible)
            {
                _hScroll.Location = new Point(RULER_SIZE, Height - SCROLLBAR_SIZE);
                _hScroll.Width = viewportSize.Width;
                _hScroll.Minimum = 0;
                _hScroll.LargeChange = Math.Max(1, viewportSize.Width);
                _hScroll.SmallChange = Math.Max(1, viewportSize.Width / 10);
                _hScroll.Maximum = Math.Max(0, (int)Math.Ceiling(sceneBounds.Width * _zoom) - viewportSize.Width);
            }

            if (_vScroll.Visible)
            {
                _vScroll.Location = new Point(Width - SCROLLBAR_SIZE, RULER_SIZE);
                _vScroll.Height = viewportSize.Height;
                _vScroll.Minimum = 0;
                _vScroll.LargeChange = Math.Max(1, viewportSize.Height);
                _vScroll.SmallChange = Math.Max(1, viewportSize.Height / 10);
                _vScroll.Maximum = Math.Max(0, (int)Math.Ceiling(sceneBounds.Height * _zoom) - viewportSize.Height);
            }

            ClampPanOffset();
            SyncScrollBarsFromOffset();
        }

        private void DrawBackground(Graphics g)
        {
            g.Clear(Color.FromArgb(25, 25, 30));

            Rectangle viewportBounds = GetViewportBounds();
            if (viewportBounds.Width <= 0 || viewportBounds.Height <= 0)
            {
                return;
            }

            using (SolidBrush workspaceBrush = new SolidBrush(Color.FromArgb(30, 30, 35)))
            using (Pen viewportBorderPen = new Pen(Color.FromArgb(45, 45, 50), 1))
            {
                g.FillRectangle(workspaceBrush, viewportBounds);
                g.DrawRectangle(viewportBorderPen, viewportBounds.X, viewportBounds.Y, viewportBounds.Width - 1, viewportBounds.Height - 1);
            }
        }

        private void DrawImages(Graphics g)
        {
            if (_images.Count == 0)
            {
                return;
            }

            Rectangle viewportBounds = GetViewportBounds();
            GraphicsState state = g.Save();
            g.SetClip(viewportBounds);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            foreach (CanvasImageItem item in _images)
            {
                RectangleF screenBounds = GetImageScreenBounds(item);
                if (screenBounds.Width <= 0 || screenBounds.Height <= 0)
                {
                    continue;
                }

                RectangleF visibleBounds = RectangleF.Intersect(screenBounds, viewportBounds);
                if (visibleBounds.Width <= 0 || visibleBounds.Height <= 0)
                {
                    continue;
                }

                if (_bgStyle == BackgroundStyle.Checkerboard)
                {
                    using (TextureBrush brush = new TextureBrush(_checkerboard, WrapMode.Tile))
                    {
                        brush.TranslateTransform(screenBounds.X, screenBounds.Y);
                        g.FillRectangle(brush, screenBounds);
                    }
                }
                else
                {
                    using (SolidBrush whiteBrush = new SolidBrush(Color.White))
                    {
                        g.FillRectangle(whiteBrush, screenBounds);
                    }
                }

                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                {
                    RectangleF shadowBounds = new RectangleF(screenBounds.X + 6, screenBounds.Y + 6, screenBounds.Width, screenBounds.Height);
                    g.FillRectangle(shadowBrush, shadowBounds);
                }

                g.DrawImage(item.Image, screenBounds);

                using (Pen borderPen = new Pen(item.IsSelected ? Color.FromArgb(90, 170, 255) : Color.FromArgb(210, 210, 215), item.IsSelected ? 2f : 1f))
                {
                    g.DrawRectangle(borderPen, screenBounds.X, screenBounds.Y, screenBounds.Width - 1, screenBounds.Height - 1);
                }
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
            int endMm = pixelPerMm <= 0 ? 0 : (int)Math.Ceiling((Width - RULER_SIZE) / pixelPerMm);
            int interval = GetAdaptiveInterval(pixelPerMm);
            int subInterval = Math.Max(1, interval / 5);

            for (int mm = 0; mm <= endMm; mm++)
            {
                float x = RULER_SIZE + mm * pixelPerMm;
                if (x > Width)
                {
                    break;
                }

                if (mm % interval == 0)
                {
                    int tickHeight = interval >= 50 ? 8 : interval >= 10 ? 12 : 16;
                    g.DrawLine(linePen, x, RULER_SIZE - tickHeight, x, RULER_SIZE - 1);
                    if (pixelPerMm * interval >= 40)
                    {
                        string label = interval >= 100 ? (mm / 100).ToString() : (mm / 10).ToString();
                        g.DrawString(label, tickFont, textBrush, new RectangleF(x + 2, 2, 32, 12));
                    }
                }
                else if (mm % subInterval == 0)
                {
                    g.DrawLine(linePen, x, RULER_SIZE - 8, x, RULER_SIZE - 1);
                }
            }
        }

        private void DrawYAxis(Graphics g, SolidBrush textBrush, Pen linePen, Font tickFont)
        {
            float pixelPerMm = MM_TO_PX * _zoom;
            int endMm = pixelPerMm <= 0 ? 0 : (int)Math.Ceiling((Height - RULER_SIZE) / pixelPerMm);
            int interval = GetAdaptiveInterval(pixelPerMm);
            int subInterval = Math.Max(1, interval / 5);

            for (int mm = 0; mm <= endMm; mm++)
            {
                float y = RULER_SIZE + mm * pixelPerMm;
                if (y > Height)
                {
                    break;
                }

                if (mm % interval == 0)
                {
                    int tickWidth = interval >= 50 ? 8 : interval >= 10 ? 12 : 16;
                    g.DrawLine(linePen, RULER_SIZE - tickWidth, y, RULER_SIZE - 1, y);
                    if (pixelPerMm * interval >= 40)
                    {
                        string label = interval >= 100 ? (mm / 100).ToString() : (mm / 10).ToString();
                        g.DrawString(label, tickFont, textBrush, new RectangleF(2, y - 7, RULER_SIZE - 4, 14));
                    }
                }
                else if (mm % subInterval == 0)
                {
                    g.DrawLine(linePen, RULER_SIZE - 8, y, RULER_SIZE - 1, y);
                }
            }
        }

        private void DrawGuides(Graphics g)
        {
            Rectangle viewportBounds = GetViewportBounds();
            if (viewportBounds.Width <= 0 || viewportBounds.Height <= 0)
            {
                return;
            }

            using (Pen guidePen = new Pen(Color.FromArgb(200, 255, 0, 0), 1f))
            {
                guidePen.DashStyle = DashStyle.Dash;

                foreach (GuideLine guide in _guides)
                {
                    if (guide.IsHorizontal)
                    {
                        float y = viewportBounds.Top + viewportBounds.Height * guide.Position;
                        if (y >= viewportBounds.Top && y <= viewportBounds.Bottom)
                        {
                            g.DrawLine(guidePen, viewportBounds.Left, y, viewportBounds.Right, y);
                        }
                    }
                    else
                    {
                        float x = viewportBounds.Left + viewportBounds.Width * guide.Position;
                        if (x >= viewportBounds.Left && x <= viewportBounds.Right)
                        {
                            g.DrawLine(guidePen, x, viewportBounds.Top, x, viewportBounds.Bottom);
                        }
                    }
                }
            }
        }

        private void DrawCrosshair(Graphics g)
        {
            Rectangle viewportBounds = GetViewportBounds();
            if (!viewportBounds.Contains(_lastMousePos))
            {
                return;
            }

            using (Pen crossPen = new Pen(Color.FromArgb(100, 255, 255, 255), 1f))
            {
                crossPen.DashStyle = DashStyle.Dot;
                g.DrawLine(crossPen, viewportBounds.Left, _lastMousePos.Y, viewportBounds.Right, _lastMousePos.Y);
                g.DrawLine(crossPen, _lastMousePos.X, viewportBounds.Top, _lastMousePos.X, viewportBounds.Bottom);
            }
        }

        private int GetAdaptiveInterval(float pixelPerMm)
        {
            if (pixelPerMm * 1 >= 30) return 1;
            if (pixelPerMm * 5 >= 30) return 5;
            if (pixelPerMm * 10 >= 30) return 10;
            if (pixelPerMm * 50 >= 30) return 50;
            return 100;
        }

        private void CreateCheckerboard()
        {
            _checkerboard = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(_checkerboard))
            using (SolidBrush brush1 = new SolidBrush(Color.FromArgb(60, 60, 70)))
            using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(80, 80, 90)))
            {
                g.FillRectangle(brush1, 0, 0, 8, 8);
                g.FillRectangle(brush2, 8, 0, 8, 8);
                g.FillRectangle(brush2, 0, 8, 8, 8);
                g.FillRectangle(brush1, 8, 8, 8, 8);
            }
        }

        private bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".tif" || ext == ".tiff";
        }

        private Bitmap LoadBitmapFromFile(string filePath)
        {
            try
            {
                return new Bitmap(filePath);
            }
            catch (Exception gdiEx) when (IsTiffFile(filePath))
            {
                Exception magickEx = null;
                try
                {
                    return LoadBitmapWithMagick(filePath);
                }
                catch (Exception ex)
                {
                    magickEx = ex;
                }

                try
                {
                    return LoadBitmapWithLibTiff(filePath);
                }
                catch (Exception libTiffEx)
                {
                    throw new InvalidOperationException(
                        $"TIFF 文件加载失败。GDI+：{gdiEx.Message}；Magick：{magickEx?.Message ?? "未执行"}；LibTiff：{libTiffEx.Message}",
                        libTiffEx);
                }
            }
        }

        private bool IsTiffFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".tif" || ext == ".tiff";
        }

        private Bitmap LoadBitmapWithMagick(string filePath)
        {
            EnsureMagickNetInitialized();

            using (MagickImageCollection collection = new MagickImageCollection(filePath))
            {
                if (collection.Count == 0)
                {
                    throw new InvalidOperationException("TIFF 中没有可显示的图像帧");
                }

                for (int i = 0; i < collection.Count; i++)
                {
                    collection[i].Compose = CompositeOperator.Over;
                    collection[i].Page = new MagickGeometry(0, 0, collection[i].Width, collection[i].Height);
                    collection[i].Alpha(AlphaOption.On);
                }

                using (MagickImage flattened = collection.Count == 1 ? (MagickImage)collection[0] : (MagickImage)collection.Flatten())
                {
                    flattened.Format = MagickFormat.Png;
                    byte[] bytes = flattened.ToByteArray();
                    using (MemoryStream ms = new MemoryStream(bytes))
                    using (Bitmap decoded = new Bitmap(ms))
                    {
                        return new Bitmap(decoded);
                    }
                }
            }
        }

        private Bitmap LoadBitmapWithLibTiff(string filePath)
        {
            using (Tiff tif = Tiff.Open(filePath, "r"))
            {
                if (tif == null)
                {
                    throw new InvalidOperationException("LibTiff 无法打开该 TIFF 文件");
                }

                FieldValue[] widthField = tif.GetField(TiffTag.IMAGEWIDTH);
                FieldValue[] heightField = tif.GetField(TiffTag.IMAGELENGTH);
                if (widthField == null || heightField == null)
                {
                    throw new InvalidOperationException("TIFF 缺少宽高信息");
                }

                int width = widthField[0].ToInt();
                int height = heightField[0].ToInt();
                if (width <= 0 || height <= 0)
                {
                    throw new InvalidOperationException($"TIFF 尺寸无效: {width}x{height}");
                }

                int[] raster = new int[width * height];
                bool ok = tif.ReadRGBAImageOriented(width, height, raster, BitMiracle.LibTiff.Classic.Orientation.TOPLEFT);
                if (!ok)
                {
                    throw new InvalidOperationException("LibTiff RGBA 解码失败");
                }

                Bitmap bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        int rgba = raster[row + x];
                        byte r = (byte)Tiff.GetR(rgba);
                        byte g = (byte)Tiff.GetG(rgba);
                        byte b = (byte)Tiff.GetB(rgba);
                        byte a = (byte)Tiff.GetA(rgba);
                        bitmap.SetPixel(x, y, Color.FromArgb(a, r, g, b));
                    }
                }

                return bitmap;
            }
        }

        private void EnsureMagickNetInitialized()
        {
            if (_magickInitialized)
            {
                return;
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDirectory) && Directory.Exists(baseDirectory))
            {
                MagickNET.Initialize(baseDirectory);
            }

            _magickInitialized = true;
        }

        private Size CalculateViewportSize(bool includeHorizontalScroll, bool includeVerticalScroll)
        {
            return new Size(
                Math.Max(0, Width - RULER_SIZE - (includeVerticalScroll ? SCROLLBAR_SIZE : 0)),
                Math.Max(0, Height - RULER_SIZE - (includeHorizontalScroll ? SCROLLBAR_SIZE : 0)));
        }

        private Rectangle GetViewportBounds()
        {
            return new Rectangle(
                RULER_SIZE,
                RULER_SIZE,
                Math.Max(0, Width - RULER_SIZE - (_vScroll.Visible ? SCROLLBAR_SIZE : 0)),
                Math.Max(0, Height - RULER_SIZE - (_hScroll.Visible ? SCROLLBAR_SIZE : 0)));
        }

        private RectangleF GetSceneBounds()
        {
            if (_images.Count == 0)
            {
                return RectangleF.Empty;
            }

            RectangleF bounds = new RectangleF(_images[0].WorldLocation, _images[0].Image.Size);
            for (int i = 1; i < _images.Count; i++)
            {
                bounds = RectangleF.Union(bounds, new RectangleF(_images[i].WorldLocation, _images[i].Image.Size));
            }

            return bounds;
        }

        private void AddImagesHorizontally(IReadOnlyList<Bitmap> bitmaps)
        {
            if (bitmaps == null || bitmaps.Count == 0)
            {
                return;
            }

            float averageWidth = (float)bitmaps.Average(bitmap => bitmap.Width);
            float spacing = Math.Max(24f, averageWidth * 0.08f);
            float currentX = 0f;
            float maxHeight = bitmaps.Max(bitmap => (float)bitmap.Height);

            foreach (Bitmap bitmap in bitmaps)
            {
                float y = (maxHeight - bitmap.Height) / 2f;
                CanvasImageItem item = new CanvasImageItem(bitmap, new PointF(currentX, y));
                _images.Add(item);
                currentX += bitmap.Width + spacing;
            }

            SelectImage(_images[_images.Count - 1], true);
            FitSceneToViewport();
            UpdateScrollBars();
            Invalidate();
        }

        private PointF GetDefaultImageLocation(Size imageSize)
        {
            Rectangle viewportBounds = GetViewportBounds();
            PointF screenCenter = new PointF(viewportBounds.Left + viewportBounds.Width / 2f, viewportBounds.Top + viewportBounds.Height / 2f);
            PointF worldCenter = ScreenToWorld(screenCenter);
            float offset = (_images.Count * 24f) / Math.Max(_zoom, 0.001f);
            return new PointF(
                worldCenter.X - imageSize.Width / 2f + offset,
                worldCenter.Y - imageSize.Height / 2f + offset);
        }

        private RectangleF GetImageScreenBounds(CanvasImageItem item)
        {
            return new RectangleF(
                _panOffset.X + item.WorldLocation.X * _zoom,
                _panOffset.Y + item.WorldLocation.Y * _zoom,
                item.Image.Width * _zoom,
                item.Image.Height * _zoom);
        }

        private PointF ScreenToWorld(PointF screenPoint)
        {
            return new PointF(
                (screenPoint.X - _panOffset.X) / _zoom,
                (screenPoint.Y - _panOffset.Y) / _zoom);
        }

        private CanvasImageItem HitTestImage(Point screenPoint)
        {
            PointF worldPoint = ScreenToWorld(screenPoint);
            for (int i = _images.Count - 1; i >= 0; i--)
            {
                RectangleF worldBounds = new RectangleF(_images[i].WorldLocation, _images[i].Image.Size);
                if (worldBounds.Contains(worldPoint))
                {
                    return _images[i];
                }
            }

            return null;
        }

        private void UpdateIdleCursor(Point location)
        {
            if (_activeTool == CanvasTool.Move && HitTestImage(location) != null)
            {
                Cursor = Cursors.SizeAll;
                return;
            }

            Cursor = Cursors.Default;
        }

        private void SelectImage(CanvasImageItem item, bool bringToFront)
        {
            foreach (CanvasImageItem image in _images)
            {
                image.IsSelected = false;
            }

            _selectedImage = item;
            if (_selectedImage != null)
            {
                _selectedImage.IsSelected = true;
                if (bringToFront)
                {
                    _images.Remove(_selectedImage);
                    _images.Add(_selectedImage);
                }
            }
        }

        private void FitSceneToViewport()
        {
            if (_images.Count == 0)
            {
                _zoom = 1f;
                _panOffset = PointF.Empty;
                return;
            }

            RectangleF sceneBounds = GetSceneBounds();
            Size viewportSize = CalculateViewportSize(false, false);
            if (viewportSize.Width <= 0 || viewportSize.Height <= 0 || sceneBounds.Width <= 0 || sceneBounds.Height <= 0)
            {
                _zoom = 1f;
                CenterScene();
                return;
            }

            float fitZoomX = viewportSize.Width / sceneBounds.Width;
            float fitZoomY = viewportSize.Height / sceneBounds.Height;
            _zoom = Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, Math.Min(fitZoomX, fitZoomY)));

            Rectangle viewportBounds = new Rectangle(RULER_SIZE, RULER_SIZE, viewportSize.Width, viewportSize.Height);
            PointF viewportCenter = new PointF(viewportBounds.Left + viewportBounds.Width / 2f, viewportBounds.Top + viewportBounds.Height / 2f);
            PointF sceneCenter = new PointF(sceneBounds.Left + sceneBounds.Width / 2f, sceneBounds.Top + sceneBounds.Height / 2f);
            _panOffset = new PointF(
                viewportCenter.X - sceneCenter.X * _zoom,
                viewportCenter.Y - sceneCenter.Y * _zoom);

            ClampPanOffset();
        }

        private void CenterScene()
        {
            if (_images.Count == 0)
            {
                _panOffset = PointF.Empty;
                return;
            }

            Rectangle viewportBounds = GetViewportBounds();
            RectangleF sceneBounds = GetSceneBounds();
            PointF viewportCenter = new PointF(viewportBounds.Left + viewportBounds.Width / 2f, viewportBounds.Top + viewportBounds.Height / 2f);
            PointF sceneCenter = new PointF(sceneBounds.Left + sceneBounds.Width / 2f, sceneBounds.Top + sceneBounds.Height / 2f);
            _panOffset = new PointF(
                viewportCenter.X - sceneCenter.X * _zoom,
                viewportCenter.Y - sceneCenter.Y * _zoom);
        }

        private float GetHorizontalPanForScrollValue(int scrollValue, RectangleF sceneBounds)
        {
            Rectangle viewportBounds = GetViewportBounds();
            float leftAlignedPan = viewportBounds.Left - sceneBounds.Left * _zoom;
            return leftAlignedPan - scrollValue;
        }

        private float GetVerticalPanForScrollValue(int scrollValue, RectangleF sceneBounds)
        {
            Rectangle viewportBounds = GetViewportBounds();
            float topAlignedPan = viewportBounds.Top - sceneBounds.Top * _zoom;
            return topAlignedPan - scrollValue;
        }

        private void ClampPanOffset()
        {
            if (_images.Count == 0)
            {
                return;
            }

            Rectangle viewportBounds = GetViewportBounds();
            RectangleF sceneBounds = GetSceneBounds();

            float leftAlignedPan = viewportBounds.Left - sceneBounds.Left * _zoom;
            float rightAlignedPan = viewportBounds.Right - sceneBounds.Right * _zoom;
            float topAlignedPan = viewportBounds.Top - sceneBounds.Top * _zoom;
            float bottomAlignedPan = viewportBounds.Bottom - sceneBounds.Bottom * _zoom;

            float minPanX = Math.Min(leftAlignedPan, rightAlignedPan);
            float maxPanX = Math.Max(leftAlignedPan, rightAlignedPan);
            float minPanY = Math.Min(topAlignedPan, bottomAlignedPan);
            float maxPanY = Math.Max(topAlignedPan, bottomAlignedPan);

            _panOffset = new PointF(
                Math.Max(minPanX, Math.Min(maxPanX, _panOffset.X)),
                Math.Max(minPanY, Math.Min(maxPanY, _panOffset.Y)));
        }

        private void ClampSelectedImageToViewport(CanvasImageItem item)
        {
            if (item == null)
            {
                return;
            }

            Rectangle viewportBounds = GetViewportBounds();
            float scaledWidth = item.Image.Width * _zoom;
            float scaledHeight = item.Image.Height * _zoom;

            float screenX = _panOffset.X + item.WorldLocation.X * _zoom;
            float screenY = _panOffset.Y + item.WorldLocation.Y * _zoom;

            float minScreenX = Math.Min(viewportBounds.Left, viewportBounds.Right - scaledWidth);
            float maxScreenX = Math.Max(viewportBounds.Left, viewportBounds.Right - scaledWidth);
            float minScreenY = Math.Min(viewportBounds.Top, viewportBounds.Bottom - scaledHeight);
            float maxScreenY = Math.Max(viewportBounds.Top, viewportBounds.Bottom - scaledHeight);

            screenX = Math.Max(minScreenX, Math.Min(maxScreenX, screenX));
            screenY = Math.Max(minScreenY, Math.Min(maxScreenY, screenY));

            item.WorldLocation = new PointF(
                (screenX - _panOffset.X) / _zoom,
                (screenY - _panOffset.Y) / _zoom);
        }

        private GuideLine GetGuideAtPoint(Point screenPoint)
        {
            Rectangle viewportBounds = GetViewportBounds();
            int hitRadius = 5;

            foreach (GuideLine guide in _guides)
            {
                if (guide.IsHorizontal)
                {
                    int y = (int)Math.Round(viewportBounds.Top + viewportBounds.Height * guide.Position);
                    if (Math.Abs(screenPoint.Y - y) <= hitRadius)
                    {
                        return guide;
                    }
                }
                else
                {
                    int x = (int)Math.Round(viewportBounds.Left + viewportBounds.Width * guide.Position);
                    if (Math.Abs(screenPoint.X - x) <= hitRadius)
                    {
                        return guide;
                    }
                }
            }

            return null;
        }

        private void UpdateGuidePosition(GuideLine guide, Point screenPoint)
        {
            Rectangle viewportBounds = GetViewportBounds();
            if (guide.IsHorizontal)
            {
                float localY = screenPoint.Y - viewportBounds.Top;
                guide.Position = Math.Max(0f, Math.Min(1f, viewportBounds.Height <= 0 ? 0f : localY / viewportBounds.Height));
            }
            else
            {
                float localX = screenPoint.X - viewportBounds.Left;
                guide.Position = Math.Max(0f, Math.Min(1f, viewportBounds.Width <= 0 ? 0f : localX / viewportBounds.Width));
            }
        }
    }
}

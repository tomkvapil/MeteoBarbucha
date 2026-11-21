using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MeteoSrazky
{
    public class ZoomablePictureBox : Control
    {
        private Image _image;
        private float _zoom = 1.0f;
        private PointF _panNorm = PointF.Empty; // Normalized offset relative to drawn image size
        private bool _dragging;
        private Point _lastMouse;
        private string _hoverOverlayText;
        private Point _hoverMousePos;

        public event Action<PointF?> ImageMouseMoved;
        public event Action<float, PointF> ViewChanged;

        public float Zoom { get { return _zoom; } }
        public PointF PanNorm { get { return _panNorm; } }

        public Image Image
        {
            get { return _image; }
            set
            {
                _image = value;
                Invalidate();
            }
        }

        public ZoomablePictureBox()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.FromArgb(245, 248, 252);
            Cursor = Cursors.Hand;
        }

        public void ResetView(bool notify = true)
        {
            _zoom = 1.0f;
            _panNorm = PointF.Empty;
            Invalidate();
            if (notify && ViewChanged != null)
                ViewChanged(_zoom, _panNorm);
        }

        public void SetView(float zoom, PointF panNorm, bool notify = false)
        {
            _zoom = Math.Max(0.8f, Math.Min(8.0f, zoom));
            _panNorm = panNorm;
            Invalidate();
            if (notify && ViewChanged != null)
                ViewChanged(_zoom, _panNorm);
        }

        public void ZoomBy(float factor, bool notify = true)
        {
            float newZoom = Math.Max(0.8f, Math.Min(8.0f, _zoom * factor));
            if (Math.Abs(newZoom - _zoom) < 0.001f) return;
            _zoom = newZoom;
            Invalidate();
            if (notify && ViewChanged != null)
                ViewChanged(_zoom, _panNorm);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _lastMouse = e.Location;
                Focus();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging && _image != null)
            {
                float drawW, drawH;
                GetDrawnSize(out drawW, out drawH);
                if (drawW > 0 && drawH > 0)
                {
                    _panNorm.X += (e.X - _lastMouse.X) / drawW;
                    _panNorm.Y += (e.Y - _lastMouse.Y) / drawH;
                }
                _lastMouse = e.Location;
                Invalidate();
                if (ViewChanged != null)
                    ViewChanged(_zoom, _panNorm);
            }
            if (ImageMouseMoved != null)
                ImageMouseMoved(ScreenToImage(e.Location));
        }

        public void SetHoverOverlay(string text, Point mousePos)
        {
            if (_hoverOverlayText == text && _hoverMousePos == mousePos)
                return;
            _hoverOverlayText = text;
            _hoverMousePos = mousePos;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverOverlayText = null;
            Invalidate();
            if (ImageMouseMoved != null)
                ImageMouseMoved(null);
        }

        public PointF? ScreenToImage(Point mouse)
        {
            if (_image == null || Width <= 0 || Height <= 0)
                return null;

            float drawW, drawH;
            GetDrawnSize(out drawW, out drawH);

            float x = (Width - drawW) / 2f + _panNorm.X * drawW;
            float y = (Height - drawH) / 2f + _panNorm.Y * drawH;

            float scale = drawW / _image.Width;
            float imgX = (mouse.X - x) / scale;
            float imgY = (mouse.Y - y) / scale;

            if (imgX < 0 || imgY < 0 || imgX > _image.Width || imgY > _image.Height)
                return null;
            return new PointF(imgX, imgY);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
                _dragging = false;
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            ResetView(true);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            var hme = e as HandledMouseEventArgs;
            if (hme != null) hme.Handled = true;
            if (_image == null) return;

            float factor = e.Delta > 0 ? 1.18f : (1.0f / 1.18f);
            float newZoom = Math.Max(0.8f, Math.Min(8.0f, _zoom * factor));
            if (Math.Abs(newZoom - _zoom) < 0.001f) return;

            float drawW, drawH;
            GetDrawnSize(out drawW, out drawH);
            if (drawW > 0 && drawH > 0)
            {
                float dx = (e.X - Width / 2f) / drawW;
                float dy = (e.Y - Height / 2f) / drawH;
                float invRatio = _zoom / newZoom;
                _panNorm.X = _panNorm.X + dx * (invRatio - 1f);
                _panNorm.Y = _panNorm.Y + dy * (invRatio - 1f);
            }

            _zoom = newZoom;
            Invalidate();
            if (ViewChanged != null)
                ViewChanged(_zoom, _panNorm);
        }

        private void GetDrawnSize(out float drawW, out float drawH)
        {
            if (_image == null || Width <= 0 || Height <= 0)
            {
                drawW = 0;
                drawH = 0;
                return;
            }
            float scaleX = (float)Width / _image.Width;
            float scaleY = (float)Height / _image.Height;
            float baseScale = Math.Min(scaleX, scaleY);
            drawW = _image.Width * baseScale * _zoom;
            drawH = _image.Height * baseScale * _zoom;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);
            if (_image == null) return;

            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

            float drawW, drawH;
            GetDrawnSize(out drawW, out drawH);

            float x = (Width - drawW) / 2f + _panNorm.X * drawW;
            float y = (Height - drawH) / 2f + _panNorm.Y * drawH;

            e.Graphics.DrawImage(_image, x, y, drawW, drawH);

            if (!string.IsNullOrEmpty(_hoverOverlayText))
            {
                using (var font = new Font("Segoe UI", 9f, FontStyle.Bold))
                using (var bg = new SolidBrush(Color.FromArgb(225, 15, 23, 42)))
                using (var border = new Pen(Color.FromArgb(96, 165, 250), 1f))
                using (var fg = new SolidBrush(Color.White))
                {
                    SizeF sz = e.Graphics.MeasureString(_hoverOverlayText, font);
                    int bw = (int)Math.Ceiling(sz.Width) + 12;
                    int bh = (int)Math.Ceiling(sz.Height) + 6;
                    int bx = Math.Min(Width - bw - 4, Math.Max(4, _hoverMousePos.X + 14));
                    int by = Math.Min(Height - bh - 4, Math.Max(4, _hoverMousePos.Y + 14));
                    e.Graphics.FillRectangle(bg, bx, by, bw, bh);
                    e.Graphics.DrawRectangle(border, bx, by, bw, bh);
                    e.Graphics.DrawString(_hoverOverlayText, font, fg, bx + 6, by + 3);
                }
            }
        }
    }
}

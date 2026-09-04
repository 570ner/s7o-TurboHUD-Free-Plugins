using Turbo.Plugins.Default;

namespace Turbo.Plugins.s7o
{
    public class MapCursor : BasePlugin, IInGameWorldPainter, INewAreaHandler
    {
        public bool ShowInTown { get; set; }
        public float PlusRadius { get; set; }
        public float CircleRadius { get; set; }
        public IBrush Brush { get; set; }
        public IBrush ShadowBrush { get; set; }
        private bool _hasLastWorldPoint;
        private float _lastWorldX;
        private float _lastWorldY;

        public MapCursor()
        {
            Enabled = true;
            ShowInTown = true;
            PlusRadius = 10.0f;
            CircleRadius = 5.0f;
        }

        public override void Load(IController hud)
        {
            base.Load(hud);

            // Draw after other map markers, before native UI clipping and top overlays.
            // Keep cursor projection independent of click-safe UI boundaries.
            Order = int.MaxValue;
            ShadowBrush = Hud.Render.CreateBrush(230, 0, 0, 0, 3.0f);
            Brush = Hud.Render.CreateBrush(255, 255, 255, 255, 1.0f);
        }

        public void OnNewArea(bool newGame, ISnoArea area)
        {
            _hasLastWorldPoint = false;
            _lastWorldX = 0.0f;
            _lastWorldY = 0.0f;
        }

        public void PaintWorld(WorldLayer layer)
        {
            if (layer != WorldLayer.Map || !Enabled || Brush == null)
                return;
            if (Hud == null || Hud.Game == null || Hud.Game.Me == null ||
                Hud.Window == null || Hud.Render == null ||
                !Hud.Game.IsInGame || Hud.Game.IsLoading)
                return;
            if (!ShowInTown && Hud.Game.IsInTown)
                return;

            float mapX;
            float mapY;
            float minimapScale;
            if (!TryGetMinimapCursorPoint(out mapX, out mapY, out minimapScale))
                return;

            float plusRadius = PlusRadius > 0.0f ? PlusRadius * minimapScale : 0.0f;
            float circleRadius = CircleRadius > 0.0f ? CircleRadius * minimapScale : 0.0f;
            if (plusRadius <= 0.0f && circleRadius <= 0.0f)
                return;

            if (ShadowBrush != null)
            {
                if (plusRadius > 0.0f)
                {
                    ShadowBrush.DrawLine(mapX - plusRadius, mapY, mapX + plusRadius, mapY);
                    ShadowBrush.DrawLine(mapX, mapY - plusRadius, mapX, mapY + plusRadius);
                }
                if (circleRadius > 0.0f)
                    ShadowBrush.DrawEllipse(mapX, mapY, circleRadius, circleRadius);
            }

            if (plusRadius > 0.0f)
            {
                Brush.DrawLine(mapX - plusRadius, mapY, mapX + plusRadius, mapY);
                Brush.DrawLine(mapX, mapY - plusRadius, mapX, mapY + plusRadius);
            }
            if (circleRadius > 0.0f)
                Brush.DrawEllipse(mapX, mapY, circleRadius, circleRadius);
        }

        private bool TryGetMinimapCursorPoint(out float mapX, out float mapY, out float minimapScale)
        {
            mapX = 0.0f;
            mapY = 0.0f;
            minimapScale = 0.0f;

            try
            {
                float worldX = _lastWorldX;
                float worldY = _lastWorldY;
                int cursorX = Hud.Window.CursorX;
                int cursorY = Hud.Window.CursorY;

                // CursorX/Y and rendering coordinates are window-client coordinates,
                // so secondary-monitor desktop offsets never enter the projection.
                // This is a directional guide, not a click target: project the real
                // cursor everywhere in the client area and deliberately ignore UI bounds.
                IScreenCoordinate screen = Hud.Window.CreateScreenCoordinate(cursorX, cursorY);
                IWorldCoordinate world = screen != null ? screen.ToWorldCoordinate() : null;
                if (world != null && world.IsValid)
                {
                    worldX = world.X;
                    worldY = world.Y;
                    _lastWorldX = worldX;
                    _lastWorldY = worldY;
                    _hasLastWorldPoint = true;
                }

                if (!_hasLastWorldPoint)
                    return false;

                Hud.Render.GetMinimapCoordinates(worldX, worldY, out mapX, out mapY);
                minimapScale = Hud.Render.MinimapScale;

                return !float.IsNaN(mapX) && !float.IsInfinity(mapX) &&
                    !float.IsNaN(mapY) && !float.IsInfinity(mapY) &&
                    !float.IsNaN(minimapScale) && !float.IsInfinity(minimapScale) &&
                    minimapScale > 0.0f;
            }
            catch
            {
                return false;
            }
        }
    }
}

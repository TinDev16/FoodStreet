using Android.App;
using Android.Gms.Maps;
using Android.Gms.Maps.Model;
using Android.Graphics;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps.Handlers;
using System.Reflection;

using AndroidColor = Android.Graphics.Color;
using AndroidPaint = Android.Graphics.Paint;

public class CustomMapHandler : MapHandler
{
    public CustomMapHandler() : base(Mapper, CommandMapper)
    {
    }

    protected override void ConnectHandler(MapView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.GetMapAsync(new MapReadyCallback((Microsoft.Maui.Controls.Maps.Map)VirtualView));
    }

    public static new IPropertyMapper<Microsoft.Maui.Controls.Maps.Map, CustomMapHandler> Mapper =
        new PropertyMapper<Microsoft.Maui.Controls.Maps.Map, CustomMapHandler>(MapHandler.Mapper)
        {
            [nameof(Microsoft.Maui.Controls.Maps.Map.Pins)] = MapPins
        };

    private static void MapPins(CustomMapHandler handler, Microsoft.Maui.Controls.Maps.Map map)
    {
        handler.PlatformView.GetMapAsync(new MapReadyCallback(map));
    }

    private sealed class MapReadyCallback : Java.Lang.Object, IOnMapReadyCallback
    {
        private readonly Microsoft.Maui.Controls.Maps.Map _map;

        public MapReadyCallback(Microsoft.Maui.Controls.Maps.Map map)
        {
            _map = map;
        }

        public void OnMapReady(GoogleMap googleMap)
        {
            googleMap.Clear();
            var pinByMarkerId = new Dictionary<string, Pin>(StringComparer.Ordinal);

            foreach (var pin in _map.Pins)
            {
                var marker = googleMap.AddMarker(new MarkerOptions()
                    .SetPosition(new LatLng(pin.Location.Latitude, pin.Location.Longitude))
                    .SetTitle(pin.Label)
                    .SetIcon(BuildPoiIcon(pin.Label)));

                if (marker is not null)
                {
                    marker.SetAnchor(0.5f, 1f);
                    pinByMarkerId[marker.Id] = pin;
                }
            }

            googleMap.SetOnMarkerClickListener(new MarkerClickListener(pinByMarkerId));
        }

        private sealed class MarkerClickListener : Java.Lang.Object, GoogleMap.IOnMarkerClickListener
        {
            private readonly IReadOnlyDictionary<string, Pin> _pinByMarkerId;

            public MarkerClickListener(IReadOnlyDictionary<string, Pin> pinByMarkerId)
            {
                _pinByMarkerId = pinByMarkerId;
            }

            public bool OnMarkerClick(Marker marker)
            {
                if (!_pinByMarkerId.TryGetValue(marker.Id, out var pin))
                {
                    return false;
                }

                try
                {
                    var sendMarkerClick = pin.GetType().GetMethod(
                        "SendMarkerClick",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (sendMarkerClick is null)
                    {
                        return false;
                    }

                    var result = sendMarkerClick.Invoke(pin, null);
                    if (result is bool consumeClick)
                    {
                        return consumeClick;
                    }

                    // If method doesn't return bool, assume handled.
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static BitmapDescriptor BuildPoiIcon(string? label)
        {
            const float textSizePx = 28f;
            const float paddingHorizontalPx = 14f;
            const float paddingVerticalPx = 8f;
            const float gapPx = 8f;
            const float iconSizePx = 45f;

            var compactLabel = BuildCompactPoiName(label);
            var textPaint = new AndroidPaint(Android.Graphics.PaintFlags.AntiAlias)
            {
                Color = AndroidColor.ParseColor("#1F2937"),
                TextSize = textSizePx
            };
            var fontMetrics = textPaint.GetFontMetrics() ?? new AndroidPaint.FontMetrics();

            var textWidth = Math.Max(1f, textPaint.MeasureText(compactLabel));
            var textHeight = fontMetrics.Bottom - fontMetrics.Top;
            var bubbleWidth = (int)Math.Ceiling(textWidth + (paddingHorizontalPx * 2f));
            var bubbleHeight = (int)Math.Ceiling(textHeight + (paddingVerticalPx * 2f));
            var totalHeight = (int)Math.Ceiling(bubbleHeight + gapPx + iconSizePx);
            var totalWidth = Math.Max(bubbleWidth, (int)Math.Ceiling(iconSizePx + 8f));

            var bitmap = Bitmap.CreateBitmap(totalWidth, totalHeight, Bitmap.Config.Argb8888!);
            using var canvas = new Canvas(bitmap);

            var bubbleLeft = (totalWidth - bubbleWidth) / 2f;
            var bubbleTop = 0f;
            var bubbleRight = bubbleLeft + bubbleWidth;
            var bubbleBottom = bubbleTop + bubbleHeight;
            var bubbleRect = new Android.Graphics.RectF(bubbleLeft, bubbleTop, bubbleRight, bubbleBottom);

            var bubblePaint = new AndroidPaint(Android.Graphics.PaintFlags.AntiAlias) { Color = AndroidColor.ParseColor("#FFF7ED") };
            var strokePaint = new AndroidPaint(Android.Graphics.PaintFlags.AntiAlias)
            {
                Color = AndroidColor.ParseColor("#FDBA74"),
                StrokeWidth = 2f
            };
            strokePaint.SetStyle(AndroidPaint.Style.Stroke);

            canvas.DrawRoundRect(bubbleRect, 18f, 18f, bubblePaint);
            canvas.DrawRoundRect(bubbleRect, 18f, 18f, strokePaint);

            var textBaseline = bubbleTop + paddingVerticalPx - fontMetrics.Top;
            canvas.DrawText(compactLabel, bubbleLeft + paddingHorizontalPx, textBaseline, textPaint);

            var iconLeft = (totalWidth - iconSizePx) / 2f;
            var iconTop = bubbleBottom + gapPx;
            var iconRect = new Android.Graphics.RectF(iconLeft, iconTop, iconLeft + iconSizePx, iconTop + iconSizePx);

            DrawPoiBadge(canvas, iconRect);

            return BitmapDescriptorFactory.FromBitmap(bitmap);
        }

        private static void DrawPoiBadge(Canvas canvas, Android.Graphics.RectF iconRect)
        {
            var context = Android.App.Application.Context;
            var resourceId = context.Resources?.GetIdentifier("marker_poi", "drawable", context.PackageName) ?? 0;
            var iconBitmap = resourceId > 0 ? BitmapFactory.DecodeResource(context.Resources, resourceId) : null;

            var badgePaint = new AndroidPaint(Android.Graphics.PaintFlags.AntiAlias)
            {
                Color = AndroidColor.ParseColor("#EA580C")
            };
            canvas.DrawRoundRect(iconRect, 6f, 6f, badgePaint);

            if (iconBitmap is not null)
            {
                var iconPadding = 5f;
                var iconTarget = new Android.Graphics.RectF(
                    iconRect.Left + iconPadding,
                    iconRect.Top + iconPadding,
                    iconRect.Right - iconPadding,
                    iconRect.Bottom - iconPadding);
                canvas.DrawBitmap(iconBitmap, null, iconTarget, null);
                return;
            }

            var fallback = new AndroidPaint(Android.Graphics.PaintFlags.AntiAlias)
            {
                Color = AndroidColor.White,
                TextSize = 20f,
                TextAlign = AndroidPaint.Align.Center
            };
            var baseline = iconRect.CenterY() - ((fallback.Descent() + fallback.Ascent()) / 2f);
            canvas.DrawText("POI", iconRect.CenterX(), baseline, fallback);
        }

        private static string BuildCompactPoiName(string? name)
        {
            var trimmed = string.IsNullOrWhiteSpace(name) ? "POI" : name.Trim();
            return trimmed.Length <= 20 ? trimmed : $"{trimmed[..19]}...";
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AlkkagiUIEditor
{
    // The items' pictures on their HUD tiles (ItemIcons): a plain white sign
    // for each, drawn from circles, lines and outlines (distance fields, so
    // the edges are smooth at any size), all on one sheet in Resources, one
    // sprite each named after its ItemId. The tile tints it. A new item
    // needs its sign added here (Signs), then this run again.
    internal static class ItemIconBuilder
    {
        private const string SheetPath = "Assets/Resources/" + ItemIcons.Sheet + ".png";
        private const int Cell = 128;
        private const int Columns = 6;

        [MenuItem("Tools/Alkkagi UI/6. Build item icons")]
        public static void Build()
        {
            var ids = (ItemId[])Enum.GetValues(typeof(ItemId));
            var rows = (ids.Length + Columns - 1) / Columns;
            var sheet = new Texture2D(Cell * Columns, Cell * rows, TextureFormat.RGBA32, false);
            sheet.SetPixels32(new Color32[sheet.width * sheet.height]);
            var rects = new List<SpriteMetaData>();
            foreach (var id in ids)
            {
                if (!Signs.TryGetValue(id, out var sign))
                {
                    Debug.LogError("[Alkkagi] No icon drawn for " + id);
                    continue;
                }
                var i = (int)id;
                var x0 = i % Columns * Cell;
                var y0 = (rows - 1 - i / Columns) * Cell;
                const float pixel = 2f / Cell;
                for (var y = 0; y < Cell; y++)
                for (var x = 0; x < Cell; x++)
                {
                    var p = new Vector2((x + 0.5f) / Cell * 2 - 1, (y + 0.5f) / Cell * 2 - 1);
                    var cover = Mathf.Clamp01(0.5f - sign(p) / pixel);
                    sheet.SetPixel(x0 + x, y0 + y, new Color(1, 1, 1, cover));
                }
                rects.Add(new SpriteMetaData { name = id.ToString(), rect = new Rect(x0, y0, Cell, Cell), alignment = (int)SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f) });
            }
            sheet.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(SheetPath));
            File.WriteAllBytes(SheetPath, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
            AssetDatabase.ImportAsset(SheetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
#pragma warning disable 618 // the plain way to name a sheet's sprites
            importer.spritesheet = rects.ToArray();
#pragma warning restore 618
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            Debug.Log("[Alkkagi] Item icons built in " + SheetPath);
        }

        // ---- The signs: each a distance to its edge (negative inside), in a
        // square from -1 to 1, y up ----

        private static readonly Dictionary<ItemId, Func<Vector2, float>> Signs = new Dictionary<ItemId, Func<Vector2, float>>
        {
            // A weight: a block narrowing upward, a handle over it.
            [ItemId.Weight] = p => Min(Poly(p, (-0.62f, -0.7f), (0.62f, -0.7f), (0.42f, 0.12f), (-0.42f, 0.12f)), Ring(p, new Vector2(0, 0.38f), 0.26f, 0.08f)),
            // A ghost: round head, a wavy hem, two eyes out of it.
            [ItemId.Ghost] = p =>
            {
                var body = Min(Circle(p, new Vector2(0, 0.15f), 0.55f), Box(p, new Vector2(0, -0.3f), new Vector2(0.55f, 0.45f)));
                var hem = Min(Circle(p, new Vector2(-0.37f, -0.75f), 0.19f), Circle(p, new Vector2(0, -0.75f), 0.19f), Circle(p, new Vector2(0.37f, -0.75f), 0.19f));
                var eyes = Min(Circle(p, new Vector2(-0.2f, 0.2f), 0.1f), Circle(p, new Vector2(0.2f, 0.2f), 0.1f));
                return Cut(Cut(body, hem), eyes);
            },
            // An eye.
            [ItemId.Foresight] = p => Min(Cut(Lens(p, 0.82f, 0.5f), Lens(p, 0.66f, 0.36f)), Circle(p, Vector2.zero, 0.24f)),
            // A burst: a star of eight points.
            [ItemId.Blast] = p => Star(p, 8, 0.85f, 0.42f),
            // A curving arrow.
            [ItemId.Curve] = p => Min(Arc(p, new Vector2(0, -0.12f), 0.62f, 22, 200, 0.09f), Poly(p, (0.36f, 0.08f), (0.82f, 0.22f), (0.68f, -0.32f))),
            // Two chevrons: again.
            [ItemId.DoubleShot] = p => Min(Chevron(p, new Vector2(-0.3f, 0)), Chevron(p, new Vector2(0.3f, 0))),
            // An anchor.
            [ItemId.Anchor] = p => Min(Ring(p, new Vector2(0, 0.62f), 0.15f, 0.07f), Seg(p, new Vector2(0, 0.47f), new Vector2(0, -0.72f), 0.08f),
                Seg(p, new Vector2(-0.35f, 0.25f), new Vector2(0.35f, 0.25f), 0.08f), Arc(p, new Vector2(0, -0.12f), 0.6f, 200, 340, 0.08f),
                Poly(p, (-0.72f, -0.05f), (-0.42f, -0.12f), (-0.66f, -0.36f)), Poly(p, (0.72f, -0.05f), (0.66f, -0.36f), (0.42f, -0.12f))),
            // A shield.
            [ItemId.Shield] = p => Min(Cut(ShieldShape(p, 1f), ShieldShape(p, 0.76f)), ShieldShape(p, 0.5f)),
            // A drop.
            [ItemId.Glue] = p => Drop(p, new Vector2(0, -0.2f), 0.5f),
            // Corners pushed out: bigger.
            [ItemId.Grow] = p => Min(Circle(p, Vector2.zero, 0.26f), Corners(p, 0.82f, true)),
            // A drop sliding: lines behind it.
            [ItemId.Grease] = p => Min(Drop(p, new Vector2(0.25f, -0.2f), 0.4f), Seg(p, new Vector2(-0.85f, 0.05f), new Vector2(-0.35f, 0.05f), 0.07f), Seg(p, new Vector2(-0.75f, -0.3f), new Vector2(-0.3f, -0.3f), 0.07f), Seg(p, new Vector2(-0.85f, -0.65f), new Vector2(-0.35f, -0.65f), 0.07f)),
            // A snowflake.
            [ItemId.Freeze] = p => Flake(p),
            // Corners pulled in: smaller.
            [ItemId.Shrink] = p => Min(Circle(p, Vector2.zero, 0.18f), Corners(p, 0.82f, false)),
            // An eye struck through.
            [ItemId.Blind] = p => Min(Cut(Min(Cut(Lens(p, 0.82f, 0.5f), Lens(p, 0.66f, 0.36f)), Circle(p, Vector2.zero, 0.24f)), Seg(p, new Vector2(-0.75f, -0.75f), new Vector2(0.75f, 0.75f), 0.17f)), Seg(p, new Vector2(-0.7f, -0.7f), new Vector2(0.7f, 0.7f), 0.08f)),
            // A post: a column with a cap and a foot.
            [ItemId.Pillar] = p => Min(Cut(Cut(Box(p, new Vector2(0, 0), new Vector2(0.28f, 0.62f)), Box(p, new Vector2(-0.1f, 0), new Vector2(0.03f, 0.5f))), Box(p, new Vector2(0.1f, 0), new Vector2(0.03f, 0.5f))), Box(p, new Vector2(0, 0.7f), new Vector2(0.42f, 0.1f)), Box(p, new Vector2(0, -0.7f), new Vector2(0.42f, 0.1f))),
            // A crystal: a diamond with a glint cut in it.
            [ItemId.Ice] = p => Cut(Poly(p, (0, -0.85f), (0.6f, 0), (0, 0.85f), (-0.6f, 0)), Seg(p, new Vector2(-0.15f, 0.45f), new Vector2(-0.38f, 0.1f), 0.06f)),
            // The ground cracking: a zigzag over a line.
            [ItemId.Quake] = p => Min(Polyline(p, 0.08f, (-0.85f, 0.1f), (-0.5f, 0.55f), (-0.15f, -0.2f), (0.2f, 0.55f), (0.5f, -0.2f), (0.85f, 0.25f)), Seg(p, new Vector2(-0.8f, -0.65f), new Vector2(0.8f, -0.65f), 0.08f)),
            // Back: two triangles leftward.
            [ItemId.Rewind] = p => Min(Poly(p, (0f, 0.55f), (0f, -0.55f), (-0.8f, 0)), Poly(p, (0.75f, 0.55f), (0.75f, -0.55f), (-0.05f, 0))),
            // Back from below: a stone rising out of a line, an arrow over it.
            [ItemId.Revive] = p => Min(Circle(p, new Vector2(0, -0.15f), 0.28f), Seg(p, new Vector2(-0.8f, -0.68f), new Vector2(0.8f, -0.68f), 0.08f), Poly(p, (-0.35f, 0.38f), (0.35f, 0.38f), (0, 0.85f))),
            // Two arrows, each the other's way.
            [ItemId.Swap] = p => Min(Seg(p, new Vector2(-0.7f, 0.3f), new Vector2(0.4f, 0.3f), 0.08f), Poly(p, (0.3f, 0.6f), (0.85f, 0.3f), (0.3f, 0)),
                Seg(p, new Vector2(0.7f, -0.35f), new Vector2(-0.4f, -0.35f), 0.08f), Poly(p, (-0.3f, -0.05f), (-0.3f, -0.65f), (-0.85f, -0.35f))),
            // A cross.
            [ItemId.Heal] = p => Min(Box(p, Vector2.zero, new Vector2(0.24f, 0.72f)), Box(p, Vector2.zero, new Vector2(0.72f, 0.24f))),
            // A cloud.
            [ItemId.Fog] = p => Min(Circle(p, new Vector2(-0.38f, -0.1f), 0.32f), Circle(p, new Vector2(0.05f, 0.15f), 0.42f), Circle(p, new Vector2(0.45f, -0.12f), 0.3f), Box(p, new Vector2(0.02f, -0.3f), new Vector2(0.68f, 0.12f))),
            // A hole: a dark middle, a rim round it.
            [ItemId.Sinkhole] = p => Min(Ellipse(p, Vector2.zero, new Vector2(0.48f, 0.26f)), Cut(Ellipse(p, Vector2.zero, new Vector2(0.85f, 0.5f)), Ellipse(p, Vector2.zero, new Vector2(0.68f, 0.38f)))),
            // A blob dripping.
            [ItemId.Tar] = p => Min(Box(p, new Vector2(0, 0.25f), new Vector2(0.72f, 0.28f), 0.2f), Drop(p, new Vector2(-0.38f, -0.25f), 0.17f), Drop(p, new Vector2(0.12f, -0.38f), 0.2f), Drop(p, new Vector2(0.52f, -0.18f), 0.13f)),
            // A pad's arrow: three chevrons up.
            [ItemId.Boost] = p => Min(UpChevron(p, new Vector2(0, -0.5f)), UpChevron(p, new Vector2(0, 0)), UpChevron(p, new Vector2(0, 0.5f))),
            // A spiral.
            [ItemId.Vortex] = p => Spiral(p),
            // A post with a knock bouncing off: a disc and two arcs.
            [ItemId.Bumper] = p => Min(Circle(p, Vector2.zero, 0.34f), Arc(p, Vector2.zero, 0.6f, -50, 50, 0.07f), Arc(p, Vector2.zero, 0.6f, 130, 230, 0.07f), Arc(p, Vector2.zero, 0.84f, -40, 40, 0.06f), Arc(p, Vector2.zero, 0.84f, 140, 220, 0.06f)),
        };

        // ---- Shapes ----

        private static float Min(params float[] d) => d.Min();
        private static float Cut(float d, float hole) => Mathf.Max(d, -hole);

        private static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;
        private static float Ring(Vector2 p, Vector2 c, float r, float w) => Mathf.Abs((p - c).magnitude - r) - w;

        private static float Ellipse(Vector2 p, Vector2 c, Vector2 r)
        {
            // Near enough for signs this size: the circle's distance, scaled by the smaller radius.
            var q = new Vector2((p.x - c.x) / r.x, (p.y - c.y) / r.y);
            return (q.magnitude - 1) * Mathf.Min(r.x, r.y);
        }

        private static float Box(Vector2 p, Vector2 c, Vector2 half, float round = 0)
        {
            var d = new Vector2(Mathf.Abs(p.x - c.x) - half.x + round, Mathf.Abs(p.y - c.y) - half.y + round);
            return Vector2.Max(d, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0) - round;
        }

        private static float Seg(Vector2 p, Vector2 a, Vector2 b, float w)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude - w;
        }

        private static float Polyline(Vector2 p, float w, params (float x, float y)[] points)
        {
            var d = float.MaxValue;
            for (var i = 0; i + 1 < points.Length; i++)
                d = Mathf.Min(d, Seg(p, new Vector2(points[i].x, points[i].y), new Vector2(points[i + 1].x, points[i + 1].y), w));
            return d;
        }

        // A filled polygon (any winding).
        private static float Poly(Vector2 p, params (float x, float y)[] corners)
        {
            var v = corners.Select(c => new Vector2(c.x, c.y)).ToArray();
            var d = float.MaxValue;
            var inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                d = Mathf.Min(d, Seg(p, v[j], v[i], 0));
                if ((v[i].y > p.y) != (v[j].y > p.y) && p.x < (v[j].x - v[i].x) * (p.y - v[i].y) / (v[j].y - v[i].y) + v[i].x) inside = !inside;
            }
            return inside ? -d : d;
        }

        // Part of a ring, from one angle to another (degrees, anticlockwise from +x).
        private static float Arc(Vector2 p, Vector2 c, float r, float from, float to, float w)
        {
            var q = p - c;
            var angle = Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg;
            var middle = (from + to) / 2;
            if (Mathf.Abs(Mathf.DeltaAngle(middle, angle)) <= (to - from) / 2) return Mathf.Abs(q.magnitude - r) - w;
            Vector2 End(float a) => c + new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * r;
            return Mathf.Min((p - End(from)).magnitude, (p - End(to)).magnitude) - w;
        }

        // An almond (two circles' overlap), half width and half height.
        private static float Lens(Vector2 p, float halfWidth, float halfHeight)
        {
            var r = (halfWidth * halfWidth + halfHeight * halfHeight) / (2 * halfHeight);
            return Mathf.Max(Circle(p, new Vector2(0, halfHeight - r), r), Circle(p, new Vector2(0, r - halfHeight), r));
        }

        private static float Star(Vector2 p, int points, float outer, float inner)
        {
            var corners = Enumerable.Range(0, points * 2).Select(i =>
            {
                var a = (90 + 180f * i / points) * Mathf.Deg2Rad;
                var r = i % 2 == 0 ? outer : inner;
                return (Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }).ToArray();
            return Poly(p, corners);
        }

        private static float Chevron(Vector2 p, Vector2 c) =>
            Polyline(p, 0.11f, (c.x - 0.22f, c.y + 0.5f), (c.x + 0.22f, c.y), (c.x - 0.22f, c.y - 0.5f));

        private static float UpChevron(Vector2 p, Vector2 c) =>
            Polyline(p, 0.1f, (c.x - 0.55f, c.y - 0.2f), (c.x, c.y + 0.22f), (c.x + 0.55f, c.y - 0.2f));

        // A drop: round below, pointed above.
        private static float Drop(Vector2 p, Vector2 c, float r) =>
            Min(Circle(p, c, r), Poly(p, (c.x - r * 0.92f, c.y + r * 0.38f), (c.x + r * 0.92f, c.y + r * 0.38f), (c.x, c.y + r * 2.1f)));

        private static float ShieldShape(Vector2 p, float s) =>
            Poly(p, (-0.66f * s, 0.72f * s), (0.66f * s, 0.72f * s), (0.66f * s, 0.05f * s), (0, -0.85f * s), (-0.66f * s, 0.05f * s));

        // Four corner marks, pointing out (or in).
        private static float Corners(Vector2 p, float at, bool outward)
        {
            var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
            var tip = outward ? new Vector2(at, at) : new Vector2(at - 0.42f, at - 0.42f);
            var step = outward ? -0.32f : 0.32f;
            var a = Seg(q, tip, tip + new Vector2(step, 0), 0.08f);
            var b = Seg(q, tip, tip + new Vector2(0, step), 0.08f);
            var c = Seg(q, new Vector2(at - 0.42f, at - 0.42f) + Vector2.one * 0.06f, new Vector2(at, at) - Vector2.one * 0.06f, 0.07f);
            return Min(a, b, c);
        }

        private static float Flake(Vector2 p)
        {
            var d = float.MaxValue;
            for (var i = 0; i < 6; i++)
            {
                var a = i * 60 * Mathf.Deg2Rad;
                var way = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var side = new Vector2(-way.y, way.x);
                d = Mathf.Min(d, Seg(p, Vector2.zero, way * 0.85f, 0.07f));
                d = Mathf.Min(d, Seg(p, way * 0.5f, way * 0.72f + side * 0.2f, 0.06f));
                d = Mathf.Min(d, Seg(p, way * 0.5f, way * 0.72f - side * 0.2f, 0.06f));
            }
            return d;
        }

        private static float Spiral(Vector2 p)
        {
            var points = new List<(float, float)>();
            for (var t = 0f; t <= 2.6f; t += 0.02f)
            {
                var a = t * Mathf.PI * 2;
                var r = 0.08f + t * 0.3f;
                points.Add((Mathf.Cos(a) * r, Mathf.Sin(a) * r));
            }
            return Polyline(p, 0.08f, points.ToArray());
        }
    }
}

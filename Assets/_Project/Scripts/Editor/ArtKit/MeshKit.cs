using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScrapYardKing.EditorTools.Art
{
    /// <summary>
    /// Small procedural modeling kit for the stylized "toy industrial" look: rounded boxes, lathe shapes (cylinders,
    /// cones, domes, tires, barrels), tori, bevelled prisms and flat quads, accumulated into one mesh with one submesh per
    /// material slot. Shapes are placed with a transform stack (<see cref="Push"/>/<see cref="Pop"/>), wound automatically,
    /// and get box-projected UVs in metres so tiling textures read at a constant scale.
    /// </summary>
    public sealed class MeshKit
    {
        readonly List<Vector3> verts = new();
        readonly List<Vector3> normals = new();
        readonly List<Vector2> uvs = new();
        readonly List<List<int>> slots = new();
        readonly List<int> vertSlot = new();
        int currentSlot;
        readonly Stack<Matrix4x4> stack = new();
        Matrix4x4 matrix = Matrix4x4.identity;

        /// <summary>UV units per metre.</summary>
        public float UvScale { get; set; } = 1f;

        public bool IsEmpty => verts.Count == 0;
        public int SlotCount => slots.Count;

        // ---------- transform stack ----------

        public MeshKit Push(Vector3 position, Vector3 euler = default, Vector3? scale = null)
        {
            stack.Push(matrix);
            matrix *= Matrix4x4.TRS(position, Quaternion.Euler(euler), scale ?? Vector3.one);
            return this;
        }

        public MeshKit Push(Matrix4x4 m)
        {
            stack.Push(matrix);
            matrix *= m;
            return this;
        }

        public MeshKit Pop()
        {
            matrix = stack.Pop();
            return this;
        }

        // ---------- primitives ----------

        /// <summary>Rounded box. <paramref name="bevel"/> is the corner radius; <paramref name="segments"/> rounds it (1 = soft chamfer).</summary>
        public MeshKit Box(int slot, Vector3 center, Vector3 size, float bevel = 0.04f, Vector3 euler = default, int segments = 1)
        {
            currentSlot = slot;
            var m = matrix * Matrix4x4.TRS(center, Quaternion.Euler(euler), Vector3.one);
            var nm = m.inverse.transpose;
            Vector3 h = size * 0.5f;
            float b = Mathf.Max(0f, Mathf.Min(bevel, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.98f));
            Vector3 inner = h - Vector3.one * b;
            // Tiny bevels are invisible at game distance: plain 12-triangle box. Single-segment bevels use a proper
            // chamfer (6 faces + 12 edge strips + 8 corners = 44 triangles) with smooth edge normals.
            if (b < MinBevel) b = 0f;
            segments = Mathf.Min(segments, MaxSegments);
            if (b <= 0f || segments <= 1)
            {
                Chamfer(slot, m, nm, h, b);
                return this;
            }

            float[][] coords = { Coords(h.x, inner.x, b, segments), Coords(h.y, inner.y, b, segments), Coords(h.z, inner.z, b, segments) };

            for (int axis = 0; axis < 3; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int u = (axis + 1) % 3, w = (axis + 2) % 3;
                var cu = coords[u];
                var cw = coords[w];
                int start = verts.Count;
                for (int j = 0; j < cw.Length; j++)
                for (int i = 0; i < cu.Length; i++)
                {
                    var p = Vector3.zero;
                    p[axis] = sign * h[axis];
                    p[u] = cu[i];
                    p[w] = cw[j];
                    var clamped = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                    Vector3 d = p - clamped;
                    Vector3 normal, pos;
                    if (b <= 0f || d.sqrMagnitude < 1e-10f)
                    {
                        normal = Vector3.zero;
                        normal[axis] = sign;
                        pos = p;
                    }
                    else
                    {
                        normal = d.normalized;
                        pos = clamped + normal * b;
                    }

                    AddVertex(m, nm, pos, normal, new Vector2(cu[i], cw[j]) * UvScale);
                }

                for (int j = 0; j < cw.Length - 1; j++)
                for (int i = 0; i < cu.Length - 1; i++)
                {
                    int a = start + j * cu.Length + i;
                    Quad(slot, a, a + 1, a + cu.Length + 1, a + cu.Length);
                }
            }

            return this;
        }

        /// <summary>Bevels smaller than this become sharp edges (saves triangles on small details).</summary>
        public static float MinBevel = 0.012f;

        /// <summary>Cap on rounding segments (mobile budget): 1 = chamfer with smooth normals.</summary>
        public static int MaxSegments = 1;

        /// <summary>Scales the side count of round shapes with more than 8 sides (lower = cheaper).</summary>
        public static float RoundDetail = 1f;

        static int Sides(int sides) => sides <= 8 ? sides : Mathf.Max(8, Mathf.RoundToInt(sides * RoundDetail));

        void Chamfer(int slot, Matrix4x4 m, Matrix4x4 nm, Vector3 h, float b)
        {
            Vector3 i = h - Vector3.one * b;
            // Vertex on face (axis, sign) at tangential position given by signs su/sw on the two other axes.
            Vector3 FacePoint(int axis, int sign, int su, int sw)
            {
                int u = (axis + 1) % 3, w = (axis + 2) % 3;
                var p = Vector3.zero;
                p[axis] = sign * h[axis];
                p[u] = su * i[u];
                p[w] = sw * i[w];
                return p;
            }

            Vector2 Uv(int axis, Vector3 p)
            {
                int u = (axis + 1) % 3, w = (axis + 2) % 3;
                return new Vector2(p[u], p[w]) * UvScale;
            }

            // Face corners are shared by the face quad, two edge strips and a corner triangle (same position, normal and
            // UV), so a chamfered box needs only 24 vertices.
            var cache = new Dictionary<int, int>();
            int V(int axis, int sign, int su, int sw)
            {
                int key = axis * 27 + (sign + 1) * 9 + (su + 1) * 3 + (sw + 1);
                if (cache.TryGetValue(key, out int index)) return index;
                var p = FacePoint(axis, sign, su, sw);
                var n = Vector3.zero;
                n[axis] = sign;
                index = AddVertex(m, nm, p, n, Uv(axis, p));
                cache[key] = index;
                return index;
            }

            // Faces.
            for (int axis = 0; axis < 3; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
                Quad(slot, V(axis, sign, -1, -1), V(axis, sign, 1, -1), V(axis, sign, 1, 1), V(axis, sign, -1, 1));
            if (b <= 0f) return;

            // Edge strips between faces A (axis a) and B (axis c), running along the third axis.
            for (int a = 0; a < 3; a++)
            for (int c = a + 1; c < 3; c++)
            {
                int run = 3 - a - c;
                for (int sa = -1; sa <= 1; sa += 2)
                for (int sc = -1; sc <= 1; sc += 2)
                {
                    int Corner(int faceAxis, int faceSign, int otherAxis, int otherSign, int runSign)
                    {
                        int u = (faceAxis + 1) % 3;
                        int su = u == otherAxis ? otherSign : runSign;
                        int sw = u == otherAxis ? runSign : otherSign;
                        return V(faceAxis, faceSign, su, sw);
                    }

                    int a0 = Corner(a, sa, c, sc, -1), a1 = Corner(a, sa, c, sc, 1);
                    int c0 = Corner(c, sc, a, sa, -1), c1 = Corner(c, sc, a, sa, 1);
                    Quad(slot, a0, a1, c1, c0);
                    _ = run;
                }
            }

            // Corner triangles.
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var signs = new[] { sx, sy, sz };
                int Cv(int axis)
                {
                    int u = (axis + 1) % 3, w = (axis + 2) % 3;
                    return V(axis, signs[axis], signs[u], signs[w]);
                }

                Tri(slot, Cv(0), Cv(1), Cv(2));
            }
        }

        static float[] Coords(float h, float inner, float b, int segments)
        {
            if (b <= 0f) return new[] { -h, h };
            var list = new List<float> { -h };
            segments = Mathf.Max(1, segments);
            for (int k = segments - 1; k >= 1; k--) list.Add(-(inner + b * Mathf.Tan(Mathf.PI * 0.25f * k / segments)));
            list.Add(-inner);
            list.Add(inner);
            for (int k = 1; k <= segments - 1; k++) list.Add(inner + b * Mathf.Tan(Mathf.PI * 0.25f * k / segments));
            list.Add(h);
            return list.ToArray();
        }

        /// <summary>
        /// Revolves a profile (x = radius, y = height) around the local Y axis. Corners sharper than
        /// <paramref name="hardAngle"/> degrees get split normals; open ends at radius &gt; 0 get flat caps.
        /// </summary>
        public MeshKit Lathe(int slot, Vector3 center, Vector2[] profile, int sides = 16, Vector3 euler = default, bool caps = true, float hardAngle = 50f,
            bool closed = false, float arcDegrees = 360f, float startDegrees = 0f, bool faceted = false)
        {
            // Low side counts (square hoppers, hex nuts) read as flat panels, not as a smooth cone.
            faceted |= sides <= 6;
            currentSlot = slot;
            sides = Sides(sides);
            var m = matrix * Matrix4x4.TRS(center, Quaternion.Euler(euler), Vector3.one);
            var nm = m.inverse.transpose;
            int count = profile.Length;
            int segCount = closed ? count : count - 1;
            bool fullCircle = arcDegrees >= 359.9f;
            int ringVerts = fullCircle ? sides + 1 : sides + 1;

            // Segment normals in (r, y) space, then one or two vertex normals per profile point (split on hard corners).
            var segNormal = new Vector2[segCount];
            for (int s = 0; s < segCount; s++)
            {
                Vector2 a = profile[s], b = profile[(s + 1) % count];
                Vector2 dir = (b - a).normalized;
                segNormal[s] = new Vector2(dir.y, -dir.x);
            }

            float perimeter = 0f;
            var along = new float[count + 1];
            for (int s = 0; s < segCount; s++)
            {
                along[s + 1] = along[s] + Vector2.Distance(profile[s], profile[(s + 1) % count]);
                perimeter = along[s + 1];
            }

            for (int s = 0; s < segCount; s++)
            {
                int i0 = s, i1 = (s + 1) % count;
                Vector2 n0 = PointNormal(i0, s, true), n1 = PointNormal(i1, s, false);
                if (faceted)
                {
                    for (int k = 0; k < sides; k++)
                    {
                        float a0 = (startDegrees + arcDegrees * k / sides) * Mathf.Deg2Rad, a1 = (startDegrees + arcDegrees * (k + 1) / sides) * Mathf.Deg2Rad;
                        float am = (a0 + a1) * 0.5f;
                        var fn = new Vector3(segNormal[s].x * Mathf.Cos(am), segNormal[s].y, segNormal[s].x * Mathf.Sin(am));
                        int f0 = AddVertex(m, nm, new Vector3(profile[i0].x * Mathf.Cos(a0), profile[i0].y, profile[i0].x * Mathf.Sin(a0)), fn,
                            new Vector2(a0 * profile[i0].x, along[s]) * UvScale);
                        int f1 = AddVertex(m, nm, new Vector3(profile[i1].x * Mathf.Cos(a0), profile[i1].y, profile[i1].x * Mathf.Sin(a0)), fn,
                            new Vector2(a0 * profile[i1].x, along[s + 1]) * UvScale);
                        int f2 = AddVertex(m, nm, new Vector3(profile[i1].x * Mathf.Cos(a1), profile[i1].y, profile[i1].x * Mathf.Sin(a1)), fn,
                            new Vector2(a1 * profile[i1].x, along[s + 1]) * UvScale);
                        int f3 = AddVertex(m, nm, new Vector3(profile[i0].x * Mathf.Cos(a1), profile[i0].y, profile[i0].x * Mathf.Sin(a1)), fn,
                            new Vector2(a1 * profile[i0].x, along[s]) * UvScale);
                        Quad(slot, f0, f1, f2, f3);
                    }

                    continue;
                }

                int start = verts.Count;
                for (int k = 0; k < ringVerts; k++)
                {
                    float ang = (startDegrees + arcDegrees * k / sides) * Mathf.Deg2Rad;
                    float c = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                    float uu = arcDegrees * Mathf.Deg2Rad * k / sides;
                    AddVertex(m, nm, new Vector3(profile[i0].x * c, profile[i0].y, profile[i0].x * sn), new Vector3(n0.x * c, n0.y, n0.x * sn),
                        new Vector2(uu * Mathf.Max(0.2f, profile[i0].x), along[s]) * UvScale);
                    AddVertex(m, nm, new Vector3(profile[i1].x * c, profile[i1].y, profile[i1].x * sn), new Vector3(n1.x * c, n1.y, n1.x * sn),
                        new Vector2(uu * Mathf.Max(0.2f, profile[i1].x), along[s + 1]) * UvScale);
                }

                for (int k = 0; k < sides; k++)
                {
                    int a = start + k * 2;
                    Quad(slot, a, a + 1, a + 3, a + 2);
                }
            }

            if (caps && !closed)
            {
                if (profile[0].x > 1e-4f) Cap(slot, m, nm, profile[0], -Mathf.Sign(profile[1].y - profile[0].y + 1e-6f), sides, arcDegrees, startDegrees);
                if (profile[count - 1].x > 1e-4f)
                    Cap(slot, m, nm, profile[count - 1], Mathf.Sign(profile[count - 1].y - profile[count - 2].y + 1e-6f), sides, arcDegrees, startDegrees);
            }

            return this;

            Vector2 PointNormal(int point, int seg, bool isStart)
            {
                int prevSeg = isStart ? seg - 1 : seg;
                int nextSeg = isStart ? seg : seg + 1;
                if (closed)
                {
                    prevSeg = (prevSeg + segCount) % segCount;
                    nextSeg %= segCount;
                }

                Vector2 own = segNormal[seg];
                int other = isStart ? prevSeg : nextSeg;
                if (other < 0 || other >= segCount) return own;
                Vector2 o = segNormal[other];
                if (Vector2.Angle(own, o) > hardAngle) return own;
                return (own + o).normalized;
            }
        }

        void Cap(int slot, Matrix4x4 m, Matrix4x4 nm, Vector2 rim, float up, int sides, float arcDegrees, float startDegrees)
        {
            int center = AddVertex(m, nm, new Vector3(0f, rim.y, 0f), new Vector3(0f, up, 0f), Vector2.zero);
            int start = verts.Count;
            for (int k = 0; k <= sides; k++)
            {
                float ang = (startDegrees + arcDegrees * k / sides) * Mathf.Deg2Rad;
                var p = new Vector3(rim.x * Mathf.Cos(ang), rim.y, rim.x * Mathf.Sin(ang));
                AddVertex(m, nm, p, new Vector3(0f, up, 0f), new Vector2(p.x, p.z) * UvScale);
            }

            for (int k = 0; k < sides; k++) Tri(slot, center, start + k, start + k + 1);
        }

        /// <summary>Cylinder along local Y with optional soft bevel and a different top radius (cone / frustum).</summary>
        public MeshKit Cylinder(int slot, Vector3 center, float radius, float height, int sides = 16, Vector3 euler = default, float bevel = 0.02f,
            float topRadius = -1f)
        {
            float rt = topRadius < 0f ? radius : topRadius;
            float hh = height * 0.5f;
            float b = Mathf.Min(bevel, Mathf.Min(radius, rt) * 0.5f, hh * 0.9f);
            Vector2[] profile = b > 0f
                ? new[] { new Vector2(radius - b, -hh), new Vector2(radius, -hh + b), new Vector2(rt, hh - b), new Vector2(rt - b, hh) }
                : new[] { new Vector2(radius, -hh), new Vector2(rt, hh) };
            return Lathe(slot, center, profile, sides, euler, true, 60f);
        }

        /// <summary>Ellipsoid (smooth). <paramref name="radii"/> per axis.</summary>
        public MeshKit Sphere(int slot, Vector3 center, Vector3 radii, int sides = 16, int rings = 10, Vector3 euler = default)
        {
            rings = rings <= 6 ? rings : Mathf.Max(6, Mathf.RoundToInt(rings * RoundDetail));
            var profile = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * i / rings - Mathf.PI * 0.5f;
                profile[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }

            Push(center, euler, radii);
            Lathe(slot, Vector3.zero, profile, sides, default, false, 90f);
            Pop();
            return this;
        }

        /// <summary>Upper half of an ellipsoid (helmets, domes, hair caps).</summary>
        public MeshKit Dome(int slot, Vector3 center, Vector3 radii, int sides = 18, int rings = 6, Vector3 euler = default, bool cap = true,
            float fromDegrees = 0f, float toDegrees = 90f, float arcDegrees = 360f, float startDegrees = 0f)
        {
            var profile = new Vector2[rings + 1];
            float from = fromDegrees * Mathf.Deg2Rad;
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.Lerp(from, toDegrees * Mathf.Deg2Rad, i / (float)rings);
                profile[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }

            Push(center, euler, radii);
            Lathe(slot, Vector3.zero, profile, sides, default, cap, 90f, false, arcDegrees, startDegrees);
            Pop();
            return this;
        }

        /// <summary>Torus in the local XZ plane: <paramref name="ring"/> radius to the tube centre, <paramref name="tube"/> radius.</summary>
        public MeshKit Torus(int slot, Vector3 center, float ring, float tube, int sides = 20, int tubeSides = 8, Vector3 euler = default,
            Vector2? tubeScale = null, float arcDegrees = 360f, float startDegrees = 0f)
        {
            var s = tubeScale ?? Vector2.one;
            var profile = new Vector2[tubeSides];
            for (int i = 0; i < tubeSides; i++)
            {
                float a = 2f * Mathf.PI * i / tubeSides;
                profile[i] = new Vector2(ring + Mathf.Cos(a) * tube * s.x, Mathf.Sin(a) * tube * s.y);
            }

            return Lathe(slot, center, profile, sides, euler, false, 70f, true, arcDegrees, startDegrees);
        }

        /// <summary>
        /// Prism: a convex outline in the local YZ plane (x of each point = Z / forward, y = up) extruded along X by
        /// <paramref name="width"/>, with a chamfer of <paramref name="bevel"/> on both side edges. Car bodies, wedges, ramps.
        /// </summary>
        public MeshKit Prism(int slot, Vector3 center, Vector2[] outline, float width, float bevel = 0.03f, Vector3 euler = default)
        {
            currentSlot = slot;
            var m = matrix * Matrix4x4.TRS(center, Quaternion.Euler(euler), Vector3.one);
            var nm = m.inverse.transpose;
            int n = outline.Length;
            float hw = width * 0.5f;
            float b = Mathf.Min(bevel, hw * 0.9f);
            var inset = Inset(outline, b);

            // Side band: each outline edge as a flat quad from -hw+b to hw-b.
            for (int i = 0; i < n; i++)
            {
                Vector2 a = outline[i], c = outline[(i + 1) % n];
                Vector2 dir = (c - a).normalized;
                var normal = new Vector3(0f, -dir.x, dir.y);
                // Orientation is fixed by winding below; the normal sign follows the outline order.
                float len = Vector2.Distance(a, c);
                int s0 = AddVertex(m, nm, new Vector3(-hw + b, a.y, a.x), normal, new Vector2(-hw + b, 0f) * UvScale);
                int s1 = AddVertex(m, nm, new Vector3(-hw + b, c.y, c.x), normal, new Vector2(-hw + b, len) * UvScale);
                int s2 = AddVertex(m, nm, new Vector3(hw - b, c.y, c.x), normal, new Vector2(hw - b, len) * UvScale);
                int s3 = AddVertex(m, nm, new Vector3(hw - b, a.y, a.x), normal, new Vector2(hw - b, 0f) * UvScale);
                FixOutward(s0, s1, s2, s3, a, c, outline);
                Quad(slot, s0, s1, s2, s3);
            }

            // Chamfer bands and caps on both sides.
            for (int side = -1; side <= 1; side += 2)
            {
                float xo = side * (hw - b), xi = side * hw;
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = outline[i], c = outline[(i + 1) % n], ai = inset[i], ci = inset[(i + 1) % n];
                    Vector2 dir = (c - a).normalized;
                    var outward = OutwardNormal2D(a, c, outline);
                    var normal = (new Vector3(0f, outward.y, outward.x) + new Vector3(side, 0f, 0f)).normalized;
                    int q0 = AddVertex(m, nm, new Vector3(xo, a.y, a.x), normal, new Vector2(a.x, a.y) * UvScale);
                    int q1 = AddVertex(m, nm, new Vector3(xo, c.y, c.x), normal, new Vector2(c.x, c.y) * UvScale);
                    int q2 = AddVertex(m, nm, new Vector3(xi, ci.y, ci.x), normal, new Vector2(ci.x, ci.y) * UvScale);
                    int q3 = AddVertex(m, nm, new Vector3(xi, ai.y, ai.x), normal, new Vector2(ai.x, ai.y) * UvScale);
                    Quad(slot, q0, q1, q2, q3);
                    _ = dir;
                }

                var capNormal = new Vector3(side, 0f, 0f);
                int start = verts.Count;
                for (int i = 0; i < n; i++) AddVertex(m, nm, new Vector3(xi, inset[i].y, inset[i].x), capNormal, new Vector2(inset[i].x, inset[i].y) * UvScale);
                for (int i = 1; i < n - 1; i++) Tri(slot, start, start + i, start + i + 1);
            }

            return this;
        }

        static Vector2 OutwardNormal2D(Vector2 a, Vector2 c, Vector2[] outline)
        {
            Vector2 dir = (c - a).normalized;
            var normal = new Vector2(dir.y, -dir.x);
            Vector2 centroid = Vector2.zero;
            foreach (var p in outline) centroid += p;
            centroid /= outline.Length;
            if (Vector2.Dot(normal, (a + c) * 0.5f - centroid) < 0f) normal = -normal;
            return normal;
        }

        void FixOutward(int s0, int s1, int s2, int s3, Vector2 a, Vector2 c, Vector2[] outline)
        {
            var o = OutwardNormal2D(a, c, outline);
            var local = new Vector3(0f, o.y, o.x);
            // Recompute the stored normals in world space from the corrected local normal.
            var nm = normalsMatrix;
            var world = nm.MultiplyVector(local).normalized;
            normals[s0] = normals[s1] = normals[s2] = normals[s3] = world;
        }

        Matrix4x4 normalsMatrix = Matrix4x4.identity;

        static Vector2[] Inset(Vector2[] outline, float d)
        {
            int n = outline.Length;
            var result = new Vector2[n];
            Vector2 centroid = Vector2.zero;
            foreach (var p in outline) centroid += p;
            centroid /= n;
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = outline[(i - 1 + n) % n], cur = outline[i], next = outline[(i + 1) % n];
                Vector2 n0 = OutwardNormal2D(prev, cur, outline), n1 = OutwardNormal2D(cur, next, outline);
                Vector2 bis = (n0 + n1).normalized;
                float cos = Mathf.Max(0.3f, Vector2.Dot(bis, n0));
                result[i] = cur - bis * (d / cos);
            }

            return result;
        }

        /// <summary>Flat quad facing local +Y (decals, stripes, signs). UVs 0..1 unless <paramref name="tiled"/>.</summary>
        public MeshKit Plane(int slot, Vector3 center, Vector2 size, Vector3 euler = default, bool tiled = false, Rect? uvRect = null)
        {
            currentSlot = slot;
            var m = matrix * Matrix4x4.TRS(center, Quaternion.Euler(euler), Vector3.one);
            var nm = m.inverse.transpose;
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            var r = uvRect ?? new Rect(0f, 0f, 1f, 1f);
            Vector2 U(float u, float v) => tiled ? new Vector2(u * size.x, v * size.y) * UvScale : new Vector2(r.x + u * r.width, r.y + v * r.height);
            int a = AddVertex(m, nm, new Vector3(-hx, 0f, -hz), Vector3.up, U(0f, 0f));
            int b = AddVertex(m, nm, new Vector3(-hx, 0f, hz), Vector3.up, U(0f, 1f));
            int c = AddVertex(m, nm, new Vector3(hx, 0f, hz), Vector3.up, U(1f, 1f));
            int d = AddVertex(m, nm, new Vector3(hx, 0f, -hz), Vector3.up, U(1f, 0f));
            Quad(slot, a, b, c, d);
            return this;
        }

        /// <summary>Adds another kit's geometry (slot for slot) under the current transform.</summary>
        public MeshKit Append(MeshKit other)
        {
            var m = matrix;
            var nm = m.inverse.transpose;
            int offset = verts.Count;
            for (int i = 0; i < other.verts.Count; i++)
            {
                currentSlot = other.vertSlot[i];
                AddVertex(m, nm, other.verts[i], other.normals[i], other.uvs[i]);
            }
            for (int s = 0; s < other.slots.Count; s++)
            {
                var list = Slot(s);
                foreach (int idx in other.slots[s]) list.Add(idx + offset);
            }

            return this;
        }

        // ---------- building blocks ----------

        int AddVertex(Matrix4x4 m, Matrix4x4 nm, Vector3 p, Vector3 n, Vector2 uv)
        {
            normalsMatrix = nm;
            vertSlot.Add(currentSlot);
            verts.Add(m.MultiplyPoint3x4(p));
            var wn = nm.MultiplyVector(n);
            normals.Add(wn.sqrMagnitude > 1e-12f ? wn.normalized : Vector3.up);
            uvs.Add(uv);
            return verts.Count - 1;
        }

        List<int> Slot(int slot)
        {
            while (slots.Count <= slot) slots.Add(new List<int>());
            return slots[slot];
        }

        void Quad(int slot, int a, int b, int c, int d)
        {
            Tri(slot, a, b, c);
            Tri(slot, a, c, d);
        }

        /// <summary>Adds a triangle wound so its face points along the averaged vertex normals (Unity: clockwise = front).</summary>
        void Tri(int slot, int a, int b, int c)
        {
            Vector3 pa = verts[a], pb = verts[b], pc = verts[c];
            Vector3 face = Vector3.Cross(pb - pa, pc - pa);
            if (face.sqrMagnitude < 1e-14f) return;
            Vector3 avg = normals[a] + normals[b] + normals[c];
            var list = Slot(slot);
            if (Vector3.Dot(face, avg) >= 0f)
            {
                list.Add(a);
                list.Add(b);
                list.Add(c);
            }
            else
            {
                list.Add(a);
                list.Add(c);
                list.Add(b);
            }
        }

        // ---------- output ----------

        /// <summary>Builds a Unity mesh; <paramref name="slotCount"/> forces a submesh count (to match a material array).</summary>
        public Mesh ToMesh(string name, int slotCount = -1)
        {
            int count = Mathf.Max(slotCount, slots.Count);
            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = Mathf.Max(1, count);
            for (int s = 0; s < count; s++) mesh.SetTriangles(s < slots.Count ? slots[s] : new List<int>(), s, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Builds a mesh with only the slots that hold triangles, in slot order. <paramref name="used"/> lists the
        /// original slot of each submesh, so a material table can be mapped onto it.
        /// </summary>
        public Mesh ToMeshCompact(string name, out List<int> used)
        {
            used = new List<int>();
            for (int s = 0; s < slots.Count; s++)
                if (slots[s].Count > 0) used.Add(s);
            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = Mathf.Max(1, used.Count);
            for (int i = 0; i < used.Count; i++) mesh.SetTriangles(slots[used[i]], i, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Single-submesh mesh for a palette-atlas material: every vertex gets the UV of its slot's colour cell
        /// (<see cref="ArtPalette"/>), so one material draws a multi-coloured model and props batch together.
        /// </summary>
        public Mesh ToPaletteMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            var paletteUv = new List<Vector2>(verts.Count);
            foreach (int s in vertSlot) paletteUv.Add(ArtPalette.Uv(s));
            mesh.SetUVs(0, paletteUv);
            var all = new List<int>();
            foreach (var list in slots) all.AddRange(list);
            mesh.SetTriangles(all, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Bounds of what has been added so far (in kit space).</summary>
        public Bounds Bounds
        {
            get
            {
                if (verts.Count == 0) return default;
                var b = new Bounds(verts[0], Vector3.zero);
                foreach (var v in verts) b.Encapsulate(v);
                return b;
            }
        }
    }
}

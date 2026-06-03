using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ImageRelief
{
    public static class ReliefMeshBuilder
    {
        // Builds an aspect-correct, +Z-displaced grid mesh from the height field.
        // Longer image axis maps to worldSize and resolution cells; shorter axis scales proportionally.
        public static Mesh Build(ReliefSettings s, float[,] heights, int hw, int hh,
                                 string name = "ReliefMesh")
        {
            // Aspect-correct cell counts
            int cellsX, cellsY;
            float extX, extY;
            float halfSize = s.worldSize * 0.5f;

            float imgAspect = (hh > 0 && hw > 0) ? (float)hw / hh : 1f;

            if (hw >= hh)
            {
                cellsX = s.resolution;
                cellsY = Mathf.Max(1, Mathf.RoundToInt(s.resolution / imgAspect));
                extX = halfSize;
                extY = halfSize / imgAspect;
            }
            else
            {
                cellsY = s.resolution;
                cellsX = Mathf.Max(1, Mathf.RoundToInt(s.resolution * imgAspect));
                extY = halfSize;
                extX = halfSize * imgAspect;
            }

            int vertsX = cellsX + 1;
            int vertsY = cellsY + 1;
            int frontCount = vertsX * vertsY;

            // Border loop length for skirt (perimeter vertices, no corners duplicated)
            int borderLen = s.addBase ? 2 * (cellsX + cellsY) : 0;
            int sideCount = s.addBase ? borderLen * 2 : 0; // front-edge + back copy per border vert
            int backCount  = s.addBase ? frontCount : 0;
            int totalVerts = frontCount + sideCount + backCount;

            var verts = new Vector3[totalVerts];
            var uvs   = new Vector2[totalVerts];
            var tris  = new List<int>();

            // ---- Front face ----
            for (int iy = 0; iy < vertsY; iy++)
            {
                for (int ix = 0; ix < vertsX; ix++)
                {
                    float u = ix / (float)cellsX;
                    float v = iy / (float)cellsY;

                    float x = Mathf.Lerp(-extX, extX, u);
                    float y = Mathf.Lerp(-extY, extY, v);
                    float z = SampleBilinear(heights, hw, hh, u, v) * s.heightScale;

                    bool border = ix == 0 || ix == cellsX || iy == 0 || iy == cellsY;
                    if (s.clampEdges && border) z = 0f;

                    int vi = iy * vertsX + ix;
                    verts[vi] = new Vector3(x, y, z);
                    uvs[vi]   = new Vector2(u, v);
                }
            }

            // Front face triangles — CW winding, front faces +Z
            for (int iy = 0; iy < cellsY; iy++)
            {
                for (int ix = 0; ix < cellsX; ix++)
                {
                    int bl = iy * vertsX + ix;
                    int br = bl + 1;
                    int tl = bl + vertsX;
                    int tr = tl + 1;

                    tris.Add(bl); tris.Add(br); tris.Add(tr);
                    tris.Add(bl); tris.Add(tr); tris.Add(tl);
                }
            }

            // ---- Base slab (skirt + back) ----
            if (s.addBase)
            {
                float backZ = -s.baseThickness;

                // Clockwise border loop (viewed from +Z): bottom row L→R, right col B→T,
                // top row R→L, left col T→B. Gives outward-facing side normals via RecalculateNormals.
                var border = new List<int>(borderLen);
                for (int ix = 0; ix < cellsX; ix++) border.Add(0 * vertsX + ix);
                for (int iy = 0; iy < cellsY; iy++) border.Add(iy * vertsX + cellsX);
                for (int ix = cellsX; ix > 0; ix--) border.Add(cellsY * vertsX + ix);
                for (int iy = cellsY; iy > 0; iy--) border.Add(iy * vertsX + 0);

                int sideBase = frontCount;

                // One pair of verts per border point: front-z copy and back copy
                for (int i = 0; i < borderLen; i++)
                {
                    Vector3 fp = verts[border[i]];
                    Vector2 fuv = uvs[border[i]];
                    verts[sideBase + i * 2 + 0] = fp;
                    uvs  [sideBase + i * 2 + 0] = fuv;
                    verts[sideBase + i * 2 + 1] = new Vector3(fp.x, fp.y, backZ);
                    uvs  [sideBase + i * 2 + 1] = fuv;
                }

                // Side quads — outward normals computed by RecalculateNormals
                for (int i = 0; i < borderLen; i++)
                {
                    int next = (i + 1) % borderLen;
                    int f0 = sideBase + i    * 2;
                    int b0 = sideBase + i    * 2 + 1;
                    int f1 = sideBase + next * 2;
                    int b1 = sideBase + next * 2 + 1;

                    tris.Add(f0); tris.Add(b0); tris.Add(b1);
                    tris.Add(f0); tris.Add(b1); tris.Add(f1);
                }

                // Back face — CW from -Z (faces outward on -Z side)
                int backBase = frontCount + sideCount;
                for (int iy = 0; iy < vertsY; iy++)
                {
                    for (int ix = 0; ix < vertsX; ix++)
                    {
                        int fi = iy * vertsX + ix;
                        Vector3 fp = verts[fi];
                        verts[backBase + fi] = new Vector3(fp.x, fp.y, backZ);
                        uvs  [backBase + fi] = uvs[fi];
                    }
                }

                for (int iy = 0; iy < cellsY; iy++)
                {
                    for (int ix = 0; ix < cellsX; ix++)
                    {
                        int bl = backBase + iy * vertsX + ix;
                        int br = bl + 1;
                        int tl = bl + vertsX;
                        int tr = tl + 1;

                        // Reversed winding vs front → faces -Z
                        tris.Add(bl); tris.Add(tl); tris.Add(tr);
                        tris.Add(bl); tris.Add(tr); tris.Add(br);
                    }
                }
            }

            var mesh = new Mesh { name = name };
            if (totalVerts > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices  = verts;
            mesh.uv        = uvs;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        static float SampleBilinear(float[,] h, int hw, int hh, float u, float v)
        {
            if (hw <= 0 || hh <= 0) return 0f;
            float fx = u * (hw - 1);
            float fy = v * (hh - 1);
            int x0 = Mathf.Clamp((int)fx, 0, hw - 1);
            int y0 = Mathf.Clamp((int)fy, 0, hh - 1);
            int x1 = Mathf.Min(x0 + 1, hw - 1);
            int y1 = Mathf.Min(y0 + 1, hh - 1);
            float tx = fx - x0, ty = fy - y0;
            return Mathf.Lerp(Mathf.Lerp(h[x0, y0], h[x1, y0], tx),
                              Mathf.Lerp(h[x0, y1], h[x1, y1], tx), ty);
        }
    }
}

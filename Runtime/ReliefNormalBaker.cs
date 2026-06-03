using UnityEngine;

namespace ImageRelief
{
    public static class ReliefNormalBaker
    {
        // Sobel-filter the height field into a tangent-space normal map.
        // Resamples to outRes x outRes first if needed.
        // wrapMode = Clamp (plaque doesn't tile).
        public static Texture2D Bake(float[,] heights, int w, int h, int outRes, float strength)
        {
            outRes = Mathf.Clamp(outRes, 1, 4096);

            float[,] field = (w == outRes && h == outRes)
                ? heights
                : Resample(heights, w, h, outRes, outRes);

            var tex = new Texture2D(outRes, outRes, TextureFormat.RGBA32, true)
            {
                name       = "ReliefNormalMap",
                hideFlags  = HideFlags.HideAndDontSave,
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color[outRes * outRes];
            for (int y = 0; y < outRes; y++)
            {
                for (int x = 0; x < outRes; x++)
                {
                    float tl = S(field, outRes, outRes, x - 1, y + 1);
                    float t  = S(field, outRes, outRes, x,     y + 1);
                    float tr = S(field, outRes, outRes, x + 1, y + 1);
                    float l  = S(field, outRes, outRes, x - 1, y    );
                    float r  = S(field, outRes, outRes, x + 1, y    );
                    float bl = S(field, outRes, outRes, x - 1, y - 1);
                    float b  = S(field, outRes, outRes, x,     y - 1);
                    float br = S(field, outRes, outRes, x + 1, y - 1);

                    float dX = ((tr + 2f * r + br) - (tl + 2f * l + bl)) * strength;
                    float dY = ((tl + 2f * t + tr) - (bl + 2f * b + br)) * strength;

                    Vector3 n = new Vector3(-dX, -dY, 1f).normalized;
                    pixels[y * outRes + x] = new Color(
                        n.x * 0.5f + 0.5f,
                        n.y * 0.5f + 0.5f,
                        n.z * 0.5f + 0.5f,
                        1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        static float[,] Resample(float[,] src, int sw, int sh, int dw, int dh)
        {
            var dst = new float[dw, dh];
            for (int y = 0; y < dh; y++)
            {
                for (int x = 0; x < dw; x++)
                {
                    float u = x / (float)(dw - 1);
                    float v = y / (float)(dh - 1);
                    float fx = u * (sw - 1);
                    float fy = v * (sh - 1);
                    int x0 = Mathf.Clamp((int)fx, 0, sw - 1);
                    int y0 = Mathf.Clamp((int)fy, 0, sh - 1);
                    int x1 = Mathf.Min(x0 + 1, sw - 1);
                    int y1 = Mathf.Min(y0 + 1, sh - 1);
                    float tx = fx - x0, ty = fy - y0;
                    dst[x, y] = Mathf.Lerp(
                        Mathf.Lerp(src[x0, y0], src[x1, y0], tx),
                        Mathf.Lerp(src[x0, y1], src[x1, y1], tx), ty);
                }
            }
            return dst;
        }

        // Clamp-border sample
        static float S(float[,] h, int w, int height, int x, int y)
        {
            x = Mathf.Clamp(x, 0, w - 1);
            y = Mathf.Clamp(y, 0, height - 1);
            return h[x, y];
        }
    }
}

using UnityEngine;

namespace ImageRelief
{
    public static class HeightMaskBuilder
    {
        // Returns a normalized [0..1] height field indexed [x, y] at image pixel resolution.
        // Throws UnityException if the texture is not readable.
        public static float[,] Build(ReliefSettings s, out int w, out int h)
        {
            if (s.sourceImage == null)
            {
                w = 1; h = 1;
                return new float[1, 1];
            }

            Color[] pixels = s.sourceImage.GetPixels(); // throws if not readable
            w = s.sourceImage.width;
            h = s.sourceImage.height;

            // Luminance
            float[] L = new float[w * h];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                L[i] = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            }

            // Separable box blur
            if (s.blurRadius > 0)
                L = BoxBlur(L, w, h, s.blurRadius);

            // Invert
            if (s.invert)
                for (int i = 0; i < L.Length; i++)
                    L[i] = 1f - L[i];

            float soft      = s.thresholdSoftness;
            float threshold = s.threshold;

            var heights = new float[w, h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float lum = L[y * w + x];
                    float height;

                    switch (s.maskMode)
                    {
                        case ReliefSettings.MaskMode.Luminance:
                            height = lum;
                            break;

                        case ReliefSettings.MaskMode.Threshold:
                        {
                            float m = Smoothstep(threshold - soft, threshold + soft, lum);
                            height = m;
                            break;
                        }

                        default: // Hybrid
                        {
                            float m = Smoothstep(threshold - soft, threshold + soft, lum);
                            height = m * (s.baseHeight + s.internalDetail * lum);
                            break;
                        }
                    }

                    heights[x, y] = Mathf.Min(height, s.maxHeightClamp);
                }
            }

            return heights;
        }

        static float Smoothstep(float a, float b, float x)
        {
            if (Mathf.Abs(b - a) < 1e-6f)
                return x >= b ? 1f : 0f;
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float[] BoxBlur(float[] src, int w, int h, int radius)
        {
            float inv = 1f / (radius * 2 + 1);
            var tmp = new float[src.Length];
            var dst = new float[src.Length];

            // Horizontal pass
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                        sum += src[y * w + Mathf.Clamp(x + k, 0, w - 1)];
                    tmp[y * w + x] = sum * inv;
                }
            }

            // Vertical pass
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                        sum += tmp[Mathf.Clamp(y + k, 0, h - 1) * w + x];
                    dst[y * w + x] = sum * inv;
                }
            }

            return dst;
        }
    }
}

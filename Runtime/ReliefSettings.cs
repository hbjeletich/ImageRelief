using UnityEngine;

namespace ImageRelief
{
    [System.Serializable]
    public class ReliefSettings
    {
        public Texture2D sourceImage;

        public enum MaskMode { Luminance, Threshold, Hybrid }
        public MaskMode maskMode = MaskMode.Luminance;

        public bool invert;

        [Range(0, 8)]   public int   blurRadius         = 0;
        [Range(0, 1)]   public float threshold           = 0.5f;
        [Range(0, 0.5f)] public float thresholdSoftness  = 0.05f;
        [Range(0, 1)]   public float internalDetail      = 0.5f;
        [Range(0, 1)]   public float baseHeight          = 0.5f;

        [Range(2, 512)] public int   resolution          = 128;
        public float worldSize = 0.2f;
        [Range(0, 1)]   public float heightScale         = 0.05f;
        [Range(0, 1)]   public float maxHeightClamp      = 1f;

        public bool addBase;
        [Range(0, 0.5f)] public float baseThickness      = 0.05f;
        public bool clampEdges;

        [Range(64, 2048)] public int  normalMapResolution = 512;
        [Range(0, 4)]     public float normalStrength     = 2f;

        public ReliefSettings Clone() => (ReliefSettings)MemberwiseClone();
    }
}

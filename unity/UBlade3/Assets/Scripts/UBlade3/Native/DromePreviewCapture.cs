using System;
using System.IO;
using UnityEngine;

namespace UBlade3.Native
{
    // Renders a preview camera to a PNG at the requested size. Works in an Editor batch-mode run
    // (which needs a graphics device: pass -batchmode without -nographics) and in the Windows
    // player; no external process is involved.
    public static class DromePreviewCapture
    {
        public const int DefaultWidth = 1280;

        public const int DefaultHeight = 720;

        public sealed class Result
        {
            public string Path;

            public int Width;

            public int Height;

            public int Bytes;
        }

        public static Result Capture(Camera camera, string path, int width, int height)
        {
            if (camera == null)
            {
                throw new ArgumentNullException("camera");
            }

            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("capture path is empty", "path");
            }

            if (width <= 0)
            {
                width = DefaultWidth;
            }

            if (height <= 0)
            {
                height = DefaultHeight;
            }

            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.name = "UBlade3DromePreviewCapture";
            target.Create();

            RenderTexture savedTarget = camera.targetTexture;
            RenderTexture savedActive = RenderTexture.active;

            try
            {
                camera.targetTexture = target;
                camera.aspect = (float)width / height;
                camera.Render();

                RenderTexture.active = target;

                Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply(false, false);

                byte[] png = image.EncodeToPNG();

                UnityEngine.Object.DestroyImmediate(image);

                string directory = Path.GetDirectoryName(Path.GetFullPath(path));

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(path, png);

                Result result = new Result();
                result.Path = Path.GetFullPath(path);
                result.Width = width;
                result.Height = height;
                result.Bytes = png.Length;

                return result;
            }
            finally
            {
                camera.targetTexture = savedTarget;
                RenderTexture.active = savedActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}

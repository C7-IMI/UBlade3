using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace UBlade3.Native
{
    // Unity host for the exact HyperBlade engine. The engine is linked into the player as the
    // ublade3_native plugin; it is not a second process. Every gameplay pixel comes from the
    // original 640x480 software renderer and every action is fed back to its input system.
    [DisallowMultipleComponent]
    public sealed class NativeEngineHost : MonoBehaviour
    {
        private const string Dll = "ublade3_native_v6";
        private const int MenuWidth = 640;
        private const int MenuHeight = 480;

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int UBlade3_Initialize(string gameRoot, string pakDir);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int UBlade3_Frame();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int UBlade3_Width();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int UBlade3_Height();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int UBlade3_State();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr UBlade3_Pixels();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int UBlade3_Key(int scancode, int down);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int UBlade3_MouseMove(int x, int y);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int UBlade3_MouseButton(int button, int down, int x, int y);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int UBlade3_Shutdown();

        private Texture2D _texture;
        private byte[] _pixels;
        private int _width = MenuWidth;
        private int _height = MenuHeight;
        private bool _ready;
        private bool _failed;
        private string _error;
        private int _lastMouseX = -1;
        private int _lastMouseY = -1;
        private bool _lastLeft;
        private bool _lastRight;
        private readonly HashSet<int> _heldKeys = new HashSet<int>();
        private bool _captureRequested;
        private bool _autoStart;
        private int _captureFrame = 90;
        private string _capturePath;
        private int _frames;

        // SDL scancodes (USB HID usage table) for the first release's key set.
        private static readonly Dictionary<KeyCode, int> Keys = new Dictionary<KeyCode, int>
        {
            [KeyCode.Escape] = 41, [KeyCode.Return] = 40, [KeyCode.Space] = 44,
            [KeyCode.W] = 26, [KeyCode.A] = 4, [KeyCode.S] = 22, [KeyCode.D] = 7,
            [KeyCode.UpArrow] = 82, [KeyCode.DownArrow] = 81, [KeyCode.LeftArrow] = 80, [KeyCode.RightArrow] = 79,
            [KeyCode.LeftShift] = 225, [KeyCode.RightShift] = 229, [KeyCode.LeftControl] = 224, [KeyCode.RightControl] = 228,
            [KeyCode.LeftAlt] = 226, [KeyCode.RightAlt] = 230,
            [KeyCode.Tab] = 43, [KeyCode.Backspace] = 42,
            [KeyCode.Q] = 20, [KeyCode.E] = 8, [KeyCode.R] = 21, [KeyCode.F] = 9,
            [KeyCode.C] = 6, [KeyCode.V] = 25, [KeyCode.X] = 27, [KeyCode.Z] = 29,
            [KeyCode.G] = 10, [KeyCode.H] = 11, [KeyCode.J] = 13, [KeyCode.K] = 14, [KeyCode.L] = 15,
            [KeyCode.M] = 16, [KeyCode.N] = 17, [KeyCode.O] = 18, [KeyCode.P] = 19, [KeyCode.B] = 5, [KeyCode.I] = 12,
            [KeyCode.Alpha1] = 30, [KeyCode.Alpha2] = 31, [KeyCode.Alpha3] = 32, [KeyCode.Alpha4] = 33,
            [KeyCode.Alpha5] = 34, [KeyCode.Alpha6] = 35, [KeyCode.Alpha7] = 36, [KeyCode.Alpha8] = 37,
            [KeyCode.Alpha9] = 38, [KeyCode.Alpha0] = 39, [KeyCode.F1] = 58, [KeyCode.F2] = 59,
            [KeyCode.F3] = 60, [KeyCode.F4] = 61, [KeyCode.F5] = 62, [KeyCode.F6] = 63,
            [KeyCode.F7] = 64, [KeyCode.F8] = 65, [KeyCode.F9] = 66, [KeyCode.F10] = 67,
            [KeyCode.F11] = 68, [KeyCode.F12] = 69,
        };

        private void Awake()
        {
            Application.runInBackground = true;

            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith("--native-capture=", StringComparison.Ordinal))
                {
                    _captureRequested = true;
                    _capturePath = arg.Substring("--native-capture=".Length);
                }
                else if (arg == "--native-autostart")
                {
                    _autoStart = true;
                    _captureFrame = 300;
                }
            }

            var gameRoot = ResolveGameRoot();

            if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
            {
                Fail("HyperBlade data not found. Set HYPERBLADE_GAME_DATA or StreamingAssets/game-data-path.txt.");
                return;
            }

            try
            {
                var pakDir = Path.Combine(Application.streamingAssetsPath, "20xx") + Path.DirectorySeparatorChar;
                if (UBlade3_Initialize(EnsureTrailingSlash(gameRoot), pakDir) == 0)
                {
                    Fail("The native engine rejected the game data root: " + gameRoot);
                    return;
                }

                _ready = true;
                Debug.Log("UBLADE3_NATIVE_ENGINE_READY data=" + gameRoot);
            }
            catch (Exception ex)
            {
                Fail("Native engine load failed: " + ex.Message);
            }
        }

        private static string EnsureTrailingSlash(string path)
        {
            return path.EndsWith("\\", StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal) ? path : path + "\\";
        }

        private static string ResolveGameRoot()
        {
            var env = Environment.GetEnvironmentVariable("HYPERBLADE_GAME_DATA");
            if (!string.IsNullOrEmpty(env)) return env;

            var pathFile = Path.Combine(Application.streamingAssetsPath, "game-data-path.txt");
            if (File.Exists(pathFile))
            {
                foreach (var line in File.ReadAllLines(pathFile))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 0 && !trimmed.StartsWith("#", StringComparison.Ordinal)) return trimmed;
                }
            }

            const string local = @"D:\Games\HyperBlade\HYB";
            return Directory.Exists(local) ? local : string.Empty;
        }

        private void Fail(string message)
        {
            _failed = true;
            _error = message;
            Debug.LogError("UBLADE3_NATIVE_ENGINE_FAILED " + message);
        }

        private void Update()
        {
            if (!_ready) return;

            if (UBlade3_Frame() == 0)
            {
                _ready = false;
                return;
            }

            var width = UBlade3_Width();
            var height = UBlade3_Height();
            if (width <= 0 || height <= 0) return;

            if (_texture == null || width != _width || height != _height)
            {
                _width = width;
                _height = height;
                _pixels = new byte[_width * _height * 4];
                _texture = new Texture2D(_width, _height, TextureFormat.BGRA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "UBlade3 exact native frame"
                };
            }

            var pointer = UBlade3_Pixels();
            if (pointer != IntPtr.Zero)
            {
                Marshal.Copy(pointer, _pixels, 0, _pixels.Length);
                FlipRows(_pixels, _width, _height, 4);
                _texture.LoadRawTextureData(_pixels);
                _texture.Apply(false, false);
            }

            HandleInput();
            _frames++;

            if (_autoStart)
            {
                if (_frames == 20) NativeClick(320, 170);
                else if (_frames == 60) NativeClick(400, 245);
                else if (_frames == 120) NativeClick(500, 430);
            }

            if (_captureRequested && _frames == _captureFrame)
            {
                Capture();
            }
        }

        private static void FlipRows(byte[] pixels, int width, int height, int stride)
        {
            var rowBytes = width * stride;
            var temp = new byte[rowBytes];
            for (var y = 0; y < height / 2; y++)
            {
                var top = y * rowBytes;
                var bottom = (height - 1 - y) * rowBytes;
                Buffer.BlockCopy(pixels, top, temp, 0, rowBytes);
                Buffer.BlockCopy(pixels, bottom, pixels, top, rowBytes);
                Buffer.BlockCopy(temp, 0, pixels, bottom, rowBytes);
            }
        }

        private Rect FrameRect()
        {
            var scale = Mathf.Min(Screen.width / (float)_width, Screen.height / (float)_height);
            var w = _width * scale;
            var h = _height * scale;
            return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        }

        private Vector2 ToNative(Vector2 gui)
        {
            var rect = FrameRect();
            var x = Mathf.Clamp((gui.x - rect.x) / rect.width * _width, 0, _width - 1);
            var y = Mathf.Clamp((gui.y - rect.y) / rect.height * _height, 0, _height - 1);
            return new Vector2(x, y);
        }

        private void HandleInput()
        {
            var mouse = Input.mousePosition;
            var gui = new Vector2(mouse.x, Screen.height - mouse.y);
            var native = ToNative(gui);
            var nx = Mathf.RoundToInt(native.x);
            var ny = Mathf.RoundToInt(native.y);

            if (nx != _lastMouseX || ny != _lastMouseY)
            {
                UBlade3_MouseMove(nx, ny);
                _lastMouseX = nx;
                _lastMouseY = ny;
            }

            var left = Input.GetMouseButton(0);
            if (left != _lastLeft)
            {
                UBlade3_MouseButton(1, left ? 1 : 0, nx, ny);
                _lastLeft = left;
            }

            var right = Input.GetMouseButton(1);
            if (right != _lastRight)
            {
                UBlade3_MouseButton(3, right ? 1 : 0, nx, ny);
                _lastRight = right;
            }

            foreach (var pair in Keys)
            {
                if (Input.GetKeyDown(pair.Key)) UBlade3_Key(pair.Value, 1);
                if (Input.GetKeyUp(pair.Key)) UBlade3_Key(pair.Value, 0);
            }
        }

        private static void NativeClick(int x, int y)
        {
            UBlade3_MouseMove(x, y);
            UBlade3_MouseButton(1, 1, x, y);
            UBlade3_MouseButton(1, 0, x, y);
        }

        private void Capture()
        {
            try
            {
                var png = _texture.EncodeToPNG();
                var directory = Path.GetDirectoryName(_capturePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(_capturePath, png);
                Debug.Log("UBLADE3_NATIVE_CAPTURE_OK frame=" + _capturePath + " state=" + UBlade3_State());
            }
            catch (Exception ex)
            {
                Debug.LogError("UBLADE3_NATIVE_CAPTURE_FAILED " + ex.Message);
            }
        }

        private void OnGUI()
        {
            if (!_ready)
            {
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
                GUI.color = Color.white;
                if (_failed)
                {
                    GUI.Label(new Rect(20, 20, Screen.width - 40, 100), _error);
                }
                return;
            }

            GUI.DrawTexture(FrameRect(), _texture, ScaleMode.StretchToFill, false);
        }

        private void OnApplicationQuit()
        {
            if (!_ready) return;
            _ready = false;
            try { UBlade3_Shutdown(); } catch { }
        }
    }
}

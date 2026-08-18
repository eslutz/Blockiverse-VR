/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Meta.MCPBridge.Attributes;
using Meta.MCPBridge.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meta.MCPBridge.Editor
{
    /// <summary>
    /// UI verification tools for MCP clients to capture and inspect Unity Editor windows.
    ///
    /// This tool enables AI agents like Devmate to "see" the Unity Editor by capturing
    /// screenshots of Editor windows for visual comparison against Figma designs.
    ///
    /// Key Features:
    /// - List all open Editor windows with type, title, position, size
    /// - Open specific Editor windows by type name
    /// - Capture window screenshots as base64 PNG
    /// - Resize windows to specific dimensions for consistent captures
    /// - Simulate basic interactions for testing interactive states
    ///
    /// Use Cases:
    /// - Verify UI code changes match Figma designs
    /// - Capture hover/selected states for visual testing
    /// - Ensure UI consistency across Editor windows
    ///
    /// MCP Client Usage Pattern:
    /// 1. OpenWindow("BuildingBlocksWindow")
    /// 2. ResizeWindow("BuildingBlocksWindow", 1200, 800) to match Figma frame
    /// 3. CaptureWindow("BuildingBlocksWindow") → base64 PNG
    /// 4. AI Vision compares screenshot to Figma reference
    /// </summary>
    [Tool(
        "Tools for capturing and inspecting Unity Editor windows for visual verification.",
        "WHEN TO USE: After UI code changes, to verify Editor windows match Figma designs.",
        "WORKFLOW: 1) OpenWindow() 2) ResizeWindow() 3) CaptureWindow() 4) AI Vision comparison.",
        "IMPORTANT: Works with already-running Editor. Captures IMGUI and UI Toolkit windows."
    )]
    internal class UIVerificationTools : SingletonService<UIVerificationTools>
    {
#if UNITY_EDITOR_WIN
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
            byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
        }

        private const uint PW_RENDERFULLCONTENT = 0x00000002;
        private const uint DIB_RGB_COLORS = 0;
#endif

        /// <summary>
        /// Information about an open Editor window.
        /// </summary>
        internal class WindowInfo
        {
            public string TypeName { get; set; }
            public string Title { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
            public bool IsFocused { get; set; }
            public bool IsDocked { get; set; }
        }

#if UNITY_EDITOR_WIN
        /// <summary>
        /// Get the native Win32 HWND for an EditorWindow by finding the OS window
        /// that matches Unity's PID and the EditorWindow's screen position.
        /// </summary>
        private static IntPtr GetEditorWindowHWND(EditorWindow window)
        {
            if (window == null) return IntPtr.Zero;

            try
            {
                uint unityPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                var windowRect = window.position;
                float dpi = EditorGUIUtility.pixelsPerPoint;

                // EditorWindow.position uses logical (DPI-independent) coordinates.
                // GetWindowRect returns physical (DPI-scaled) coordinates.
                float physX = windowRect.x * dpi;
                float physY = windowRect.y * dpi;
                float physR = (windowRect.x + windowRect.width) * dpi;
                float physB = (windowRect.y + windowRect.height) * dpi;

                IntPtr bestMatch = IntPtr.Zero;
                int bestArea = 0;

                EnumWindows((hwnd, _) =>
                {
                    GetWindowThreadProcessId(hwnd, out uint pid);
                    if (pid != unityPid) return true;
                    if (!IsWindowVisible(hwnd)) return true;

                    GetWindowRect(hwnd, out RECT rect);
                    int w = rect.Right - rect.Left;
                    int h = rect.Bottom - rect.Top;
                    if (w <= 0 || h <= 0) return true;

                    // Check if the EditorWindow's physical rect falls within this OS window.
                    // Pick the smallest containing window (most specific match).
                    const float tol = 2f;
                    if (physX >= rect.Left - tol && physY >= rect.Top - tol &&
                        physR <= rect.Right + tol && physB <= rect.Bottom + tol)
                    {
                        int area = w * h;
                        if (bestMatch == IntPtr.Zero || area < bestArea)
                        {
                            bestMatch = hwnd;
                            bestArea = area;
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                return bestMatch;
            }
            catch
            {
                // EnumWindows failed — caller will fall back to ReadScreenPixel
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Capture window pixels using Win32 PrintWindow — works even when the window is
        /// occluded (behind other windows like VS Code). Falls back to ReadScreenPixel
        /// if PrintWindow fails.
        /// </summary>
        private static Color[] CaptureWindowPixelsWin32(EditorWindow window, int logicalWidth, int logicalHeight,
            int screenX, int screenY, out bool usedPrintWindow)
        {
            usedPrintWindow = false;
            var hwnd = GetEditorWindowHWND(window);

            if (hwnd == IntPtr.Zero)
            {
                return UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(
                    new Vector2(screenX, screenY), logicalWidth, logicalHeight);
            }

            GetClientRect(hwnd, out RECT clientRect);
            int physWidth = clientRect.Right - clientRect.Left;
            int physHeight = clientRect.Bottom - clientRect.Top;

            if (physWidth <= 0 || physHeight <= 0)
            {
                return UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(
                    new Vector2(screenX, screenY), logicalWidth, logicalHeight);
            }

            // Create a memory DC and compatible bitmap for PrintWindow to render into
            IntPtr windowDC = GetDC(hwnd);
            IntPtr memDC = CreateCompatibleDC(windowDC);
            IntPtr hBitmap = CreateCompatibleBitmap(windowDC, physWidth, physHeight);
            IntPtr oldBitmap = SelectObject(memDC, hBitmap);

            try
            {
                bool printed = PrintWindow(hwnd, memDC, PW_RENDERFULLCONTENT);
                if (!printed)
                {
                    return UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(
                        new Vector2(screenX, screenY), logicalWidth, logicalHeight);
                }

                // Extract pixel data from the bitmap
                var bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = physWidth;
                bmi.bmiHeader.biHeight = -physHeight; // negative = top-down
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0; // BI_RGB

                var pixelData = new byte[physWidth * physHeight * 4];
                int result = GetDIBits(memDC, hBitmap, 0, (uint)physHeight, pixelData, ref bmi, DIB_RGB_COLORS);

                if (result == 0)
                {
                    return UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(
                        new Vector2(screenX, screenY), logicalWidth, logicalHeight);
                }

                usedPrintWindow = true;

                // Convert BGRA byte array → Unity Color[] at logical resolution
                // If the physical and logical sizes differ (DPI scaling), we need to
                // resample. For simplicity and correctness, use nearest-neighbor
                // downscale to match the logical size that callers expect.
                var colors = new Color[logicalWidth * logicalHeight];
                float scaleX = (float)physWidth / logicalWidth;
                float scaleY = (float)physHeight / logicalHeight;

                for (int y = 0; y < logicalHeight; y++)
                {
                    int srcY = Mathf.Clamp((int)(y * scaleY), 0, physHeight - 1);
                    for (int x = 0; x < logicalWidth; x++)
                    {
                        int srcX = Mathf.Clamp((int)(x * scaleX), 0, physWidth - 1);
                        int srcIdx = (srcY * physWidth + srcX) * 4;

                        // BGRA → RGBA
                        float b = pixelData[srcIdx] / 255f;
                        float g = pixelData[srcIdx + 1] / 255f;
                        float r = pixelData[srcIdx + 2] / 255f;
                        float a = pixelData[srcIdx + 3] / 255f;

                        // ReadScreenPixel returns bottom-up (row 0 = bottom), so
                        // flip Y to match: store row 0 at bottom of the array
                        int dstY = logicalHeight - 1 - y;
                        colors[dstY * logicalWidth + x] = new Color(r, g, b, a);
                    }
                }

                return colors;
            }
            finally
            {
                SelectObject(memDC, oldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(memDC);
                ReleaseDC(hwnd, windowDC);
            }
        }
#endif

        /// <summary>
        /// Capture an EditorWindow's pixels. On Windows, uses PrintWindow for occluded
        /// capture. On other platforms, falls back to ReadScreenPixel.
        /// </summary>
        private static Color[] CaptureWindowPixels(EditorWindow window, int logicalWidth, int logicalHeight,
            int screenX, int screenY)
        {
            return CaptureWindowPixels(window, logicalWidth, logicalHeight, screenX, screenY,
                out _);
        }

        private static Color[] CaptureWindowPixels(EditorWindow window, int logicalWidth, int logicalHeight,
            int screenX, int screenY, out string captureMethod)
        {
#if UNITY_EDITOR_WIN
            var pixels = CaptureWindowPixelsWin32(window, logicalWidth, logicalHeight, screenX, screenY,
                out bool usedPrintWindow);
            captureMethod = usedPrintWindow ? "PrintWindow" : "ReadScreenPixel (fallback)";
            return pixels;
#else
            captureMethod = "ReadScreenPixel";
            return UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(
                new Vector2(screenX, screenY), logicalWidth, logicalHeight);
#endif
        }

        [Tool(Description = "List all currently open Editor windows with their type, title, position, and size",
            Returns = "Array of window objects with typeName, title, x, y, width, height, isFocused, isDocked")]
        internal object ListOpenWindows()
        {
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            var focusedWindow = EditorWindow.focusedWindow;

            var windowInfos = windows
                .Where(w => w != null)
                .Select(w => new
                {
                    typeName = w.GetType().Name,
                    fullTypeName = w.GetType().FullName,
                    title = w.titleContent?.text ?? "",
                    x = w.position.x,
                    y = w.position.y,
                    width = w.position.width,
                    height = w.position.height,
                    isFocused = w == focusedWindow,
                    isDocked = w.docked
                })
                .OrderBy(w => w.typeName)
                .ToArray();

            return new
            {
                count = windowInfos.Length,
                windows = windowInfos
            };
        }

        [Tool(Description = "Open an Editor window by type name. Creates or focuses the window.",
            Returns = "JSON object confirming the window was opened with its properties")]
        internal object OpenWindow(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return new { error = "typeName is required" };
            }

            var windowType = FindWindowType(typeName);
            if (windowType == null)
            {
                return new
                {
                    error = $"Window type '{typeName}' not found",
                    hint = "Use ListOpenWindows() to see available window types"
                };
            }

            try
            {
                var window = EditorWindow.GetWindow(windowType);
                window.Focus();
                window.Repaint();

                return new
                {
                    opened = true,
                    typeName = window.GetType().Name,
                    title = window.titleContent?.text ?? "",
                    x = window.position.x,
                    y = window.position.y,
                    width = window.position.width,
                    height = window.position.height
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    error = $"Failed to open window: {ex.Message}",
                    typeName
                };
            }
        }

        [Tool(Description = "Capture a screenshot of an Editor window as base64-encoded PNG",
            Returns = "JSON object with base64Png data, width, height, and typeName")]
        internal async Task<object> CaptureWindow(string typeName = null, int? targetWidth = null, int? targetHeight = null)
        {
            EditorWindow window;

            if (string.IsNullOrEmpty(typeName))
            {
                window = EditorWindow.focusedWindow;
                if (window == null)
                {
                    return new { error = "No focused window and typeName not specified" };
                }
            }
            else
            {
                window = FindWindowByTypeName(typeName);
                if (window == null)
                {
                    return new
                    {
                        error = $"Window '{typeName}' not found",
                        hint = "Use ListOpenWindows() to see open windows, or OpenWindow() to open it"
                    };
                }
            }

            if (targetWidth.HasValue && targetHeight.HasValue)
            {
                var currentPos = window.position;
                window.position = new Rect(currentPos.x, currentPos.y, targetWidth.Value, targetHeight.Value);
            }

            window.Focus();
            window.Repaint();

            await Task.Delay(100);

            try
            {
                var pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                var windowRect = window.position;

                var captureWidth = (int)windowRect.width;
                var captureHeight = (int)windowRect.height;

                var screenX = (int)windowRect.x;
                var screenY = (int)windowRect.y;

                Color[] pixels;
                string captureMethod;

                try
                {
                    pixels = CaptureWindowPixels(window, captureWidth, captureHeight, screenX, screenY,
                        out captureMethod);
                }
                catch
                {
                    return new
                    {
                        error = "Failed to capture screen pixels. Window may be minimized.",
                        typeName = window.GetType().Name
                    };
                }

                var texture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
                texture.SetPixels(pixels);
                texture.Apply();

                var pngBytes = texture.EncodeToPNG();
                var base64Png = Convert.ToBase64String(pngBytes);

                UnityEngine.Object.DestroyImmediate(texture);

                return new
                {
                    base64Png,
                    width = captureWidth,
                    height = captureHeight,
                    logicalWidth = windowRect.width,
                    logicalHeight = windowRect.height,
                    dpiScale = pixelsPerPoint,
                    typeName = window.GetType().Name,
                    title = window.titleContent?.text ?? "",
                    captureMethod
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    error = $"Failed to capture window: {ex.Message}",
                    typeName = window.GetType().Name
                };
            }
        }

        [Tool(Description = "Resize an Editor window to specific dimensions",
            Returns = "JSON object confirming the resize with new dimensions")]
        internal object ResizeWindow(string typeName, int width, int height)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return new { error = "typeName is required" };
            }

            var window = FindWindowByTypeName(typeName);
            if (window == null)
            {
                return new
                {
                    error = $"Window '{typeName}' not found",
                    hint = "Use OpenWindow() to open it first"
                };
            }

            var currentPos = window.position;
            window.position = new Rect(currentPos.x, currentPos.y, width, height);
            window.Repaint();

            return new
            {
                resized = true,
                typeName = window.GetType().Name,
                width = window.position.width,
                height = window.position.height
            };
        }

        [Tool(Description = "Scroll a UI Toolkit window's ScrollView to a vertical offset (pixels from top). Use to reveal content below the fold before capturing.",
            Returns = "JSON object with scroll position and content height")]
        internal object ScrollWindow(string typeName, int scrollY = 0)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return new { error = "typeName is required" };
            }

            var window = FindWindowByTypeName(typeName);
            if (window == null)
            {
                return new
                {
                    error = $"Window '{typeName}' not found",
                    hint = "Use OpenWindow() to open it first"
                };
            }

            var root = window.rootVisualElement;
            if (root == null)
            {
                return new { error = "Window has no rootVisualElement (IMGUI window?)", typeName };
            }

            var scrollView = root.Q<UnityEngine.UIElements.ScrollView>();
            if (scrollView == null)
            {
                return new { error = "No ScrollView found in window", typeName };
            }

            scrollView.scrollOffset = new Vector2(scrollView.scrollOffset.x, scrollY);
            window.Repaint();

            return new
            {
                scrolled = true,
                typeName = window.GetType().Name,
                scrollY = scrollView.scrollOffset.y,
                contentHeight = scrollView.contentContainer.worldBound.height,
                viewportHeight = scrollView.worldBound.height
            };
        }

        [Tool(Description = "Move an Editor window to specific screen coordinates",
            Returns = "JSON object confirming the move with new position")]
        internal object MoveWindow(string typeName, int x, int y)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return new { error = "typeName is required" };
            }

            var window = FindWindowByTypeName(typeName);
            if (window == null)
            {
                return new
                {
                    error = $"Window '{typeName}' not found",
                    hint = "Use OpenWindow() to open it first"
                };
            }

            var currentPos = window.position;
            window.position = new Rect(x, y, currentPos.width, currentPos.height);
            window.Repaint();

            return new
            {
                moved = true,
                typeName = window.GetType().Name,
                x = window.position.x,
                y = window.position.y,
                width = window.position.width,
                height = window.position.height
            };
        }

        [Tool(Description = "Focus an Editor window and bring it to the front",
            Returns = "JSON object confirming the window is focused")]
        internal object FocusWindow(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return new { error = "typeName is required" };
            }

            var window = FindWindowByTypeName(typeName);
            if (window == null)
            {
                return new
                {
                    error = $"Window '{typeName}' not found",
                    hint = "Use OpenWindow() to open it first"
                };
            }

            window.Focus();
            window.Repaint();

            return new
            {
                focused = true,
                typeName = window.GetType().Name,
                title = window.titleContent?.text ?? ""
            };
        }

        [Tool(Description = "Set the Editor theme to dark or light mode. " +
            "Uses reflection to set skinIndex and swaps RLDS stylesheets on all open UIToolkit windows.",
            Returns = "JSON object confirming the theme change")]
        internal object SetEditorTheme(bool dark)
        {
            try
            {
                bool wasDark = EditorGUIUtility.isProSkin;
                int targetSkinIndex = dark ? 1 : 0;

                var skinProp = typeof(EditorGUIUtility).GetProperty("skinIndex",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (skinProp == null)
                {
                    return new { error = "Could not find EditorGUIUtility.skinIndex property" };
                }

                skinProp.SetValue(null, targetSkinIndex);

                bool isLightMode = !dark;
                var rldsUtilsType = System.AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .FirstOrDefault(t => t.FullName == "Meta.XR.Editor.UserInterface.RLDS.RLDSUtils");

                int swappedCount = 0;
                if (rldsUtilsType != null)
                {
                    var loadMethod = rldsUtilsType.GetMethod("LoadStyleSheet",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    if (loadMethod != null)
                    {
                        var newSheet = loadMethod.Invoke(null, new object[] { isLightMode }) as StyleSheet;
                        var oldSheet = loadMethod.Invoke(null, new object[] { !isLightMode }) as StyleSheet;

                        if (newSheet != null)
                        {
                            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                            {
                                var root = window.rootVisualElement;
                                if (root == null || root.styleSheets.count == 0) continue;

                                bool hadOldSheet = oldSheet != null && root.styleSheets.Contains(oldSheet);
                                if (hadOldSheet)
                                {
                                    root.styleSheets.Remove(oldSheet);
                                    root.styleSheets.Add(newSheet);
                                    swappedCount++;
                                }
                            }
                        }
                    }
                }

                foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                {
                    window.Repaint();
                }

                return new
                {
                    applied = true,
                    previousTheme = wasDark ? "dark" : "light",
                    currentTheme = EditorGUIUtility.isProSkin ? "dark" : "light",
                    skinIndex = targetSkinIndex,
                    windowsSwapped = swappedCount
                };
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to set theme: {ex.Message}" };
            }
        }

        [Tool(Description = "Set the RLDS Showcase window's theme to light or dark mode. " +
            "This controls the design-system stylesheet used by the Showcase window, " +
            "independent of Unity's global editor theme. Opens the window if not already open.",
            Returns = "JSON object with the applied theme and window state")]
        internal object SetShowcaseTheme(string theme)
        {
            if (string.IsNullOrEmpty(theme))
            {
                return new { error = "theme is required ('light' or 'dark')" };
            }

            bool isLight;
            switch (theme.ToLowerInvariant())
            {
                case "light":
                    isLight = true;
                    break;
                case "dark":
                    isLight = false;
                    break;
                default:
                    return new { error = $"Invalid theme '{theme}'. Use 'light' or 'dark'" };
            }

            try
            {
                var showcaseType = FindWindowType("RLDSShowcaseWindow");
                if (showcaseType == null)
                {
                    return new { error = "RLDSShowcaseWindow type not found. Is OVR_INTERNAL_CODE defined?" };
                }

                var window = EditorWindow.GetWindow(showcaseType);
                window.Show();
                window.Focus();

                var setThemeMethod = showcaseType.GetMethod("SetTheme",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (setThemeMethod != null)
                {
                    setThemeMethod.Invoke(window, new object[] { isLight });
                }
                else
                {
                    var field = showcaseType.GetField("_isLightMode",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (field == null)
                    {
                        return new { error = "Cannot find SetTheme method or _isLightMode field on RLDSShowcaseWindow" };
                    }
                    field.SetValue(window, isLight);

                    var buildMethod = showcaseType.GetMethod("BuildUI",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    buildMethod?.Invoke(window, new object[] { window.rootVisualElement });
                }

                window.Repaint();

                var isLightProp = showcaseType.GetProperty("IsLightMode",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                var currentTheme = isLightProp != null ? (bool)isLightProp.GetValue(window) : isLight;

                return new
                {
                    applied = true,
                    theme = currentTheme ? "light" : "dark",
                    typeName = window.GetType().Name,
                    title = window.titleContent?.text ?? ""
                };
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to set showcase theme: {ex.Message}" };
            }
        }

        [Tool(Description = "Get information about a specific Editor window",
            Returns = "JSON object with detailed window information")]
        internal object GetWindowInfo(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return new { error = "typeName is required" };
            }

            var window = FindWindowByTypeName(typeName);
            if (window == null)
            {
                return new
                {
                    error = $"Window '{typeName}' not found",
                    hint = "Use ListOpenWindows() to see available windows"
                };
            }

            var focusedWindow = EditorWindow.focusedWindow;

            return new
            {
                typeName = window.GetType().Name,
                fullTypeName = window.GetType().FullName,
                title = window.titleContent?.text ?? "",
                x = window.position.x,
                y = window.position.y,
                width = window.position.width,
                height = window.position.height,
                isFocused = window == focusedWindow,
                isDocked = window.docked,
                hasUnsavedChanges = window.hasUnsavedChanges,
                minSize = new { width = window.minSize.x, height = window.minSize.y },
                maxSize = new { width = window.maxSize.x, height = window.maxSize.y },
                dpiScale = EditorGUIUtility.pixelsPerPoint
            };
        }

        [Tool(Description = "Close an Editor window",
            Returns = "JSON object confirming the window was closed")]
        internal object CloseWindow(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return new { error = "typeName is required" };
            }

            var window = FindWindowByTypeName(typeName);
            if (window == null)
            {
                return new
                {
                    error = $"Window '{typeName}' not found",
                    alreadyClosed = true
                };
            }

            var closedTypeName = window.GetType().Name;
            window.Close();

            return new
            {
                closed = true,
                typeName = closedTypeName
            };
        }

        [Tool(Description = "Repaint all Editor windows to ensure UI is up to date",
            Returns = "JSON object confirming repaint was triggered")]
        internal object RepaintAllWindows()
        {
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            var count = 0;

            foreach (var window in windows)
            {
                if (window != null)
                {
                    window.Repaint();
                    count++;
                }
            }

            return new
            {
                repainted = true,
                windowCount = count
            };
        }

        [Tool(Description = "Execute a Unity Editor menu item by its full path (e.g. 'Window/General/Console', 'Meta/Tools/NUX/Show NUX'). Works for any registered menu item including custom ones.",
            Returns = "JSON object confirming whether the menu item was executed successfully")]
        internal object ExecuteMenuItem(string menuPath)
        {
            if (string.IsNullOrEmpty(menuPath))
            {
                return new { error = "menuPath is required (e.g. 'Window/General/Console')" };
            }

            try
            {
                var result = EditorApplication.ExecuteMenuItem(menuPath);
                return new
                {
                    executed = result,
                    menuPath,
                    message = result
                        ? $"Menu item '{menuPath}' executed successfully"
                        : $"Menu item '{menuPath}' not found or could not be executed"
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    error = $"Failed to execute menu item: {ex.Message}",
                    menuPath
                };
            }
        }

        [Tool(Description = "Capture a screenshot of a popup/dropdown window (opened via ShowAsDropDown or ShowPopup) as base64-encoded PNG. Use this for windows that don't appear in ListOpenWindows.",
            Returns = "JSON object with base64Png data, width, height, and typeName")]
        internal async Task<object> CapturePopupWindow(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return new { error = "typeName is required" };
            }

            var window = FindWindowIncludingPopups(typeName);
            if (window == null)
            {
                return new
                {
                    error = $"Popup window '{typeName}' not found",
                    hint = "Ensure the popup is open before capturing. Use ListOpenWindows() to see all windows."
                };
            }

            window.Focus();
            window.Repaint();
            await Task.Delay(300);

            try
            {
                var windowRect = window.position;
                var captureWidth = (int)windowRect.width;
                var captureHeight = (int)windowRect.height;
                var screenX = (int)windowRect.x;
                var screenY = (int)windowRect.y;

                Color[] pixels;
                try
                {
                    pixels = CaptureWindowPixels(window, captureWidth, captureHeight, screenX, screenY);
                }
                catch
                {
                    return new
                    {
                        error = "Failed to capture screen pixels. Popup may be closed.",
                        typeName = window.GetType().Name
                    };
                }

                var texture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
                texture.SetPixels(pixels);
                texture.Apply();

                var pngBytes = texture.EncodeToPNG();
                var base64Png = Convert.ToBase64String(pngBytes);
                UnityEngine.Object.DestroyImmediate(texture);

                return new
                {
                    base64Png,
                    width = captureWidth,
                    height = captureHeight,
                    typeName = window.GetType().Name,
                    title = window.titleContent?.text ?? ""
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    error = $"Failed to capture popup window: {ex.Message}",
                    typeName = window.GetType().Name
                };
            }
        }

        [Tool(Description = "Capture a specific VisualElement within a window as base64-encoded PNG. Query the element using USS selectors (#name, .class, Type). Use index to select the Nth matching element (0-based).",
            Returns = "JSON object with base64Png data of the cropped element, plus element bounds info")]
        internal async Task<object> CaptureElement(string typeName, string query, int index = 0)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return new { error = "typeName is required" };
            }
            if (string.IsNullOrEmpty(query))
            {
                return new { error = "query is required (USS selector, e.g. '#my-element', '.my-class', 'Button')" };
            }
            if (index < 0)
            {
                return new { error = "index must be non-negative (0-based)" };
            }

            var window = FindWindowIncludingPopups(typeName);
            if (window == null)
            {
                return new
                {
                    error = $"Window '{typeName}' not found",
                    hint = "Ensure the window is open before capturing."
                };
            }

            var root = window.rootVisualElement;
            if (root == null)
            {
                return new
                {
                    error = $"Window '{typeName}' has no rootVisualElement (may be IMGUI-only)",
                    typeName = window.GetType().Name
                };
            }

            VisualElement element = null;
            {
                string name = query.StartsWith("#") ? query.Substring(1) : query.StartsWith(".") ? null : query;
                string cls = query.StartsWith(".") ? query.Substring(1) : null;
                var matches = root.Query(name, cls).ToList();
                if (index < matches.Count)
                {
                    element = matches[index];
                }
            }

            if (element == null)
            {
                return new
                {
                    error = $"Element not found with query '{query}' in window '{typeName}'",
                    hint = "Use USS selectors: '#name' for name, '.class' for USS class, 'Type' for element type"
                };
            }

            window.Focus();
            window.Repaint();
            await Task.Delay(300);

            try
            {
                var windowRect = window.position;
                var captureWidth = (int)windowRect.width;
                var captureHeight = (int)windowRect.height;
                var screenX = (int)windowRect.x;
                var screenY = (int)windowRect.y;

                Color[] pixels;
                try
                {
                    pixels = CaptureWindowPixels(window, captureWidth, captureHeight, screenX, screenY);
                }
                catch
                {
                    return new
                    {
                        error = "Failed to capture screen pixels. Window may be closed.",
                        typeName = window.GetType().Name
                    };
                }

                var fullTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
                Texture2D croppedTexture = null;
                try
                {
                    fullTexture.SetPixels(pixels);
                    fullTexture.Apply();

                    var elementBound = element.worldBound;
                    var cropX = (int)elementBound.x;
                    var cropY = (int)(captureHeight - elementBound.y - elementBound.height);
                    var cropWidth = (int)elementBound.width;
                    var cropHeight = (int)elementBound.height;

                    // Clamp to texture bounds
                    if (cropX < 0) { cropWidth += cropX; cropX = 0; }
                    if (cropY < 0) { cropHeight += cropY; cropY = 0; }
                    if (cropX + cropWidth > captureWidth) cropWidth = captureWidth - cropX;
                    if (cropY + cropHeight > captureHeight) cropHeight = captureHeight - cropY;

                    if (cropWidth <= 0 || cropHeight <= 0)
                    {
                        return new
                        {
                            error = $"Element '{query}' is outside the visible window bounds",
                            elementBounds = new
                            {
                                x = elementBound.x,
                                y = elementBound.y,
                                width = elementBound.width,
                                height = elementBound.height
                            },
                            query,
                            typeName = window.GetType().Name
                        };
                    }

                    var croppedPixels = fullTexture.GetPixels(cropX, cropY, cropWidth, cropHeight);
                    croppedTexture = new Texture2D(cropWidth, cropHeight, TextureFormat.RGBA32, false);
                    croppedTexture.SetPixels(croppedPixels);
                    croppedTexture.Apply();

                    var pngBytes = croppedTexture.EncodeToPNG();
                    var base64Png = Convert.ToBase64String(pngBytes);

                    return new
                    {
                        base64Png,
                        width = cropWidth,
                        height = cropHeight,
                        elementBounds = new
                        {
                            x = elementBound.x,
                            y = elementBound.y,
                            width = elementBound.width,
                            height = elementBound.height
                        },
                        query,
                        typeName = window.GetType().Name,
                        title = window.titleContent?.text ?? ""
                    };
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(fullTexture);
                    if (croppedTexture != null)
                        UnityEngine.Object.DestroyImmediate(croppedTexture);
                }
            }
            catch (Exception ex)
            {
                return new
                {
                    error = $"Failed to capture element: {ex.Message}",
                    typeName = window.GetType().Name,
                    query
                };
            }
        }

        [Tool(Description = "Open the Meta XR SDK status menu dropdown as it appears in production (via ShowAsDropDown). This triggers the real StatusMenu.ShowDropdown() with version text and all registered tools.",
            Returns = "JSON object confirming the dropdown was opened")]
        internal object ShowStatusMenuDropdown()
        {
            try
            {
                var statusMenuType = FindWindowType("StatusMenu");
                if (statusMenuType == null)
                {
                    return new { error = "StatusMenu type not found in loaded assemblies" };
                }

                var showMethod = statusMenuType.GetMethod("ShowDropdown",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Rect) },
                    null);

                if (showMethod == null)
                {
                    return new { error = "StatusMenu.ShowDropdown(Rect) method not found" };
                }

                var rect = new Rect(100, 100, 432, 30);
                // Defer to EditorApplication.update so EditorStyles is available (one-shot)
                EditorApplication.CallbackFunction callback = null;
                callback = () =>
                {
                    EditorApplication.update -= callback;
                    try { showMethod.Invoke(null, new object[] { rect }); }
                    catch (Exception ex) { Debug.LogException(ex); }
                };
                EditorApplication.update += callback;

                return new
                {
                    opened = true,
                    hint = "Use CapturePopupWindow('StatusMenuDrawer') to capture the dropdown"
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    error = $"Failed to open status menu dropdown: {ex.Message}"
                };
            }
        }

        [Tool(Description = "Query resolved styles of a VisualElement in a window. Returns computed CSS values (background-color, padding, margin, border, font-size, color, etc.) for comparing against Figma specs. Use USS selectors (#name, .class) and optional index for Nth match.",
            Returns = "JSON object with resolved style properties of the element")]
        internal object QueryResolvedStyles(string typeName, string query, int index = 0)
        {
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(query))
            {
                return new { error = "typeName and query are required" };
            }
            if (index < 0)
            {
                return new { error = "index must be non-negative (0-based)" };
            }

            var window = FindWindowIncludingPopups(typeName);
            if (window == null)
            {
                return new { error = $"Window '{typeName}' not found" };
            }

            var root = window.rootVisualElement;
            if (root == null)
            {
                return new { error = $"Window '{typeName}' has no rootVisualElement" };
            }

            string name = query.StartsWith("#") ? query.Substring(1) : query.StartsWith(".") ? null : query;
            string cls = query.StartsWith(".") ? query.Substring(1) : null;
            var matches = root.Query(name, cls).ToList();

            if (index >= matches.Count)
            {
                return new { error = $"Element not found: query='{query}', index={index}, matchCount={matches.Count}" };
            }

            var el = matches[index];
            var rs = el.resolvedStyle;

            var classes = el.GetClasses().ToList();

            return new
            {
                element = new
                {
                    name = el.name,
                    typeName = el.GetType().Name,
                    classes = classes.ToArray(),
                    childCount = el.childCount
                },
                resolvedStyle = new
                {
                    backgroundColor = ColorToHex(rs.backgroundColor),
                    color = ColorToHex(rs.color),
                    borderTopColor = ColorToHex(rs.borderTopColor),
                    borderBottomColor = ColorToHex(rs.borderBottomColor),
                    borderLeftColor = ColorToHex(rs.borderLeftColor),
                    borderRightColor = ColorToHex(rs.borderRightColor),
                    borderTopWidth = rs.borderTopWidth,
                    borderBottomWidth = rs.borderBottomWidth,
                    borderLeftWidth = rs.borderLeftWidth,
                    borderRightWidth = rs.borderRightWidth,
                    borderTopLeftRadius = rs.borderTopLeftRadius,
                    borderTopRightRadius = rs.borderTopRightRadius,
                    borderBottomLeftRadius = rs.borderBottomLeftRadius,
                    borderBottomRightRadius = rs.borderBottomRightRadius,
                    paddingTop = rs.paddingTop,
                    paddingBottom = rs.paddingBottom,
                    paddingLeft = rs.paddingLeft,
                    paddingRight = rs.paddingRight,
                    marginTop = rs.marginTop,
                    marginBottom = rs.marginBottom,
                    marginLeft = rs.marginLeft,
                    marginRight = rs.marginRight,
                    width = rs.width,
                    height = rs.height,
                    fontSize = rs.fontSize,
                    unityFontStyleAndWeight = rs.unityFontStyleAndWeight.ToString(),
                    flexDirection = rs.flexDirection.ToString(),
                    alignItems = rs.alignItems.ToString(),
                    justifyContent = rs.justifyContent.ToString(),
                    flexGrow = rs.flexGrow,
                    flexShrink = rs.flexShrink,
                    opacity = rs.opacity,
                    display = rs.display.ToString(),
                    visibility = rs.visibility.ToString(),
                    unityBackgroundImageTintColor = ColorToHex(rs.unityBackgroundImageTintColor),
                    unityTextAlign = rs.unityTextAlign.ToString(),
                    backgroundImage = rs.backgroundImage.texture != null ? rs.backgroundImage.texture.name : null,
                    textOverflow = rs.textOverflow.ToString()
                },
                inlineStyle = new
                {
                    backgroundColor = el.style.backgroundColor.keyword != StyleKeyword.Undefined
                        ? ColorToHex(el.style.backgroundColor.value) : null,
                    color = el.style.color.keyword != StyleKeyword.Undefined
                        ? ColorToHex(el.style.color.value) : null,
                    width = el.style.width.keyword != StyleKeyword.Undefined
                        ? (float?)el.style.width.value.value : null,
                    height = el.style.height.keyword != StyleKeyword.Undefined
                        ? (float?)el.style.height.value.value : null,
                    fontSize = el.style.fontSize.keyword != StyleKeyword.Undefined
                        ? (float?)el.style.fontSize.value.value : null
                }
            };
        }

        [Tool(Description = "Get the screen-absolute bounding rectangle of a VisualElement in a UIToolkit window. " +
            "Use USS selectors (#name, .class) or type name to find the element. Returns screen coordinates " +
            "suitable for ClickAtScreenPosition. For IMGUI windows (no rootVisualElement), returns an error — " +
            "use screenshot-based positioning as fallback.",
            Returns = "JSON with element bounds (screen-absolute x, y, width, height, centerX, centerY)")]
        internal object GetElementBounds(string typeName, string query, int index = 0)
        {
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(query))
            {
                return new { error = "typeName and query are required" };
            }

            var window = FindWindowIncludingPopups(typeName);
            if (window == null)
            {
                return new { error = $"Window '{typeName}' not found" };
            }

            var root = window.rootVisualElement;
            if (root == null)
            {
                return new
                {
                    error = $"Window '{typeName}' has no rootVisualElement (IMGUI window)",
                    hint = "Use screenshot-based positioning for IMGUI windows"
                };
            }

            string name = query.StartsWith("#") ? query.Substring(1) : query.StartsWith(".") ? null : query;
            string cls = query.StartsWith(".") ? query.Substring(1) : null;
            var matches = root.Query(name, cls).ToList();

            if (matches.Count == 0)
            {
                return new { error = $"No elements found for query '{query}'", typeName };
            }

            if (index < 0 || index >= matches.Count)
            {
                return new { error = $"Index {index} out of range, {matches.Count} matches found for '{query}'", typeName };
            }

            var el = matches[index];
            var localBound = el.worldBound;
            var windowPos = window.position;

            // Convert from window-local to screen-absolute
            float screenX = windowPos.x + localBound.x;
            float screenY = windowPos.y + localBound.y;
            float centerX = screenX + localBound.width / 2f;
            float centerY = screenY + localBound.height / 2f;

            return new
            {
                element = new
                {
                    name = el.name ?? "",
                    typeName = el.GetType().Name,
                    text = (el as TextElement)?.text ?? (el as Label)?.text ?? "",
                    classes = el.GetClasses().ToArray(),
                    childCount = el.childCount
                },
                bounds = new
                {
                    localX = localBound.x,
                    localY = localBound.y,
                    screenX,
                    screenY,
                    width = localBound.width,
                    height = localBound.height,
                    centerX,
                    centerY
                },
                matchCount = matches.Count,
                windowPosition = new { x = windowPos.x, y = windowPos.y }
            };
        }

        [Tool(Description = "List all VisualElements matching a query in a UIToolkit window, with their bounds. " +
            "Useful for discovering element names/classes before using GetElementBounds or ClickElement.",
            Returns = "JSON array of matching elements with name, type, text, and screen bounds")]
        internal object ListElements(string typeName, string query = null, int maxResults = 20)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return new { error = "typeName is required" };
            }

            var window = FindWindowIncludingPopups(typeName);
            if (window == null)
            {
                return new { error = $"Window '{typeName}' not found" };
            }

            var root = window.rootVisualElement;
            if (root == null)
            {
                return new
                {
                    error = $"Window '{typeName}' has no rootVisualElement (IMGUI window)",
                    hint = "Use screenshot-based positioning for IMGUI windows"
                };
            }

            List<VisualElement> matches;
            if (string.IsNullOrEmpty(query))
            {
                matches = root.Query().ToList();
            }
            else
            {
                string name = query.StartsWith("#") ? query.Substring(1) : query.StartsWith(".") ? null : query;
                string cls = query.StartsWith(".") ? query.Substring(1) : null;
                matches = root.Query(name, cls).ToList();
            }

            var windowPos = window.position;
            var elements = matches.Take(maxResults).Select((el, i) =>
            {
                var wb = el.worldBound;
                return new
                {
                    index = i,
                    name = el.name ?? "",
                    type = el.GetType().Name,
                    text = (el as TextElement)?.text ?? (el as Label)?.text ?? "",
                    classes = el.GetClasses().ToArray(),
                    screenX = windowPos.x + wb.x,
                    screenY = windowPos.y + wb.y,
                    width = wb.width,
                    height = wb.height,
                    centerX = windowPos.x + wb.x + wb.width / 2f,
                    centerY = windowPos.y + wb.y + wb.height / 2f
                };
            }).ToArray();

            return new
            {
                matchCount = matches.Count,
                showing = elements.Length,
                elements
            };
        }

#if UNITY_EDITOR_LINUX
        [Tool(Description = "Move the mouse cursor to a screen-absolute position without clicking. " +
            "Uses real X11 XTest events. Works in Linux batchmode on Xvfb. " +
            "Use GetElementBounds to get coordinates, then pass centerX/centerY here. " +
            "Useful for testing hover states (cursors, tooltips).",
            Returns = "JSON confirming the move position")]
        internal object MoveToScreenPosition(int x, int y)
        {
            try
            {
                var display = System.Environment.GetEnvironmentVariable("DISPLAY") ?? ":99";
                var displayPtr = X11Native.XOpenDisplay(display);
                if (displayPtr == IntPtr.Zero)
                {
                    return new { error = $"Cannot open X11 display '{display}'" };
                }

                try
                {
                    X11Native.XTestFakeMotionEvent(displayPtr, 0, x, y, 0);
                    X11Native.XFlush(displayPtr);
                }
                finally
                {
                    X11Native.XCloseDisplay(displayPtr);
                }

                return new
                {
                    moved = true,
                    x,
                    y,
                    display
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    error = $"X11 move failed: {ex.Message}",
                    hint = "Ensure Xvfb is running and libXtst.so.6 is available"
                };
            }
        }

        [Tool(Description = "Move the mouse cursor to a VisualElement by query — combines GetElementBounds + MoveToScreenPosition. " +
            "Finds the element, computes its center, and moves the mouse there without clicking. " +
            "Useful for testing hover states (cursors, tooltips).",
            Returns = "JSON with element info and move result")]
        internal object MoveToElement(string typeName, string query, int index = 0)
        {
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(query))
            {
                return new { error = "typeName and query are required" };
            }

            var window = FindWindowIncludingPopups(typeName);
            if (window == null)
            {
                return new { error = $"Window '{typeName}' not found" };
            }

            var root = window.rootVisualElement;
            if (root == null)
            {
                return new
                {
                    error = $"Window '{typeName}' has no rootVisualElement (IMGUI window)",
                    hint = "Use CaptureWindow + MoveToScreenPosition with visual estimation"
                };
            }

            string name = query.StartsWith("#") ? query.Substring(1) : query.StartsWith(".") ? null : query;
            string cls = query.StartsWith(".") ? query.Substring(1) : null;
            var matches = root.Query(name, cls).ToList();

            if (matches.Count == 0)
            {
                return new { error = $"No elements found for query '{query}'", typeName };
            }

            if (index < 0 || index >= matches.Count)
            {
                return new { error = $"Index {index} out of range, {matches.Count} matches found", typeName };
            }

            var el = matches[index];
            var wb = el.worldBound;
            var windowPos = window.position;

            int screenCenterX = (int)(windowPos.x + wb.x + wb.width / 2f);
            int screenCenterY = (int)(windowPos.y + wb.y + wb.height / 2f);

            var moveResult = MoveToScreenPosition(screenCenterX, screenCenterY);

            return new
            {
                element = new
                {
                    name = el.name ?? "",
                    type = el.GetType().Name,
                    text = (el as TextElement)?.text ?? (el as Label)?.text ?? "",
                    query,
                    index,
                    matchCount = matches.Count
                },
                screenX = screenCenterX,
                screenY = screenCenterY,
                moveResult
            };
        }

        [Tool(Description = "Click at a screen-absolute position using real X11 XTest events. " +
            "Works in Linux batchmode on Xvfb. Use GetElementBounds to get coordinates, " +
            "then pass centerX/centerY here. For double-click, set clickCount=2.",
            Returns = "JSON confirming the click position")]
        internal object ClickAtScreenPosition(int x, int y, int clickCount = 1)
        {
            try
            {
                var display = System.Environment.GetEnvironmentVariable("DISPLAY") ?? ":99";
                var displayPtr = X11Native.XOpenDisplay(display);
                if (displayPtr == IntPtr.Zero)
                {
                    return new { error = $"Cannot open X11 display '{display}'" };
                }

                try
                {
                    for (int c = 0; c < clickCount; c++)
                    {
                        if (c > 0) System.Threading.Thread.Sleep(50);

                        // Move mouse
                        X11Native.XTestFakeMotionEvent(displayPtr, 0, x, y, 0);
                        X11Native.XFlush(displayPtr);
                        System.Threading.Thread.Sleep(30);

                        // Press
                        X11Native.XTestFakeButtonEvent(displayPtr, 1, true, 0);
                        X11Native.XFlush(displayPtr);
                        System.Threading.Thread.Sleep(30);

                        // Release
                        X11Native.XTestFakeButtonEvent(displayPtr, 1, false, 0);
                        X11Native.XFlush(displayPtr);
                    }
                }
                finally
                {
                    X11Native.XCloseDisplay(displayPtr);
                }

                return new
                {
                    clicked = true,
                    x,
                    y,
                    clickCount,
                    display
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    error = $"X11 click failed: {ex.Message}",
                    hint = "Ensure Xvfb is running and libXtst.so.6 is available"
                };
            }
        }

        [Tool(Description = "Click on a VisualElement by query — combines GetElementBounds + ClickAtScreenPosition. " +
            "Finds the element, computes its center, and sends a real X11 click. " +
            "For IMGUI windows, returns an error with the hint to use screenshot-based fallback.",
            Returns = "JSON with element info and click result")]
        internal object ClickElement(string typeName, string query, int index = 0, int clickCount = 1)
        {
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(query))
            {
                return new { error = "typeName and query are required" };
            }

            var window = FindWindowIncludingPopups(typeName);
            if (window == null)
            {
                return new { error = $"Window '{typeName}' not found" };
            }

            var root = window.rootVisualElement;
            if (root == null)
            {
                return new
                {
                    error = $"Window '{typeName}' has no rootVisualElement (IMGUI window)",
                    hint = "Use CaptureWindow + ClickAtScreenPosition with visual estimation"
                };
            }

            string name = query.StartsWith("#") ? query.Substring(1) : query.StartsWith(".") ? null : query;
            string cls = query.StartsWith(".") ? query.Substring(1) : null;
            var matches = root.Query(name, cls).ToList();

            if (matches.Count == 0)
            {
                return new { error = $"No elements found for query '{query}'", typeName };
            }

            if (index < 0 || index >= matches.Count)
            {
                return new { error = $"Index {index} out of range, {matches.Count} matches found", typeName };
            }

            var el = matches[index];
            var wb = el.worldBound;
            var windowPos = window.position;

            int screenCenterX = (int)(windowPos.x + wb.x + wb.width / 2f);
            int screenCenterY = (int)(windowPos.y + wb.y + wb.height / 2f);

            // Perform the click
            var clickResult = ClickAtScreenPosition(screenCenterX, screenCenterY, clickCount);

            return new
            {
                element = new
                {
                    name = el.name ?? "",
                    type = el.GetType().Name,
                    text = (el as TextElement)?.text ?? (el as Label)?.text ?? "",
                    query,
                    index,
                    matchCount = matches.Count
                },
                screenX = screenCenterX,
                screenY = screenCenterY,
                clickResult
            };
        }

        private static class X11Native
        {
            private const string LibX11 = "libX11.so.6";
            private const string LibXtst = "libXtst.so.6";

            [System.Runtime.InteropServices.DllImport(LibX11)]
            public static extern IntPtr XOpenDisplay(string display);

            [System.Runtime.InteropServices.DllImport(LibX11)]
            public static extern int XFlush(IntPtr display);

            [System.Runtime.InteropServices.DllImport(LibX11)]
            public static extern int XCloseDisplay(IntPtr display);

            [System.Runtime.InteropServices.DllImport(LibXtst)]
            public static extern int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, ulong delay);

            [System.Runtime.InteropServices.DllImport(LibXtst)]
            public static extern int XTestFakeButtonEvent(IntPtr display, uint button, bool isPress, ulong delay);
        }
#endif

        private static string ColorToHex(Color c)
        {
            var r = (int)(c.r * 255);
            var g = (int)(c.g * 255);
            var b = (int)(c.b * 255);
            var a = (int)(c.a * 255);
            return a < 255
                ? $"#{r:X2}{g:X2}{b:X2}{a:X2}"
                : $"#{r:X2}{g:X2}{b:X2}";
        }

        private static Type FindWindowType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(typeName);
                    if (type != null && typeof(EditorWindow).IsAssignableFrom(type))
                    {
                        return type;
                    }

                    var matchingType = assembly.GetTypes()
                        .FirstOrDefault(t =>
                            typeof(EditorWindow).IsAssignableFrom(t) &&
                            (t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                             t.FullName?.Equals(typeName, StringComparison.OrdinalIgnoreCase) == true));

                    if (matchingType != null)
                    {
                        return matchingType;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static EditorWindow FindWindowByTypeName(string typeName)
        {
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            return windows.FirstOrDefault(w =>
                w != null &&
                (w.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                 w.GetType().FullName?.Equals(typeName, StringComparison.OrdinalIgnoreCase) == true));
        }

        private static EditorWindow FindWindowIncludingPopups(string typeName)
        {
            // FindWindowByTypeName already uses Resources.FindObjectsOfTypeAll which
            // includes popups, dropdowns, and all other EditorWindow instances.
            return FindWindowByTypeName(typeName);
        }
    }
}

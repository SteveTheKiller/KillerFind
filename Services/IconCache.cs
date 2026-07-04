using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KillerFind.Services
{
    // Real Windows shell icons for the results list, cached per extension so a
    // 100k-result search costs a handful of SHGetFileInfo calls, not 100k.
    // SHGFI_USEFILEATTRIBUTES resolves the icon from the extension alone - no
    // disk access - except for the few types that carry per-file icons.
    public static class IconCache
    {
        private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static ImageSource? For(string filePath)
        {
            string ext;
            try { ext = Path.GetExtension(filePath); } catch { ext = string.Empty; }
            if (string.IsNullOrEmpty(ext)) ext = ".";

            bool perFile = ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
            string key = perFile ? filePath : ext;

            lock (Cache)
                if (Cache.TryGetValue(key, out var hit)) return hit;

            var img = Load(perFile ? filePath : "x" + ext, perFile);
            lock (Cache) Cache[key] = img;
            return img;
        }

        private static ImageSource? Load(string pathOrName, bool real)
        {
            var info = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_SMALLICON;
            if (!real) flags |= SHGFI_USEFILEATTRIBUTES;

            IntPtr r = SHGetFileInfo(pathOrName, FILE_ATTRIBUTE_NORMAL, ref info,
                                     (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (r == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch { return null; }
            finally { DestroyIcon(info.hIcon); }
        }

        private const uint SHGFI_ICON              = 0x100;
        private const uint SHGFI_SMALLICON         = 0x1;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        private const uint FILE_ATTRIBUTE_NORMAL   = 0x80;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int    iIcon;
            public uint   dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]  public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}

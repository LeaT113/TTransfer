using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TTransfer.Explorer
{
    

    static class IconService
    {
        // Cache
        static Dictionary<string, ImageSource> iconCache;
        static Dictionary<int, ImageSource> folderIconCache;
        static readonly string[] cacheExcludedExtensions = new string[] { ".exe", ".ico", ".url", "dir" };

        // Directory
        [StructLayout(LayoutKind.Sequential)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        };

        [DllImport("shell32.dll")]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

        [DllImport("User32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_LARGEICON = 0x0;
        private const uint SHGFI_SMALLICON = 0x000000001;
        static readonly ImageSource folderBackupIcon = new BitmapImage(new Uri("Icons/icon_folderBackup.png", UriKind.Relative));
        static readonly ImageSource fileBackupIcon = new BitmapImage(new Uri("Icons/icon_fileBackup.png", UriKind.Relative));



        public static ImageSource GetInstantIcon(string extension, string fullName)
        {
            // Get backup
            if (extension == null)
                return folderBackupIcon;
            else
                return fileBackupIcon;
        }
        public static async Task<ImageSource> GetIconAsync(string extension, string fullName)
        {
            // TODO Move to initialize method
            if (iconCache == null)
                iconCache = new Dictionary<string, ImageSource>();
            if (folderIconCache == null)
                folderIconCache = new Dictionary<int, ImageSource>();
            extension = extension ?? "dir";


            // File cache
            if (iconCache.ContainsKey(extension))
                return iconCache[extension];

            // TODO Consider other things for Task.Run to speed up further
            // Get icon pointer
            IntPtr handleIcon;
            int iIcon = 0;
            if (extension == "dir")
            {
                SHFILEINFO shinfo = new SHFILEINFO();
                SHGetFileInfo(fullName, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);
                handleIcon = shinfo.hIcon;
                iIcon = shinfo.iIcon; // TODO Can use this method for files as well? Would allow to cache depending on iIcon, so same exe icons could also be cached

                // Folder icon cache
                if (folderIconCache.ContainsKey(iIcon))
                    return folderIconCache[iIcon];
            }
            else
            {
                handleIcon = Icon.ExtractAssociatedIcon(fullName).Handle;
            }
            

            ImageSource icon = null;
            if (handleIcon != IntPtr.Zero)
            {
                await Task.Run(() =>
                { 
                    icon = Imaging.CreateBitmapSourceFromHIcon(handleIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    icon.Freeze();
                });
            }
            DestroyIcon(handleIcon);


            // Add to cache
            if(extension == "dir")
            {
                folderIconCache.Add(iIcon, icon);
            }
            else
            {
                if (!cacheExcludedExtensions.Contains(extension))
                    iconCache.Add(extension, icon);
            }
            

            return icon;
        }
    }
}

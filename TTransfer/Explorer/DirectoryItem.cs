using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Media;
using System.Windows;
using System.Windows.Interop;
using System.ComponentModel;

namespace TTransfer.Explorer
{
    public class DirectoryItem : ICloneable, INotifyPropertyChanged
    {
        // Visible
        public string Name { get { return name; } }
        public ImageSource Icon { get { return icon; } }
        public string SizeReadable { get { return sizeString; } }
        public string LastModifiedReadable { get { return LastModified.ToString("dd. MM. yyyy  HH:mm"); } }
        public float IconOpacity { get { return isHidden ? 0.3f : 1f; } }

        // Public
        public bool IsFolder { get { return Extension == null; } }
        public string Path { get { return path; } }
        public DateTime LastModified { get { return lastModified; } }
        public long Size { get { return size; } }
        public string Extension { get { return extension; } }

        public event PropertyChangedEventHandler PropertyChanged;

        // Data
        ImageSource icon;
        DateTime lastModified;
        string name;
        string path;
        string extension;
        long size;
        string sizeString;
        bool isHidden;


        public DirectoryItem() { }
        public DirectoryItem(FileInfo file)
        {
            name = file.Name;
            path = file.FullName;
            extension = file.Extension;
            size = file.Length;
            lastModified = file.LastWriteTime;

            sizeString = ExplorerControl.FormatFileSize(size);
            isHidden = file.Attributes.HasFlag(FileAttributes.Hidden);
            icon = IconService.GetInstantIcon(extension, path);
        }
        public DirectoryItem(DirectoryInfo directory)
        {
            name = directory.Name;
            path = directory.FullName;
            lastModified = directory.LastWriteTime;

            isHidden = directory.Attributes.HasFlag(FileAttributes.Hidden);

            icon = IconService.GetInstantIcon(extension, path);
        }



        // Public
        /// <summary>
        /// Gets all first level child files
        /// </summary>
        public DirectoryItem[] GetChildFiles()
        {
            if (!IsFolder)
                return null;

            return Directory.GetFiles(path)
                .Select(f => new DirectoryItem(new FileInfo(f)))
                .ToArray();
        }

        /// <summary>
        /// Gets all first level child folders
        /// </summary>
        public DirectoryItem[] GetChildFolders()
        {
            if (!IsFolder)
                return null;

            return Directory.GetDirectories(path)
                .Select(f => new DirectoryItem(new DirectoryInfo(f)))
                .ToArray();
        }

        /// <summary>
        /// Gets all first level child folders and files in that order
        /// </summary>
        public DirectoryItem[] GetChildren()
        {
            if (!IsFolder)
                return null;

            return GetChildFolders()
                .Concat(GetChildFiles())
                .ToArray();
        }


        // Calculate
        /// <summary>
        /// Gets the total size of an file or folder, calculating folders recursively
        /// </summary>
        public long GetTotalSize()
        {
            if (!IsFolder)
                return size;
            else
            {
                long totalSize = 0;

                DirectoryItem[] contents = GetChildren();
                if (contents == null || contents.Length == 0)
                    return 0;

                foreach (var c in contents)
                    totalSize += c.GetTotalSize();

                return totalSize;
            }
        }

        public int GetTotalChildCount()
        {
            if (!IsFolder)
                return 0;
            else
            {
                int totalCount = 0;

                DirectoryItem[] contents = GetChildren();
                if (contents == null || contents.Length == 0)
                    return 0;

                // Count first level children
                totalCount += contents.Length;

                // Recursive count
                foreach (var c in contents)
                    totalCount += c.GetTotalChildCount();

                return totalCount;
            }

        }

        /// <summary>
        /// Counts the number of first level children
        /// </summary>
        public int GetChildCount()
        {
            if (!IsFolder)
                return 0;

            DirectoryItem[] children = GetChildren();
            if (children == null)
                return 0;

            return children.Count();
        }

        public void SetIcon(ImageSource icon)
        {
            this.icon = icon;

            NotifyPropertyChanged();
        }


        public void NotifyPropertyChanged()
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(""));
            }
        }

        public object Clone()
        {
            var clone = new DirectoryItem();

            clone.icon = icon;
            clone.lastModified = lastModified;
            clone.name = name;
            clone.path = path;
            clone.extension = extension;
            clone.size = size;
            clone.sizeString = sizeString;
            clone.isHidden = isHidden;

            return clone;
        }
    }
}

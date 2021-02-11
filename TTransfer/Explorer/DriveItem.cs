using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TTransfer.Explorer
{
    public class DriveItem
    {
        // Static
        static ImageSource[] icons = new ImageSource[] { new BitmapImage(new Uri("Icons/disk_windows.ico", UriKind.Relative)), new BitmapImage(new Uri("Icons/disk_fixed.ico", UriKind.Relative)), new BitmapImage(new Uri("Icons/disk_network.ico", UriKind.Relative)) };


        // Visible
        public string Name { 
            get 
            {
                if (driveType == DriveType.Network)
                    return Path;
                else
                    return label + " (" + Path + ")";
            } 
        }
        public ImageSource Icon { get { return icon; } }

        // Public
        public string Path { get { return letter + ":"; } }

        // Data
        string label;
        char letter;
        DriveType driveType;
        ImageSource icon;



        public DriveItem(DriveInfo driveInfo) 
        {
            label = driveInfo.DriveType == DriveType.Network ? "" : driveInfo.VolumeLabel;
            letter = driveInfo.Name[0];
            driveType = driveInfo.DriveType;

            
            switch(driveInfo.DriveType)
            {
                case DriveType.Fixed:
                    if (letter == 'C')
                        icon = icons[0];
                    else
                        icon = icons[1];
                    break;

                case DriveType.Network:
                    icon = icons[2];
                    break;

                // TODO Add more drive types
            }
        }
    }
}

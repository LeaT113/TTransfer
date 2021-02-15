using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TTransfer.Network
{
    public class TransferProgressReport
    {
        // Public
        public long TotalBytes 
        { 
            get { return totalBytes; }
            set
            {
                totalBytes = value;
                totalSize = Explorer.ExplorerControl.FormatFileSize(value);
            }
        }
        public long CurrentBytes { get { return currentBytes; } set { currentBytes = value; } }
        public int PercentDone 
        { 
            get 
            {
                if (forceHide)
                    return 100;

                return (int)((currentBytes * 100) / totalBytes); 
            } 
        }
        public string TotalSize { get { return totalSize; } }
        public string ActiveItem { get { return activeItem; } set { activeItem = value; } }
        public bool IsSender { get { return sending; } set { sending = value; } }


        // Data
        long totalBytes;
        long currentBytes;
        string totalSize;
        string activeItem;
        bool sending;
        bool forceHide = false;



        public TransferProgressReport()
        {

        }
        public TransferProgressReport(bool forceHide)
        {
            this.forceHide = forceHide;
        }
    }
}

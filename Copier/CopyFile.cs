using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Copier
{
    public class CopyFile : BindableBase
    {
        private string filePath = "";
        public string FilePath 
        {
            get => filePath; 
            set => SetProperty(ref filePath, value);
        }

        private string fileName = "";
        public string FileName 
        {
            get => fileName; 
            set => SetProperty(ref fileName, value); 
        }

        private long totalBytes;
        public long TotalBytes 
        {
            get => totalBytes;
            set
            {
                SetProperty(ref totalBytes, value);
                RaisePropertyChanged(nameof(Progress));
            }
            
        }

        private long bytesCopied;
        public long BytesCopied 
        {
            get => bytesCopied;
            set
            {
                SetProperty(ref bytesCopied, value);
                RaisePropertyChanged(nameof(Progress));
            }
        }
        public double Progress 
        {
            get
            {
                if (TotalBytes == 0)
                {
                    return 0;
                }
                return (double)BytesCopied / TotalBytes;
            }
        }

        private FileState filestate;
        public FileState FileState 
        {
            get => filestate; 
            set => SetProperty(ref filestate, value); 
        }
    }
}

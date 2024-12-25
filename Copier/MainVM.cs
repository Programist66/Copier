using Microsoft.Win32;
using Prism.Mvvm;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.DirectoryServices.ActiveDirectory;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace Copier
{
    public class MainVM : BindableBase
    {
        private const int MaxDegreeOfParallelism = 4;
        private object _locker = new object();

        private CancellationTokenSource cancel;
        private SynchronizationContext synchronizationContext;

        private string sourceDirectory = "";
        private string destinationDirectory = "";

        private long totalBytes = 0;
        private long bytesCopied = 0;

        private bool isCopied = false;
        public string SourceDirectory
        {
            get => sourceDirectory;
            set
            {
                SetProperty(ref sourceDirectory, value);
            }
        }

        public string DestinationDirectory
        {
            get => destinationDirectory;
            set
            {
                SetProperty(ref destinationDirectory, value);
            }
        }

        private ConcurrentQueue<CopyFile> Allfiles = [];
        private List<string> filesCopied = [];

        private ObservableCollection<CopyFile> doneAndCopyFiles = [];
        public ObservableCollection<CopyFile> DoneAndCopyFiles 
        {
            get => doneAndCopyFiles; 
            //set => SetProperty(ref doneAndCopyFiles, value); 
        }
        public double TotalProgress 
        {
            get 
            {
                if (totalBytes == 0) 
                {
                    return 0;
                }
                return (double)bytesCopied / totalBytes;
            }
        }

        public MainVM() 
        {
            cancel = new();
            StartCopyOrCancelCommand = new DelegateCommand(StartCopyOrCancel);
            ChoiseSourceFolderCommand = new DelegateCommand(ChoiseSourceFolder);
            ChoiseDestinationFolderCommand = new DelegateCommand(ChoiseDestinationFolder);
            synchronizationContext = SynchronizationContext.Current!;
        }
        private void Cancel()
        {
            cancel.Cancel();
        }

        public ICommand ChoiseSourceFolderCommand { get; }
        private void ChoiseSourceFolder()
        {
            OpenFolderDialog dialog = new();
            if (dialog.ShowDialog() == true)
            {
                SourceDirectory = dialog.FolderName;
            }
        }

        public ICommand ChoiseDestinationFolderCommand { get; }
        private void ChoiseDestinationFolder()
        {
            OpenFolderDialog dialog = new();
            if (dialog.ShowDialog() == true)
            {
                DestinationDirectory = dialog.FolderName;
            }
        }
        public ICommand StartCopyOrCancelCommand { get; private set; }
        private void StartCopyOrCancel() 
        {
            if (isCopied)
            {
                Cancel();
            }
            else 
            {
                StartCopy();
            }
        }

        private async void StartCopy() 
        {
            isCopied = true;
            await CopyFilesAsync();
            StartCopyOrCancelCommand = new DelegateCommand(StartCopy);
            MessageBox.Show("Копирование завершено");
            isCopied = false;
        }
        private async Task CopyFilesAsync()
        {
            if (string.IsNullOrEmpty(SourceDirectory) || string.IsNullOrEmpty(DestinationDirectory) || !Directory.Exists(SourceDirectory) || !Directory.Exists(DestinationDirectory))
            {
                MessageBox.Show("Выбирете корректное значение пути");
                return;
            }
            cancel = new CancellationTokenSource();
            try
            {
                string[] files = Directory.GetFiles(SourceDirectory, "*.*", SearchOption.AllDirectories);
                totalBytes = files.Sum(file => new FileInfo(file).Length);
                bytesCopied = 0;

                foreach (var file in files)
                {
                    FileInfo fileInfo = new(file);
                    CopyFile fileCopy = new()
                    {
                        FilePath = file,
                        FileName = fileInfo.Name,
                        TotalBytes = fileInfo.Length
                    };
                    Allfiles.Enqueue(fileCopy);
                }
                Task[] tasks = new Task[MaxDegreeOfParallelism];
                for (int i = 0; i < MaxDegreeOfParallelism; i++)
                {
                    tasks[i] = Task.Run(() => CopyFileAsync(cancel.Token));
                }
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {                
                await Task.Run(() => CleanupPartialCopies());
                MessageBox.Show("Копирование отменено.");
            }
            catch (Exception ex)
            {                
                await Task.Run(() => CleanupPartialCopies());
                MessageBox.Show($"Ошибка копирования: {ex.Message}");
            }
            finally
            {
                totalBytes = 0;
                DoneAndCopyFiles.Clear();
                RaisePropertyChanged(nameof(TotalProgress));
                Allfiles.Clear();
                filesCopied.Clear();
            }
        }

        private async Task CopyFileAsync(CancellationToken token)
        {
            while (Allfiles.TryDequeue(out CopyFile? fileCopy))
            {
                token.ThrowIfCancellationRequested();
                fileCopy.FileState = FileState.Copy;
                var destinationFile = Path.Combine(DestinationDirectory, fileCopy.FileName);
                lock (_locker)
                {
                    synchronizationContext.Post(_ =>
                    {
                        filesCopied.Add(destinationFile);
                        DoneAndCopyFiles.Add(fileCopy);
                    }, null);
                }
                using FileStream sourceStream = new FileStream(fileCopy.FilePath, FileMode.Open, FileAccess.Read);
                using FileStream destinationStream = new FileStream(destinationFile, FileMode.Create, FileAccess.Write);
                byte[] buffer = new byte[4096];
                int readBytes;

                while ((readBytes = await sourceStream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None)) > 0)
                {
                    if (token.IsCancellationRequested)
                    {
                        token.ThrowIfCancellationRequested();
                        synchronizationContext.Post(_ =>
                        {
                            lock (_locker)
                            {
                                DoneAndCopyFiles.Remove(fileCopy);
                            }
                        }, null);
                        RaisePropertyChanged(nameof(DoneAndCopyFiles));
                    }
                    await destinationStream.WriteAsync(buffer, 0, readBytes, CancellationToken.None);
                    fileCopy.BytesCopied += readBytes;
                    bytesCopied += readBytes;

                    RaisePropertyChanged(nameof(TotalProgress));
                    RaisePropertyChanged(nameof(DoneAndCopyFiles));
                }
                fileCopy.FileState = FileState.Done;
                
                synchronizationContext.Post(_ =>
                    {
                        lock (_locker)
                        {                            
                            DoneAndCopyFiles.Remove(fileCopy);
                        }
                    }, null);                
            }
        }

        private void CleanupPartialCopies()
        {
            foreach (var file in filesCopied)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    MessageBox.Show(file);
                }
            }
        }
    }
}

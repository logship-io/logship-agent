// <copyright file="TempDirectory.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

namespace Logship.Agent.FileBasedTests.Utility
{
    internal sealed class TempDirectory : IDisposable
    {
        private volatile bool disposed;
        public DirectoryInfo DirectoryInfo { get; private set; }
        public string Path => DirectoryInfo.FullName;

        public TempDirectory() : this(System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName()))
        {
        }

        public TempDirectory(string path) : this(new DirectoryInfo(path))
        {
        }

        public TempDirectory(DirectoryInfo directory)
        {
            DirectoryInfo = directory;
            if (!DirectoryInfo.Exists)
            {
                DirectoryInfo.Create();
            }
        }

        public void Dispose()
        {
            try
            {
                if (!disposed)
                {
                    if (DirectoryInfo.Exists)
                    {
                        DirectoryInfo.Delete(true);
                    }
                    disposed = true;
                }
                GC.SuppressFinalize(this);
            }
            catch (Exception)
            {
                // Ignore disposal errors
            }
        }

        ~TempDirectory()
        {
            if (!disposed)
            {
                Dispose();
            }
        }
    }
}

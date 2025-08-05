// <copyright file="TempFile.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>


using System.Text;

namespace Logship.Agent.FileBasedTests.Utility
{
    internal sealed class TempFile : IDisposable
    {
        private volatile bool disposed;
        public FileInfo FileInfo { get; private set; }

        public TempFile() : this(Path.GetTempFileName())
        {
            // noop
        }

        public TempFile(string path) : this(new FileInfo(path))
        {
            // noop
        }

        public TempFile(FileInfo file)
        {
            this.FileInfo = file;
        }

        public Task AppendLine(string content, Encoding encoding, CancellationToken token)
        {
            return this.Append(string.Concat(content, Environment.NewLine), encoding, token);
        }

        public async Task Append(string content, Encoding encoding, CancellationToken token)
        {
            using var stream = this.FileInfo.OpenWrite();
            stream.Seek(0, SeekOrigin.End);
            var bytes = encoding.GetBytes(content);

            await stream.WriteAsync(bytes, token);
            await stream.FlushAsync(token);
        }

        public Task Truncate(CancellationToken token)
        {
            using var stream = this.FileInfo.Open(FileMode.Truncate, FileAccess.Write);
            return stream.FlushAsync(token);
        }

        public void Dispose()
        {
            try
            {
                if (false == disposed)
                {
                    this.FileInfo.Delete();
                    disposed = true;
                }

                GC.SuppressFinalize(this);
            }
            catch (Exception) { }
        }

        ~TempFile()
        {
            if (!disposed)
            {
                Dispose();
            }
        }
    }
}


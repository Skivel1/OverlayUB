using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LCUSharp.Utility
{
    /// <summary>
    /// Manages the operations relating to the league client's lockfile.
    /// </summary>
    internal class LockFileHandler
    {
        private const string FileName = "lockfile";

        /// <summary>
        /// Creates a new instance of the <see cref="LockFileHandler"/> class.
        /// </summary>
        public LockFileHandler()
        {
        }

        /// <summary>
        /// Waits for the lockfile to be created and then parses it for the token, port, etc.
        /// </summary>
        /// <param name="path">The lockfile's directory path.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>The league client's port and the user's authentication token.</returns>
        public async Task<(int port, string token)> ParseLockFileAsync(string path, CancellationToken cancellationToken = default)
        {
            var lockfilePath = await WaitForFileAsync(path, cancellationToken).ConfigureAwait(false);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var fileStream = new FileStream(
                        lockfilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);

                    using var reader = new StreamReader(fileStream);
                    var contents = (await reader.ReadToEndAsync().ConfigureAwait(false)).Trim();

                    if (!string.IsNullOrWhiteSpace(contents))
                    {
                        // Format: Name:PID:Port:Password:Protocol
                        var items = contents.Split(':');
                        if (items.Length >= 4 && int.TryParse(items[2], out int port) && port > 0)
                        {
                            var token = items[3];
                            if (!string.IsNullOrWhiteSpace(token))
                            {
                                return (port, token);
                            }
                        }
                    }
                }
                catch (IOException)
                {
                    // File still locked or being written by Riot/League client
                }
                catch (UnauthorizedAccessException)
                {
                    // Transient access issue while file is created/updated
                }

                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Waits until the lockfile is created by the league client.
        /// </summary>
        /// <param name="path">The directory to search in.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>The lockfile's path.</returns>
        private async Task<string> WaitForFileAsync(string path, CancellationToken cancellationToken = default)
        {
            var filePath = Path.Combine(path, FileName);
            if (File.Exists(filePath))
                return filePath;

            while (!Directory.Exists(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(filePath))
                return filePath;

            var fileCreated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var fileWatcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite
            };

            void OnFileEvent(object sender, FileSystemEventArgs e)
            {
                if (string.Equals(e.Name, FileName, StringComparison.OrdinalIgnoreCase) && File.Exists(filePath))
                {
                    fileCreated.TrySetResult(true);
                }
            }

            fileWatcher.Created += OnFileEvent;
            fileWatcher.Changed += OnFileEvent;
            fileWatcher.EnableRaisingEvents = true;

            // Doble chequeo por si se creó justo antes de activar EnableRaisingEvents
            if (File.Exists(filePath))
            {
                fileWatcher.EnableRaisingEvents = false;
                return filePath;
            }

            using var registration = cancellationToken.Register(() => fileCreated.TrySetCanceled());
            await fileCreated.Task.ConfigureAwait(false);
            fileWatcher.EnableRaisingEvents = false;

            return filePath;
        }
    }
}
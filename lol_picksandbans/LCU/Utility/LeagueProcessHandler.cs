using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace LCUSharp.Utility
{
    /// <summary>
    /// Manages the operations relating to the league client's process.
    /// </summary>
    internal class LeagueProcessHandler
    {
        private const string ProcessName = "LeagueClientUx";

        /// <summary>
        /// Triggers when the league client is exited.
        /// </summary>
        public event EventHandler? Exited;

        /// <summary>
        /// The league client's process.
        /// </summary>
        public Process? Process { get; private set; }

        /// <summary>
        /// The league client's executable path.
        /// </summary>
        public string ExecutablePath { get; private set; } = string.Empty;

        /// <summary>
        /// Waits for the league client's process to start.
        /// </summary>
        /// <returns>True if the process was found successfully, otherwise false.</returns>
        public bool WaitForProcess()
        {
            while (true)
            {
                Process[] processes = Process.GetProcessesByName(ProcessName);
                Process? matchedProcess = null;
                string? exeDirectory = null;

                foreach (Process candidate in processes)
                {
                    if (matchedProcess == null)
                    {
                        try
                        {
                            if (!candidate.HasExited && candidate.MainModule != null && !string.IsNullOrWhiteSpace(candidate.MainModule.FileName))
                            {
                                string? dir = Path.GetDirectoryName(candidate.MainModule.FileName);
                                if (!string.IsNullOrWhiteSpace(dir))
                                {
                                    matchedProcess = candidate;
                                    exeDirectory = dir;
                                    continue;
                                }
                            }
                        }
                        catch (Win32Exception)
                        {
                            // Process initializing or insufficient privileges; retry on next poll
                        }
                        catch (InvalidOperationException)
                        {
                            // Process exited before MainModule could be read
                        }
                    }

                    candidate.Dispose();
                }

                if (matchedProcess != null && !string.IsNullOrWhiteSpace(exeDirectory))
                {
                    if (Process != null)
                    {
                        Process.Exited -= OnProcessExited;
                        Process.Dispose();
                    }

                    Process = matchedProcess;
                    ExecutablePath = exeDirectory;

                    try
                    {
                        Process.EnableRaisingEvents = true;
                        Process.Exited += OnProcessExited;
                    }
                    catch (InvalidOperationException)
                    {
                        // Process already exited right after detection; loop again
                        Process.Dispose();
                        Process = null;
                        ExecutablePath = string.Empty;
                        Thread.Sleep(250);
                        continue;
                    }

                    break;
                }

                Thread.Sleep(250);
            }

            return true;
        }

        /// <summary>
        /// Called when the league client is exited.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnProcessExited(object? sender, EventArgs e)
        {
            Exited?.Invoke(this, EventArgs.Empty);
        }
    }
}
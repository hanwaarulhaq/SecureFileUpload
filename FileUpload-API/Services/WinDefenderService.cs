using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System;
using System.Diagnostics;
using System.Threading;

namespace WebApplication.Services
{
 
    //https://github.com/jitbit/WinDefender/tree/mai


    ////check by filename
    //bool isVirus = await WinDefender.IsVirus(@"c:\path\to\file");

    ////check by byte array
    //byte[] fileContents = ReadFileFromSomewhere();
    //bool isVirus = await WinDefender.IsVirus(fileContents);

    ////cancellation token support if you want ot abort
    //bool isVirus = await WinDefender.IsVirus(fileContents, cancellationToken);


    public class WinDefenderService : IAntivirusChecker
    {
        private static bool _isDefenderAvailable;
        private static string _defenderPath = string.Empty;
        private static SemaphoreSlim _lock = new SemaphoreSlim(5); //limit to 5 concurrent checks at a time

        //static ctor
        static WinDefenderService()
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                _defenderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
                _isDefenderAvailable = File.Exists(_defenderPath);
            }
            else
                _isDefenderAvailable = false;
        }
        /// <summary>
        /// check by byte array, cancellation token support if you want ot abort
        /// </summary>
        /// <param name="file"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>true if file contains virus</returns>
        public async Task<bool> IsVirus(byte[] fileBytes, CancellationToken cancellationToken = default)
        {
            if (!_isDefenderAvailable) return false;

            string path = Path.GetTempFileName(); //Path.GetRandomFileName() do not work here
            await Task.Run(() => File.WriteAllBytes(path, fileBytes), cancellationToken); //save temp file

            if (cancellationToken.IsCancellationRequested) return false;

            try
            {
                await _lock.WaitAsync(cancellationToken);

                using (var process = new Process())
                {
                    process.StartInfo.FileName = _defenderPath;
                    process.StartInfo.Arguments = $"-Scan -ScanType 3 -File \"{path}\" -DisableRemediation";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardError = true;
                    process.EnableRaisingEvents = true; // Required for Exited event

                    try
                    {
                        process.Start();

                        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
                        process.Exited += (sender, args) =>
                        {
                            tcs.SetResult(true);
                        };

                        Task timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(2500), cancellationToken);
                        Task completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                        if (completedTask == timeoutTask)
                        {
                            throw new TimeoutException("Timeout waiting for MpCmdRun.exe to return");
                        }
                        else if (process.ExitCode != 0)
                        {
                            string errorMessage = await process.StandardError.ReadToEndAsync();
                            throw new Exception($"An error occurred while running Windows Defender: {errorMessage}");
                        }

                        return process.ExitCode == 1;
                    }
                    catch (InvalidOperationException ex)
                    {
                        _isDefenderAvailable = false; //disable future attempts
                        throw new InvalidOperationException("Failed to start MpCmdRun.exe", ex);
                    }
                    finally
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(); //always kill the process if it's still running
                        }
                    }
                }
            }
            finally
            {
                _lock.Release();
                File.Delete(path); //cleanup temp file
            }
        }
    }
}
/*
| Exit code | Description |
|---|---|---|
| 0 | No malware was found or malware was successfully remediated and no additional user action is required. |
| 1 | Malware was found and remediated successfully. |
| 2 | Malware was found and not remediated or additional user action is required to complete remediation or there is error in scanning. |
| 3 | An invalid command was used. |
| 4 | A required parameter was missing. |
| 5 | An invalid parameter value was used. |
| 6 | The specified file or directory does not exist. |
| 7 | The specified file or directory is locked by another process. |
| 8 | The specified file or directory is too large to scan. |
| 9 | The specified file or directory is corrupted. |
| 10 | The specified file or directory is not accessible. |
| 11 | An unexpected error occurred while scanning the file or directory. |
| 12 | Windows Defender was unable to start. |
| 13 | Windows Defender was unable to load the malware signatures. |
| 14 | Windows Defender was unable to update the malware signatures. |
 */


/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Meta.XR.RuntimeOptimizer.Editor
{
    /// <summary>Provides a wrapper around the Android Debug Bridge (ADB) command-line tool for managing devices and executing commands.</summary>
    public class OVRADBTool
    {
        public bool isReady;

        public string androidSdkRoot;
        public string androidPlatformToolsPath;
        public string adbPath;

        public OVRADBTool(string androidSdkRoot)
        {
            if (!String.IsNullOrEmpty(androidSdkRoot))
            {
                this.androidSdkRoot = androidSdkRoot;
            }
            else
            {
                this.androidSdkRoot = String.Empty;
            }

            if (this.androidSdkRoot.EndsWith("\\") || this.androidSdkRoot.EndsWith("/"))
            {
                this.androidSdkRoot = this.androidSdkRoot.Remove(this.androidSdkRoot.Length - 1);
            }

            androidPlatformToolsPath = Path.Combine(this.androidSdkRoot, "platform-tools");
#if UNITY_EDITOR_OSX
        adbPath = Path.Combine(androidPlatformToolsPath, "adb");
#else
            adbPath = Path.Combine(androidPlatformToolsPath, "adb.exe");
#endif
            isReady = File.Exists(adbPath);
        }

        /// <summary>Checks whether the given Android SDK root path contains a valid ADB executable.</summary>
        /// <param name="androidSdkRoot">The root path of the Android SDK installation.</param>
        /// <returns><c>true</c> if the ADB executable exists at the expected location within the SDK root; otherwise, <c>false</c>.</returns>
        public static bool IsAndroidSdkRootValid(string androidSdkRoot)
        {
            OVRADBTool tool = new OVRADBTool(androidSdkRoot);
            return tool.isReady;
        }

        /// <summary>Callback invoked repeatedly while waiting for an ADB process to exit, allowing the caller to perform work such as updating UI.</summary>
        public delegate void WaitingProcessToExitCallback();

        /// <summary>Starts the ADB server by running the <c>start-server</c> command.</summary>
        /// <param name="waitingProcessToExitCallback">Optional callback invoked repeatedly while waiting for the process to exit.</param>
        /// <returns>The exit code of the ADB process.</returns>
        public int StartServer(WaitingProcessToExitCallback waitingProcessToExitCallback)
        {
            string outputString;
            string errorString;

            int exitCode = RunCommand(new string[] { "start-server" }, waitingProcessToExitCallback, out outputString,
                out errorString);
            return exitCode;
        }

        /// <summary>Stops the ADB server by running the <c>kill-server</c> command.</summary>
        /// <param name="waitingProcessToExitCallback">Optional callback invoked repeatedly while waiting for the process to exit.</param>
        /// <returns>The exit code of the ADB process.</returns>
        public int KillServer(WaitingProcessToExitCallback waitingProcessToExitCallback)
        {
            string outputString;
            string errorString;

            int exitCode = RunCommand(new string[] { "kill-server" }, waitingProcessToExitCallback, out outputString,
                out errorString);
            return exitCode;
        }

        /// <summary>Forwards a local TCP port to the same port on the connected device using <c>adb forward</c>.</summary>
        /// <param name="port">The TCP port number to forward.</param>
        /// <param name="waitingProcessToExitCallback">Optional callback invoked repeatedly while waiting for the process to exit.</param>
        /// <returns>The exit code of the ADB process.</returns>
        public int ForwardPort(int port, WaitingProcessToExitCallback waitingProcessToExitCallback)
        {
            string outputString;
            string errorString;

            string portString = string.Format("tcp:{0}", port);

            int exitCode = RunCommand(new string[] { "forward", portString, portString }, waitingProcessToExitCallback,
                out outputString, out errorString);
            return exitCode;
        }

        /// <summary>Removes a previously forwarded TCP port using <c>adb forward --remove</c>.</summary>
        /// <param name="port">The TCP port number to stop forwarding.</param>
        /// <param name="waitingProcessToExitCallback">Optional callback invoked repeatedly while waiting for the process to exit.</param>
        /// <returns>The exit code of the ADB process.</returns>
        public int ReleasePort(int port, WaitingProcessToExitCallback waitingProcessToExitCallback)
        {
            string outputString;
            string errorString;

            string portString = string.Format("tcp:{0}", port);

            int exitCode = RunCommand(new string[] { "forward", "--remove", portString }, waitingProcessToExitCallback,
                out outputString, out errorString);
            return exitCode;
        }

        /// <summary>Returns a list of serial numbers for all connected ADB devices.</summary>
        /// <returns>A list of device serial number strings.</returns>
        public List<string> GetDevices()
        {
            return new List<string>(GetDevicesWithStatus().Keys);
        }

        /// <summary>
        /// Returns a dictionary mapping device serial numbers to their connection status strings.
        /// </summary>
        /// <returns>A dictionary of device serial numbers to status strings (e.g., "device", "offline").</returns>
        public Dictionary<string, string> GetDevicesWithStatus()
        {
            string outputString;
            string errorString;

            RunCommand(new string[] { "devices" }, null, out outputString, out errorString);
            string[] devices = outputString.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

            List<string> deviceList = new List<string>(devices);

            Dictionary<string, string> deviceStatuses = new Dictionary<string, string>(deviceList.Count);
            if (!deviceList.Any())
            {
                return deviceStatuses;
            }
            deviceList.RemoveAt(0);

            for (int i = 0; i < deviceList.Count; i++)
            {
                string deviceItem = deviceList[i];
                int index = deviceItem.IndexOf('\t');
                if (index >= 0)
                {
                    string deviceName = deviceItem.Substring(0, index);
                    string deviceStatus = deviceItem.Substring(index + 1);
                    deviceStatuses.Add(deviceName, deviceStatus);
                }
                else
                    deviceList[i] = "";
            }

            return deviceStatuses;
        }

        private StringBuilder outputStringBuilder = null;
        private StringBuilder errorStringBuilder = null;

        /// <summary>Runs an ADB command synchronously and captures its standard output and error streams.</summary>
        /// <param name="arguments">The command-line arguments to pass to ADB.</param>
        /// <param name="waitingProcessToExitCallback">Optional callback invoked repeatedly while waiting for the process to exit.</param>
        /// <param name="outputString">The captured standard output of the process.</param>
        /// <param name="errorString">The captured standard error of the process.</param>
        /// <param name="stdIn">Optional string to write to the process standard input.</param>
        /// <returns>The exit code of the ADB process, or <c>-1</c> if the tool is not ready.</returns>
        public int RunCommand(string[] arguments, WaitingProcessToExitCallback waitingProcessToExitCallback,
            out string outputString, out string errorString, string stdIn = null)
        {
            int exitCode = -1;

            if (!isReady)
            {
                Debug.LogWarning("OVRADBTool not ready");
                outputString = string.Empty;
                errorString = "OVRADBTool not ready";
                return exitCode;
            }

            string args = string.Join(" ", arguments);

            ProcessStartInfo startInfo = new ProcessStartInfo(adbPath, args);
            startInfo.WorkingDirectory = androidSdkRoot;
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;
            if (stdIn != null)
            {
                startInfo.RedirectStandardInput = true;
            }
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            outputStringBuilder = new StringBuilder("");
            errorStringBuilder = new StringBuilder("");

            Process process = Process.Start(startInfo);
            process.OutputDataReceived += new DataReceivedEventHandler(OutputDataReceivedHandler);
            process.ErrorDataReceived += new DataReceivedEventHandler(ErrorDataReceivedHandler);

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (stdIn != null)
            {
                StreamWriter StreamIn = process.StandardInput;
                StreamIn.WriteLine(stdIn);
                StreamIn.Close();
            }

            try
            {
                do
                {
                    if (waitingProcessToExitCallback != null)
                    {
                        waitingProcessToExitCallback();
                    }
                } while (!process.WaitForExit(100));
                // don't get stock forever if something goes wrong
                process.WaitForExit(60 * 1000);
            }
            catch (Exception e)
            {
                Debug.LogWarningFormat("[OVRADBTool.RunCommand] exception {0}", e.Message);
            }

            exitCode = process.ExitCode;

            process.Close();
            outputString = outputStringBuilder?.ToString() ?? string.Empty;
            errorString = errorStringBuilder?.ToString() ?? string.Empty;
            outputStringBuilder = null;
            errorStringBuilder = null;

            return exitCode;
        }

        /// <summary>Runs an ADB command asynchronously, returning the running process for the caller to manage.</summary>
        /// <param name="arguments">The command-line arguments to pass to ADB.</param>
        /// <param name="outputDataRecievedHandler">Optional handler invoked when a line of output data is received from the process.</param>
        /// <returns>The started <see cref="Process"/>, or <c>null</c> if the tool is not ready.</returns>
        public Process RunCommandAsync(string[] arguments, DataReceivedEventHandler outputDataRecievedHandler)
        {
            if (!isReady)
            {
                Debug.LogWarning("OVRADBTool not ready");
                return null;
            }

            string args = string.Join(" ", arguments);

            ProcessStartInfo startInfo = new ProcessStartInfo(adbPath, args);
            startInfo.WorkingDirectory = androidSdkRoot;
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            Process process = Process.Start(startInfo);
            if (outputDataRecievedHandler != null)
            {
                process.OutputDataReceived += new DataReceivedEventHandler(outputDataRecievedHandler);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }

        /// <summary>Attempts to retrieve a system property from a device using <c>adb shell getprop</c>, returning a default value on failure.</summary>
        /// <param name="device">The serial number of the target device.</param>
        /// <param name="property">The name of the system property to retrieve.</param>
        /// <param name="defaultValue">The value to return if the property cannot be retrieved.</param>
        /// <param name="value">The retrieved property value, or <paramref name="defaultValue"/> if the command fails or returns empty.</param>
        /// <returns><c>true</c> if the ADB command executed successfully; otherwise, <c>false</c>.</returns>
        public bool TryGetSystemProperty(string device, string property, string defaultValue,
            out string value)
        {
            if (RunCommand(new[]
                {
                "-s", device,
                "shell", "getprop", property
            }, null, out var stdout, out _) != 0)
            {
                value = defaultValue;
                return false;
            }

            stdout = stdout?.Trim();
            value = string.IsNullOrEmpty(stdout) ? defaultValue : stdout;
            return true;
        }

        /// <summary>Attempts to retrieve a system property from a device as an integer using <c>adb shell getprop</c>.</summary>
        /// <param name="device">The serial number of the target device.</param>
        /// <param name="property">The name of the system property to retrieve.</param>
        /// <param name="value">The parsed integer value of the property, or <c>0</c> if retrieval or parsing fails.</param>
        /// <returns><c>true</c> if the property was successfully retrieved and parsed as an integer; otherwise, <c>false</c>.</returns>
        public bool TryGetSystemProperty(string device, string property, out int value)
        {
            value = 0;
            return TryGetSystemProperty(device, property, "0", out var strValue) &&
                   int.TryParse(strValue, out value);
        }

        private void OutputDataReceivedHandler(object sendingProcess, DataReceivedEventArgs args)
        {
            // Collect the sort command output.
            if (!string.IsNullOrEmpty(args.Data))
            {
                // Add the text to the collected output.
                outputStringBuilder.Append(args.Data);
                outputStringBuilder.Append(Environment.NewLine);
            }
        }

        private void ErrorDataReceivedHandler(object sendingProcess, DataReceivedEventArgs args)
        {
            // Collect the sort command output.
            if (!string.IsNullOrEmpty(args.Data))
            {
                // Add the text to the collected output.
                errorStringBuilder.Append(args.Data);
                errorStringBuilder.Append(Environment.NewLine);
            }
        }
    }
}

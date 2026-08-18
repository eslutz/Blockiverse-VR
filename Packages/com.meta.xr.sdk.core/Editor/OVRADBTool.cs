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
using Meta.XR.Telemetry;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Wrapper around the Android Debug Bridge (ADB) command-line tool for communicating with connected Android/Quest devices.
/// Use this to run ADB commands, query connected devices, forward ports, and read device system properties from the Unity Editor.
/// </summary>
public class OVRADBTool
{
    public bool isReady;

    public string androidSdkRoot;
    public string androidPlatformToolsPath;
    public string adbPath;

    /// <summary>
    /// Initializes the ADB tool with the given Android SDK root path and validates that the adb executable exists.
    /// </summary>
    /// <param name="androidSdkRoot">Absolute path to the Android SDK root directory containing the platform-tools folder.</param>
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

    /// <summary>
    /// Checks whether the given Android SDK root path contains a valid ADB executable.
    /// </summary>
    /// <param name="androidSdkRoot">Absolute path to the Android SDK root directory.</param>
    /// <returns><c>true</c> if the ADB executable exists at the expected location within the SDK root; otherwise <c>false</c>.</returns>
    public static bool IsAndroidSdkRootValid(string androidSdkRoot)
    {
        OVRADBTool tool = new OVRADBTool(androidSdkRoot);
        return tool.isReady;
    }

    /// <summary>
    /// Callback invoked periodically while waiting for an ADB process to exit, allowing callers to update progress UI or check for cancellation.
    /// </summary>
    public delegate void WaitingProcessToExitCallback();

    /// <summary>
    /// Starts the ADB server process.
    /// </summary>
    /// <param name="waitingProcessToExitCallback">Optional callback invoked repeatedly while waiting for the process to exit. May be <c>null</c>.</param>
    /// <returns>The ADB process exit code. 0 indicates success.</returns>
    public int StartServer(WaitingProcessToExitCallback waitingProcessToExitCallback)
    {
        string outputString;
        string errorString;

        int exitCode = RunCommand(new string[] { "start-server" }, waitingProcessToExitCallback, out outputString,
            out errorString);
        return exitCode;
    }

    /// <summary>
    /// Kills the running ADB server process.
    /// </summary>
    /// <param name="waitingProcessToExitCallback">Optional callback invoked repeatedly while waiting for the process to exit. May be <c>null</c>.</param>
    /// <returns>The ADB process exit code. 0 indicates success.</returns>
    public int KillServer(WaitingProcessToExitCallback waitingProcessToExitCallback)
    {
        string outputString;
        string errorString;

        int exitCode = RunCommand(new string[] { "kill-server" }, waitingProcessToExitCallback, out outputString,
            out errorString);
        return exitCode;
    }

    /// <summary>
    /// Forwards a TCP port from the host machine to the connected device.
    /// </summary>
    /// <param name="port">The TCP port number to forward (used for both host and device sides).</param>
    /// <param name="waitingProcessToExitCallback">Optional callback invoked repeatedly while waiting for the process to exit. May be <c>null</c>.</param>
    /// <returns>The ADB process exit code. 0 indicates success.</returns>
    public int ForwardPort(int port, WaitingProcessToExitCallback waitingProcessToExitCallback)
    {
        string outputString;
        string errorString;

        string portString = string.Format("tcp:{0}", port);

        int exitCode = RunCommand(new string[] { "forward", portString, portString }, waitingProcessToExitCallback,
            out outputString, out errorString);
        return exitCode;
    }

    /// <summary>
    /// Releases a previously forwarded TCP port.
    /// </summary>
    /// <param name="port">The TCP port number to release.</param>
    /// <param name="waitingProcessToExitCallback">Optional callback invoked repeatedly while waiting for the process to exit. May be <c>null</c>.</param>
    /// <returns>The ADB process exit code. 0 indicates success.</returns>
    public int ReleasePort(int port, WaitingProcessToExitCallback waitingProcessToExitCallback)
    {
        string outputString;
        string errorString;

        string portString = string.Format("tcp:{0}", port);

        int exitCode = RunCommand(new string[] { "forward", "--remove", portString }, waitingProcessToExitCallback,
            out outputString, out errorString);
        return exitCode;
    }

    /// <summary>
    /// Returns a list of serial numbers for all connected ADB devices.
    /// </summary>
    /// <returns>A list of device serial number strings. Empty if no devices are connected.</returns>
    public List<string> GetDevices()
    {
        return new List<string>(GetDevicesWithStatus().Keys);
    }

    /// <summary>
    /// Returns a dictionary mapping device serial numbers to their connection status (e.g., "device", "unauthorized").
    /// </summary>
    /// <returns>A dictionary where keys are device serial numbers and values are status strings. Empty if no devices are connected.</returns>
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

    /// <summary>
    /// Executes an ADB command synchronously and returns the exit code, capturing stdout and stderr output.
    /// </summary>
    /// <param name="arguments">The ADB command arguments to execute.</param>
    /// <param name="waitingProcessToExitCallback">Optional callback invoked repeatedly while waiting for the process to exit. May be <c>null</c>.</param>
    /// <param name="outputString">Receives the captured standard output from the ADB process.</param>
    /// <param name="errorString">Receives the captured standard error from the ADB process.</param>
    /// <param name="stdIn">Optional string to write to the process standard input. Pass <c>null</c> to skip.</param>
    /// <returns>The ADB process exit code. 0 indicates success; -1 if the tool is not ready.</returns>
    public int RunCommand(string[] arguments, WaitingProcessToExitCallback waitingProcessToExitCallback,
        out string outputString, out string errorString, string stdIn = null)
    {
        int exitCode = -1;

        if (!isReady)
        {
            IssueTracker.TrackWarning(IssueTracker.SDK.Core, "ovr-adb-tool-not-ready",
                "OVRADBTool not ready");
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

            process.WaitForExit();
        }
        catch (Exception e)
        {
            IssueTracker.TrackWarning(IssueTracker.SDK.Core, "ovr-adb-tool-command-exception", e);
        }

        exitCode = process.ExitCode;

        process.Close();

        outputString = outputStringBuilder.ToString();
        errorString = errorStringBuilder.ToString();

        outputStringBuilder = null;
        errorStringBuilder = null;

        return exitCode;
    }

    /// <summary>
    /// Executes an ADB command asynchronously, returning the running Process. Output is delivered via the provided event handler.
    /// </summary>
    /// <param name="arguments">The ADB command arguments to execute.</param>
    /// <param name="outputDataRecievedHandler">Event handler called when output data is received from the process. May be <c>null</c>.</param>
    /// <returns>The running <see cref="Process"/> instance, or <c>null</c> if the tool is not ready.</returns>
    public Process RunCommandAsync(string[] arguments, DataReceivedEventHandler outputDataRecievedHandler)
    {
        if (!isReady)
        {
            IssueTracker.TrackWarning(IssueTracker.SDK.Core, "ovr-adb-tool-not-ready-async",
                "OVRADBTool not ready");
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

    /// <summary>
    /// Attempts to read a system property from the specified device via "adb shell getprop". Returns false and uses the default value on failure.
    /// </summary>
    /// <param name="device">The device serial number to target.</param>
    /// <param name="property">The system property name to read (e.g., "ro.build.version.sdk").</param>
    /// <param name="defaultValue">The value to use if the property cannot be read.</param>
    /// <param name="value">Receives the property value, or <paramref name="defaultValue"/> on failure.</param>
    /// <returns><c>true</c> if the property was successfully read; otherwise <c>false</c>.</returns>
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

    /// <summary>
    /// Attempts to read an integer system property from the specified device. Returns false if the property cannot be read or parsed.
    /// </summary>
    /// <param name="device">The device serial number to target.</param>
    /// <param name="property">The system property name to read.</param>
    /// <param name="value">Receives the parsed integer value, or 0 on failure.</param>
    /// <returns><c>true</c> if the property was successfully read and parsed as an integer; otherwise <c>false</c>.</returns>
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

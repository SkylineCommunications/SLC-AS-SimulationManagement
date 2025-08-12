/*
****************************************************************************
*  Copyright (c) 2022,  Skyline Communications NV  All Rights Reserved.    *
****************************************************************************

By using this script, you expressly agree with the usage terms and
conditions set out below.
This script and all related materials are protected by copyrights and
other intellectual property rights that exclusively belong
to Skyline Communications.

A user license granted for this script is strictly for personal use only.
This script may not be used in any way by anyone without the prior
written consent of Skyline Communications. Any sublicensing of this
script is forbidden.

Any modifications to this script by the user are only allowed for
personal use and within the intended purpose of the script,
and will remain the sole responsibility of the user.
Skyline Communications will not be responsible for any damages or
malfunctions whatsoever of the script resulting from a modification
or adaptation by the user.

The content of this script is confidential information.
The user hereby agrees to keep this confidential information strictly
secret and confidential and not to disclose or reveal it, in whole
or in part, directly or indirectly to any person, entity, organization
or administration without the prior written consent of
Skyline Communications.

Any inquiries can be addressed to:

    Skyline Communications NV
    Ambachtenstraat 33
    B-8870 Izegem
    Belgium
    Tel.    : +32 51 31 35 69
    Fax.    : +32 51 31 01 29
    E-mail    : info@skyline.be
    Web        : www.skyline.be
    Contact    : Ben Vandenberghe

****************************************************************************
Revision History:

DATE        VERSION        AUTHOR            COMMENTS

12/07/2022    1.0.0.1        JLE, Skyline    Initial Version
****************************************************************************
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;

using Jle.StartQASnmpSimulation.General;

using RT_QASNMPAgent_Helper;

using Skyline.DataMiner.Automation;

using SLNetMessages = Skyline.DataMiner.Net.Messages;
using System.Diagnostics;
using System.Xml;
using System.Threading;

//---------------------------------
// StartQASnmpSimulation.cs
//---------------------------------

public class Script
{
    public static readonly string ScriptName = "StartQASnmpSimulation";
    public static readonly string Directory = @"C:\Skyline_Data";

    public void Run(Engine engine)
    {
        var startTime = DateTime.Now;
        string readableDate = startTime.ToString("yyyy_MM_dd_HH_mm_ss");

        try
        {
            string simulationName = engine.GetScriptParam("SimulationName").Value;

            string subDirectory = Path.Combine(Directory, ScriptName);
            System.IO.Directory.CreateDirectory(subDirectory);

            Log log = new Log(Path.Combine(subDirectory, ScriptName + readableDate + ".txt"), engine, debug: true, logIfSuccess: false);
            log.WriteLine(Log.Level.INFO, "Script started");

            SNMPAgentHelper snmpAgent = new SNMPAgentHelper(log);
            var simulations = snmpAgent.GetAvailableSimulations();

            if (!snmpAgent.IsSimulationAvailable(simulationName))
            {
                log.WriteLine(Log.Level.ERROR, "Simulation is not present! " + simulationName);
                engine.ExitFail("Simulation is not present! " + simulationName);
                return;
            }

            snmpAgent.StartSimulations(new[] { simulationName });
        }
        catch (Exception e)
        {
            engine.ExitFail("My Unexpected exception: " + e.ToString());
        }
    }
}
//---------------------------------
// General\Log.cs
//---------------------------------
namespace Jle.StartQASnmpSimulation.General
{
    using System;
    using System.IO;
    using System.Text;

    using Skyline.DataMiner.Automation;

    public class Log
    {
        private StreamWriter file;
        private StringBuilder stringBuilder;

        public Log(String logFileName, Engine engine, bool debug, bool logIfSuccess)
        {
            this.Engine = engine;
            this.Debug = debug;
            this.LogIfSuccess = logIfSuccess;
            stringBuilder = new StringBuilder();
            file = new StreamWriter(logFileName, false);
        }

        public enum Level
        {
            DEBUG,
            INFO,
            ERROR,
            WARN,
            SUCCESS,
        }

        public Engine Engine { get; set; }

        public bool Debug { get; set; }

        public bool LogIfSuccess { get; set; }

        public string Data => stringBuilder.ToString();

        public bool HasLoggedFailure { get; private set; }

        public void Close()
        {
            if (file != null)
            {
                file.Write(stringBuilder.ToString());
                file.Close();
            }
        }

        public Log WriteLine(Level level, String line)
        {
            string timeStamp = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.ff");
            string textLine = $"{timeStamp}|[{level}]|{line}";

            if (Debug)
            {
                Engine.GenerateInformation(textLine);
            }

            if ((level == Level.SUCCESS && LogIfSuccess) || level != Level.SUCCESS)
            {
                stringBuilder.AppendLine(textLine);
            }

            HasLoggedFailure |= level == Level.ERROR;

            return this;
        }

        public Log WriteLine()
        {
            stringBuilder.AppendLine();
            return this;
        }
    }
}
//---------------------------------
// RT_QASNMPAgent_Helper\AgentProcess.cs
//---------------------------------

namespace RT_QASNMPAgent_Helper
{
    /// <summary>
    /// A wrapper for the Process running the SNMP Agent for safer control.
    /// </summary>
    public class AgentProcess : IDisposable
    {
        public readonly Process m_process;
        private Boolean m_closed;
        private Boolean m_killed;

        public AgentProcess(Process process)
        {
            m_process = process;
        }

        /// <summary>
        /// Releases all resources used by the component, does not explicitly kill or stop the process.
        /// </summary>
        public void Dispose()
        {
            m_process?.Dispose();
        }

        /// <summary>
        /// Stops and frees the process by calling Kill() and Close(), making the Process handle invalid.
        /// </summary>
        public void Stop()
        {
            Kill();
            Close();
        }

        /// <summary>
        /// Closes the main window, allows HasExited() to be called on the Process object.
        /// Intended for unit testing.
        /// </summary>
        public void Kill()
        {
            if (m_killed)
                return;

            try
            {
                m_process.Kill();
            }
            catch (InvalidOperationException e)
                when (e.Message.Contains("Cannot process request because the process has exited."))
            {
                // ignore this error
            }

            m_killed = true;
        }

        private void Close()
        {
            if (m_closed)
                return;

            m_closed = true;
            m_process.Close();
        }

        /// <summary>
        /// Checks if the process has exited.
        /// </summary>
        /// <returns><i>true</i> if the process has exited, also returns true if there is no process or it is closed.</returns>
        public Boolean HasExited()
        {
            if (m_process != null && !m_closed)
                return m_process.HasExited;

            return true;
        }
    }
}

//---------------------------------
// RT_QASNMPAgent_Helper\SimulationInfo.cs
//---------------------------------

namespace RT_QASNMPAgent_Helper
{
    public class SimulationInfo
    {
        public String Name { get; private set; }
        public AgentInfo[] Agents { get; private set; }

        public static bool TryParse(string folderPath, string simulationName, out SimulationInfo info)
        {
            var fullSimulationPath = folderPath + simulationName;
            List<AgentInfo> agents = new List<AgentInfo>(2);

            try
            {
                using (var xmlReader = XmlReader.Create(fullSimulationPath, new XmlReaderSettings { CheckCharacters = false }))
                {
                    xmlReader.MoveToContent();

                    while (xmlReader.Read())
                    {
                        if (xmlReader.NodeType != XmlNodeType.Element
                            || xmlReader.Name != "Agent"
                            || !AgentInfo.TryParse(xmlReader, out var agent))
                        {
                            continue;
                        }

                        agents.Add(agent);
                    }
                }
            }
            catch (Exception)
            {
                info = null;
                return false;
            }

            info = new SimulationInfo { Name = simulationName, Agents = agents.ToArray() };

            return true;
        }
    }

    public class AgentInfo
    {
        public String Name { get; private set; }
        public String IP { get; private set; }
        public Int16 SNMPVersion { get; private set; }
        public UInt16 Port { get; private set; }
        public UInt16[] Ports { get; private set; }

        private AgentInfo()
        {
            SNMPVersion = -1;
        }

        public static bool TryParse(XmlReader reader, out AgentInfo info)
        {
            try
            {
                info = new AgentInfo();
                while (reader.MoveToNextAttribute())
                {
                    // Get element name and switch on it.
                    switch (reader.Name)
                    {
                        case "Name":
                            info.Name = reader.Value;
                            break;
                        case "ip":
                            info.IP = reader.Value;
                            break;
                        case "SNMPVersion":
                            if (Int16.TryParse(reader.Value, out var tempVer))
                            {
                                info.SNMPVersion = tempVer;
                            }
                            break;
                        case "Port":
                            var sPort = reader.Value;
                            var rangeIndex = sPort.IndexOf('-');

                            if (rangeIndex >= 0)
                            {
                                var startPort = sPort.Substring(0, rangeIndex);
                                var endPort = sPort.Substring(rangeIndex + 1);


                                if (UInt16.TryParse(startPort, out var tempStartPort)
                                    && UInt16.TryParse(endPort, out var tempEndPort))
                                {
                                    info.Port = tempStartPort;

                                    var range = tempEndPort - tempStartPort + 1;
                                    info.Ports = new ushort[range];
                                    for (var i = 0; i < range; i++)
                                    {
                                        info.Ports[i] = (UInt16)(tempStartPort + i);
                                    }
                                }
                            }
                            if (UInt16.TryParse(sPort, out var tempPort))
                            {
                                info.Port = tempPort;
                            }
                            break;
                    }
                }
            }
            catch (Exception)
            {
                info = null;
                return false;
            }

            return true;
        }
    }
}

//---------------------------------
// RT_QASNMPAgent_Helper\SNMPAgentHelper.cs
//---------------------------------

namespace RT_QASNMPAgent_Helper
{
    public static class GlobalRTConsts
    {
        public const String RootFolder = @"C:\RTManager\";

        public const String GlobalDependenciesFolder = RootFolder + @"GlobalDependencies\";
        public const String TeamDependenciesFolder = RootFolder + @"TeamDependencies\";
        public const String TestDependenciesFolder = RootFolder + @"TestDependencies\";
    }

    public class SNMPAgentHelper
    {
        public const String SimulationsFolder = @"C:\QASNMPSimulations\";
        public const String AgentsFolder = GlobalRTConsts.GlobalDependenciesFolder + @"QASNMPAgent\";
        public const String AgentFile = @"QASNMPAgent.exe";
        public const String DeviceSimulatorFolder = GlobalRTConsts.GlobalDependenciesFolder + @"QADeviceSimulator\";
        public const String DeviceSimulatorFile = @"QADeviceSimulator.exe";
        public const String LatestDeviceSimulator = DeviceSimulatorFolder + DeviceSimulatorFile;

        private Log m_logger;

        /// <summary>
        /// Creates an SNMP Agent helper that will log problems to the provided logger.
        /// </summary>
        /// <param name="logger"></param>
        public SNMPAgentHelper(Log log)
        {
            m_logger = log;
        }

        // Get available agent versions (folder names)

        /// <summary>
        /// Looks for any available Agents in the global dependencies folder and returns their version (folder) name
        /// </summary>
        /// <returns>Versions of available SNMP Agents</returns>
        public IEnumerable<String> GetAgentVersions()
        {
            if (!Directory.Exists(AgentsFolder))
            {
                LogDebug("SNMP Agents folder ({0}) does not exist yet in the global dependencies.", AgentsFolder);
                yield break;
            }

            foreach (var dir in Directory.EnumerateDirectories(AgentsFolder))
            {
                if (File.Exists(dir + @"\" + AgentFile) || File.Exists(dir + @"\" + DeviceSimulatorFile))
                {
                    yield return dir.Substring(dir.LastIndexOf('\\') + 1);
                }
            }
        }

        /// <summary>
        /// Checks if the agent executable is available for the provided version.
        /// </summary>
        /// <param name="agentVersion">The version to check.</param>
        /// <returns><i>false</i> if the agent is not available or the version could not be validated.</returns>
        public Boolean IsAgentVersionAvailable(string agentVersion)
        {
            if (agentVersion.Contains("\\") || agentVersion.Contains("/"))
            {
                LogInfo("Agent version '{0}' should not contain slashes.", agentVersion);
                return false;
            }

            var fullPath = GetAgentFullPath(agentVersion);

            if (!File.Exists(fullPath))
            {
                LogInfo("Neither DeviceSimulator or Agent executable file could be found. (\"{0}\" or \"{1}\")", GetQASNMPAgentFullPath(agentVersion), GetDeviceSimulatorFullPath(agentVersion));
                return false;
            }

            LogDebug("Found simulator '{0}'.", fullPath);

            return true;
        }

        private static String GetAgentFullPath(string agentVersion)
        {
            var fullPath = GetDeviceSimulatorFullPath(agentVersion);

            if (!File.Exists(fullPath))
            {
                fullPath = GetQASNMPAgentFullPath(agentVersion);
            }

            return fullPath;
        }

        private static String GetQASNMPAgentFullPath(string agentVersion)
        {
            return AgentsFolder + agentVersion + "\\" + AgentFile;
        }

        private static String GetDeviceSimulatorFullPath(string agentVersion)
        {
            return AgentsFolder + agentVersion + "\\" + DeviceSimulatorFile;
        }

        /// <summary>
        /// Looks for any available SNMP Simulations in the fixed simulations folder and returns their file name.
        /// </summary>
        /// <returns>File names of SNMP Simulations</returns>
        public IEnumerable<String> GetAvailableSimulations()
        {
            if (!Directory.Exists(SimulationsFolder))
            {
                LogDebug("SNMP Simulations folder ({0}) does not exist.", SimulationsFolder);
                yield break;
            }

            foreach (var simulation in Directory.EnumerateFiles(
                SimulationsFolder, "*.xml", SearchOption.TopDirectoryOnly))
            {
                yield return simulation.Substring(simulation.LastIndexOf('\\') + 1);
            }
        }

        /// <summary>
        /// Checks the validity of the file name and if the File exists.
        /// </summary>
        /// <param name="simulationFileName">Simulation file name to check.</param>
        /// <returns><i>true</i> if available</returns>
        public Boolean IsSimulationAvailable(string simulationFileName)
        {
            if (!IsValidSimulationFileName(simulationFileName))
            {
                return false;
            }

            return File.Exists(SimulationsFolder + simulationFileName);
        }

        /// <summary>
        /// Opens the simulation and returns some basic information.
        /// </summary>
        /// <param name="simulationFileName">The simulation to open.</param>
        /// <returns><i>null</i> if information could not be read for some reason.</returns>
        public SimulationInfo GetSimulationInfo(string simulationFileName)
        {
            if (!IsValidSimulationFileName(simulationFileName))
            {
                return null;
            }

            if (!SimulationInfo.TryParse(SimulationsFolder, simulationFileName, out var info))
            {
                LogInfo("Failed to parse the simulation file '{0}', make sure the syntax is correct.", simulationFileName);
                return null;
            }

            return info;
        }

        private Boolean IsValidSimulationFileName(string simulationFileName)
        {
            if (simulationFileName.Contains(@"\")
                || simulationFileName.Contains("/"))
            {
                LogInfo("Simulation file name '{0}' should not contain slashes.", simulationFileName);
                return false;
            }

            if (!simulationFileName.ToLower().EndsWith(".xml"))
            {
                LogInfo("Simulation file name '{0}' should end with '.xml'.", simulationFileName);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Tries to copy the simulation from the test's dependencies folder to the QASNMPSimulations folder.
        /// </summary>
        /// <param name="simulationFileName">The filename (including extension) of the file to copy.</param>
        /// <param name="testName">The name of the test, which determines the folder to copy from</param>
        /// <param name="force">If the simulation file should be overwritten if it already exists.</param>
        /// <returns><i>false</i> if the File.Copy operation failed.</returns>
        public bool TryCopySimulationFromDependencies(string simulationFileName, string testName, bool force = true)
        {
            try
            {
                var sourcePath = Path.Combine(GlobalRTConsts.TestDependenciesFolder, testName, simulationFileName);
                if (!File.Exists(sourcePath))
                {
                    LogInfo("Could not copy simulation file '{0}' because it does not exist.", sourcePath);
                    return false;
                }

                if (!Directory.Exists(SimulationsFolder))
                {
                    Directory.CreateDirectory(SimulationsFolder);
                }

                var destinationPath = Path.Combine(SimulationsFolder, simulationFileName);
                File.Copy(sourcePath, destinationPath, force);
            }
            catch (Exception e)
            {
                LogInfo("Failed to copy the simulation '{0}': {1}", simulationFileName, e.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Tries to start the latest QADeviceSimulator with the specified simulation files. Simulations that could not be validated will be skipped.
        /// Waits up to one minute for the Agent to start up. Waits a second by default because otherwise the process wouldn't stop nicely.
        /// We return the process object so it can be closed if wanted, use process.Stop().
        /// </summary>
        /// <param name="agentVersion">The agent version to start.</param>
        /// <param name="simulationFileNames">The simulation files to run when the agent starts.</param>
        /// <param name="enableLogging">If the log output in the agent should be enabled at start.</param>
        /// <returns>The process handle for the agent.</returns>
        public AgentProcess StartSimulations(string[] simulationFileNames, bool enableLogging = false)
        {
            if (!File.Exists(LatestDeviceSimulator))
            {
                return null;
            }

            return StartSimulationsInternal(LatestDeviceSimulator, simulationFileNames, enableLogging);
        }

        /// <summary>
        /// Tries to start the agent with the specified simulation files. Simulations that could not be validated will be skipped.
        /// Waits up to one minute for the Agent to start up. Waits a second by default because otherwise the process wouldn't stop nicely.
        /// We return the process object so it can be closed if wanted, use process.Stop().
        /// </summary>
        /// <param name="agentVersion">The agent version to start.</param>
        /// <param name="simulationFileNames">The simulation files to run when the agent starts.</param>
        /// <param name="enableLogging">If the log output in the agent should be enabled at start.</param>
        /// <returns>The process handle for the agent.</returns>
        public AgentProcess StartSimulations(string agentVersion, string[] simulationFileNames, bool enableLogging = false)
        {
            if (!IsAgentVersionAvailable(agentVersion))
            {
                return null;
            }

            var agentExecutablePath = GetAgentFullPath(agentVersion);
            return StartSimulationsInternal(agentExecutablePath, simulationFileNames, enableLogging);
        }

        private AgentProcess StartSimulationsInternal(string agentExecutablePath, string[] simulationFileNames,
            bool enableLogging)
        {
            StringBuilder sbArguments = new StringBuilder();

            foreach (var simulation in simulationFileNames)
            {
                if (IsSimulationAvailable(simulation))
                {
                    sbArguments.AppendFormat("\"{0}\" ", simulation);
                }
            }

            if (!enableLogging)
            {
                sbArguments.Append("/d");
            }

            try
            {
                var proc = Process.Start(agentExecutablePath, sbArguments.ToString());

                if (proc == null)
                    return null;

                proc.WaitForInputIdle(59000);

                // This is fucking ugly, but for some reason, the Agent won't close nicely if we don't wait a bit more.
                Thread.Sleep(1000);

                return new AgentProcess(proc);
            }
            catch (Exception e)
            {
                LogInfo("Failed to start simulator process: {0}", e.Message);
                return null;
            }
        }

        private void LogDebug(string format, params object[] args)
        {
            if (m_logger != null)
                m_logger.WriteLine(Log.Level.DEBUG, String.Format(format, args));
        }

        private void LogInfo(string format, params object[] args)
        {
            if (m_logger != null)
                m_logger.WriteLine(Log.Level.INFO, String.Format(format, args));
        }
    }
}

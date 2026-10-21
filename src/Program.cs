using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PclGuard
{
    static class Program
    {
        private const string BaseHash = "30EB93C62FC51EDE0898648E67455478A3D5A4F279938AE65AEC77B84CB9703F";
        private const string EmbeddedResName = "PCL_Baseline.exe";

        class TargetProcessInfo
        {
            public int Pid { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
        }

        [STAThread]
        static void Main(string[] args)
        {
            bool isSilent = false;
            if (args != null)
            {
                foreach (string arg in args)
                {
                    if (arg.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("-s", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("-q", StringComparison.OrdinalIgnoreCase))
                    {
                        isSilent = true;
                        break;
                    }
                }
            }

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                string targetExeName = "Plain Craft Launcher.exe";
                string targetExePath = Path.Combine(baseDir, targetExeName);
                string pclDir = Path.Combine(baseDir, "PCL");
                string iniPath = Path.Combine(pclDir, "Setup.ini");

                // 读取 Setup.ini 获取游戏整合包目录
                string gameFolder = GetGameFolderFromIni(iniPath);

                // 1. 检测正在运行的 PCL 主程序及其拉起的子程序 (如 javaw.exe)
                List<TargetProcessInfo> runningTargets = FindRelatedProcesses(baseDir, targetExePath, gameFolder);

                if (runningTargets.Count > 0 && !isSilent)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("检测到以下正在运行的程序（由 PCL 启动或 PCL 主程序）：");
                    sb.AppendLine();
                    foreach (var target in runningTargets)
                    {
                        sb.AppendFormat("• {0} (PID: {1}) - {2}\n", target.Name, target.Pid, target.Description);
                    }
                    sb.AppendLine();
                    sb.AppendLine("是否要关闭这些程序以继续进行保护与校验？");
                    sb.AppendLine("（点击【是】将安全关闭这些程序；点击【否】将保留运行）");

                    DialogResult dr = MessageBox.Show(
                        sb.ToString(),
                        "pcl-guard - 运行检测与关闭确认",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    if (dr == DialogResult.Yes)
                    {
                        CloseProcessesGracefully(runningTargets);
                    }
                }

                // 2. 检查并锁定 PCL\Setup.ini 中的 SystemSystemUpdate:3
                if (!Directory.Exists(pclDir))
                {
                    Directory.CreateDirectory(pclDir);
                }
                EnsureSetupIni(iniPath);

                // 3. 校验并自愈回退
                bool needRestore = false;
                bool wasMissing = false;
                if (!File.Exists(targetExePath))
                {
                    needRestore = true;
                    wasMissing = true;
                }
                else
                {
                    ClearReadOnly(targetExePath);

                    string currentHash = ComputeFileSha256(targetExePath);
                    if (!string.Equals(currentHash, BaseHash, StringComparison.OrdinalIgnoreCase))
                    {
                        needRestore = true;
                    }
                }

                if (needRestore)
                {
                    if (!wasMissing && File.Exists(targetExePath))
                    {
                        PerformBackup(baseDir, pclDir, targetExePath);
                    }

                    RestoreEmbeddedPcl(targetExePath);
                }

                // 4. 纯守护结束通知 (绝不启动任何主程序)
                if (!isSilent)
                {
                    string msg;
                    if (needRestore)
                    {
                        if (wasMissing)
                        {
                            msg = "PCL 保护与初始化完成！\n\n" +
                                  "• 配置状态：已锁定关闭自动更新 (SystemSystemUpdate:3)\n" +
                                  "• 启动器生成：已从内嵌母本释放基准版本 2.13.1.1";
                        }
                        else
                        {
                            msg = "PCL 保护与版本回退完成！\n\n" +
                                  "• 配置状态：已锁定关闭自动更新 (SystemSystemUpdate:3)\n" +
                                  "• 版本回退：检测到主程序被修改或更新，已自动备份旧文件并恢复至基准版本 2.13.1.1\n" +
                                  "• 备份目录：PCL\\Backup\\";
                        }
                    }
                    else
                    {
                        msg = "PCL 保护与校验完成！\n\n" +
                              "• 配置状态：已锁定关闭自动更新 (SystemSystemUpdate:3)\n" +
                              "• 版本校验：吻合基准版本 2.13.1.1 (未被修改)";
                    }

                    MessageBox.Show(
                        msg,
                        "pcl-guard 保护完成",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
            }
            catch (Exception ex)
            {
                if (!isSilent)
                {
                    MessageBox.Show(
                        "PCL 守护与校验提示:\n" + ex.Message,
                        "pcl-guard 提示",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            }
        }

        private static string GetGameFolderFromIni(string iniPath)
        {
            try
            {
                if (File.Exists(iniPath))
                {
                    string[] lines = File.ReadAllLines(iniPath, Encoding.UTF8);
                    foreach (string line in lines)
                    {
                        if (line.Trim().StartsWith("LaunchFolderSelect:", StringComparison.OrdinalIgnoreCase))
                        {
                            return line.Substring("LaunchFolderSelect:".Length).Trim();
                        }
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        private static List<TargetProcessInfo> FindRelatedProcesses(string baseDir, string targetExePath, string gameFolder)
        {
            Dictionary<int, TargetProcessInfo> matched = new Dictionary<int, TargetProcessInfo>();
            HashSet<int> pclPids = new HashSet<int>();
            int currentPid = Process.GetCurrentProcess().Id;

            // 1. 查找当前目录下运行的 PCL 主程序 (显式 Dispose 释放句柄)
            Process[] allProcs = Process.GetProcesses();
            foreach (Process p in allProcs)
            {
                try
                {
                    if (p.Id != currentPid)
                    {
                        string mainModulePath = string.Empty;
                        try { mainModulePath = p.MainModule.FileName; } catch { }

                        if (!string.IsNullOrEmpty(mainModulePath))
                        {
                            if (string.Equals(mainModulePath, targetExePath, StringComparison.OrdinalIgnoreCase) ||
                                (string.Equals(p.ProcessName, "Plain Craft Launcher", StringComparison.OrdinalIgnoreCase) &&
                                 mainModulePath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)))
                            {
                                pclPids.Add(p.Id);
                                matched[p.Id] = new TargetProcessInfo
                                {
                                    Pid = p.Id,
                                    Name = p.ProcessName + ".exe",
                                    Description = "PCL2 启动器主程序"
                                };
                            }
                        }
                    }
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }

            // 2. 借助 Toolhelp32 查找父进程为 PCL 的子进程
            if (pclPids.Count > 0)
            {
                try
                {
                    Dictionary<int, int> parentMap = GetProcessParentMap();
                    foreach (var kvp in parentMap)
                    {
                        int childPid = kvp.Key;
                        int parentPid = kvp.Value;

                        if (pclPids.Contains(parentPid) && !matched.ContainsKey(childPid))
                        {
                            try
                            {
                                using (Process childProc = Process.GetProcessById(childPid))
                                {
                                    string desc = childProc.ProcessName.ToLower().Contains("java") ? "Minecraft 游戏进程" : "PCL 启动的子程序";
                                    matched[childPid] = new TargetProcessInfo
                                    {
                                        Pid = childPid,
                                        Name = childProc.ProcessName + ".exe",
                                        Description = desc
                                    };
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            // 3. 针对即使 PCL 已关闭、但由其启动的 Minecraft 仍在运行的情况进行命令行关联识别
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, CommandLine FROM Win32_Process WHERE Name = 'javaw.exe' OR Name = 'java.exe'"))
                using (ManagementObjectCollection collection = searcher.Get())
                {
                    foreach (ManagementObject mo in collection)
                    {
                        try
                        {
                            int pid = Convert.ToInt32(mo["ProcessId"]);
                            if (!matched.ContainsKey(pid))
                            {
                                string cmdLine = mo["CommandLine"] as string ?? string.Empty;
                                string procName = mo["Name"] as string ?? "javaw.exe";

                                bool isOurGame = false;
                                if (!string.IsNullOrEmpty(gameFolder) && cmdLine.IndexOf(gameFolder, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    isOurGame = true;
                                }
                                else if (cmdLine.IndexOf(baseDir, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         cmdLine.IndexOf(".minecraft", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    isOurGame = true;
                                }

                                if (isOurGame)
                                {
                                    matched[pid] = new TargetProcessInfo
                                    {
                                        Pid = pid,
                                        Name = procName,
                                        Description = "Minecraft 游戏进程"
                                    };
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            mo.Dispose();
                        }
                    }
                }
            }
            catch { }

            return new List<TargetProcessInfo>(matched.Values);
        }

        private static void CloseProcessesGracefully(List<TargetProcessInfo> targets)
        {
            // 第一步：先发送窗口关闭消息，友好退出
            foreach (var t in targets)
            {
                try
                {
                    using (Process p = Process.GetProcessById(t.Pid))
                    {
                        if (!p.HasExited)
                        {
                            p.CloseMainWindow();
                        }
                    }
                }
                catch { }
            }

            // 等待最长 0.8 秒让进程自行保存退出
            Thread.Sleep(800);

            // 第二步：对于仍未退出的进程强制 Kill，确保释放文件锁
            foreach (var t in targets)
            {
                try
                {
                    using (Process p = Process.GetProcessById(t.Pid))
                    {
                        if (!p.HasExited)
                        {
                            p.Kill();
                            p.WaitForExit(1000);
                        }
                    }
                }
                catch { }
            }
        }

        #region Toolhelp Snapshot
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static Dictionary<int, int> GetProcessParentMap()
        {
            Dictionary<int, int> map = new Dictionary<int, int>();
            IntPtr snapshot = CreateToolhelp32Snapshot(0x00000002, 0); // TH32CS_SNAPPROCESS
            if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return map;

            try
            {
                PROCESSENTRY32 pe = new PROCESSENTRY32();
                pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));

                if (Process32First(snapshot, ref pe))
                {
                    do
                    {
                        map[(int)pe.th32ProcessID] = (int)pe.th32ParentProcessID;
                    } while (Process32Next(snapshot, ref pe));
                }
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return map;
        }
        #endregion

        private static void EnsureSetupIni(string iniPath)
        {
            try
            {
                if (!File.Exists(iniPath))
                {
                    File.WriteAllText(iniPath, "SystemSystemUpdate:3\r\n", new UTF8Encoding(false));
                    return;
                }

                string[] lines = File.ReadAllLines(iniPath, Encoding.UTF8);
                bool found = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith("SystemSystemUpdate:", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = "SystemSystemUpdate:3";
                        found = true;
                    }
                }

                if (!found)
                {
                    Array.Resize(ref lines, lines.Length + 1);
                    lines[lines.Length - 1] = "SystemSystemUpdate:3";
                }

                File.WriteAllLines(iniPath, lines, new UTF8Encoding(false));
            }
            catch { }
        }

        private static string ComputeFileSha256(string filePath)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("X2"));
                }
                return sb.ToString();
            }
        }

        private static void PerformBackup(string baseDir, string pclDir, string targetExePath)
        {
            try
            {
                string backupDir = Path.Combine(pclDir, "Backup");
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }

                string timeTag = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string timeBackupPath = Path.Combine(backupDir, string.Format("Plain Craft Launcher_{0}.exe.bak", timeTag));
                File.Copy(targetExePath, timeBackupPath, true);

                string rootBakPath = Path.Combine(baseDir, "Plain Craft Launcher.exe.bak");
                File.Copy(targetExePath, rootBakPath, true);
            }
            catch { }
        }

        private static void RestoreEmbeddedPcl(string destinationPath)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream resStream = asm.GetManifestResourceStream(EmbeddedResName))
            {
                if (resStream == null)
                {
                    throw new InvalidOperationException("未找到内嵌的 PCL 基准母本资源: " + EmbeddedResName);
                }

                ClearReadOnly(destinationPath);

                using (FileStream destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    resStream.CopyTo(destStream);
                }
            }
        }

        private static void ClearReadOnly(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    FileAttributes attr = File.GetAttributes(filePath);
                    if ((attr & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(filePath, attr & ~FileAttributes.ReadOnly);
                    }
                }
            }
            catch { }
        }
    }
}

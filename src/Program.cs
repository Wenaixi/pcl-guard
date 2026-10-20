using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace PclGuard
{
    static class Program
    {
        private const string BaseHash = "30EB93C62FC51EDE0898648E67455478A3D5A4F279938AE65AEC77B84CB9703F";
        private const string EmbeddedResName = "PCL_Baseline.exe";

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string currentExePath = Application.ExecutablePath;

                // 1. 检查并锁定 PCL\Setup.ini 中的 SystemSystemUpdate:3
                string pclDir = Path.Combine(baseDir, "PCL");
                if (!Directory.Exists(pclDir))
                {
                    Directory.CreateDirectory(pclDir);
                }
                string iniPath = Path.Combine(pclDir, "Setup.ini");
                EnsureSetupIni(iniPath);

                // 2. 确定目标文件名
                string targetExeName = "Plain Craft Launcher.exe";
                if (string.Equals(Path.GetFileName(currentExePath), targetExeName, StringComparison.OrdinalIgnoreCase))
                {
                    targetExeName = "Plain Craft Launcher 2.exe";
                }
                string targetExePath = Path.Combine(baseDir, targetExeName);

                // 3. 校验并自愈回退
                bool needRestore = false;
                if (!File.Exists(targetExePath))
                {
                    needRestore = true;
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
                    if (File.Exists(targetExePath))
                    {
                        PerformBackup(baseDir, pclDir, targetExePath);
                    }

                    RestoreEmbeddedPcl(targetExePath);
                }

                // 4. 启动目标程序并透传命令行参数
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = targetExePath;
                psi.WorkingDirectory = baseDir;
                if (args != null && args.Length > 0)
                {
                    psi.Arguments = string.Join(" ", args);
                }
                psi.UseShellExecute = true;

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "PCL 启动与防更自愈过程中遇到异常:\n" + ex.Message,
                    "pcl-guard",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

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

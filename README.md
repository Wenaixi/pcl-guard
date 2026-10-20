# pcl-guard

PCL2 (Plain Craft Launcher 2) 启动守护程序。内嵌 2.13.1.1 官方版本主程序，通过拦截配置锁定关闭自动更新，并在文件发生非预期更新或被修改时自动回退。

## 工作机制

1. **更新拦截**：启动时读取 `PCL\Setup.ini`，确保 `SystemSystemUpdate` 键值为 `3`。PCL2 在该参数下会关闭所有更新检查与后台下载。文件中的其他配置项保持不变。
2. **哈希校验与版本自愈**：计算同目录下 `Plain Craft Launcher.exe` 的 SHA256。如果文件缺失或哈希不匹配，程序会先备份当前文件，然后从内嵌资源中释放基准母本。
3. **覆盖前自动备份**：触发覆盖回退时，现存文件会被复制到 `PCL\Backup\Plain Craft Launcher_时间戳.exe.bak`，同时在同级目录保留一份 `Plain Craft Launcher.exe.bak`。
4. **标准文件属性**：所有生成与恢复的文件均使用标准系统属性，不附加只读标记。
5. **单文件便携**：程序内嵌完整的启动器母本二进制数据，复制到任何目录都可以直接运行。

## 技术基准

- 内嵌版本：Plain Craft Launcher 2.13.1.1
- 母本体积：5,750,272 字节
- 基准 SHA256：`30EB93C62FC51EDE0898648E67455478A3D5A4F279938AE65AEC77B84CB9703F`
- 运行环境：Windows 7 及更高版本（依赖 .NET Framework 4.5 或以上系统运行时）

## 使用方法

将编译生成的 `pcl-guard.exe` 放入 Minecraft 或 PCL 所在目录直接运行。

首次在新目录运行时，程序会自动生成 `Plain Craft Launcher.exe` 并写入防更配置文件。后续启动会自动拉起游戏启动器，并透传收到的命令行参数。

## 构建说明

本项目使用 Windows 自带的 C# 编译器（csc.exe）编译，不需要安装 Visual Studio。

双击运行根目录下的 `build.bat`，或者在控制台执行：

```bat
build.bat
```

也可以手动调用编译器：

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /out:bin\pcl-guard.exe /win32icon:src\PCL_Icon.ico /resource:"src\Plain Craft Launcher.exe",PCL_Baseline.exe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll src\Program.cs
```

编译产物位于 `bin\pcl-guard.exe`。

## 目录结构

```text
pcl-guard/
├── .gitignore
├── LICENSE
├── README.md
├── build.bat
├── src/
│   ├── Program.cs
│   ├── PCL_Icon.ico
│   └── Plain Craft Launcher.exe
└── bin/
    └── pcl-guard.exe
```

## 许可证

本项目基于 MIT 许可证开源。详见 LICENSE 文件。

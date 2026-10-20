# SWJTU 保活

Windows 托盘小工具，适用于已保存 `SWJTU-WiFi` 配置文件、且访问 `http://192.168.189.136/` 可自动恢复校园网认证的设备。

下载 [Windows 压缩包](https://github.com/Korloa/SWJTU-WIFI-Connector/releases/latest/download/SWJTU-Keeper-win-x64.zip)，解压后运行 `SWJTU-Keeper-win-x64.exe`，无需另装 .NET。

启动后会出现在任务栏折叠图标中。右键图标可开关“自动连接与保活”和“开机自动启动”、立即检查或退出；双击图标可查看状态并修改设置。开机启动默认关闭，可在托盘菜单或设置窗口开启，登录 Windows 后自动运行。开启保活时，程序在 Wi-Fi 断开后尝试使用 Windows 已保存的配置文件重连，并按设定间隔访问认证页。默认间隔 30 秒，可设为 30–300 秒。连接其他 Wi-Fi 时，程序会等待，不会切换当前网络。

首次使用请先通过浏览器正常登录校园网。之后程序静默请求认证地址，绝不会启动浏览器。若请求仍不能恢复认证，托盘会提示需要手动登录。程序不会存储校园网密码。日志和设置位于 `%LOCALAPPDATA%\SWJTU-Keeper\`。

开发构建：`dotnet build -c Release`。生成单文件 Windows 程序：`dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist`。

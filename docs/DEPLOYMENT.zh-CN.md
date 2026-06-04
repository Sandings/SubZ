# SubZ 部署说明

## 推荐命令

在仓库根目录运行：

```powershell
.\scripts\deploy-unraid.ps1
```

脚本会自动完成：

- 构建 `SubZ.Plugin.dll`
- 从仓库上级目录的本地 `deploy_info.md` 读取 Unraid 连接信息
- 优先使用 `deploy_info.md` 中名称包含“备用”或 `backup` 的连接行；如果没有，则使用第一条连接行
- 自动选择 `plink/pscp` 或 `sshpass/ssh/scp`
- 上传 DLL 到 Emby 插件目录
- 重启 Emby 容器

脚本只输出部署目标、是否读取到密码和 host key，不输出密码、完整 `deploy_info.md` 内容或私人服务器地址说明。

## 覆盖目标

需要临时部署到其他地址时，可以显式传参：

```powershell
.\scripts\deploy-unraid.ps1 -RemoteHost example.host.name -RemotePort 22
```

只要没有显式传入 `-RemoteHost` 或 `-RemotePort`，脚本都会从本地 `deploy_info.md` 选择连接行。

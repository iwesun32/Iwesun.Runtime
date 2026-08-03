# Iwesun 专用清理环境

此目录不属于日常构建环境。唯一入口 `Invoke-IwesunRepositoryCleanup.ps1` 只接受：

- `Audit`：只读核对固定源路径、隔离路径和 Runtime 迁移标记；
- `Quarantine`：把固定旧仓库整体移动到 `.archive/retired-repositories/2026-07-22`。

脚本不接受任意路径，不使用通配符，不包含递归删除命令，也不物理删除文件。隔离目录由 `.gitignore`
排除；确认长期不再需要时，应由人工在独立维护窗口处理。

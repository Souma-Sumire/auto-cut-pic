# AutoCutPic

Windows 桌面批量照片裁切工具，专为照片冲印与批量构图设计。

## 下载运行

在 [Releases](https://github.com/Souma-Sumire/auto-cut-pic/releases) 下载 `AutoCutPic-win-x64.zip`，解压即可运行（无需安装环境）。

## 功能特性

- **冲印规格**：内置 3寸、5寸、6寸、7寸、8寸等标准尺寸（300 DPI），支持横竖相纸切换与黑白边检测。
- **免修过滤**：自动识别裁切损耗低于 1.5% 的照片；切换上一张/下一张时自动跳过免修照片。
- **裁剪与留白**：支持单图独立切换“裁剪填充”与“留白完整”，留白模式支持自由调整白边。
- **预导出缓存**：编辑后续照片时后台自动预渲染，最终导出直接复用缓存。
- **工作区保存**：定时自动落盘，支持意外关闭恢复与 `Ctrl+S` 手动保存工程。

## 快捷键

| 按键 / 操作 | 功能 |
| :--- | :--- |
| **拖拽裁切框 / 角手柄** | 调整构图位置与缩放 |
| `W` / `A` / `S` / `D` 或 `方向键` | 微调偏移（`Shift` 10倍，`Ctrl` 贴边） |
| `Q` / `E` | 上一张 / 下一张照片 |
| `F` / `空格` | 切换裁剪填充 / 留白完整 |
| `X` | 翻转相纸方向（横向 / 纵向） |
| `R` | 重置当前照片构图与缩放 |
| `Ctrl + 滚轮` / `Ctrl + 0` | 缩放缩略图卡片 / 恢复默认尺寸 |
| `Ctrl + A` | 全选列表照片 |
| `Ctrl + S` / `Ctrl + O` | 保存工程 / 打开工程 |
| **鼠标中键** | 列表平滑自动滚动 |

## 本地构建

需要 [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
# 运行
dotnet run

# 测试
dotnet test

# 打包单文件独立程序
dotnet publish AutoCutPic.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
```

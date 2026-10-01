# AutoCutPic

基于 .NET 8 (WPF) 与 Magick.NET 开发的批量照片裁剪工具，专为照片冲印场景设计，支持多核并行处理、工业级 300 DPI 输出以及 HEIC/Live Photo 兼容。

---

## 运行环境

- 操作系统：Windows 10 / 11 (x64)
- 运行依赖：[.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)（开发编译需 .NET 8.0 SDK）

---

## 快速开始

### 1. 运行与编译

在项目根目录下使用终端执行：

```powershell
# 直接运行
dotnet run

# 编译 Release 版本
dotnet build -c Release

# 发布独立无依赖可执行文件
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### 2. 使用步骤

1. **导入照片**：直接将单张/多张图片或包含图片的文件夹拖入软件左侧画廊区域（支持递归遍历子文件夹）。
2. **选择规格**：
   - **输出尺寸**：支持 3寸、5寸、6寸（默认）、7寸、8寸（按 300 DPI 严格换算像素）。
   - **裁剪方式**：
     - **裁剪填充**：按目标比例裁切并填满画面。
     - **留白完整**：完整保留原始画面，四周以白色填充留白。
3. **调整画幅位置**：在左侧画廊中选中照片（支持多选），使用键盘快捷键微调裁切中心。
4. **批量导出**：点击右侧“批量开始导出”按钮，程序将并行处理并输出到原图同级目录下的 `Clipped_Photos` 文件夹中。

---

## 快捷键说明

支持在选中一张或多张照片后通过键盘高效微调：

| 快捷键 | 功能说明 |
| :--- | :--- |
| `W` / `A` / `S` / `D` 或 `方向键` | 以 1% 步进微调裁切框偏移 |
| `Shift + WASD` 或 `Shift + 方向键` | 以 10% 步进快速移动裁切框 |
| `Ctrl + WASD` 或 `Ctrl + 方向键` | 瞬间贴边对齐（顶 / 左 / 底 / 右） |
| `Space`（空格键） | 切换裁剪模式（填充 / 留白） |
| `Enter`（回车键） | 开始批量导出 |

---

## 支持格式与特性

- **格式支持**：JPG、JPEG、PNG、WEBP、HEIC、TIFF、BMP，以及封装为 ZIP 的 Live Photo。
- **色彩与质量**：输出固定 300 DPI，JPG Quality 95。
- **自动画幅旋转**：自动识别横构图与竖构图并自适应调整目标输出宽高。
- **极速并发**：导出流程基于多核 `Parallel.ForEach` 并行处理。

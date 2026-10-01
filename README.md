# AutoCutPic

基于 .NET 8 (WPF) 与 Magick.NET 开发的高性能专业桌面批量照片裁剪工具，专为照片冲印场景设计，支持多核并行处理、工业级 300 DPI 像素高精度换算、Photoshop 风格工作台交互以及 HEIC/Live Photo 兼容。

---

## 运行环境

- 操作系统：Windows 10 / 11 (x64)
- 运行依赖：[.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)（开发编译需 .NET 8.0 SDK 或更高版本）

---

## 快速开始

### 1. 运行与编译

在项目根目录下使用终端执行：

```powershell
# 直接运行
dotnet run

# 运行自动化单元测试
dotnet test

# 编译 Release 版本
dotnet build -c Release

# 发布独立无依赖可执行文件
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### 2. 使用步骤

1. **导入照片**：启动后直接将单张/多张图片或文件夹拖入全屏引导区（支持递归扫描子目录），或点击“导入照片文件夹”与“添加照片文件”。
2. **选择冲印规格**：
   - **输出尺寸**：支持 3寸、5寸、6寸（默认）、7寸、8寸（按 300 DPI 严格换算像素，横竖构图自适应）。
   - **裁切模式**：
     - **裁剪填充 (Fill)**：按相纸画幅比例填满，超出区域暗化半透明遮罩呈现，支持自由平移构图。
     - **留白完整 (Fit)**：相纸纯白底板加立体投影，完整呈现原始画面与白边。
3. **调整构图位置**：
   - **鼠标拖拽**：在中央大图相纸视口中按住鼠标左键直接拖动画布，实时平移裁切中心。
   - **键盘微调**：使用 `WASD` 或方向键微调，参考九宫格三分线实现精准构图。
   - **翻片切换**：按 `Q` / `E` 或点击底部胶卷条快速切换上一张/下一张。
4. **批量导出**：点击右侧“批量开始导出”按钮，程序将多核并发处理并输出到原图同级目录下的 `Clipped_Photos` 文件夹中，导出过程中实时显示进度条并支持随时安全中止。

---

## 快捷键与操作手势

| 按键 / 手势 | 功能说明 |
| :--- | :--- |
| **鼠标左键按住拖拽** | 直接在相纸画幅上自由平移画面构图 |
| `W` / `A` / `S` / `D` 或 `方向键` | 1% 步进微调构图偏移 |
| `Shift + WASD` 或 `Shift + 方向键` | 10% 步进快速大幅移动 |
| `Ctrl + WASD` 或 `Ctrl + 方向键` | 瞬间向对应边缘贴边对齐 |
| `Q` / `E` | 快速切换上一张 / 下一张照片 |
| `Space`（空格键） | 快速切换填充（Fill）与留白（Fit）模式 |
| `Enter`（回车键） | 触发批量导出 |

---

## 工程架构与设计规范

- **分层解耦（Clean Architecture & MVVM）**：
  - `Core/Calculators`：纯数学与无副作用几何计算器（`CropGeometryCalculator`），无外部依赖，统一驱动 UI 预览与导出处理。
  - `Core/Abstractions`：定义 `IImageProcessor` 与可观测导出模型（进度汇报、取消令牌、错误隔离）。
  - `Core/ImageProcessor`：基于 Magick.NET-Q8-OpenMP 实现底层非托管资源安全生命周期管理与 EXIF 方向纠正。
  - `ViewModels`：支持依赖注入、命令状态自动化管控以及并发异步调度。
- **质量保障与测试（Automated Testing）**：
  - 配备完整的 `AutoCutPic.Tests` xUnit 测试套件，覆盖相纸 300 DPI 换算精度、Fill 裁剪边界钳位、Fit 留白缩放、并发导出与取消链路。

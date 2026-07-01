# WindowsFormsApp1 项目功能文档

## 1. 项目定位

本项目是一个基于 `.NET Framework 4.7.2` 的 WinForms 图片处理工具，当前主要包含两条核心业务线：

1. `套图`
用于把素材图按模板规则合成输出，支持单张处理、批量处理，以及远程 WebSocket 请求驱动的自动处理。

2. `排版输出`
用于将多张素材按设定的版面参数排布到一张大图中，并导出为 TIFF 等结果文件。

此外，主界面内置了一个可缩放、可拖拽、可右键操作的画布组件，用于预览处理结果。

---

## 2. 当前实际编译入口

项目编译入口由 [WindowsFormsApp1.csproj](D:/C#/WindowsFormsApp1/WindowsFormsApp1/WindowsFormsApp1.csproj) 控制。  
当前真正参与编译的核心源码目录为：

- `Models/UI`
- `Services/Imaging`
- `Services/Layout`
- `UI/Controls`
- `UI/Dialogs`
- `UI/Main`
- `Properties`
- `Program.cs`

这意味着目录中有些 `.cs` 文件虽然存在，但如果没有被 `csproj` 引用，它们并不会参与正式构建。

---

## 3. 目录结构与职责

### 3.1 根目录

#### [Program.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/Program.cs)

项目启动入口。

- 初始化 WinForms 运行环境
- 启动主窗体 `Form1`

#### [App.config](D:/C#/WindowsFormsApp1/WindowsFormsApp1/App.config)

应用配置文件。

- 存放运行时配置
- 参与最终程序配置输出

#### [packages.config](D:/C#/WindowsFormsApp1/WindowsFormsApp1/packages.config)

NuGet 依赖声明文件。

- 记录 Aspose、Magick.NET、LibTiff 等依赖包版本

#### [WindowsFormsApp1.csproj](D:/C#/WindowsFormsApp1/WindowsFormsApp1/WindowsFormsApp1.csproj)

项目编译清单与引用配置。

- 控制源码是否参与编译
- 控制资源文件嵌入
- 控制程序集依赖和构建输出

---

### 3.2 `UI/Main`

主窗体目录，负责应用壳层、工具栏、状态栏、画布承载，以及两个业务窗口的调度。

#### [UI/Main/Form1.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/UI/Main/Form1.cs)

主窗体主文件，负责整体界面初始化。

主要职责：

- 定义主窗体基础字段
- 初始化整体暗色主题
- 初始化顶部工具栏
- 初始化画布承载区域
- 初始化状态栏
- 挂接窗体生命周期事件
- 定义远程套图请求的数据模型

核心对象：

- `RemoteMergeMessage`
  远程 WebSocket 原始消息模型

- `RemoteMergeRequest`
  主程序内部统一使用的远程套图请求模型

#### [UI/Main/Form1.Canvas.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/UI/Main/Form1.Canvas.cs)

主窗体中的画布协作逻辑。

主要职责：

- 打开 `MergeDialog`
- 打开 `LayoutOutputDialog`
- 将窗口处理结果回载到 `RulerCanvas`
- 单图加载
- 多图横向平铺加载
- 工具栏缩放同步
- 画布工具状态同步

这是主界面与业务窗口之间的桥接层。

#### [UI/Main/Form1.Remote.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/UI/Main/Form1.Remote.cs)

主窗体中的远程任务逻辑。

主要职责：

- 主窗体显示后启动远程 WebSocket 客户端
- 维护 WebSocket 重连循环
- 接收远程 JSON 消息
- 反序列化为 `RemoteMergeRequest`
- 将远程请求排队
- 顺序拉起 `MergeDialog` 执行远程套图任务
- 安全更新状态栏
- 窗体关闭时释放远程连接资源

这是项目里自动化处理链路的核心入口。

#### [UI/Main/Form1.Designer.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/UI/Main/Form1.Designer.cs)

WinForms 设计器文件。

- 承载设计器生成的窗体初始化代码
- 通常不建议手工大改

#### [UI/Main/Form1.resx](D:/C#/WindowsFormsApp1/WindowsFormsApp1/UI/Main/Form1.resx)

主窗体资源文件。

- 供设计器和资源系统使用

---

### 3.3 `UI/Dialogs`

业务弹窗目录，承载套图与排版两个独立功能界面。

#### [UI/Dialogs/MergeDialog.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/UI/Dialogs/MergeDialog.cs)

套图业务窗口，是当前项目中最重的业务窗体之一。

主要职责：

- 选择模板目录、素材目录、保存目录
- 设置输出格式
- 固定使用满版模式进行合成
- 支持单张与批量任务
- 支持远程请求参数回填
- 支持通道名称维护
- 校验模板、素材、输出路径有效性
- 生成任务列表
- 控制批量任务执行、暂停、取消
- 展示执行状态和结果列表
- 输出结果路径给主窗体回显

核心业务特征：

- `套图模式` 控件已隐藏
- 当前内部默认使用 `满版模式`
- 既可手动操作，也可被远程请求驱动

#### [UI/Dialogs/LayoutOutputDialog.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/UI/Dialogs/LayoutOutputDialog.cs)

排版输出窗口。

主要职责：

- 选择源图目录与输出目录
- 维护输出文件名
- 设置大图尺寸、DPI、首格坐标、格子尺寸、间距、行列数
- 扫码或输入素材标识
- 预览排版结果
- 触发正式排版导出
- 返回导出结果路径给主窗体画布

界面特点：

- 左侧参数输入
- 右侧预览区域
- 支持刷新预览与一键排版

---

### 3.4 `UI/Controls`

自定义控件目录。

#### [UI/Controls/RulerCanvas.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/UI/Controls/RulerCanvas.cs)

当前项目最核心的画布控件。

主要职责：

- 显示图片结果
- 支持单图加载
- 支持多图横向平铺加载
- 支持拖拽移动图片
- 支持缩放、平移、滚动条联动
- 支持标尺绘制
- 支持参考线显示与拖动
- 支持拖放本地图片文件到画布
- 支持右键图片删除
- 支持右键空白区域显示 `清空画布 / 重置视图`
- 支持菜单 hover 蓝色高亮

内部关键能力：

- `CanvasImageItem`
  画布中每一张图片的运行时对象

- `HitTestImage`
  命中测试，用于判断鼠标点到哪张图

- `LoadImagesHorizontally`
  将多张图片按横向顺序合理排布

- `ResetView`
  重置画布视图

- `ClearScene`
  清空所有图片并释放资源

#### [UI/Controls/RulerControl.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/UI/Controls/RulerControl.cs)

一个独立的标尺面板控件。

理论职责：

- 绘制顶部与左侧标尺
- 绘制网格
- 绘制中心十字参考

当前状态：

- 已参与编译
- 但目前没有发现主界面或业务窗口对它的实例化调用
- 更像是旧版标尺方案或实验性控件

---

### 3.5 `Services/Imaging`

图像处理服务目录，负责底层图片读写、合成、格式转换、预览等能力。

#### [Services/Imaging/AsposePSDHelper.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/Services/Imaging/AsposePSDHelper.cs)

图像处理底层主工具类。

主要职责：

- 读取 PSD、TIF 等图像
- 生成预览图
- 将图片加载为 `Bitmap`
- 执行模板与素材的图像合成
- 支持标准模式与满版模式
- 支持排版输出时生成 TIFF
- 写入白墨、光油等专色通道
- 执行旋转、镜像等处理

这个文件是“套图”和“排版输出”两条业务线共同依赖的底层核心。

#### [Services/Imaging/MagickImageCollection.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/Services/Imaging/MagickImageCollection.cs)

Magick.NET 相关封装或兼容处理文件。

主要职责：

- 参与多页 TIFF 或特殊图像读取流程
- 为 `RulerCanvas` 和 `PSDAnalyzer` 提供底层支持

#### [Services/Imaging/PSDAnalyzer.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/Services/Imaging/PSDAnalyzer.cs)

PSD 分析与调试辅助类。

主要职责：

- 输出 PSD 图层信息到控制台
- 分析模板图层与素材图层匹配关系
- 辅助检查 TIFF / PSD 图层与通道结构

当前定位：

- 偏开发调试工具类
- 已参与编译
- 但未发现当前主业务流程直接调用入口

---

### 3.6 `Services/Layout`

排版服务目录，承载“排版输出”业务算法。

#### [Services/Layout/LayoutOutputHelper.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/Services/Layout/LayoutOutputHelper.cs)

排版输出的核心算法类。

包含的数据结构：

- `SheetLayoutSettings`
  版面尺寸与格位参数

- `LayoutOutputRequest`
  排版请求模型

- `LayoutOutputResult`
  排版结果模型

- `PreparedLayout`
  预计算后的格位结构

主要职责：

- 校验版面参数
- 毫米转像素
- 计算整张大图尺寸
- 计算所有槽位的矩形位置
- 读取待排版图片列表
- 校验输入图像是否可用
- 将图片按格位等比缩放后居中绘制
- 导出最终 TIFF 大图

它是 `LayoutOutputDialog` 的业务引擎。

---

### 3.7 `Models/UI`

轻量 UI 模型目录。

#### [Models/UI/UiModels.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/Models/UI/UiModels.cs)

存放当前 UI 运行过程中使用的小型模型类。

包含：

- `GuideLine`
  画布参考线模型

- `ChannelControl`
  套图窗口中“通道卡片控件组”的承载模型

该目录的作用是把纯数据结构从窗体业务文件中剥离出来，降低耦合。

---

### 3.8 `Properties`

项目通用配置与资源目录。

#### [Properties/AssemblyInfo.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/Properties/AssemblyInfo.cs)

- 程序集基础信息

#### [Properties/Resources.resx](D:/C#/WindowsFormsApp1/WindowsFormsApp1/Properties/Resources.resx)
#### [Properties/Resources.Designer.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/Properties/Resources.Designer.cs)

- 应用资源定义与生成代码

#### [Properties/Settings.settings](D:/C#/WindowsFormsApp1/WindowsFormsApp1/Properties/Settings.settings)
#### [Properties/Settings.Designer.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/Properties/Settings.Designer.cs)

- 应用级设置
- 用于保存模板目录、素材目录、输出目录等默认路径

---

## 4. 主要业务流程

### 4.1 手动套图流程

1. 用户点击主界面 `套图`
2. 打开 `MergeDialog`
3. 用户选择模板目录、素材目录、输出目录
4. 系统构建单任务或批量任务列表
5. 调用 `AsposePSDHelper` 完成图像合成
6. 输出结果路径返回主界面
7. 主界面调用 `RulerCanvas` 加载预览结果

### 4.2 远程套图流程

1. 主窗体显示后启动 WebSocket 客户端
2. 远端发送 JSON 请求
3. `Form1.Remote.cs` 反序列化并构造 `RemoteMergeRequest`
4. 请求进入本地队列
5. 主程序顺序调起 `MergeDialog`
6. `MergeDialog` 按远程参数自动执行套图
7. 结果可返回到主画布显示

### 4.3 排版输出流程

1. 用户点击主界面 `排版输出`
2. 打开 `LayoutOutputDialog`
3. 用户填写版面参数并刷新预览
4. `LayoutOutputHelper` 计算布局槽位
5. 调用 `AsposePSDHelper` 读取素材并生成大图
6. 输出 TIFF 结果
7. 主界面加载结果到画布

### 4.4 画布交互流程

1. 主窗体将结果图加载到 `RulerCanvas`
2. 用户可拖动图片、缩放视图、查看标尺
3. 多图结果会横向铺开显示
4. 右键图片可删除
5. 右键空白区域可清空画布或重置视图

---

## 5. 当前结构设计评价

### 5.1 目前已经比较合理的地方

- `Form1` 已拆为主文件、画布协作、远程处理三个 partial 文件
- `MergeDialog` 和 `LayoutOutputDialog` 已从主窗体中独立出去
- `Services`、`Models`、`UI` 已有明显分层
- 主业务入口与图像算法已基本分离

### 5.2 仍然建议继续优化的地方

- `MergeDialog.cs` 仍然偏大，建议后续继续拆为多个 partial 文件
- `RulerCanvas.cs` 功能较多，后续也可以继续拆分绘制、输入、菜单、加载逻辑
- 一部分中文注释和界面文字存在编码历史问题，建议后续统一修复为 UTF-8
- `PSDAnalyzer.cs` 更像开发调试类，后续可以单独归档到 `Tools` 或 `Debug` 目录

---

## 6. 疑似废弃、无用或建议清理的文件

以下判断基于两条标准：

1. 是否被 `WindowsFormsApp1.csproj` 编译引用
2. 是否被当前主流程代码实际调用

### 6.1 可直接视为非正式业务源码的文件

这些文件当前不在 `csproj` 编译列表中，属于明显的测试、排查或临时工具文件：

- [analyze_template.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/analyze_template.cs)
- [check_output.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/check_output.cs)
- [debug_tiff.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/debug_tiff.cs)
- [test_check.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/test_check.cs)
- [test_read.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/test_read.cs)

说明：

- 当前它们都没有出现在 `WindowsFormsApp1.csproj` 的 `<Compile Include=...>` 列表中
- 删除前只需要确认你自己是否还在本地手工用它们做排查即可

### 6.2 建议确认后清理的文件

#### [UI/Controls/RulerControl.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/UI/Controls/RulerControl.cs)

状态：

- 仍参与编译
- 但代码搜索中未发现任何地方 `new RulerControl()` 或实际引用它

判断：

- 高概率为旧版标尺控件
- 如果你确认已经完全由 `RulerCanvas` 接管标尺能力，可以从项目中移除

#### [Services/Imaging/PSDAnalyzer.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/Services/Imaging/PSDAnalyzer.cs)

状态：

- 参与编译
- 但未发现主界面或业务流程直接调用入口

判断：

- 更偏开发调试辅助类
- 若后续不再需要控制台分析 PSD，可单独移出正式项目

#### [BitMiracle.LibTiff.NET.dll](D:/C#/WindowsFormsApp1/WindowsFormsApp1/BitMiracle.LibTiff.NET.dll)

状态：

- 文件存在于项目根目录
- 但 `csproj` 当前引用的是 `..\packages\BitMiracle.LibTiff.NET.2.4.660\...`

判断：

- 根目录这个 DLL 高概率是历史遗留拷贝
- 一般可以清理，避免误导

#### [WindowsFormsApp1.csproj.user](D:/C#/WindowsFormsApp1/WindowsFormsApp1/WindowsFormsApp1.csproj.user)

状态：

- Visual Studio 本地用户配置文件

判断：

- 不建议纳入正式版本管理
- 可忽略或从仓库中移除

### 6.3 构建输出目录，通常不需要纳入源码管理

这些目录和文件不属于源码，应作为构建产物或临时目录处理：

- `bin/`
- `obj/`
- `.claude-build/`
- [bin/Release.rar](D:/C#/WindowsFormsApp1/WindowsFormsApp1/bin/Release.rar)

说明：

- `bin` 和 `obj` 是标准构建输出目录
- `.claude-build` 是临时构建或运行产物目录
- `Release.rar` 如果只是手工打包产物，也不建议长期和源码混放

### 6.4 目录中值得单独注意的异常项

#### [packages/PSDAnalyzer.cs](D:/C#/WindowsFormsApp1/WindowsFormsApp1/packages/PSDAnalyzer.cs)

状态：

- 出现在 `packages` 目录下
- 不属于标准 NuGet 包目录结构的一部分
- 也不参与当前项目编译

判断：

- 很像误放进去的源码副本
- 建议单独打开确认内容后删除或移回正规源码目录

---

## 7. 建议你的下一步清理顺序

建议按下面顺序清理，风险最低：

1. 先清理不参与编译的测试文件
2. 再清理根目录遗留 DLL 和 `.csproj.user`
3. 再确认 `RulerControl.cs` 是否彻底不用
4. 最后决定 `PSDAnalyzer.cs` 是否从正式项目剥离
5. 构建目录统一加入忽略规则，避免再次混入仓库

---

## 8. 总结

当前项目已经从“单文件堆业务”的形态，逐步整理成了较清晰的 WinForms 分层结构：

- `UI` 负责界面
- `Services` 负责图像与排版业务
- `Models` 负责轻量模型
- `Properties` 负责配置资源

如果后续继续演进，最值得优先处理的两个方向是：

1. 继续拆分 `MergeDialog.cs`
2. 清理测试遗留文件和未使用控件文件


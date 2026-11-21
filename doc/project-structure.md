# 萌快网维 MoeQuick NetScan — 项目目录与模块职责划分

| 项目 | 内容 |
| --- | --- |
| 文档名称 | project-structure.md |
| 版本 | v1.0 |
| 状态 | 已确认（基线版） |
| 上游文档 | [doc/product-requirements.md](./product-requirements.md)、[doc/architecture.md](./architecture.md) |
| 解决方案 | `MQNetScan.sln` |
| 发布产物 | `MQNetScan.exe`（单文件，零第三方依赖） |

---

## 1. 拆分原则

1. **发布物必须是单文件**：因此**不建独立类库项目**（避免 exe + dll 两件套），引擎代码以**目录**形式位于主程序内。
2. **分层靠目录 + 命名空间双约束**：目录名与命名空间一一对应，评审时可机械判定文件归属。
3. **测试项目不随发布**：独立 Console 项目，通过 `InternalsVisibleTo` 访问引擎内部类型，不引入任何测试框架 NuGet。
4. **约定优于配置**：目录结构固定后，新增文件必须能一句话说明“为什么放这里”，否则说明分层有问题。
5. **资源内嵌**：图标、默认 ini 模板以 `EmbeddedResource` 嵌入 exe，运行时无需外部文件。

---

## 2. 解决方案结构

```
MQNetScan.sln
├── src/
│   └── MQNetScan/                      ← 主程序（WinExe, .NET Framework 4.0, AnyCPU）
└── tests/
    └── MQNetScan.Tests/                ← 自测控制台（Console, 不随发布）
```

> **明确不做**：不做 `MQNetScan.Core` 类库项目（会导致发布物变成 exe + dll，违反单文件红线）。若未来确需复用引擎，再评估“源码级分层 + 编译期合并”，v1.0 不预留。

---

## 3. 主程序目录树（完整版）

```
src/MQNetScan/
│
├── MQNetScan.csproj
├── Program.cs                          ← 入口：异常总兜底 + 单实例 + 启动 MainForm
├── app.manifest                        ← asInvoker 清单（关键文件，见 §5.2）
├── app.config                          ← 仅放 supportedRuntime v4.0 声明
│
├── Presentation/                       ← 【展示层】只依赖 Application/Contracts
│   ├── MainForm.cs
│   ├── MainForm.Designer.cs
│   ├── MainForm.resx
│   ├── Common/
│   │   ├── AlertBanner.cs              ← 顶部红色预警区（可复用控件）
│   │   ├── UiText.cs                   ← 所有中文提示文案集中处（便于统一修订）
│   │   ├── ListViewHelper.cs           ← 列头排序器、Ctrl+C 复制、角色配色
│   │   └── UiDispatcher.cs             ← Timer 驱动的批量刷新器（§4.4）
│   └── Pages/
│       ├── ScanPage.cs / .Designer.cs / .resx        ← Tab 1 局域网探测
│       ├── ToolsPage.cs / .Designer.cs / .resx       ← Tab 2 便捷功能
│       └── SettingsPage.cs / .Designer.cs / .resx    ← Tab 3 设置
│
├── Application/                        ← 【应用层】用例编排，不碰 Socket/UI 控件
│   ├── ScanSessionController.cs        ← 创建/控制 ScanSession，桥接 UI 与引擎
│   ├── ToolsService.cs                 ← 清 ARP / 刷 DNS / 开面板 / 单点测通 / 自检
│   ├── ReportBuilder.cs                ← 四段式纯文本报告生成
│   └── SettingsService.cs              ← 读写配置、校验范围、恢复默认
│
├── Engine/                             ← 【引擎层】纯计算 + 网络探测，禁止引用 WinForms
│   ├── Contracts/
│   │   ├── IResultSink.cs              ← 引擎 → UI 的结果投递接口
│   │   ├── IProgressSource.cs          ← UI 读取进度快照接口
│   │   ├── IScanController.cs          ← 开始/暂停/继续/终止
│   │   └── Dto/
│   │       ├── HostRecord.cs
│   │       ├── ScanParameters.cs
│   │       ├── ScanProgress.cs
│   │       ├── AlertItem.cs
│   │       ├── Ping3Result.cs
│   │       └── PortRule.cs
│   ├── ScanSession.cs                  ← 一次扫描的实例（持有参数快照与线程集合）
│   ├── AddressQueue.cs                 ← 自建阻塞队列（Monitor.Wait/Pulse）
│   ├── ArpProber.cs                    ← SendARP 封装 + 重试 + 信号量限流(16)
│   ├── PortProber.cs                   ← 非阻塞 connect + Socket.Select 批量收割
│   ├── ConflictDetector.cs             ← IP 冲突判定（双证据链）
│   ├── RoleResolver.cs                 ← 角色判定（内置优先级，不可改）
│   ├── ProgressTracker.cs              ← Interlocked 计数 + 耗时统计
│   ├── ResultQueue.cs                  ← 加锁结果队列
│   └── ScanStateMachine.cs             ← Idle/Running/Paused/Stopped/Completed
│
├── Infrastructure/                     ← 【基础设施层】
│   ├── NativeMethods.cs                ← 全部 P/Invoke 集中处（唯一允许 DllImport 的文件）
│   ├── NativeStructs.cs                ← MIB_IPNETTABLE / MIB_IPNETROW 封送结构
│   ├── NetworkAdapterService.cs        ← 网卡枚举 + 网段计算
│   ├── IniConfigStore.cs               ← 自建 INI 解析 + 原子写
│   ├── IniSchema.cs                    ← 键名常量 + 默认值（唯一事实来源）
│   ├── ProcessLauncher.cs              ← mstsc / 浏览器 / ncpa.cpl / services.msc / netsh
│   ├── PrivilegeChecker.cs             ← 是否为管理员（清 ARP 前置判断）
│   ├── ErrorClassifier.cs              ← SocketException/Win32 错误码 → 统一分类枚举
│   └── Logger.cs                       ← 轻量滚动日志（默认关闭）
│
├── Resources/
│   ├── app.ico                         ← 【占位图标，待替换】见 §7
│   ├── default-rules.ini               ← 内嵌默认端口规则（EmbeddedResource）
│   └── icons/                          ← 按钮小图标（可选，同样内嵌）
│
└── Properties/
    ├── AssemblyInfo.cs                 ← 版本号、产品名、标题一致性声明
    └── Resources.Designer.cs / .resx
```

---

## 4. 各层职责与禁止事项

### 4.1 Presentation（`MQNetScan.Presentation.*`）

**职责**：控件布局、事件绑定、结果渲染、用户提示弹出。

| 允许 | 禁止 |
| --- | --- |
| 引用 `Application` 层与 `Engine.Contracts` 的 DTO | 直接 `DllImport`（含 `SendARP`） |
| 直接调 `Application` 的公开方法 | 直接 `new Socket` / 直接读 ini 文件 |
| 持有 `ListView`、`Timer`、`ContextMenuStrip` | 在引擎类里出现任何 `System.Windows.Forms` 引用 |
| 通过 `UiText` 取文案 | 在 `Engine/` 里写中文用户提示 |

### 4.2 Application（`MQNetScan.Application`）

**职责**：把“用户意图”翻译成“引擎调用序列”，组装报告，管理配置。

| 关键类 | 单一句职责 |
| --- | --- |
| `ScanSessionController` | 唯一持有当前 `ScanSession` 的地方；`Start/Pause/Resume/Stop` 全部经此转发 |
| `ToolsService` | Tab 2 全部系统操作；内部负责“是否管理员 → 走哪条分支” |
| `ReportBuilder` | 输入 `HostRecord[] + ScanProgress + AlertItem[] + 参数快照`，输出 `string`；**不写文件**（写文件由 `Presentation` 调 `File` 完成，或由本类返回字符串后 UI 保存） |
| `SettingsService` | 校验并发 16~128、超时 200~5000，非法即纠正并返回提示 |

> **禁止**：`Application` 引用 `System.Windows.Forms`（保证可被测试项目直接调用）。

### 4.3 Engine（`MQNetScan.Engine`）

**职责**：并发调度、ARP 探测、端口探测、角色判定、状态机。

| 关键类 | 说明 | 会话级/无状态 |
| --- | --- | --- |
| `ScanSession` | 一次性实例，持有 `ScanParameters` 快照、工作线程数组、队列、状态机 | 会话级 |
| `AddressQueue` | `Queue<int>` + `MONITOR`，支持 `PulseAll` 唤醒 | 会话级 |
| `ArpProber` | 封装 `SendARP` 调用 + `Semaphore`(≤16) + 重试 | 无状态（可复用） |
| `PortProber` | 非阻塞 connect + `Select` 轮询；**每个 socket 用完即关** | 无状态 |
| `ConflictDetector` | 记录 `IP → MAC 集合`，第二 MAC 出现即标记 | 会话级 |
| `RoleResolver` | 纯函数：`int[] openPorts + PortRule[] rules → string role` | 无状态 |
| `ProgressTracker` | `Interlocked` 计数、耗时（支持暂停累计） | 会话级 |
| `ScanStateMachine` | 状态迁移唯一入口；`volatile` 状态字段 | 会话级 |

> **禁止**：`Engine` 内出现 `MessageBox`、`Control`、`Console.WriteLine`（日志走 `ILogger` 接口由 `Infrastructure.Logger` 实现）。

### 4.4 UI 刷新器（`Presentation/Common/UiDispatcher.cs`）

这是“不卡”的落点，单独说明：

| 职责 | 实现 |
| --- | --- |
| 持有 `System.Windows.Forms.Timer`（200ms） | 单一实例，由 `ScanPage` 创建并 `Dispose` |
| 每次 Tick 从 `ResultQueue` 批量取结果 | 单次上限 200 行（architecture §3.6） |
| 批量插入 `ListView` | `BeginUpdate()` → `Items.Add` → `EndUpdate()` 包裹 |
| 刷新进度条/状态栏/预警横幅 | 读 `IProgressSource.Snapshot()` |
| 深度测速回填 | 提供 `UpdateRow(index, column, text)`，只改 `SubItems[i].Text` |

> **禁止**：其它类直接向 `ListView` 加行；所有行插入必须经 `UiDispatcher`，否则刷新可能跨线程。

### 4.5 Infrastructure（`MQNetScan.Infrastructure`）

| 文件 | 边界规则 |
| --- | --- |
| `NativeMethods.cs` | **全项目唯一**允许 `[DllImport]` 的文件；所有 P/Invoke 声明集中于此 |
| `NativeStructs.cs` | 与 `NativeMethods` 配套的 `StructLayout` 定义，集中一处便于 32/64 位对齐审查 |
| `IniConfigStore.cs` | 唯一读写 `MQNetScan.ini` 的文件；其它类只能通过 `SettingsService` 取值 |
| `IniSchema.cs` | 键名/默认值常量集中处，**任何硬编码键名出现在其它文件即为违规** |
| `ProcessLauncher.cs` | 所有 `Process.Start` 集中处，统一捕获异常并返回中文结果 |
| `ErrorClassifier.cs` | Win32/Socket 错误码映射，避免错误码散落 |

---

## 5. 关键项目文件配置

### 5.1 `MQNetScan.csproj` 关键项

| 配置项 | 值 | 理由 |
| --- | --- | --- |
| `OutputType` | `WinExe` | 无控制台窗口 |
| `TargetFrameworkVersion` | `v4.0` | **编译期即拦截 4.5+ API** |
| `TargetFrameworkProfile` | 留空（完整框架） | Client Profile 缺少部分类型，且已淘汰 |
| `PlatformTarget` | `AnyCPU` | 兼容 32/64 位系统 |
| `Prefer32Bit` | 该选项是 4.5+ 工具链特性，**不设置** | 4.0 无此项 |
| `LangVersion` | `3`（C# 3.0） | 强制不使用 6.0+ 语法糖，避免“写出来才发现 4.0 不支持” |
| `UseVSHostingProcess` / `DebugType` | Debug/Test = `full`；Release = `none` | 见 §6 |
| `AssemblyName` | `MQNetScan` | 输出 `MQNetScan.exe` |
| `AssemblyTitle` | `MoeQuick NetScan v1.0 - 萌快网维` | 与窗体标题一致 |
| `ApplicationIcon` | `Resources\app.ico` | 驱动 exe 图标 |
| `ApplicationManifest` | `app.manifest` | `asInvoker` |
| `InternalsVisibleTo` | `MQNetScan.Tests` | 仅测试用 |

> **`LangVersion=3` 的副作用**：不能用 `var`？——`var` 是 C# 3.0 特性，可用。不能用的是插值字符串 `$""`（6.0）、`nameof`（6.0）、表达式体成员（6.0/7.0）、`?.`（6.0）。这与 .NET 4.0 的“不允许 4.5+ API”是同一条红线的两面。

### 5.2 `app.manifest` 要点

```xml
<requestedExecutionLevel level="asInvoker" uiAccess="false" />
```
- 必须包含 `supportedOS` 声明（Win7 到 Win10/11 的 GUID），否则 Win10 上会走兼容性影射导致版本误判。
- **不得**声明 `requireAdministrator`（PRD NFR-P1）。

### 5.3 `app.config` 要点

| 节 | 内容 | 理由 |
| --- | --- | --- |
| `<startup supportedRuntime>` | `v4.0` + `sku=".NETFramework,Version=v4.0"` | 若客户端未装 4.0，弹出系统级可读提示而非无反应 |
| 其它 | **留空** | 不配置 `useLegacyV2RuntimeActivationPolicy` 等无关项 |

---

## 6. 构建配置与产物矩阵

| 配置 | 用途 | pdb | 优化 | 输出位置 | 是否交付 |
| --- | --- | --- | --- | --- | --- |
| `Debug` | 开发自测 | 生成（full） | 关 | `build\Debug\` | 否 |
| `Test` | 交付用户试用的测试版 | **生成（full）** | 开 | `build\Test\` | 是（试用） |
| `Release` | 正式发布版 | **不生成**（`DebugType=none`、`DebugSymbols=false`） | 开 | `build\Release\` | 是 |

**输出目录约定**：统一输出到解决方案根下的 `build\<Config>\`（而非 `bin\Debug`），避免嵌套过深且便于脚本校验。

**Release 产物应仅有**：`MQNetScan.exe`（+ 运行后自动生成的 `MQNetScan.ini`、可选 `MQNetScan.log`）。**出现任何 `.dll`、`.pdb`、`.xml`、`.config` 之外的附属文件即为构建配置错误。**
> 注：`MQNetScan.exe.config` 会随 exe 生成，属**例外允许**项；若希望彻底单文件，可将 `[startup]` 声明移出并接受“未装 4.0 时无提示”的代价——**v1.0 保留 `.config`**，因为“老机器缺框架时给不出任何提示”是更严重的问题。

### 6.1 引用程序集来源：双轨解析（**实施期修正**）

> **本节为 S0 实施过程中实测后新增，修正了原 v1.0 的一个错误假设。**

**原假设（错误）**：只要设置 `TargetFrameworkVersion=v4.0`，编译期就能拦截 4.5+ API。

**实测结果（本机 Windows 11 Build 26200 + 仅 .NET 4.8 运行时 + 无 4.0 Targeting Pack）**：

```
warning MSB3644: 未找到框架".NETFramework,Version=v4.0"的引用程序集...
                 ...将从全局程序集缓存(GAC)解析程序集
```

| 测试代码 | 预期 | 实测（无 Targeting Pack） |
| --- | --- | --- |
| `Task.Run(...)` | 编译失败 | **编译通过** ❌ |
| `async/await` | 编译失败 | **编译通过** ❌ |

后果：编出的 exe 引用仍写 `mscorlib 4.0.0.0`，但实际按 4.8 程序集解析 —— **在真 .NET 4.0 的 Win7 上会 `MissingMethodException` 崩溃**，正是本项目最需要防的事故。

**修正方案：双轨解析（A + B 双轨，脚本自动择优）**

| 轨道 | 来源 | 优先级 | 说明 |
| --- | --- | --- | --- |
| 轨道 1（B） | 系统官方 Targeting Pack `%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0` | **优先** | 若用户在某台机器上装成了官方 Developer Pack，自动使用它 |
| 轨道 2（A） | 项目内 `.toolchain\net40\build\.NETFramework\v4.0` | 回退 | 从 NuGet 官方包 `Microsoft.NETFramework.ReferenceAssemblies.net40` 1.0.3（29MB）解压；首次构建由 `build.ps1` 自动下载 |

**编译时通过 `/p:FrameworkPathOverride=<轨道路径>` 强制指定引用程序集来源。**

**验证效果（实测通过）**：

| 测试 | 结果 |
| --- | --- |
| `Task.Run` | `error CS0117: Task 不包含 Run 的定义` → **成功拦截** |
| `async/await` | `error CS0518 / CS1993` → **成功拦截** |
| 正常 WinForms 代码 | `exit=0`，引用 `mscorlib 4.0.0.0` → **正常通过** |

**`.toolchain` 的版本控制策略**：**不纳入版本控制**（`.gitignore` 已排除），避免仓库膨胀 178MB。首次构建需联网；离线构建前请先联网执行一次 `build.ps1` 完成缓存。

### 6.2 本机构建环境实测结论（S0 记录）

| 组件 | 状态 | 说明 |
| --- | --- | --- |
| .NET Framework 4.8（4.8.09221） | 已装 | 仅运行时 |
| `MSBuild.exe` 4.0（`Framework\v4.0.30319\`） | 已装 | 可编 net40 |
| `csc.exe`（4.8.9221，支持至 C# 5） | 已装 | 编译器本体 |
| .NET Framework 4.0 Targeting Pack | **缺失** | 故需 §6.1 双轨方案 |
| Visual Studio | 未装 | 不影响（命令行构建） |
| .NET SDK / `dotnet` | 未装 | 不影响（net40 不需要） |
| VS Code | 已装 | 仅编辑器；已装 `ms-dotnettools.csharp` 扩展 |
| PowerShell | **仅 5.1**（无 pwsh 7） | `build.ps1` 以 5.1 兼容方式编写 |

> **脚本编码警告（已实测）**：Windows PowerShell 5.1 以 ANSI 读取**无 BOM** 的 `.ps1`，中文注释会乱码并导致**解析失败**（已实际踩到：`build.ps1` 首次运行时整片 `Unexpected token`）。
> **`.ps1` 必须保存为 UTF-8 with BOM**；`.vscode/settings.json` 已对 `[powershell]` 强制 `utf8bom`。
> **注意**：`.cs` 源文件**不需要** BOM —— 实测 csc 对含中文的 UTF-8（无 BOM）源文件解析正常，加不加 BOM 均能正确输出中文。此差异容易被误推广，特此注明。

---

## 7. 图标与资源（占位约定）

| 项 | 约定 |
| --- | --- |
| 文件 | `Resources/app.ico`（**占位图标，待用户提供后替换**） |
| 建议尺寸 | 单文件多尺寸：16×16、32×32、48×48、256×256 |
| 使用点 | ① 项目 `ApplicationIcon` → exe 图标；② `MainForm.Icon` 从内嵌资源加载；③ `NotifyIcon` 不使用（无托盘） |
| 替换方式 | 覆盖同名文件 → 重新编译，**不需要改任何代码或 csproj** |
| 校验策略 | `build.ps1` **只校验**“资源存在 + exe 带图标”，**不比对图标内容哈希**（便于自由更换图标而不触发校验失败） |
| 内部默认规则 | `Resources/default-rules.ini` 作为 `EmbeddedResource`，程序首次运行且有 ini 缺失时用于生成默认规则；**若同级已存在 `MQNetScan.ini`，以级联文件为准，绝不覆盖** |

**占位图标说明**：v1.0 开发阶段使用一张纯色底 + “MQ”字样的 32×32 占位图，仅用于验证“图标能正确显示在 exe/任务栏/标题栏”，**不代表最终视觉设计**。

---

## 8. 测试项目结构（`tests/MQNetScan.Tests`）

```
tests/MQNetScan.Tests/
├── MQNetScan.Tests.csproj          ← Console, v4.0, 引用 src 项目
├── Program.cs                      ← 手写测试入口：顺序执行、汇总通过/失败、返回退出码
├── TestRunner.cs                   ← 极简断言工具（AssertEqual/AssertTrue/AssertThrows）
├── ArpByteOrderTests.cs            ← IP → uint 字节序（architecture A-C2）
├── NetSegmentTests.cs              ← CIDR/网段/目标数计算、前缀边界（8~30）
├── RoleResolverTests.cs            ← 角色优先级（1433+445 同开 → 本地服务器）
├── IniConfigStoreTests.cs          ← 默认生成/损坏容错/只读目录/原子写
├── ReportBuilderTests.cs           ← 四段结构完整性、空结果、时间戳文件名
├── StateMachineTests.cs            ← 状态迁移合法性（禁止 Completed → Paused）
├── AddressQueueTests.cs            ← 阻塞/唤醒/暂停/终止语义
└── Fixtures/
    ├── SampleScanResult.txt        ← 报告快照基准（人工比对）
    └── broken.ini / overflow.ini   ← 损坏与越界配置样本
```

**测试项目红线**：

| 项 | 规定 |
| --- | --- |
| 测试框架 | **不使用** NUnit/xUnit/MSTest（均需 NuGet）；手写断言，失败即 `Console` 输出 + 退出码 1 |
| 覆盖范围 | 只测**纯逻辑**（字节序、网段、角色、ini、报告、状态机、队列）；**不测**真实网络探测（不可控） |
| 网络相关 | `PortProber`/`ArpProber` 仅测“参数构造与错误码分类”，不发真实包 |
| 发布影响 | 测试项目**不参与**发布构建；`build.ps1` 在 Release 时只编译 `src` |

---

## 9. 命名规范

| 对象 | 规范 | 示例 |
| --- | --- | --- |
| 命名空间 | `MQNetScan.<Layer>.<Sub>` | `MQNetScan.Engine.Contracts.Dto` |
| 文件名 | PascalCase，与主类型同名 | `ScanSession.cs` |
| 私有字段 | `_camelCase` | `_workerThreads` |
| 常量 | `PascalCase`（不用 `ALL_CAPS`） | `MaxArpConcurrency = 16` |
| 事件处理器 | `On<Event>` | `OnStartButtonClick` |
| 中文文案 | 全部集中在 `UiText.cs` 或 `IniSchema.cs` 默认值 | 禁止散落字符串字面量拼接 |
| 接口 | `I` 前缀 | `IResultSink` |

---

## 10. 边界检查清单（评审时逐条核对）

这些是可以**机械检查**的硬规则，用于防止架构腐化：

| 编号 | 规则 | 检查方法 |
| --- | --- | --- |
| B-1 | 只有 `Infrastructure/NativeMethods.cs` 含 `[DllImport]` | 全局搜索 `DllImport`，命中数应为 1 个文件 |
| B-2 | `Engine/` 与 `Application/` 不含 `using System.Windows.Forms` | 按目录搜索 |
| B-3 | `Engine/` 不含 `MessageBox`、`Console.` | 搜索关键字 |
| B-4 | 只有 `Infrastructure/IniConfigStore.cs` 直接 `File.ReadAllText`/`File.WriteAllText` 于 `MQNetScan.ini` | 搜索 `MQNetScan.ini` 字面量出现次数（应集中在 `IniSchema`） |
| B-5 | 只有 `Infrastructure/ProcessLauncher.cs` 调用 `Process.Start` | 搜索 `Process.Start` |
| B-6 | 无 `async` / `await` / `Task` / `HttpClient` | `build.ps1` 自动扫描（§11） |
| B-7 | `ListView.Items.Add` 只出现在 `UiDispatcher` | 搜索 |
| B-8 | 所有 ini 键名来自 `IniSchema` 常量 | 搜索 `"[General]"` 等字面量 |
| B-9 | 窗体标题严格为 `MoeQuick NetScan v1.0 - 萌快网维` | 搜索该字符串出现于 `MainForm` 与 `AssemblyInfo` |

---

## 11. 构建与校验脚本（`build.ps1`）

**位置**：解决方案根目录。

| 步骤 | 动作 | 失败处理 |
| --- | --- | --- |
| 1 | 参数校验：`-Configuration Debug\|Test\|Release` | 非法值直接退出 |
| 2 | 全局禁用 API 扫描：源码中查找 `async `、`await `、`Task.Run`、`Task<`、`HttpClient`、`BlockingCollection` | 命中即**构建失败**并列出文件行号 |
| 3 | `msbuild MQNetScan.sln /p:Configuration=<cfg> /p:TargetFrameworkVersion=v4.0` | 编译错误即失败 |
| 4 | 产物校验：`build\<cfg>\` 下除 `MQNetScan.exe`、`MQNetScan.exe.config`、`*.pdb`（仅 Debug/Test）外不得有其他文件 | 多余文件即失败 |
| 5 | 引用程序集白名单校验：反射读取 exe 的 `GetReferencedAssemblies()`，只允许 `mscorlib`、`System`、`System.Core`、`System.Drawing`、`System.Windows.Forms`、`System.Xml` | 出现白名单外程序集即失败 |
| 6 | Release 专项：断言 `build\Release\` 下**无 `.pdb`** | 有 pdb 即失败 |
| 7 | 图标校验：exe 的 `Icon` 资源可提取（`Icon.ExtractAssociatedIcon` 或资源名检查） | 无图标即失败（不比对内容） |
| 8 | 复制 `doc/` 下的四份文档到 `build\<cfg>\doc\`（可选，`-WithDocs` 开关） | — |

> **白名单为何是这 6 个**：`System.Windows.Forms` 依赖 `System.Drawing`/`System`，`System.Core` 提供 LINQ（4.0 内置），`System.Xml` 仅在未来需要 XML 配置时用。**出现 `System.Net.Http`、`System.Runtime.CompilerServices`（除 `InternalsVisibleTo` 所需）或任何第三方名称，即说明红线被破坏。**

---

## 12. 与上游文档的映射

| architecture.md | project-structure.md 落点 |
| --- | --- |
| §2 四层架构 | §3 目录树（Presentation/Application/Engine/Infrastructure） |
| §3.1 固定线程 + 阻塞队列 | `Engine/AddressQueue.cs`、`Engine/ScanSession.cs` |
| §3.2 SendARP 限流 32 | `Engine/ArpProber.cs` |
| §3.4 非阻塞 connect + Select | `Engine/PortProber.cs` |
| §3.5 状态机 | `Engine/ScanStateMachine.cs` |
| §3.6 Timer 批量提交 | `Presentation/Common/UiDispatcher.cs` |
| §4.1 P/Invoke 集中 | `Infrastructure/NativeMethods.cs`、`NativeStructs.cs` |
| §4.2 网卡枚举 | `Infrastructure/NetworkAdapterService.cs` |
| §4.3 ini 原子写 | `Infrastructure/IniConfigStore.cs`、`IniSchema.cs` |
| §5.1 ListView 选型 | `Presentation/Pages/ScanPage.*`、`Common/ListViewHelper.cs` |
| §7 错误分类 | `Infrastructure/ErrorClassifier.cs` |

| product-requirements.md | 落点 |
| --- | --- |
| FR-1.6 右键菜单 6 项 | `ScanPage` 的 `ContextMenuStrip` + `ProcessLauncher` |
| FR-1.7 报告四段 | `Application/ReportBuilder.cs` |
| FR-3 便捷功能 | `Application/ToolsService.cs` |
| FR-4.2 规则表维护 | `SettingsPage` + `SettingsService` + `IniSchema` |
| UI-1 标题一致 | `AssemblyInfo` + `MainForm.Text`（B-9 校验） |
| NFR-P1 asInvoker | `app.manifest` |

---

## 13. 变更记录

| 版本 | 日期 | 变更内容 |
| --- | --- | --- |
| v1.0 | 2026-11-17 | 首版基线。确认要点：**2 个项目**（主程序 + 自测 Console，不建 Core 类库以保证单文件）；**按分层建目录**（Presentation/Application/Engine/Infrastructure/Resources）；**Contracts 收拢接口与 DTO**；`build.ps1` 三道机器化校验 + 引用白名单；Debug/Test 生成 pdb，**Release 不生成**；图标使用占位 `Resources/app.ico` 待替换 |
| v1.1 | 2026-11-17 | **S0 实施期修正**（环境实测所得）：① 新增 §6.1 **引用程序集双轨解析** —— 原“设置 `TargetFrameworkVersion=v4.0` 即可拦截 4.5+ API”的假设在本机不成立（无 Targeting Pack 时 `Task.Run`/`async` 均编译通过），改为 `FrameworkPathOverride` + 项目内 `.toolchain` 双轨，并已验证拦截有效；② 新增 §6.2 本机构建环境实测结论（无 VS/dotnet/pwsh7、仅 PowerShell 5.1）；③ 明确 `.ps1` 必须 UTF-8 with BOM（`.cs` 不需要，已实测） |
| v1.2 | 2026-11-17 | 产品中文名变更为 **萌快网维**：`UiText.AppTitle`、`AssemblyInfo`、`app.manifest`、`Program` 错误标题、`SmokeTests` 断言、四份文档标题与正文同步更新；B-9 边界检查的目标字符串随之改为 `MoeQuick NetScan v1.0 - 萌快网维` |

---

## 14. S0 实施记录（实际产出）

> 本节记录 S0 阶段**实际建成**的骨架，与本文档描述的目录结构对照。

### 14.1 已创建文件

| 路径 | 说明 |
| --- | --- |
| `MQNetScan.sln` | 解决方案，含 Debug/Test/Release 三配置（`Any CPU`） |
| `build.ps1` | 构建与红线校验脚本（8 步），UTF-8 with BOM |
| `.gitignore` | 排除 `build/`、`.toolchain/`、运行期 ini/log |
| `.vscode/settings.json` | PowerShell 强制 UTF-8 BOM、排除构建目录 |
| `.vscode/tasks.json` | 5 个任务：Debug/Test/Release 构建、运行自测、启动程序 |
| `src/MQNetScan/MQNetScan.csproj` | `WinExe`、`v4.0`、`LangVersion=3`、`AnyCPU` |
| `src/MQNetScan/app.manifest` | `asInvoker` + 4 个 `supportedOS` |
| `src/MQNetScan/app.config` | `supportedRuntime v4.0` |
| `src/MQNetScan/Properties/AssemblyInfo.cs` | 标题与 `UiText.AppTitle` 一致；`InternalsVisibleTo` |
| `src/MQNetScan/Program.cs` | 入口 + 全局异常兜底（中文提示） |
| `src/MQNetScan/Resources/app.ico` | **占位图标**（32×32，深蓝底 + "MQ"），待替换 |
| `src/MQNetScan/Presentation/MainForm.cs` / `.Designer.cs` | 主窗体：3 Tab + 状态栏，标题取自 `UiText` |
| `src/MQNetScan/Presentation/Common/UiText.cs` | 文案单一来源（标题、Tab 名、占位说明） |
| `src/MQNetScan/Presentation/Common/PlaceholderPage.cs` | 占位页基类（S0 临时，后续被真实实现替换） |
| `src/MQNetScan/Presentation/Pages/{Scan,Tools,Settings}Page.cs` / `.Designer.cs` | 3 个 Tab 页 |
| `tests/MQNetScan.Tests/*` | 自测 Console：`Program`、`TestRunner`、`SmokeTests` |

### 14.2 S0 验收结果（实测）

| 验收项 | 结果 |
| --- | --- |
| `build.ps1 -Configuration Debug` 跑通 | ✅ 1.3s，8 步全绿 |
| `build.ps1 -Configuration Release` 跑通 | ✅ 1.2s，含 `.pdb` 缺失校验通过 |
| 输出目录仅 `MQNetScan.exe` + `.exe.config` | ✅ 16 KB |
| 双击启动无 UAC 弹窗 | ✅ 进程直接启动 |
| 窗口标题正确 | ✅ `MoeQuick NetScan v1.0 - 萌快网维` |
| 三 Tab 可切换 | ✅ |
| exe 图标存在 | ✅ 32×32（不比对内容） |
| 引用白名单 | ✅ 仅 4 个：`mscorlib`、`System`、`System.Drawing`、`System.Windows.Forms` |
| `mscorlib` 引用版本 | ✅ `4.0.0.0` |
| 禁用 API 扫描有效 | ✅ 注入 `Task.Run` 后报 `CS0117` 并定位到文件行号 |
| 权限级别校验 | ✅ `asInvoker`（新增第 7 步校验） |
| 自测通过 | ✅ 6 通过 / 0 失败，退出码 0 |
| Release 无 `.pdb` | ✅ |

### 14.3 S0 期间发现并修复的问题

| 编号 | 问题 | 根因 | 修复 |
| --- | --- | --- | --- |
| I-1 | `build.ps1` 整片 `Unexpected token` 语法错误 | PowerShell 5.1 以 ANSI 读取无 BOM 的 `.ps1`，中文注释乱码 | 脚本改存 UTF-8 with BOM |
| I-2 | MSBuild 路径解析出畸形字符串 | `@($a + '\x', $b + '\y')` 中 `+` 优先级高于 `,`，两元素被拼成一个字符串 | 改用 `Join-Path` 分别构造后加入数组 |
| I-3 | `MSB4126: 解决方案配置"Debug\|AnyCPU"无效` | sln 中平台名是 `Any CPU`（带空格） | 传参改为 `/p:Platform=Any CPU` |
| I-4 | `CS0246: 未能找到类型 MainForm` | `Program.cs` 缺 `using MQNetScan.Presentation;` | 补 using |
| I-5 | manifest 注释中的 `requireAdministrator` 字面量被字符串校验误报为已启用提权 | 注释会原样嵌入 exe | 注释改写规避字面量；并新增第 7 步 `asInvoker` 精确校验 |
| I-6 | `edit` 工具保存后丢失 `.ps1` 的 BOM | 工具按无 BOM UTF-8 写回 | 每次编辑后校验并重建 BOM（已记录为操作注意事项） |

### 14.4 S0 遗留事项

| 项 | 说明 |
| --- | --- |
| 占位图标待替换 | `Resources/app.ico` 为临时占位；替换后无需改代码 |
| `warn` 类告警未消 | 编译输出中 `MSB3644`（缺 Targeting Pack）已由脚本过滤显示，属预期 |
| 自测仅冒烟 | S0 只有 6 条冒烟断言；真实单测随各阶段加入 |

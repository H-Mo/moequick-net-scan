# 萌快网维 MoeQuick NetScan — 系统架构与底层探测引擎设计

| 项目 | 内容 |
| --- | --- |
| 文档名称 | architecture.md |
| 版本 | v1.0 |
| 状态 | 已确认（基线版） |
| 上游文档 | [doc/product-requirements.md](./product-requirements.md) |
| 适用产品版本 | v1.0.0 |
| 技术基线 | .NET Framework 4.0 / WinForms / 零第三方依赖 / `asInvoker` |

---

## 1. 架构目标与设计约束

### 1.1 架构目标

1. **支持两种可选存活探测**：ARP 模式使用 `SendARP`（`iphlpapi.dll`），适合同一广播域且可获取 MAC；Ping 模式使用 ICMP Echo，适合路由可达的目标网段。用户明确选择模式，不自动回退。
2. **并发可控且不压垮老机器**：并发度由用户设置决定并**硬性封顶**，引擎不得自行扩张线程。
3. **UI 永不“未响应”**：所有网络 I/O 在后台线程；UI 线程只做定时批量取结果 + 重绘。
4. **在 .NET 4.0 上不写回调地狱**：不使用 `async/await`、`Task`、`Task.Run`；改用**显式线程 + 阻塞队列 + 状态机**，逻辑线性可读。
5. **可中断**：任何阶段都能在有限时间内（≤2 秒）响应暂停/终止。
6. **失败即降级**：任一底层 API 失败只影响当前探测单元，不得向上抛出未捕获异常。

### 1.2 硬性技术红线（架构层面落实）

| 红线 | 架构落实方式 |
| --- | --- |
| 禁止 `async/await`、`Task`、`Task.Run` | 后台工作使用 `Thread`（`IsBackground = true`）；UI 与后台通信使用**共享队列 + `System.Windows.Forms.Timer`**，不使用 `Control.BeginInvoke` 的高频调用 |
| 禁止 `HttpClient` / `WebClient` | 外网/域名连通性仅用 `Dns.GetHostAddresses` + `Socket.Connect` |
| 禁止第三方 DLL / NuGet | 仅引用 GAC 程序集：`System`、`System.Core`、`System.Windows.Forms`、`System.Drawing`、`System.Xml`（读 ini 可不用） |
| `asInvoker` | 架构中不设计“自提权”通道；清 ARP 由用户手动以管理员运行或重启程序（见 PRD FR-3.1.1） |
| Win7 SP1 兼容 | 不使用 `Span`、`ValueTuple`、`IReadOnlyList`、`string.IsNullOrWhiteSpace`（4.0 无）、`Enum.TryParse`（4.0 无）、`Socket.ConnectAsync`（4.5 才有 `Task` 版） |
| 老机器资源受限 | 默认并发 32；SendARP 阶段上限 32；线程栈不额外放大；结果集数量上限保护 |

> **4.0 可用性备忘**：`string.IsNullOrWhiteSpace`、`Enum.TryParse<T>`、`Tuple<...>`（`System.Tuple` 在 4.0 存在）、`Task`（4.0 存在但本产品禁用）——凡本文档未列出的 API，实现前必须确认目标框架可用性。

---

## 2. 分层架构

```
┌─────────────────────────────────────────────────────────────┐
│  Presentation（WinForms UI 层）                              │
│  MainForm / Tab1ScanPage / Tab2ToolsPage / Tab3SettingsPage  │
│  · 只调用引擎公开方法，不直接 P/Invoke                         │
│  · 通过 Timer 轮询 IResultSink 取增量结果                      │
└───────────────────────────┬─────────────────────────────────┘
                            │ 方法调用 / 结果队列
┌───────────────────────────┴─────────────────────────────────┐
│  Application（应用层：用例编排）                              │
│  ScanSessionController / ToolsService / ReportBuilder         │
│  · 组装参数快照、管理会话生命周期、生成报告文本                 │
└───────────────────────────┬─────────────────────────────────┘
                            │
┌───────────────────────────┴─────────────────────────────────┐
│  Engine（探测引擎层）                                         │
│  ScanSession / AddressQueue / ArpProber / PortProber          │
│  ProgressTracker / ResultQueue / ScanStateMachine             │
└───────────────────────────┬─────────────────────────────────┘
                            │
┌───────────────────────────┴─────────────────────────────────┐
│  Infrastructure（基础设施层）                                 │
│  NativeMethods(SendARP/GetIpNetTable) / NetworkAdapterService │
│  IniConfigStore / ProcessLauncher / Logger                    │
└─────────────────────────────────────────────────────────────┘
```

**依赖方向**：Presentation → Application → Engine → Infrastructure。反向依赖（Infrastructure 依赖上层）**禁止**；Engine 不引用 `System.Windows.Forms`（保证可单元测试）。

---

## 3. 探测引擎设计（核心）

### 3.1 线程模型：固定线程 + 阻塞队列

已确认采用**方案 A（自建固定数量工作线程 + 生产者-消费者队列）**，明确不使用 `ThreadPool.QueueUserWorkItem`。

```
                     ┌──────────────────┐
   UI 线程 ──压入──▶ │ AddressQueue     │  (阻塞式队列，容量 = 目标地址数)
  (开始扫描)         │ 待探测 IP 列表    │
                     └────────┬─────────┘
                              │ TryTake（阻塞等待，可被暂停/终止唤醒）
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
        ┌──────────┐   ┌──────────┐   ┌──────────┐
        │ Worker 1 │   │ Worker 2 │ … │ Worker N │   N = min(并发设置, 引擎上限)
        └────┬─────┘   └────┬─────┘   └────┬─────┘
             │  单个 IP 的处理链（见 3.3）    │
             └───────────────┼───────────────┘
                             ▼
                    ┌──────────────────┐
                    │ ResultQueue      │  加锁队列，存放 ArpResult / PortResult
                    └────────┬─────────┘
                             │ Timer.Tick(200ms) 批量取走
                             ▼
                    ┌──────────────────┐
                    │ MainForm (UI)    │  只做 ListView 行插入 + 局部重绘
                    └──────────────────┘
```

**关键设计决定**

| 项 | 决定 | 理由 |
| --- | --- | --- |
| 工作线程数量 N | `N = Clamp(用户设置, 16, 128)`，默认 **32** | 用户可控，引擎不自行扩张 |
| 线程类型 | `IsBackground = true` | 关闭窗口时不阻塞进程退出 |
| 线程栈 | 默认大小，不通过构造函数放大 | 老机器虚拟内存有限 |
| 队列实现 | 自建 `AddressQueue`：`Queue<int>` + `Monitor.Wait/Pulse` | .NET 4.0 无 `BlockingCollection`（4.0 实际有 `System.Collections.Concurrent`，但为兼容性与可控性自建） |
| 出队唤醒 | 暂停/终止时 `PulseAll` 唤醒所有等待线程，让它们检查状态标志后退出 | 保证 ≤2 秒响应终止 |
| 异常处理 | 工作线程外层 `try/catch` 全包裹，异常记录后**继续取下一个任务**，绝不终止线程 | 单个 IP 探测失败不能拖垮整轮扫描 |

> **4.0 说明**：`System.Collections.Concurrent` 在 .NET 4.0 中已存在，但其线程唤醒语义与暂停控制不如自建队列直观。本文档统一按**自建队列**设计，实现时不得引入 `Task`/`BlockingCollection` 的 `Task` 重载。

### 3.2 SendARP 阶段：独立限流（上限 32，v1.1 实测修正）

**已确认：并发 128 个 `SendARP` 不可接受。**

`SendARP` 是**同步阻塞**调用，单次调用在其内部等待期间占用一个线程。若 128 个工作线程同时调用，老机器将承担 128 个线程调度 + 各自的 ARP 等待，内存与 CPU 均不可接受。

**设计：两阶段限流**

| 阶段 | 限流方式 | 上限 |
| --- | --- | --- |
| 阶段 1：ARP 存活探测 | **信号量（`Semaphore`，初始 = 32）**，工作线程取到 IP 后先 `WaitOne` 再调用 `SendARP`，返回即 `Release` | **32**（硬编码上限，用户设置 128 也仍为 32） |
| 阶段 2：端口探测 | 不加额外信号量，由工作线程数量 N 天然限制 | N（≤128，默认 32） |

**为什么端口阶段不需要信号量**：阶段 2 使用**非阻塞 `Connect` + `Socket.Select` 轮询**，一个工作线程可同时持有该 IP 的 12 个 socket 而**不阻塞线程**；等待发生在 `Select` 上，不产生额外线程占用。

#### 3.2.1 SendARP 耗时的实测真相（v1.1 修正，**重要**）

> **本节修正了本文档 v1.0 的一处错误判断**：原文写"单次 `SendARP` 在局域网内通常是个位数毫秒级，254 个地址在 32 路并行下仍在数秒量级完成"。**实测证明这是错的。**

在 Windows 11 (Build 26200) + QEMU/KVM 实测：

| 目标类型 | 单次 SendARP 耗时 | 返回码 |
| --- | --- | --- |
| 有响应（本机） | **0 ms** | 0 |
| 有响应（网关） | **35 ms** | 0 |
| **无响应（空地址）** | **3157~4169 ms** | 67 |
| 跨网段 | 3171 ms | 67 |

**关键结论**：耗时几乎完全由**无响应地址**决定（等 ARP 重传超时），有响应地址极快。典型局域网中绝大多数地址是无响应的，因此**扫描耗时 ≈ 地址数 ÷ 并发数 × 3.5 秒**。

**并发上限实测调优**（32 个空地址）：

| 并发 | 总耗时 | 平均 |
| --- | --- | --- |
| 32 路 | 7393 ms | 231 ms/地址 |
| **32 路** | **4180 ms** | **131 ms/地址** |
| 64 路 | 4186 ms | 131 ms/地址（**与 32 路持平**） |

**结论**：32 是性价比拐点，64 路不再提速（系统 ARP 栈已饱和）。据此把原定的 16 上调为 **32**，`/24` 网段预期约 28 秒。

**推算参考（全无响应的最坏情况）**：

| 网段 | 地址数 | 32 路并发预期耗时 |
| --- | --- | --- |
| /24 | 254 | ≈ 28 秒 |
| /23 | 510 | ≈ 56 秒 |
| /22 | 1022 | ≈ 112 秒 |
| /16 | 65534 | ≈ 2 小时（**不可接受**） |

> **由此产生的产品决策**：**不做耗时预估，只显示进度条**（用户已确认）。因为实际耗时强依赖现场在线设备数量（有响应的地址几乎瞬时完成），无法可靠预估，给出错误数字反而会误导运维判断。

### 3.3 单地址处理链（工作线程内的处理流程）

```
工作线程取到一个 IP
        │
        ├─▶ [1] 状态检查：若 已暂停 → 回队列头部（或等待） / 若 已终止 → 立即退出
        │
        ├─▶ [2] ARP 探测（受信号量 16 限流）
        │      ├─ 成功拿到 MAC ──▶ 记录 ArpResult（IP、MAC、耗时）
        │      └─ 失败/超时   ──▶ 按 SendArpRetry 重试，仍失败 → 该 IP 结束（不入结果）
        │
        ├─▶ [3] IP 冲突判定（仅对已存活 IP）
        │      └─ 与冲突表比对：同 IP 出现第二个不同 MAC → 标记冲突事件
        │
        ├─▶ [4] 端口探测（非阻塞 connect + Select）
        │      ├─ 从规则表快照取全部启用端口（默认 12 个）
        │      ├─ 一次性对全部端口发起非阻塞 Connect
        │      ├─ 单次 Select(等待 socket 列表, 超时 = 单端口超时)
        │      ├─ 逐个判定：可写且有错误 → 关闭；可写且无错误 → 开放
        │      └─ 未完成的 socket 进入下一轮 Select（受整体截止时间保护）
        │
        ├─▶ [5] 角色判定（按内置优先级，见 PRD §7.2）
        │
        └─▶ [6] 组装 HostRecord → 推入 ResultQueue，进度计数 +1
```

**整体截止时间保护**：单个 IP 的端口阶段设置**总预算 = 单端口超时 × 2**（默认 1600ms）。超过预算仍未吐出的 socket 一律记为“关闭”，防止个别黑洞地址拖住工作线程。

### 3.4 端口探测实现要点（非阻塞 connect + `Socket.Select`）

| 要点 | 设计 |
| --- | --- |
| socket 创建 | `new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)`，**逐个创建**（12 个/IP） |
| 非阻塞 | `socket.Blocking = false` 后再调用 `Connect` |
| 连接发起 | `Connect` 对非阻塞 socket 会抛 `SocketException`，错误码 `WSAEWOULDBLOCK`(10035) 属**正常**，表示连接进行中，必须吞掉 |
| 结果判定 | 用 `Socket.Select(null, writeList, errorList, microSeconds)`；socket 出现在 writeList/errorList 即“可判定”，再读 `socket.Poll(0, SelectMode.SelectError)` 或 `socket.Connected` 区分成功/被拒 |
| 超时 | `Select` 的 `microSeconds` 参数，取值 = 剩余预算（µs） |
| 清理 | 无论成功失败，`finally` 中 `socket.Close()`；**禁止**依赖 GC/终结器释放 socket |
| 句柄保护 | 单个 IP 的 socket 数固定 ≤ 端口数，避免句柄暴涨 |
| 端口去重 | 规则表快照在会话创建时**去重并升序**，重复端口只探测一次 |

> **禁止事项**：不得为每个端口创建线程；不得使用 `Socket.ConnectAsync`（4.5+ 语义）；不得使用 `TcpClient.BeginConnect` + 回调（会产生大量回调与未捕获异常风险，且难以统一暂停/终止）。

### 3.5 状态机（暂停/继续/终止的唯一真相来源）

```
        ┌────────┐  开始扫描   ┌────────────┐
        │  Idle  │────────────▶│  Running   │
        └────────┘             └──┬──┬───┬──┘
             ▲                    │  │   │
   终止/完成 │         暂停       │  │   │ 全部地址处理完
             │                    ▼  │   │
             │              ┌────────┴─┐ │
             │              │  Paused  │ │
             │              └────┬─────┘ │
             │        继续        │       │
             │              ┌─────▼──────▼──┐
             │              │   Completed   │
             │              └───────────────┘
             │
        ┌────┴─────┐   终止
        │ Stopped  │◀──────────（Running / Paused 均可直接进入）
        └──────────┘
```

| 状态 | 引擎行为 | UI 表现 |
| --- | --- | --- |
| `Idle` | 无工作线程 | 仅“开始扫描”可用 |
| `Running` | 工作线程正常取任务 | “暂停”“终止”可用，“开始”禁用 |
| `Paused` | 工作线程阻塞在 `Wait`，不再取新任务；在途 ARP/socket 自然完成 | “继续”“终止”可用，状态栏显示“已暂停” |
| `Stopped` | 线程被唤醒后立即退出，结果保留 | 状态栏“已终止（部分结果）” |
| `Completed` | 线程自然退出（队列空） | 状态栏“扫描完成” |

**状态变更唯一入口**：`ScanSessionStateMachine.Transition(newState)`，UI 与工作线程均只能通过它改状态；工作线程在**每个处理链节点前**读取 `Volatile.Read`（用 `volatile` 字段即可）判断是否继续。

### 3.6 进度与结果回传

| 机制 | 设计 |
| --- | --- |
| 进度计数 | `ProgressTracker`：`Interlocked.Increment` 的 `doneCount` + 固定 `totalCount`；暂停时记录“已用时长”暂停累计 |
| 结果队列 | `ResultQueue`：`lock(queue)` 保护 `Queue<HostRecord>`，工作线程 `Enqueue`，UI `Dequeue` 全部 |
| UI 刷新 | `System.Windows.Forms.Timer`，`Interval = 200ms`；Tick 中**一次性取走所有待提交结果**，然后只做 `ListView.BeginUpdate/EndUpdate` 一次包裹的批量插入 |
| 刷新上限保护 | 单次 Tick 最多插入 200 行，剩余留到下一 Tick，避免单次 Tick 过长导致 UI 掉帧 |
| 深度测速回填 | 右键测速结果**不经结果队列**，由 UI 线程直接更新该行的 SubItems（避免跨线程写控件） |
| 红色预警 | 由 `AlertQueue` 单独承载（网关不通 / IP 冲突），UI 在 Tick 中取走并更新横幅；横幅显隐不参与列表重绘 |

**为什么用 `Timer` 而不是 `Control.BeginInvoke`**：高并发下 `BeginInvoke` 会向 UI 消息队列灌入大量消息，导致消息泵拥堵、界面掉帧甚至假死。`Timer` 把“消息数量”与“扫描速度”彻底解耦，无论引擎多快，UI 每 200ms 只处理 1 次。

### 3.7 终止与资源释放契约

| 步骤 | 要求 |
| --- | --- |
| 1 | `Transition(Stopped)` + `PulseAll` 唤醒所有等待中的工作线程 |
| 2 | 工作线程检查状态后跳过剩余处理，`finally` 关闭自身持有的所有 socket |
| 3 | 主线程 `Join` 所有工作线程，**超时上限 2000ms**；超时则记录日志并放弃（线程为后台线程，不阻塞进程退出） |
| 4 | 清空 `ResultQueue` 前先把已有结果提交给 UI（保证“保留部分结果”） |
| 5 | `ScanSession` 实例置空，允许下次新建 |

> 连续 20 次“开始→终止”循环必须无句柄泄漏（PRD NFR-S1）；因此**每个 socket 的所有权必须限定在创建它的工作线程内**，禁止跨线程关闭。

---

## 4. 底层 API 封装（Infrastructure）

### 4.1 P/Invoke 清单

| API | DLL | 用途 | 关键声明要点 |
| --- | --- | --- | --- |
| `SendARP` | `iphlpapi.dll` | 发送 ARP 请求，获取目标 IP 的 MAC | `destIp` 传入的 `uint` 必须**按小端（主机字节序）**构造，见下方字节序实测；`pMacAddr` 为 6 字节缓冲；`phyAddrLen` 传入前必须设为 6，返回后更新 |
| `GetIpNetTable` | `iphlpapi.dll` | 枚举本机 ARP 表（扫描前后交叉比对，辅助 IP 冲突判定） | `MIB_IPNETTABLE` 结构体需手工封送（含可变长数组）；**必须显式 `CharSet = CharSet.Auto`**，32/64 位结构体对齐一致（字段全部为 `int`/`uint`，无指针） |

**`SendARP` 实现约束**

| 项 | 约束 |
| --- | --- |
| 返回码 | `NO_ERROR`(0) = 成功；`ERROR_BAD_NET_NAME`(67)、`ERROR_GEN_FAILURE`(31)、`ERROR_NOT_SUPPORTED`(50) 等 = 未获取到 MAC（**不是异常**） |
| 魔数过滤 | 返回 MAC 全 `0xFF`（广播）或全 `0x00` 视为**无效回应**，不得判定为存活 |
| **IP 字节序** | 目标 IP 由点分字符串转 `uint` 后**必须按小端构造**（即 `BitConverter.ToUInt32(ip.GetAddressBytes(), 0)`）。**这是本 API 最大的坑**，详见下方实测记录 |
| 重试 | `SendArpRetry` 次（默认 1，范围 1~3），每次间隔 50ms |
| 线程安全 | `SendARP` 本身可多线程并发调用，但受 §3.2 信号量限制到 32 路 |
| 超时 | 该 API **无超时参数**，靠系统 ARP 栈行为；因此限流是唯一保护手段 |

#### 4.1.1 字节序实测记录（v1.1 修正，**重要**）

> **本节修正了本文档 v1.0 的一处错误**：原文写"按大端写入"，实测证明**错误**，按大端实现会导致**全部探测失败**。

在 Windows 11 (Build 26200) + QEMU/KVM 虚拟机上实测：

```
目标            字节序构造             SendARP 结果
192.168.122.1   大端 0xC0A87A01  →  rc=67（ERROR_BAD_NET_NAME）  失败
192.168.122.1   小端 0x017AA8C0  →  rc=0  mac=52:54:00:63:FB:B8  成功
192.168.122.39  大端 0xC0A87A27  →  rc=67                        失败
192.168.122.39  小端 0x277AA8C0  →  rc=0  mac=52:54:00:F1:94:F6  成功
```

**正确实现**：

| 步骤 | 做法 |
| --- | --- |
| 1 | `byte[] bytes = IPAddress.Parse(ipText).GetAddressBytes();`（得到网络序字节数组） |
| 2 | `uint destIp = BitConverter.ToUInt32(bytes, 0);`（在 x86/x64 小端机上即得小端值） |
| 3 | 调用 `SendARP(destIp, 0, mac, ref len)` |

**错误表现**：若误用大端，`SendARP` 对所有地址（**包括网关**）均返回 `67`，表现为"扫不到任何设备，但 `arp -a` 明明有条目"。**S5 阶段必须优先排除此问题。**

**单测要求**：`192.168.1.1` → `BitConverter.ToUInt32` 结果应为 `0x0101A8C0`（**不是** `0xC0A80101`）。

### 4.2 网卡枚举（`NetworkAdapterService`）

| 项 | 设计 |
| --- | --- |
| API | `NetworkInterface.GetAllNetworkInterfaces()`（**不用 WMI**，PRD NFR-C4） |
| 过滤条件 | `OperationalStatus == Up` 且 `NetworkInterfaceType ∈ { Ethernet, Wireless80211, GigabitEthernet, FastEthernetFx, FastEthernetT }` |
| 排除条件 | 描述中含 `VMware`/`VirtualBox`/`Hyper-V`/`TAP`/`VPN`/`Loopback`/`Tunnel`/`Npcap`/`Bluetooth` 的一律排除（双保险） |
| 取址 | 遍历 `GetIPProperties().UnicastAddresses`，只取 `AddressFamily.InterNetwork`，跳过 `169.254.x.x`（APIPA）与 `127.0.0.1` |
| 默认网关 | `GetIPProperties().GatewayAddresses` 非空即视为“有网关”；默认选中**第一块有网关的网卡** |
| 网段计算 | 前缀长度来自 `IPv4Mask` → 转换为 CIDR → 计算网络号与可用地址范围 |

**网段计算边界**

| 输入 | 处理 |
| --- | --- |
| 前缀 < 8 | 拒绝（提示“掩码长度需在 8~30 之间”） |
| 前缀 31/32 | 拒绝（无可用主机范围） |
| 目标数 > 254 | 触发确认框（PRD FR-1.1.5） |
| 目标数 > 5000 行上限 | 后台继续探测并计数，但**结果不再入 UI 列表**（PRD NFR-S2）；报告中出现“结果已截断”提示 |
| 跨子网 | 目标地址与本机不在同一子网时，在确认框提示“跨网段设备可能无法被 ARP 发现”（PRD R1） |

### 4.3 配置读写（`IniConfigStore`）

| 项 | 设计 |
| --- | --- |
| 格式 | 标准 INI：`[Section]` + `Key=Value` + `;` 注释（PRD §9.3 键名表） |
| 实现 | 自建轻量解析器（`StreamReader` 逐行 + `Dictionary<string, Dictionary<string,string>>`），**不使用** `GetPrivateProfileString`（其 ANSI 编码与长度限制会导致中文丢失/截断） |
| 编码 | 读写统一 **UTF-8 无 BOM**；读入时若检测到 BOM 则跳过 |
| 原子写 | 写 `MQNetScan.ini.tmp` → `Flush` + `File.Replace` 或 `File.Delete`+`File.Move`；目标不存在时直接 `Move` |
| 容错 | 每个键读取走 `TryGetInt/TryGetBool` 模式：失败即用默认值，并累积“配置项无效”提示列表，由 UI 在状态栏一次性展示 |
| 只读目录 | 写失败捕获 `UnauthorizedAccessException`，转成“配置无法保存”提示，**不抛未捕获异常** |
| 缺省生成 | ini 不存在时按内置默认值生成完整文件（含端口规则默认 12 条） |

---

## 5. UI 层设计（不卡的关键）

### 5.1 控件选型（已确认：`ListView` Details 视图，无虚拟模式）

| 需求 | 实现方式 |
| --- | --- |
| 表格展示 | `ListView`，`View = Details`，`FullRowSelect = true`，`GridLines = true`，`HideSelection = false` |
| 批量插入 | `BeginUpdate()` → 逐条 `Items.Add` → `EndUpdate()`，**一次 Tick 一次包裹** |
| 列头排序 | 自定义 `ListViewItemSorter : IComparer`，通过 `Tag` 记录是否数值列（IP 需按 4 段数值比较，不能按字符串比较） |
| 角色配色 | 按行设置 `BackColor`；配色仅作辅助（PRD §7.3） |
| 复制选中行 | `KeyDown` 捕获 `Ctrl+C` → 拼接 `SubItems` 文本，`\t` 分隔 → `Clipboard.SetText` |
| 双击禁止 | **不绑定 `DoubleClick`/`MouseDoubleClick` 事件**（架构级保证，而非运行时判断）；右键菜单绑定在 `MouseUp` 且仅 `e.Button == Right` |
| 右键菜单 | `ContextMenuStrip`，`Opening` 事件中根据 `SelectedItems.Count` 统一设置 `Enabled` |
| 局部刷新 | 深度测速回填只更新对应 `ListViewItem.SubItems[i].Text`，不重建整表 |

### 5.2 UI 线程与后台的唯一通信面

| 通道 | 方向 | 内容 |
| --- | --- | --- |
| `IResultSink.Post(HostRecord)` | 引擎 → UI | 只入队，不触发重绘 |
| `IResultSink.PostAlert(AlertItem)` | 引擎 → UI | 红色预警事件 |
| `IProgressSource.Snapshot()` | UI → 引擎 | 读取 done/total/状态/耗时（只读快照，加锁返回结构体） |
| `ScanSessionController.Start/Pause/Resume/Stop()` | UI → 引擎 | 唯一控制入口 |

**禁止**：引擎持有任何 `Control` 引用；UI 直接调用 `SendARP`/`Socket`（仅 `ToolsService` 的单点测通例外，其内部也走独立线程）。

### 5.3 单点测通与自检的线程策略

| 功能 | 线程策略 | 超时 |
| --- | --- | --- |
| 单点握手测通 | 每次点击创建一个**临时后台线程**，完成后通过 `Timer` 轮询一个小结果槽回填（或 `Control.BeginInvoke` 单次调用——此处频率极低，允许） | ≤5 秒（PRD FR-3.2.5） |
| 三项连通性自检 | 同上，单项一个线程，三线程并行 | 每项 ≤3 秒 |
| 清 ARP / 打开面板 | 同步调用但极快（`Process.Start` 不阻塞）；`netsh` 用 `Process` + 等待 ≤5 秒 | 5 秒 |

> 低频操作允许 `BeginInvoke`，**高频结果回传必须走 `Timer` 队列**——这是本架构区分“能用”与“好用”的分界线。

---

## 6. 关键流程时序

### 6.1 一次完整扫描

```
用户点“开始扫描”
   │
   ├─▶ UI: 校验网段 → 目标数 >254 则弹确认框
   │
   ├─▶ Controller: 创建 ScanSession（参数快照：并发、超时、端口表副本、重试次数）
   │
   ├─▶ Engine: Transition(Running) → 启动 N 个工作线程 → 主流程线程开始“网关探测”
   │
   ├─▶ 网关探测（ARP + TCP 80/443 兜底）失败 → PostAlert(网关不可达)
   │
   ├─▶ 工作线程循环：取 IP → ARP(限16) → 冲突检查 → 端口(非阻塞+Select) → 角色 → Post
   │
   ├─▶ UI Timer(200ms): 批量插入行 + 更新进度条/状态栏 + 刷新预警横幅
   │
   └─▶ 队列空 → Transition(Completed) → 线程退出 → 状态栏“扫描完成”
```

### 6.2 暂停 / 继续 / 终止

| 动作 | 时序要点 |
| --- | --- |
| 暂停 | `Transition(Paused)` → `PulseAll` → 工作线程在“取任务”与“处理链节点”两处检查到 `Paused` 即 `Wait`；在途 socket 走完当前预算（≤1.6 秒）后停止 |
| 继续 | `Transition(Running)` → `PulseAll`；已入 `ResultQueue` 未提交的结果仍会被 UI 取走（不丢） |
| 终止 | `Transition(Stopped)` → `PulseAll` → `Join(2000ms)` → 提交残留结果 → 状态置“已终止（部分结果）” |

### 6.3 导出报告

```
用户点“导出报告”
   ├─▶ Controller: 从 UI 已展示的 HostRecord 列表 + 会话快照（网段/参数/统计/告警）组装
   ├─▶ ReportBuilder: 纯字符串拼接（StringBuilder），四段结构固定（PRD §6.7）
   ├─▶ 路径决策: 桌面可写 → 桌面；否则 exe 同级；用户可另存
   └─▶ 写文件: UTF-8 带 BOM（new UTF8Encoding(true)）→ 完成后提示完整路径
```

**注意**：报告数据源是**UI 已接受的记录**，不是引擎内部队列——保证“报告内容 = 用户看到的列表”，避免报告与界面不一致。

---

## 7. 错误分类与降级策略

| 分类 | 典型来源 | 处理 |
| --- | --- | --- |
| `ArpNoResponse` | `SendARP` 返回非 0 / MAC 为全 FF/00 | 该 IP 不入列表，静默计入“无响应计数”（仅统计行，不列明细） |
| `ArpUnsupported` | 跨网段、`ERROR_NOT_SUPPORTED` | 计入无响应；会话级提示一次“存在跨网段目标，ARP 可能无效” |
| `PortTimeout` | `Select` 超时 | 记为端口关闭 |
| `PortRefused` | `Poll` 报错（WSAECONNREFUSED 10061） | 记为端口关闭（用于区分拒绝/超时，测通器展示差异） |
| `HostUnreachable` | WSAEHOSTUNREACH 10065 / WSAENETUNREACH 10051 | 记为端口关闭；单点测通器显示“主机不可达” |
| `DnsFail` | `Dns.GetHostAddresses` 抛异常 | 自检项显示“DNS 解析失败” |
| `ConfigInvalid` | ini 键值越界/乱码 | 回退默认 + 状态栏汇总提示 |
| `ConfigNotWritable` | 只读目录 | 仅保存时提示 |
| `ProcessLaunchFail` | mstsc/浏览器/netsh 启动失败 | 弹提示，程序继续 |
| `Unexpected` | 其他 | `Logger` 记录（默认关闭）+ 中文兜底提示 |

**统一约定**：所有异常在**工作线程边界**与**UI 事件处理器边界**各有一层 `try/catch`；任何情况下不得出现未处理异常导致的进程退出（PRD UI-5）。

---

## 8. 数据结构（设计级定义，非代码）

| 结构 | 字段 | 用途 |
| --- | --- | --- |
| `HostRecord` | `Index, IpAddress(string), IpValue(uint), MacAddress(string), OuiPrefix(string), OpenPorts(int[]), Role(string), Ping3Result(nullable struct), ConflictFlag(bool)` | 结果表一行 |
| `ScanParameters` | `Concurrency, PortTimeoutMs, ArpRetry, EnabledPorts(int[]), TargetStart, TargetEnd, CustomRoles` | **会话快照**，创建后不可变 |
| `ScanProgress` | `State, Total, Done, ElapsedMs, AliveCount, NoResponseCount, Truncated(bool)` | 进度快照（加锁返回） |
| `AlertItem` | `Kind(Gateway/Conflict/Config/Info), Message, Suggestion, Timestamp` | 红色预警 / 状态提示 |
| `Ping3Result` | `Attempt1Ms, Attempt2Ms, Attempt3Ms, AverageMs, SuccessCount` | 深度测速结果 |
| `PortRule` | `Port, Role, Comment, Enabled` | 规则表条目 |

**内存约束**：`HostRecord` 只保存必要字段，`OpenPorts` 用 `int[]` 而非 `List<int>`（减少装箱与扩容开销）；结果表上限 5000 行（PRD NFR-S2）。

---

## 9. 兼容性验证清单（架构侧）

| 编号 | 验证点 | 方法 |
| --- | --- | --- |
| A-C1 | 无 4.5+ API | 代码评审 + 编译目标设为 `.NET Framework 4.0` 客户端配置文件，编译期即报错拦截 |
| A-C2 | `SendARP` 字节序正确 | 单测：`192.168.1.1` 经 `BitConverter.ToUInt32(GetAddressBytes(),0)` 应为 `0x0101A8C0`（**小端，不是 0xC0A80101**）；真机对照 `arp -a` |
| A-C3 | 32/64 位结构体对齐 | `GetIpNetTable` 在 Win7 x86 与 Win10 x64 各跑一次，比对 ARP 表条目数 |
| A-C4 | 无 `BlockingCollection`/`Task`/`async` 残留 | 全局搜索关键字（`async`、`Task`、`await`、`HttpClient`、`BlockingCollection`） |
| A-C5 | Win7 下 `Select` 微秒参数行为 | 在 Win7 SP1 实测 `Socket.Select` 超时精度（Windows 计时粒度约 15.6ms，**不得设计依赖毫秒级精度**） |
| A-C6 | 句柄泄漏 | 任务管理器/Process Explorer 观察 20 轮扫描后 Handle 与 Private Bytes 曲线 |
| A-C7 | 无虚拟网卡污染 | 安装 VMware/VirtualBox 的机器上网卡列表仅剩物理网卡 |

> **A-C5 重要推论**：`Socket.Select` 的实际超时精度受系统计时器粒度限制（通常 15~16ms）。因此 **单端口超时设置的下限 200ms 是合理的**；实现时不得假设能精确到 1ms，报告中的耗时数据应标注“含系统计时粒度误差”。

---

## 10. 已知架构取舍与遗留风险

| 编号 | 取舍/风险 | 说明 | 后续可优化点 |
| --- | --- | --- | --- |
| D1 | SendARP 限流 32 可能限制超大网段速度 | 老机器承受力优先，明确不接受 128 路 ARP | 若未来需支持 /16 全扫，可提供“高速模式”开关并附明确风险提示（v1.0 不做） |
| D2 | `ListView` 无虚拟化 | 几十到几百台设备完全够用；>5000 行靠截断保护 | 真遇到大规模需求再评估虚拟列表 |
| D3 | `SendARP` 无超时参数 | 只能靠限流与重试控制；极端网络下单次调用可能较长 | 无法在 4.0 下改善，属 API 固有行为 |
| D4 | 跨网段 ARP 无效 | 协议限制 | 文档层面在确认框与报告中明确提示（PRD R1） |
| D5 | 自建队列而非标准并发集合 | 换取暂停/终止的可控语义 | 若未来升级框架可替换为 `BlockingCollection` |
| D6 | 角色判定依赖端口 | 设备改端口即判为“未知” | 规则表可编辑（PRD FR-4.2）+ 报告中提示补充规则 |

---

## 11. 与 PRD 的一致性映射

| PRD 条目 | 本文档落实位置 |
| --- | --- |
| FR-1.2 SendARP 存活探测 | §3.2、§4.1 |
| FR-1.3 并发端口扫描（非阻塞） | §3.3、§3.4 |
| FR-1.4 进度/暂停/终止 | §3.1、§3.5、§3.7 |
| FR-1.5 红色预警 | §3.6、§6.1 |
| FR-1.6 列表与右键菜单 | §5.1（禁用双击在架构级保证） |
| FR-1.7 报告导出 | §6.3 |
| FR-3 便捷功能 | §5.3、§7 |
| FR-4 设置与 ini 持久化 | §4.3 |
| NFR-C1~C4 兼容性 | §1.2、§9 |
| NFR-S1~S4 稳定性 | §3.7、§7 |
| NFR-P1~P5 权限安全 | §1.2、§4.3 |

---

## 12. 变更记录

| 版本 | 日期 | 变更内容 |
| --- | --- | --- |
| v1.0 | 2026-11-16 | 首版基线。确认要点：固定线程 + 自建阻塞队列（不用线程池）；**SendARP 阶段独立限流上限 32**，默认并发 **32**（范围 16~128）；端口阶段非阻塞 connect + `Socket.Select`；**`ListView` Details 视图、不使用 VirtualMode**；`Timer(200ms)` 批量提交保证 UI 不卡；`ScanSession` 一次性实例 + 参数快照 |
| v1.1 | 2026-11-17 | 产品中文名变更为 **萌快网维**（英文名与技术标识不变）。架构设计无实质变更 |
| v1.2 | 2026-11-17 | **修正 §4.1 字节序错误**：原文写 SendARP 的 destIp 按大端构造，实测证明必须按**小端**（BitConverter.ToUInt32）。新增 §4.1.1 实测记录（大端 rc=67 全失败 / 小端 rc=0 成功），并同步修正 A-C2 验收口径。此错误若带入实现会导致 S5「扫不到任何设备」 |
| v1.3 | 2026-11-18 | **S5 实测修正 §3.2**：① `SendARP` 并发上限 16 → **32**（实测 32 与 64 效果相同，32 为拐点）；② 新增 §3.2.1 实测记录——无响应地址单次要 3~4 秒（非原文所称「个位数毫秒」），`/24` 预期约 28 秒、`/16` 约 2 小时；③ 据此确立**不做耗时预估、只显示进度条**的产品决策 |

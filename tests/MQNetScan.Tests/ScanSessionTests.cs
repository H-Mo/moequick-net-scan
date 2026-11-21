using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using MQNetScan.Engine;
using MQNetScan.Engine.Contracts;

namespace MQNetScan.Tests
{
    /// <summary>
    /// S4 调度核心测试（不进行任何网络操作）。
    /// 覆盖 PRD FR-1.4.1 ~ FR-1.4.6 与 architecture §3.1 / §3.5 / §3.7。
    /// </summary>
    internal static class ScanSessionTests
    {
        public static void Run()
        {
            TestStateMachineTransitions();
            TestAddressQueueSemantics();
            TestProgressTrackerTiming();
            TestFullScanWithFakeProber();
            TestPauseAndResume();
            TestStopWithinTwoSeconds();
            TestResumeDoesNotLoseAddresses();
            TestTwentyStartStopCycles();
            TestWorkerCountClamping();
        }

        // -------------------------------------------------------------------
        // 状态机合法性
        // -------------------------------------------------------------------
        private static void TestStateMachineTransitions()
        {
            Console.WriteLine();
            Console.WriteLine("[S4-1] 状态机迁移合法性（architecture §3.5）");
            TestRunner.BeginCase("合法迁移通过、非法迁移被拒");

            ScanStateMachine sm = new ScanStateMachine();

            TestRunner.AssertEqual(ScanState.Idle, sm.State, "初始为 Idle");
            TestRunner.AssertTrue(!sm.IsActive, "Idle 非活动");

            // Idle → Running
            sm.Transition(ScanState.Running);
            TestRunner.AssertEqual(ScanState.Running, sm.State, "Idle → Running 合法");
            TestRunner.AssertTrue(sm.IsActive, "Running 为活动");
            TestRunner.AssertTrue(sm.ShouldWork, "Running 应工作");

            // Running → Paused
            sm.Transition(ScanState.Paused);
            TestRunner.AssertEqual(ScanState.Paused, sm.State, "Running → Paused 合法");
            TestRunner.AssertTrue(sm.IsActive, "Paused 仍为活动");
            TestRunner.AssertTrue(!sm.ShouldWork, "Paused 不应取任务");

            // Paused → Running
            sm.Transition(ScanState.Running);
            TestRunner.AssertTrue(sm.ShouldWork, "Paused → Running 后恢复工作");

            // Running → Completed
            sm.Transition(ScanState.Completed);
            TestRunner.AssertEqual(ScanState.Completed, sm.State, "Running → Completed 合法");
            TestRunner.AssertTrue(sm.ShouldExit, "Completed 应退出");
            TestRunner.AssertTrue(!sm.IsActive, "Completed 非活动");

            // Completed 是终态
            TestRunner.AssertTrue(!sm.TryTransition(ScanState.Paused), "Completed → Paused 被拒绝（终态）");
            TestRunner.AssertTrue(!sm.TryTransition(ScanState.Running), "Completed → Running 被拒绝");
            TestRunner.AssertEqual(ScanState.Completed, sm.State, "被拒后状态不变");

            // 终止为终态
            ScanStateMachine sm2 = new ScanStateMachine();
            sm2.Transition(ScanState.Running);
            sm2.Transition(ScanState.Stopped);
            TestRunner.AssertTrue(!sm2.TryTransition(ScanState.Running), "Stopped → Running 被拒绝（终态）");
            TestRunner.AssertTrue(!sm2.TryTransition(ScanState.Paused), "Stopped → Paused 被拒绝");

            // 非法组合直接列举验证
            TestRunner.AssertTrue(!ScanStateMachine.IsLegalTransition(ScanState.Idle, ScanState.Paused),
                "Idle → Paused 非法");
            TestRunner.AssertTrue(!ScanStateMachine.IsLegalTransition(ScanState.Idle, ScanState.Completed),
                "Idle → Completed 非法");
            TestRunner.AssertTrue(!ScanStateMachine.IsLegalTransition(ScanState.Paused, ScanState.Completed),
                "Paused → Completed 非法");

            // 幂等
            ScanStateMachine sm3 = new ScanStateMachine();
            sm3.Transition(ScanState.Running);
            sm3.Transition(ScanState.Running);
            TestRunner.AssertEqual(ScanState.Running, sm3.State, "重复迁移到同一状态是幂等的");
        }

        // -------------------------------------------------------------------
        // 队列语义
        // -------------------------------------------------------------------
        private static void TestAddressQueueSemantics()
        {
            Console.WriteLine();
            Console.WriteLine("[S4-2] 地址队列语义");
            TestRunner.BeginCase("取任务、放回队首、关闭唤醒");

            List<uint> addrs = new List<uint>();
            for (uint i = 1; i <= 5; i++) { addrs.Add(i); }

            AddressQueue q = new AddressQueue(addrs);

            TestRunner.AssertEqual(5, q.TotalCount, "初始总数 5");
            TestRunner.AssertEqual(5, q.RemainingCount, "剩余 5");

            uint a;
            Func<bool> always = delegate { return true; };

            TestRunner.AssertTrue(q.TryTake(always, out a), "取到第 1 个");
            TestRunner.AssertEqual(1u, a, "第一个是 1");
            TestRunner.AssertEqual(4, q.RemainingCount, "剩余 4");
            TestRunner.AssertEqual(1, q.TakenCount, "已取 1");

            // 放回队首
            q.PushFront(1u);
            TestRunner.AssertEqual(5, q.RemainingCount, "放回后剩余 5");

            TestRunner.AssertTrue(q.TryTake(always, out a), "再取");
            TestRunner.AssertEqual(1u, a, "放回的元素在队首（顺序未丢）");

            // 关闭后立即返回 false
            q.Close();
            TestRunner.AssertTrue(q.IsClosed, "队列已关闭");
            TestRunner.AssertTrue(!q.TryTake(always, out a), "关闭后 TryTake 返回 false");

            // shouldContinue 返回 false 时也应退出
            AddressQueue q2 = new AddressQueue(addrs);
            Func<bool> never = delegate { return false; };
            TestRunner.AssertTrue(!q2.TryTake(never, out a), "shouldContinue=false 时立即退出");
        }

        // -------------------------------------------------------------------
        // 计时扣除暂停
        // -------------------------------------------------------------------
        private static void TestProgressTrackerTiming()
        {
            Console.WriteLine();
            Console.WriteLine("[S4-3] 计时扣除暂停区间（PRD FR-1.4.2）");
            TestRunner.BeginCase("暂停时间不计入已用时长");

            ProgressTracker p = new ProgressTracker(100);

            p.StartTiming();
            Thread.Sleep(120);
            p.PauseTiming();

            long afterFirst = p.ElapsedMs;
            TestRunner.AssertTrue(afterFirst >= 90, "第一段计时约 120ms（实际 " + afterFirst + "）");

            // 暂停期间等待，不应累加
            Thread.Sleep(300);
            long afterPause = p.ElapsedMs;

            TestRunner.AssertTrue(
                Math.Abs(afterPause - afterFirst) < 30,
                "暂停 300ms 期间计时未增长（" + afterFirst + " → " + afterPause + "）");

            // 恢复计时
            p.StartTiming();
            Thread.Sleep(120);
            p.StopTiming();

            long total = p.ElapsedMs;
            TestRunner.AssertTrue(total >= 180, "恢复后累计约 240ms（实际 " + total + "）");
            TestRunner.AssertTrue(total < 450, "总时长明显小于含暂停的实际时间（" + total + "）");
        }

        // -------------------------------------------------------------------
        // 完整扫描（假数据）
        // -------------------------------------------------------------------
        private static void TestFullScanWithFakeProber()
        {
            Console.WriteLine();
            Console.WriteLine("[S4-4] 254 条假数据完整扫描（PRD FR-1.4.2）");
            TestRunner.BeginCase("进度单调递增、最终完成");

            uint[] targets = BuildTargets(254);
            ScanParameters prm = new ScanParameters(targets, 32, 800, 1, new int[] { 9100, 1433 });
            ScanSession session = new ScanSession(prm, null, new FakeHostProber(1, 5, 0.5));

            session.Start();

            List<int> percents = new List<int>();
            Stopwatch sw = Stopwatch.StartNew();
            bool completed = false;

            while (sw.ElapsedMilliseconds < 20000)
            {
                ScanProgress snap = session.Snapshot();
                percents.Add(snap.Percent);

                if (snap.State == ScanState.Completed) { completed = true; break; }

                Thread.Sleep(40);
            }

            sw.Stop();

            TestRunner.AssertTrue(completed, "扫描在 20 秒内完成（实际 " + sw.ElapsedMilliseconds + "ms）");

            ScanProgress final = session.Snapshot();
            TestRunner.AssertEqual(254, final.Done, "Done 等于目标数 254");
            TestRunner.AssertEqual(100, final.Percent, "进度为 100%");
            TestRunner.AssertEqual(ScanState.Completed, final.State, "状态为 Completed");

            // 进度单调不减
            bool monotonic = true;
            for (int i = 1; i < percents.Count; i++)
            {
                if (percents[i] < percents[i - 1]) { monotonic = false; break; }
            }
            TestRunner.AssertTrue(monotonic, "进度百分比单调不减（采样 " + percents.Count + " 次）");

            TestRunner.AssertTrue(final.AliveCount > 0, "发现存活主机（" + final.AliveCount + " 台）");

            // 结果队列里应有对应数量
            List<HostRecord> results = session.Results.TakeResults(0);
            int resultCount = results == null ? 0 : results.Count;

            TestRunner.AssertEqual(final.AliveCount, resultCount,
                "结果条数与存活计数一致（" + resultCount + "）");
        }

        // -------------------------------------------------------------------
        // 暂停与继续
        // -------------------------------------------------------------------
        private static void TestPauseAndResume()
        {
            Console.WriteLine();
            Console.WriteLine("[S4-5] 暂停与继续（PRD FR-1.4.3）");
            TestRunner.BeginCase("暂停后不再推进、继续后从断点续扫");

            uint[] targets = BuildTargets(200);
            ScanParameters prm = new ScanParameters(targets, 8, 800, 1, new int[] { 9100 });
            ScanSession session = new ScanSession(prm, null, new FakeHostProber(10, 20, 0.5));

            session.Start();

            // 先跑一会儿
            Thread.Sleep(400);
            session.Pause();

            int doneAtPause = session.Snapshot().Done;
            TestRunner.AssertEqual(ScanState.Paused, session.State, "状态为 Paused");

            // 暂停后等待，Done 不应增长（在途任务允许收尾）
            Thread.Sleep(400);
            int doneAfterPause = session.Snapshot().Done;

            // 允许少量在途任务完成，但不应继续大量推进
            int drift = doneAfterPause - doneAtPause;
            TestRunner.AssertTrue(drift >= 0, "暂停期间 Done 不减少");
            TestRunner.AssertTrue(
                drift <= 8 * 2,
                "暂停期间最多在途任务收尾（漂移 " + drift + "，线程数 8）");

            Thread.Sleep(600);
            int doneAfterLongPause = session.Snapshot().Done;
            TestRunner.AssertTrue(
                Math.Abs(doneAfterLongPause - doneAfterPause) <= 2,
                "暂停 600ms 后几乎无推进（" + doneAfterPause + " → " + doneAfterLongPause + "）");

            // 继续
            session.Resume();
            TestRunner.AssertEqual(ScanState.Running, session.State, "继续后为 Running");

            bool completed = session.WaitForCompletion(20000);
            TestRunner.AssertTrue(completed, "继续后能跑完");

            ScanProgress final = session.Snapshot();
            TestRunner.AssertEqual(200, final.Done, "最终全部 200 个地址处理完毕（断点续扫无遗漏）");
        }

        // -------------------------------------------------------------------
        // 终止时效
        // -------------------------------------------------------------------
        private static void TestStopWithinTwoSeconds()
        {
            Console.WriteLine();
            Console.WriteLine("[S4-6] 终止在 2 秒内生效（PRD FR-1.4.4）");
            TestRunner.BeginCase("大范围扫描中途终止");

            uint[] targets = BuildTargets(3000);
            ScanParameters prm = new ScanParameters(targets, 32, 800, 1, new int[] { 9100 });
            ScanSession session = new ScanSession(prm, null, new FakeHostProber(20, 40, 0.4));

            session.Start();
            Thread.Sleep(500);

            int doneBeforeStop = session.Snapshot().Done;

            Stopwatch sw = Stopwatch.StartNew();
            session.Stop();
            sw.Stop();

            long stopMs = sw.ElapsedMilliseconds;

            TestRunner.AssertTrue(stopMs <= 2000,
                "Stop() 在 2 秒内返回（实际 " + stopMs + "ms）");
            TestRunner.AssertEqual(ScanState.Stopped, session.State, "状态为 Stopped");

            // 终止后线程应逐步退出
            Thread.Sleep(300);
            TestRunner.AssertEqual(0, session.ActiveWorkers,
                "全部工作线程已退出（残留 " + session.ActiveWorkers + "）");

            // 已产生的结果必须保留
            ScanProgress final = session.Snapshot();
            List<HostRecord> results = session.Results.TakeResults(0);

            TestRunner.AssertTrue(final.Done > 0, "终止前已完成部分工作（" + final.Done + "）");
            TestRunner.AssertTrue(results != null && results.Count > 0,
                "已产生的结果被保留（" + (results == null ? 0 : results.Count) + " 条）");

            // Done 不应在终止后继续大幅增长
            int doneAfterStop = final.Done;
            Thread.Sleep(400);
            int doneLater = session.Snapshot().Done;

            TestRunner.AssertTrue(doneLater - doneAfterStop <= 32,
                "终止后不再推进（" + doneAfterStop + " → " + doneLater + "）");
        }

        // -------------------------------------------------------------------
        // 暂停瞬间不丢任务
        // -------------------------------------------------------------------
        private static void TestResumeDoesNotLoseAddresses()
        {
            Console.WriteLine();
            Console.WriteLine("[S4-7] 暂停瞬间不丢地址（PRD FR-1.4.3 断点续扫）");
            TestRunner.BeginCase("高频暂停/继续后仍能扫完所有地址");

            uint[] targets = BuildTargets(120);
            ScanParameters prm = new ScanParameters(targets, 16, 800, 1, new int[] { 9100 });
            ScanSession session = new ScanSession(prm, null, new FakeHostProber(2, 6, 0.5));

            session.Start();

            // 快速交替暂停与继续，制造竞争条件
            for (int i = 0; i < 12; i++)
            {
                Thread.Sleep(25);
                session.Pause();
                Thread.Sleep(15);
                session.Resume();
            }

            bool completed = session.WaitForCompletion(30000);

            TestRunner.AssertTrue(completed, "高频暂停后仍能完成");

            ScanProgress final = session.Snapshot();
            TestRunner.AssertEqual(120, final.Done,
                "全部 120 个地址都被处理（无丢失，实际 " + final.Done + "）");
            TestRunner.AssertEqual(ScanState.Completed, final.State,
                "最终状态为 Completed（实际 " + final.State + "）");

            // 注意：TakeResults 在队列为空时返回 null，不能直接 .Count
            List<HostRecord> firstRead = session.Results.TakeResults(0);
            int firstCount = firstRead == null ? 0 : firstRead.Count;

            List<HostRecord> secondRead = session.Results.TakeResults(0);
            int secondCount = secondRead == null ? 0 : secondRead.Count;

            TestRunner.AssertTrue(firstCount > 0, "首次读到结果（" + firstCount + " 条）");
            TestRunner.AssertEqual(0, secondCount, "第二次读取为空（队列已清空）");
        }

        // -------------------------------------------------------------------
        // 20 轮开始→终止
        // -------------------------------------------------------------------
        private static void TestTwentyStartStopCycles()
        {
            Console.WriteLine();
            Console.WriteLine("[S4-8] 连续 20 轮「开始→终止」（PRD NFR-S1）");
            TestRunner.BeginCase("无线程堆积、无异常、内存不持续增长");

            uint[] targets = BuildTargets(500);

            long memBefore = GC.GetTotalMemory(true);
            int threadsBefore = Process.GetCurrentProcess().Threads.Count;

            bool anyException = false;
            long maxStopMs = 0;

            for (int round = 0; round < 20; round++)
            {
                ScanParameters prm = new ScanParameters(targets, 16, 800, 1, new int[] { 9100 });
                ScanSession session = new ScanSession(prm, null, new FakeHostProber(5, 15, 0.5));

                try
                {
                    session.Start();
                    Thread.Sleep(60);

                    Stopwatch sw = Stopwatch.StartNew();
                    session.Stop();
                    sw.Stop();

                    if (sw.ElapsedMilliseconds > maxStopMs) { maxStopMs = sw.ElapsedMilliseconds; }

                    // 确认线程退出，避免下一轮叠加
                    int wait = 0;
                    while (session.ActiveWorkers > 0 && wait < 2000)
                    {
                        Thread.Sleep(20);
                        wait += 20;
                    }

                    if (session.ActiveWorkers != 0) { anyException = true; }
                }
                catch (Exception ex)
                {
                    anyException = true;
                    Console.WriteLine("     第 " + (round + 1) + " 轮异常: " + ex.Message);
                }

                session.Results.Clear();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Thread.Sleep(400);

            long memAfter = GC.GetTotalMemory(true);
            int threadsAfter = Process.GetCurrentProcess().Threads.Count;

            TestRunner.AssertTrue(!anyException, "20 轮全部无异常、线程均正常退出");
            TestRunner.AssertTrue(maxStopMs <= 2000, "每轮终止都在 2 秒内（最大 " + maxStopMs + "ms）");

            Console.WriteLine("     内存: " + (memBefore / 1024) + " KB → " + (memAfter / 1024) + " KB");
            Console.WriteLine("     线程数: " + threadsBefore + " → " + threadsAfter);

            // 线程数不应持续增长（允许少量波动）
            TestRunner.AssertTrue(
                threadsAfter <= threadsBefore + 8,
                "线程数无堆积（" + threadsBefore + " → " + threadsAfter + "）");

            // 内存增长允许一定波动，但不应达到数十 MB 级别
            long growthMb = (memAfter - memBefore) / (1024 * 1024);
            TestRunner.AssertTrue(
                growthMb < 20,
                "内存无明显增长（增长 " + growthMb + " MB）");
        }

        // -------------------------------------------------------------------
        // 线程数边界
        // -------------------------------------------------------------------
        private static void TestWorkerCountClamping()
        {
            Console.WriteLine();
            Console.WriteLine("[S4-9] 工作线程数取值");
            TestRunner.BeginCase("目标数少于并发数时不超发线程");

            // 目标 3 个，并发 32 → 实际线程应为 3
            uint[] few = BuildTargets(3);
            ScanParameters prm = new ScanParameters(few, 32, 800, 1, new int[] { 9100 });
            ScanSession session = new ScanSession(prm, null, new FakeHostProber(1, 2, 1.0));

            session.Start();
            TestRunner.AssertEqual(3, session.WorkerCount, "目标 3 个时只起 3 个线程");

            session.WaitForCompletion(5000);
            TestRunner.AssertEqual(ScanState.Completed, session.State, "小规模扫描正常完成");

            // 并发 16 目标 100 → 16 个线程
            uint[] many = BuildTargets(100);
            ScanParameters prm2 = new ScanParameters(many, 16, 800, 1, new int[] { 9100 });
            ScanSession session2 = new ScanSession(prm2, null, new FakeHostProber(1, 3, 0.5));

            session2.Start();
            TestRunner.AssertEqual(16, session2.WorkerCount, "并发 16 时起 16 个线程");

            session2.Stop();
        }

        // -------------------------------------------------------------------
        private static uint[] BuildTargets(int count)
        {
            uint[] arr = new uint[count];
            uint baseAddr;
            Ipv4Util.TryParse("192.168.1.1", out baseAddr);

            for (int i = 0; i < count; i++)
            {
                arr[i] = baseAddr + (uint)i;
            }

            return arr;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using MQNetScan.Engine.Contracts;

namespace MQNetScan.Infrastructure
{
    internal sealed class DeviceNameLookupQueue : IDisposable
    {
        public const int WorkerCount = 2;
        public const int MaxPending = 512;

        private sealed class WorkItem
        {
            public uint Address;
            public HostRecord Record;
            public Action<HostRecord> Completed;
        }

        private readonly Queue<WorkItem> _queue = new Queue<WorkItem>();
        private readonly HashSet<uint> _pending = new HashSet<uint>();
        private readonly object _lock = new object();
        private readonly Thread[] _workers;
        private bool _stopping;

        public DeviceNameLookupQueue()
        {
            _workers = new Thread[WorkerCount];
            for (int i = 0; i < _workers.Length; i++)
            {
                _workers[i] = new Thread(WorkerLoop);
                _workers[i].IsBackground = true;
                _workers[i].Name = "Device name lookup " + (i + 1);
                _workers[i].Start();
            }
        }

        public bool Enqueue(HostRecord record, Action<HostRecord> completed)
        {
            if (record == null || completed == null) { return false; }
            lock (_lock)
            {
                if (_stopping || _queue.Count >= MaxPending || !_pending.Add(record.IpAddress))
                {
                    return false;
                }
                WorkItem item = new WorkItem();
                item.Address = record.IpAddress;
                item.Record = record;
                item.Completed = completed;
                _queue.Enqueue(item);
                Monitor.Pulse(_lock);
                return true;
            }
        }

        private void WorkerLoop()
        {
            while (true)
            {
                WorkItem item;
                lock (_lock)
                {
                    while (!_stopping && _queue.Count == 0) { Monitor.Wait(_lock); }
                    if (_stopping) { _queue.Clear(); _pending.Clear(); return; }
                    item = _queue.Dequeue();
                }

                string name = DeviceNameResolver.Resolve(item.Address);
                lock (_lock) { _pending.Remove(item.Address); }
                if (name.Length == 0) { continue; }

                item.Record.DeviceName = name;
                try { item.Completed(item.Record); }
                catch (Exception) { }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _stopping = true;
                Monitor.PulseAll(_lock);
            }
        }
    }
}

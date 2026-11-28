using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MQNetScan.Application;

namespace MQNetScan.Presentation.Pages
{
    internal partial class LocalMachineInfoPage : UserControl
    {
        private sealed class CategoryState
        {
            public LocalInfoSnapshot Snapshot;
            public bool InProgress;
            public bool RefreshQueued;
            public int Generation;
        }

        private readonly CategoryState[] _states = new CategoryState[4];
        private readonly Func<LocalInfoCategory, LocalInfoSnapshot> _reader;
        private bool _activated;
        private bool _disposed;

        public LocalMachineInfoPage() : this(LocalMachineInfoService.Read) { }

        internal LocalMachineInfoPage(Func<LocalInfoCategory, LocalInfoSnapshot> reader)
        {
            _reader = reader ?? LocalMachineInfoService.Read;
            for (int i = 0; i < _states.Length; i++) { _states[i] = new CategoryState(); }
            InitializeComponent();
            listCategories.Items.Add("系统");
            listCategories.Items.Add("网络");
            listCategories.Items.Add("硬件");
            listCategories.Items.Add("运行环境");
            listCategories.SelectedIndex = 0;
            listCategories.SelectedIndexChanged += OnCategoryChanged;
            RenderCurrentCategory();
        }

        internal int SelectedCategoryIndex { get { return listCategories.SelectedIndex; } }

        public void ActivatePage()
        {
            if (_disposed || _activated) { return; }
            _activated = true;
            EnsureCurrentCategoryLoaded(false);
        }

        private void OnCategoryChanged(object sender, EventArgs e)
        {
            RenderCurrentCategory();
            if (_activated) { EnsureCurrentCategoryLoaded(false); }
        }

        private void OnRefreshClick(object sender, EventArgs e)
        {
            if (!_activated) { _activated = true; }
            EnsureCurrentCategoryLoaded(true);
        }

        private void EnsureCurrentCategoryLoaded(bool forceRefresh)
        {
            int index = listCategories.SelectedIndex;
            if (index < 0 || index >= _states.Length) { return; }
            CategoryState state = _states[index];

            if (state.InProgress)
            {
                if (forceRefresh) { state.RefreshQueued = true; }
                RenderCurrentCategory();
                return;
            }

            if (!forceRefresh && IsCacheValid(index, state.Snapshot))
            {
                RenderCurrentCategory();
                return;
            }

            StartLoad(index, state);
        }

        private void StartLoad(int index, CategoryState state)
        {
            state.InProgress = true;
            state.Generation++;
            int generation = state.Generation;
            RenderCurrentCategory();

            Thread worker = new Thread(delegate()
            {
                LocalInfoSnapshot snapshot;
                try { snapshot = _reader((LocalInfoCategory)index); }
                catch (Exception ex)
                {
                    snapshot = new LocalInfoSnapshot((LocalInfoCategory)index,
                        new List<LocalInfoRow>(), "读取失败：" + ex.Message);
                }
                PostLoadCompleted(index, generation, snapshot);
            });
            worker.IsBackground = true;
            worker.Name = "Local information " + CategoryName(index);
            worker.Start();
        }

        private void PostLoadCompleted(int index, int generation, LocalInfoSnapshot snapshot)
        {
            if (_disposed || IsDisposed || !IsHandleCreated) { return; }
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (_disposed || IsDisposed) { return; }
                    CategoryState state = _states[index];
                    if (state.Generation != generation) { return; }

                    state.Snapshot = snapshot;
                    state.InProgress = false;
                    bool refreshAgain = state.RefreshQueued;
                    state.RefreshQueued = false;

                    if (listCategories.SelectedIndex == index) { RenderCurrentCategory(); }
                    if (refreshAgain && !_disposed) { StartLoad(index, state); }
                }));
            }
            catch (InvalidOperationException) { }
        }

        private bool IsCacheValid(int index, LocalInfoSnapshot snapshot)
        {
            if (snapshot == null || snapshot.CollectedAt == DateTime.MinValue) { return false; }
            return DateTime.Now - snapshot.CollectedAt < CacheLifetime(index);
        }

        internal static TimeSpan CacheLifetime(int categoryIndex)
        {
            if (categoryIndex == (int)LocalInfoCategory.System) { return TimeSpan.FromMinutes(10); }
            if (categoryIndex == (int)LocalInfoCategory.Network) { return TimeSpan.FromSeconds(30); }
            if (categoryIndex == (int)LocalInfoCategory.Hardware) { return TimeSpan.FromMinutes(30); }
            return TimeSpan.FromMinutes(10);
        }

        private void RenderCurrentCategory()
        {
            int index = listCategories.SelectedIndex;
            if (index < 0 || index >= _states.Length) { return; }
            CategoryState state = _states[index];
            listInfo.BeginUpdate();
            try
            {
                listInfo.Items.Clear();
                if (state.Snapshot != null)
                {
                    if (state.Snapshot.Error.Length > 0)
                    {
                        AddRow("读取状态", state.Snapshot.Error);
                    }
                    for (int i = 0; i < state.Snapshot.Rows.Count; i++)
                    {
                        AddRow(state.Snapshot.Rows[i].Name, state.Snapshot.Rows[i].Value);
                    }
                    lblUpdated.Text = "上次读取：" + state.Snapshot.CollectedAt.ToString("HH:mm:ss");
                }
                else
                {
                    lblUpdated.Text = "尚未读取";
                }

                if (state.InProgress)
                {
                    lblLoadState.Text = state.RefreshQueued ? "正在读取，刷新已排队…" : "正在读取…";
                }
                else if (state.Snapshot == null)
                {
                    lblLoadState.Text = "等待加载";
                }
                else if (state.Snapshot.Error.Length > 0)
                {
                    lblLoadState.Text = "读取未完整";
                }
                else if (IsCacheValid(index, state.Snapshot))
                {
                    lblLoadState.Text = "已加载（缓存有效）";
                }
                else
                {
                    lblLoadState.Text = "缓存已过期；再次选择或刷新时更新";
                }
                btnRefresh.Enabled = !state.InProgress;
            }
            finally { listInfo.EndUpdate(); }
        }

        private void AddRow(string name, string value)
        {
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(value)) { return; }
            ListViewItem item = new ListViewItem(name);
            item.SubItems.Add(string.IsNullOrEmpty(value) ? "—" : value);
            listInfo.Items.Add(item);
        }

        internal void MarkDisposed()
        {
            _disposed = true;
        }

        private static string CategoryName(int index)
        {
            string[] names = { "System", "Network", "Hardware", "Runtime" };
            return index >= 0 && index < names.Length ? names[index] : "Unknown";
        }

    }
}

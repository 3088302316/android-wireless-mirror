using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using AppButton = AccessibleButton;

internal sealed class DeviceSettings : Form
{
    private readonly DeviceDirectory directory;
    private readonly DeviceRecord active;
    private List<DeviceRecord> rows;
    private readonly ListBox devices = new ListBox();
    private readonly DeviceArtwork artwork = new DeviceArtwork();
    private readonly Label deviceStatus = new Label();
    private readonly Label listCount = new Label();
    private readonly Label emptyHint = new Label();
    private readonly Label chosenName = new Label();
    private readonly Label chosenAddress = new Label();
    private readonly Label chosenDetail = new Label();
    private readonly Label detailTip = new Label();
    private readonly AntdUI.Tag chosenState = new AntdUI.Tag();
    private readonly Panel content = new Panel { Dock = DockStyle.Fill };
    private readonly Panel[] pages = new Panel[3];
    private readonly AppButton[] navigation = new AppButton[3];
    private static readonly string[] NavigationNames = { "设备管理", "快捷键", "连接设置" };
    private readonly AppButton connect, rename, history, lowLatency, refresh, pairQr, pairCode;
    private readonly Label profileStatus = new Label();
    private readonly AppButton applyProfile, probeButton, networkButton, logButton;
    private readonly ConnectionProfile draft;
    private AntdUI.Slider sizeSlider, fpsSlider, bitrateSlider, videoBufferSlider, audioBufferSlider;
    private Label sizeReading, fpsReading, bitrateReading, videoBufferReading, audioBufferReading;
    private AppButton audioButton, settingsAudioButton;
    private AntdUI.Switch statsSwitch;
    private ContextMenuStrip audioMenu;
    private readonly Timer statusTimer = new Timer { Interval = 8000 };
    private readonly ToolTip tooltips = new ToolTip();
    private readonly Font deviceNameFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
    private readonly Font smallFont = new Font("Microsoft YaHei UI", 8.7f);
    private readonly Font tinyFont = new Font("Microsoft YaHei UI", 8.5f);
    private readonly Bitmap phoneIcon;
    private readonly Bitmap appIcon;
    private string summary = "";
    private int hoverIndex = -1;
    private bool scanning;
    private bool loaded;
    public event Action<DeviceRecord> OpenRequested;
    public event Action<DeviceRecord> RenameApplied;
    public event Action<DeviceRecord, ConnectionProfile> SettingsSaved;
    public Func<DeviceRecord, string> StreamStatus;
    public ConnectionProfile SavedProfile { get; private set; }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    public DeviceSettings(DeviceDirectory directory, List<DeviceRecord> rows, int page, DeviceRecord active = null, ConnectionProfile profile = null)
    {
        this.directory = directory;
        this.active = active;
        this.rows = rows ?? new List<DeviceRecord>();
        if (this.rows.Count == 0 && active == null)
        {
            try { this.rows = directory.Cached(); }
            catch (Exception error) { AppLog.Warn("读取设备历史失败: " + error.Message); }
        }
        draft = profile == null ? ConnectionProfile.Load() : profile.Copy();
        phoneIcon = AntdUI.SvgExtend.SvgToBmp(MirrorIcons.Smartphone, 22, 22, null);
        var loadedIcon = WirelessMirror.LoadIcon();
        appIcon = loadedIcon.ToBitmap();
        if (!ReferenceEquals(loadedIcon, SystemIcons.Application)) loadedIcon.Dispose();

        Text = active == null ? "安卓无线投屏 · 设备中心" : active.Name + " · 投屏设置";
        Icon = WirelessMirror.LoadIcon();
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Microsoft YaHei UI", 10f);
        BackColor = WindowTheme.Canvas;
        ForeColor = WindowTheme.Ink;
        ClientSize = new Size(1040, 700);
        MinimumSize = new Size(900, 610);
        DoubleBuffered = true;
        DoubleBuffer(content);
        TopMost = active != null;
        WindowTheme.Attach(this);

        var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 206));
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(frame);
        frame.Controls.Add(Sidebar(), 0, 0);
        frame.Controls.Add(content, 1, 0);

        connect = ActionButton("打开投屏", PickSelected, true, MirrorIcons.Play);
        connect.Size = new Size(144, 46);
        connect.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        rename = ActionButton("重命名", Rename, false, "EditOutlined");
        history = ActionButton("连接记录", ShowHistory, false, MirrorIcons.History);
        lowLatency = ActionButton("低延迟", EnableLowLatency, false, MirrorIcons.Play);
        lowLatency.Size = new Size(96, 46);
        refresh = ActionButton("刷新设备", delegate { RefreshFromButton(); }, false, MirrorIcons.Refresh);
        refresh.Size = new Size(112, 40);
        pairQr = ActionButton("扫码配对", PairWithQr, false, MirrorIcons.QrCode);
        pairCode = ActionButton("六位码配对", PairWithCode, false, MirrorIcons.Keyboard);
        tooltips.SetToolTip(refresh, "刷新设备列表（F5）");
        tooltips.SetToolTip(connect, "打开选中设备。按住 Ctrl 可选择多台。");
        tooltips.SetToolTip(lowLatency, "套用 0ms 视频缓冲和 90fps；鼠标保持普通桌面指针，不会被窗口捕获");
        tooltips.SetToolTip(pairQr, "用手机扫描二维码完成无线调试配对");
        tooltips.SetToolTip(pairCode, "输入手机配对弹窗里的地址和六位码");

        pages[0] = DevicePage();
        pages[1] = KeyPage();
        applyProfile = ActionButton(active == null ? "保存设置" : "保存并重连", SaveProfile, true, "CheckOutlined");
        probeButton = ActionButton("ADB 延迟", ProbeConnection, false, MirrorIcons.Wifi);
        probeButton.Size = new Size(106, 40);
        networkButton = ActionButton("Wi-Fi 往返", ProbeNetwork, false, MirrorIcons.Wifi);
        networkButton.Size = new Size(118, 40);
        logButton = ActionButton("日志", OpenLogFolder, false, null);
        logButton.Size = new Size(72, 40);
        tooltips.SetToolTip(networkButton, "用 ICMP 测试电脑到手机的局域网往返、抖动和丢包");
        tooltips.SetToolTip(logButton, "打开 %LOCALAPPDATA%\\AndroidWirelessMirror\\logs");
        pages[2] = QualityPage();
        Fill(rows);
        ShowPage(Math.Max(0, Math.Min(page, pages.Length - 1)));

        statusTimer.Tick += delegate
        {
            statusTimer.Stop();
            if (!scanning) SetStatusText(summary, WindowTheme.Muted);
        };
        Shown += async delegate
        {
            BeginInvoke((MethodInvoker)delegate
            {
                if (IsDisposed) return;
                Activate();
                BringToFront();
                SetForegroundWindow(Handle);
            });
            if (loaded) return;
            loaded = true;
            await RefreshDevicesAsync();
            devices.Focus();
        };
    }

    private Control Sidebar()
    {
        var side = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = WindowTheme.Rail,
            Padding = new Padding(14, 20, 14, 16),
            Margin = new Padding(0)
        };
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));

        var brand = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var logo = new PictureBox { Image = appIcon, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(34, 34), Margin = new Padding(0, 3, 10, 0), BackColor = Color.Transparent };
        brand.Controls.Add(logo, 0, 0);
        var brandText = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        brandText.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        brandText.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        brandText.Controls.Add(new Label { Text = "安卓无线投屏", AutoSize = true, ForeColor = WindowTheme.Ink, Font = new Font("Microsoft YaHei UI", 12.4f, FontStyle.Bold), Margin = new Padding(0) }, 0, 0);
        brandText.Controls.Add(new Label { Text = "局域网设备中心", AutoSize = true, ForeColor = WindowTheme.Faint, Font = new Font("Microsoft YaHei UI", 8.6f), Margin = new Padding(0) }, 0, 1);
        brand.Controls.Add(brandText, 1, 0);
        side.Controls.Add(brand, 0, 0);

        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        var badge = new AntdUI.Tag { Text = "局域网 · Wi-Fi", ImageSvg = MirrorIcons.Wifi, Type = AntdUI.TTypeMini.Info, Radius = 8, Size = new Size(150, 33), Margin = new Padding(2, 0, 0, 16) };
        badge.TabStop = false;
        nav.Controls.Add(badge);
        nav.Controls.Add(SectionLabel("工作区"));
        string[] icons = { MirrorIcons.Smartphone, MirrorIcons.Keyboard, MirrorIcons.Sliders };
        for (int index = 0; index < NavigationNames.Length; index++)
        {
            int destination = index;
            navigation[index] = ActionButton(NavigationNames[index], delegate { ShowPage(destination); }, false, icons[index]);
            navigation[index].Size = new Size(178, 46);
            navigation[index].TextAlign = ContentAlignment.MiddleLeft;
            navigation[index].Padding = new Padding(14, 0, 0, 0);
            navigation[index].Margin = new Padding(0, 0, 0, 6);
            nav.Controls.Add(navigation[index]);
        }
        side.Controls.Add(nav, 0, 1);

        var tip = new AntdUI.Panel
        {
            Dock = DockStyle.Fill,
            Radius = 11,
            Back = WindowTheme.Surface,
            BorderWidth = 1,
            BorderColor = WindowTheme.Line,
            Margin = new Padding(0, 10, 0, 10)
        };
        var tipText = new Label
        {
            Dock = DockStyle.Fill,
            Text = "手机准备\n1. 开启无线调试\n2. 与电脑同一 Wi-Fi\n\nF5  刷新设备\nCtrl+P  六位码配对",
            ForeColor = WindowTheme.Muted,
            Font = new Font("Microsoft YaHei UI", 8.7f),
            Padding = new Padding(13, 12, 10, 10),
            TextAlign = ContentAlignment.TopLeft
        };
        tip.Controls.Add(tipText);
        side.Controls.Add(tip, 0, 3);
        return side;
    }

    private static Label SectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = WindowTheme.Faint,
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
            Margin = new Padding(7, 0, 0, 8)
        };
    }

    private static Label TextLabel(string text, float size, Color color, bool bold = false)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = color,
            Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular)
        };
    }

    internal static AppButton StyledButton(string title, string icon, Action action, bool primary)
    {
        var button = new AppButton
        {
            Text = title,
            AccessibleName = title,
            AccessibleRole = AccessibleRole.PushButton,
            IconRatio = 0.9F,
            Radius = 10,
            Size = new Size(Math.Max(94, 48 + title.Length * 14), 42),
            Type = primary ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default,
            DefaultBack = WindowTheme.Surface,
            DefaultBorderColor = Color.FromArgb(61, 72, 101),
            BorderWidth = 1,
            BackColor = primary ? WindowTheme.Accent : WindowTheme.Surface,
            ForeColor = WindowTheme.Ink,
            ForeHover = WindowTheme.Ink,
            ForeActive = WindowTheme.Ink,
            BackHover = WindowTheme.Selected,
            Margin = new Padding(0, 0, 8, 4)
        };
        if (icon != null && icon.StartsWith("<svg", StringComparison.Ordinal))
        {
            var bitmap = AntdUI.SvgExtend.SvgToBmp(icon, 32, 32, null);
            button.Icon = bitmap;
            if (bitmap != null) button.Disposed += delegate { bitmap.Dispose(); };
        }
        else button.IconSvg = icon;
        if (action != null) button.Click += delegate { action(); };
        return button;
    }

    private AppButton ActionButton(string title, Action action, bool primary = false, string icon = null)
    {
        return StyledButton(title, icon, delegate
        {
            try { action(); }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }, primary);
    }

    private Panel Page(string title, string subtitle, Control body, Control footer, Control headerAction, bool liveStatus = false)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(26, 22, 26, 20), BackColor = WindowTheme.Canvas };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = WindowTheme.Ink, Font = new Font("Microsoft YaHei UI", 23f, FontStyle.Bold), Margin = new Padding(0) }, 0, 0);
        Label caption;
        if (liveStatus)
        {
            deviceStatus.Text = subtitle;
            deviceStatus.AutoSize = false;
            deviceStatus.Dock = DockStyle.Fill;
            deviceStatus.ForeColor = WindowTheme.Muted;
            deviceStatus.Font = new Font("Microsoft YaHei UI", 9.4f);
            deviceStatus.Margin = new Padding(1, 4, 0, 0);
            caption = deviceStatus;
        }
        else
        {
            caption = new Label
            {
                Text = subtitle,
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = WindowTheme.Muted,
                Font = new Font("Microsoft YaHei UI", 9.4f),
                Margin = new Padding(1, 4, 0, 0)
            };
        }
        heading.Controls.Add(caption, 0, 1);
        header.Controls.Add(heading, 0, 0);
        if (headerAction != null)
        {
            headerAction.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            header.Controls.Add(headerAction, 1, 0);
        }
        layout.Controls.Add(header, 0, 0);
        body.Dock = DockStyle.Fill;
        body.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(body, 0, 1);
        if (footer != null)
        {
            footer.Dock = DockStyle.Fill;
            footer.Margin = new Padding(0, 8, 0, 0);
            layout.Controls.Add(footer, 0, 2);
        }
        else layout.RowStyles[2] = new RowStyle(SizeType.Absolute, 0);
        panel.Controls.Add(layout);
        return panel;
    }

    private void ShowPage(int page)
    {
        if (content.Controls.Contains(pages[page])) return;
        content.SuspendLayout();
        content.Controls.Clear();
        content.Controls.Add(pages[page]);
        for (int i = 0; i < navigation.Length; i++)
        {
            navigation[i].Text = NavigationNames[i];
            navigation[i].ForeColor = WindowTheme.Ink;
            navigation[i].ForeHover = WindowTheme.Ink;
            navigation[i].ForeActive = WindowTheme.Ink;
            navigation[i].Type = i == page ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
            navigation[i].DefaultBack = navigation[i].BackColor = i == page ? WindowTheme.Accent : WindowTheme.Rail;
            navigation[i].BorderWidth = i == page ? 1 : 0;
            navigation[i].Invalidate();
        }
        content.ResumeLayout(true);
        content.PerformLayout();
    }

    private static void DoubleBuffer(Control control)
    {
        if (control == null) return;
        try
        {
            var property = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            if (property != null) property.SetValue(control, true, null);
        }
        catch (Exception) { }
    }

    private Panel DevicePage()
    {
        DoubleBuffer(devices);
        devices.Dock = DockStyle.Fill;
        devices.SelectionMode = active == null ? SelectionMode.MultiExtended : SelectionMode.One;
        devices.DrawMode = DrawMode.OwnerDrawFixed;
        devices.ItemHeight = 72;
        devices.IntegralHeight = false;
        devices.BorderStyle = BorderStyle.None;
        devices.BackColor = WindowTheme.Canvas;
        devices.ForeColor = WindowTheme.Ink;
        devices.DrawItem += DrawDevice;
        devices.SelectedIndexChanged += delegate { UpdateButtons(); };
        devices.MouseDoubleClick += delegate(object sender, MouseEventArgs e)
        {
            int index = devices.IndexFromPoint(e.Location);
            if (e.Button == MouseButtons.Left && index >= 0) Pick((DeviceRecord)devices.Items[index]);
        };
        devices.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; PickSelected(); } };
        devices.MouseMove += delegate(object sender, MouseEventArgs e)
        {
            int index = devices.IndexFromPoint(e.Location);
            if (index != hoverIndex) { hoverIndex = index; devices.Invalidate(); }
            devices.Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        };
        devices.MouseLeave += delegate { hoverIndex = -1; devices.Invalidate(); devices.Cursor = Cursors.Default; };
        devices.Resize += delegate { devices.Invalidate(); };

        var listHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        listHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        listHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var listHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        listHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        listHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        listHeader.Controls.Add(TextLabel("设备列表", 10f, WindowTheme.Ink, true), 0, 0);
        listCount.AutoSize = true;
        listCount.ForeColor = WindowTheme.Faint;
        listCount.Font = new Font("Microsoft YaHei UI", 8.7f);
        listCount.Anchor = AnchorStyles.Right;
        listCount.Margin = new Padding(0, 1, 4, 0);
        listHeader.Controls.Add(listCount, 1, 0);
        listHost.Controls.Add(listHeader, 0, 0);

        var listBody = new Panel { Dock = DockStyle.Fill, BackColor = WindowTheme.Canvas };
        devices.Margin = new Padding(0);
        listBody.Controls.Add(devices);
        emptyHint.Dock = DockStyle.Fill;
        emptyHint.TextAlign = ContentAlignment.MiddleCenter;
        emptyHint.ForeColor = WindowTheme.Muted;
        emptyHint.Font = new Font("Microsoft YaHei UI", 9.6f);
        emptyHint.BackColor = WindowTheme.Canvas;
        emptyHint.Text = "正在准备设备列表…";
        listBody.Controls.Add(emptyHint);
        emptyHint.BringToFront();
        listHost.Controls.Add(listBody, 0, 1);

        var body = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        listHost.Margin = new Padding(0, 0, 16, 0);
        body.Controls.Add(listHost, 0, 0);
        body.Controls.Add(DeviceDetails(), 1, 0);

        var footer = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var divider = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = WindowTheme.Line, Margin = new Padding(0, 0, 0, 12) };
        footer.Controls.Add(divider, 0, 0);

        var primary = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
        primary.Controls.Add(connect);
        primary.Controls.Add(rename);
        primary.Controls.Add(history);
        primary.Controls.Add(lowLatency);
        if (active == null) primary.Controls.Add(AudioSelector(false));
        footer.Controls.Add(primary, 0, 1);

        var add = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
        var addLabel = TextLabel("添加设备", 9f, WindowTheme.Faint, true);
        addLabel.Margin = new Padding(2, 12, 10, 0);
        add.Controls.Add(addLabel);
        add.Controls.Add(pairQr);
        add.Controls.Add(pairCode);
        footer.Controls.Add(add, 0, 2);
        return Page("设备中心", "正在准备设备列表…", body, footer, refresh, true);
    }

    private Control DeviceDetails()
    {
        var card = new AntdUI.Panel
        {
            Dock = DockStyle.Fill,
            Radius = 12,
            Back = WindowTheme.Surface,
            BorderWidth = 1,
            BorderColor = WindowTheme.Line,
            Margin = new Padding(0),
            Padding = new Padding(16)
        };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = WindowTheme.Surface, Margin = new Padding(0) };
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            Margin = new Padding(0)
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var artRow = new RowStyle(SizeType.Absolute, 132);
        stack.RowStyles.Add(artRow);
        for (int i = 1; i < stack.RowCount; i++) stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        artwork.Dock = DockStyle.Fill;
        artwork.Margin = new Padding(0, 0, 0, 8);
        stack.Controls.Add(artwork, 0, 0);

        chosenName.Text = "尚未选中设备";
        chosenName.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
        chosenName.ForeColor = WindowTheme.Ink;
        chosenName.Margin = new Padding(0, 3, 0, 5);
        stack.Controls.Add(chosenName, 0, 1);

        chosenState.Size = new Size(94, 28);
        chosenState.AutoSize = false;
        chosenState.Anchor = AnchorStyles.Left;
        chosenState.Margin = new Padding(0, 0, 0, 7);
        chosenState.Visible = false;
        stack.Controls.Add(chosenState, 0, 2);

        chosenAddress.ForeColor = WindowTheme.Faint;
        chosenAddress.Font = new Font("Microsoft YaHei UI", 9f);
        chosenAddress.Margin = new Padding(1, 0, 0, 7);
        stack.Controls.Add(chosenAddress, 0, 3);

        chosenDetail.ForeColor = WindowTheme.Muted;
        chosenDetail.Font = new Font("Microsoft YaHei UI", 9f);
        chosenDetail.Margin = new Padding(1, 0, 0, 9);
        chosenDetail.Text = "先开启手机无线调试，再扫码或输入配对码。";
        stack.Controls.Add(chosenDetail, 0, 4);

        detailTip.ForeColor = WindowTheme.Muted;
        detailTip.BackColor = WindowTheme.Rail;
        detailTip.Font = new Font("Microsoft YaHei UI", 8.8f);
        detailTip.Padding = new Padding(10, 8, 10, 8);
        detailTip.Margin = new Padding(0, 2, 0, 2);
        detailTip.Text = "下一步\n手机和电脑连接同一 Wi-Fi，开启无线调试后刷新设备。";
        stack.Controls.Add(detailTip, 0, 5);

        var labels = new[] { chosenName, chosenAddress, chosenDetail, detailTip };
        foreach (var label in labels)
        {
            label.AutoSize = true;
            label.Dock = DockStyle.Fill;
        }

        scroll.Controls.Add(stack);
        card.Controls.Add(scroll);
        scroll.SizeChanged += delegate
        {
            // Leave room for the vertical scrollbar so wrapping never adds a horizontal one.
            int width = Math.Max(1, scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
            int height = Math.Max(64, Math.Min(140, Math.Min(width * 124 / 200 + 8, scroll.ClientSize.Height / 4)));
            stack.SuspendLayout();
            try
            {
                artRow.Height = height;
                foreach (var label in labels)
                    label.MaximumSize = new Size(Math.Max(1, width - label.Margin.Horizontal), 0);
            }
            finally { stack.ResumeLayout(); }
        };
        return card;
    }

    private void PairWithQr()
    {
        var result = DialogResult.Cancel;
        using (var dialog = new WifiPairingDialog())
            result = dialog.ShowDialog(this);
        if (result == DialogResult.Retry)
        {
            if (WirelessMirror.PairNew(this)) RefreshAfterChange("配对成功。设备出现在列表后，选中并打开投屏。");
        }
        else if (result == DialogResult.OK)
            RefreshAfterChange("扫码配对成功。设备出现在列表后，选中并打开投屏。");
    }

    private void PairWithCode()
    {
        if (WirelessMirror.PairNew(this))
            RefreshAfterChange("配对成功。设备出现在列表后，选中并打开投屏。");
    }

    private async void RefreshAfterChange(string message)
    {
        await RefreshDevicesAsync(message);
    }

    private async void RefreshFromButton()
    {
        await RefreshDevicesAsync();
    }

    private void DrawDevice(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= devices.Items.Count) return;
        var row = (DeviceRecord)devices.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        bool hover = hoverIndex == e.Index;
        var bounds = e.Bounds;
        using (var clear = new SolidBrush(WindowTheme.Canvas)) e.Graphics.FillRectangle(clear, bounds);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var tile = Rectangle.Inflate(bounds, -2, -4);
        if (tile.Width < 40 || tile.Height < 30) return;
        using (var path = DeviceArtwork.Shape(tile, 10))
        using (var fill = new SolidBrush(selected ? Color.FromArgb(45, 43, 82) : hover ? Color.FromArgb(37, 44, 65) : WindowTheme.Surface))
        using (var line = new Pen(selected ? WindowTheme.AccentLight : WindowTheme.Line, selected ? 1.2f : 1f))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(line, path);
        }
        if (selected)
        {
            using (var accent = new SolidBrush(WindowTheme.AccentLight))
                e.Graphics.FillRectangle(accent, tile.Left + 1, tile.Top + 12, 3, tile.Height - 24);
        }

        int iconX = tile.Left + 13;
        int iconY = tile.Top + (tile.Height - 38) / 2;
        using (var iconBack = new SolidBrush(selected ? Color.FromArgb(69, 59, 125) : Color.FromArgb(45, 52, 77)))
            e.Graphics.FillEllipse(iconBack, iconX, iconY, 38, 38);
        if (phoneIcon != null) e.Graphics.DrawImage(phoneIcon, iconX + 8, iconY + 8, 22, 22);

        int rightWidth = Math.Min(178, Math.Max(114, tile.Width / 3));
        int rightX = tile.Right - rightWidth - 10;
        int textX = iconX + 51;
        int nameWidth = Math.Max(60, rightX - textX - 8);
        var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(e.Graphics, row.Name, deviceNameFont,
            new Rectangle(textX, tile.Top + 8, nameWidth, 27), WindowTheme.Ink, flags);
        string model = row.Model == row.Name ? "Android 无线调试" : row.Model;
        TextRenderer.DrawText(e.Graphics, model, smallFont,
            new Rectangle(textX, tile.Top + 38, nameWidth, 22), WindowTheme.Muted, flags);
        string stream = GetStreamStatus(row);
        string state = stream != null ? "● " + stream : row.Online ? "● 在线" : "○ 离线";
        TextRenderer.DrawText(e.Graphics, state, smallFont,
            new Rectangle(rightX, tile.Top + 8, rightWidth, 25), stream != null ? WindowTheme.AccentLight : row.Online ? WindowTheme.Green : WindowTheme.Faint,
            flags | TextFormatFlags.Right);
        string last = row.LastUsed == DateTime.MinValue ? "尚未投屏" : Relative(row.LastUsed) + " · " + row.Connections + " 次";
        TextRenderer.DrawText(e.Graphics, last, tinyFont,
            new Rectangle(rightX, tile.Top + 38, rightWidth, 20), WindowTheme.Faint,
            flags | TextFormatFlags.Right);
        using (var separator = new Pen(Color.FromArgb(46, 55, 78)))
            e.Graphics.DrawLine(separator, textX, tile.Bottom - 1, tile.Right - 12, tile.Bottom - 1);
    }

    private static string Relative(DateTime value)
    {
        var local = value.ToLocalTime();
        if (local.Date == DateTime.Today) return "今天 " + local.ToString("HH:mm");
        if (local.Date == DateTime.Today.AddDays(-1)) return "昨天 " + local.ToString("HH:mm");
        return local.ToString("MM-dd HH:mm");
    }

    private Panel KeyPage()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = WindowTheme.Surface, Padding = new Padding(18, 12, 10, 10), AutoScroll = true };
        var list = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 11, BackColor = WindowTheme.Surface, Margin = new Padding(0) };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        string[,] keys = {
            {"F2 / 鼠标中键", "回手机主页"}, {"F3", "最近任务"}, {"F4 / F5", "展开 / 收起通知栏"},
            {"F6", "切换全屏"}, {"F7 / 鼠标右键", "返回"}, {"F8 / F9", "熄灭 / 点亮手机屏幕"},
            {"F10", "打开设备管理"}, {"F11 / 托盘菜单", "旋转手机屏幕"}, {"左 Alt + ↑ / ↓", "调节手机音量"},
            {"Ctrl + 左键拖动", "双指缩放"}, {"滚轮", "回到主页"}
        };
        for (int i = 0; i < keys.GetLength(0); i++)
        {
            list.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            var key = TextLabel(keys[i, 0], 9.8f, Color.FromArgb(143, 188, 255), true);
            var action = TextLabel(keys[i, 1], 9.8f, WindowTheme.Ink);
            key.Anchor = action.Anchor = AnchorStyles.Left;
            key.Margin = new Padding(0, 0, 0, 0);
            action.Margin = new Padding(0, 0, 0, 0);
            list.Controls.Add(key, 0, i);
            list.Controls.Add(action, 1, i);
        }
        host.Controls.Add(list);
        return Page("快捷键", "在投屏窗口内使用。也可以从投屏窗口标题栏的「⋮」打开。", host, null, null);
    }

    private Panel QualityPage()
    {
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(0, 0, 0, 4), Margin = new Padding(0) };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int row = 0; row < 3; row++) cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3));
        cards.Controls.Add(SliderCard("分辨率上限", "按手机屏幕长边计算", 480, 3840, draft.Size, n => n + " px", n => draft.Size = n, out sizeSlider, out sizeReading), 0, 0);
        cards.Controls.Add(SliderCard("帧率上限", "帧率越高，动作越顺", 15, 120, draft.Fps, n => n + " fps", n => draft.Fps = n, out fpsSlider, out fpsReading), 1, 0);
        cards.Controls.Add(SliderCard("视频码率", "卡顿时可先调低这一项", 5, 300, draft.Bitrate, n => (n / 10m).ToString("0.0") + " Mbps", n => draft.Bitrate = n, out bitrateSlider, out bitrateReading), 0, 1);
        cards.Controls.Add(SliderCard("视频缓冲", "网络波动时适当加大，延迟也会增加", 0, 250, draft.VideoBuffer, n => n + " ms", n => draft.VideoBuffer = n, out videoBufferSlider, out videoBufferReading), 1, 1);
        cards.Controls.Add(SliderCard("声音缓冲", "声音断续时可适当调高", 20, 250, draft.AudioBuffer, n => n + " ms", n => draft.AudioBuffer = n, out audioBufferSlider, out audioBufferReading), 0, 2);
        var recovery = SettingCard("断线自动重连", "投屏中断后只尝试恢复原设备");
        var toggle = new AntdUI.Switch { Checked = draft.AutoReconnect, Fill = WindowTheme.Accent, Width = 56, Height = 32, AccessibleName = "断线自动重连", Anchor = AnchorStyles.Left };
        toggle.CheckedChanged += delegate { draft.AutoReconnect = toggle.Checked; };
        recovery.Controls.Add(toggle, 1, 2);
        cards.Controls.Add(recovery, 1, 2);

        var host = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        host.Controls.Add(PresetBar(), 0, 0);
        host.Controls.Add(OptionsBar(), 0, 1);
        host.Controls.Add(cards, 0, 2);
        UpdateAudioUi();

        var footer = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 2, Padding = new Padding(0, 6, 0, 0), Margin = new Padding(0) };
        profileStatus.AutoSize = false;
        profileStatus.Dock = DockStyle.Fill;
        profileStatus.Height = 42;
        profileStatus.ForeColor = WindowTheme.Muted;
        profileStatus.Font = new Font("Microsoft YaHei UI", 9f);
        profileStatus.Text = "优先使用 IP 直连。ADB 往返只反映命令通道，不等于画面端到端延迟。保存后生效。";
        profileStatus.Margin = new Padding(0, 0, 0, 8);
        footer.Controls.Add(profileStatus, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
        actions.Controls.Add(applyProfile);
        actions.Controls.Add(probeButton);
        actions.Controls.Add(networkButton);
        actions.Controls.Add(logButton);
        footer.Controls.Add(actions, 0, 1);
        return Page("连接设置", "按场景选预设，或独立调整画质和缓冲。", host, footer, null);
    }

    private AppButton AudioSelector(bool settings)
    {
        AppButton button = null;
        string label = "声音：" + ConnectionProfile.AudioLabel(draft.Audio);
        button = StyledButton(label, null, delegate { ShowAudioMenu(button, settings); }, false);
        button.Size = new Size(settings ? 132 : 128, settings ? 36 : 46);
        button.Margin = new Padding(settings ? 0 : 10, 0, 0, 0);
        button.AccessibleName = "声音输出：" + ConnectionProfile.AudioLabel(draft.Audio);
        tooltips.SetToolTip(button, "选择电脑、手机、电脑+手机或全部静音；切换声音会重连投屏。");
        if (settings) settingsAudioButton = button;
        else audioButton = button;
        return button;
    }

    private void ShowAudioMenu(AppButton button, bool settings)
    {
        if (audioMenu != null && !audioMenu.Visible)
        {
            var previous = audioMenu;
            audioMenu = null;
            previous.Dispose();
        }
        var menu = new ContextMenuStrip();
        audioMenu = menu;
        menu.Closed += delegate { if (ReferenceEquals(audioMenu, menu)) audioMenu = null; };
        var options = new[] { AudioRoute.Pc, AudioRoute.Phone, AudioRoute.Both, AudioRoute.None };
        foreach (var option in options)
        {
            var route = option;
            var item = new ToolStripMenuItem(AudioMenuLabel(route));
            item.Checked = draft.Audio == route;
            item.Click += delegate
            {
                if (draft.Audio == route) return;
                draft.Audio = route;
                UpdateAudioUi();
                if (settings)
                {
                    profileStatus.ForeColor = WindowTheme.AccentLight;
                    profileStatus.Text = "声音输出已改为「" + AudioMenuLabel(route) + "」。点「保存并重连」生效。";
                    return;
                }
                try
                {
                    draft.Save();
                    SetNotice("声音输出已改为「" + AudioMenuLabel(route) + "」，下次投屏生效。", WindowTheme.Green);
                }
                catch (Exception error)
                {
                    SetNotice("声音设置保存失败：" + error.Message, WindowTheme.Red);
                }
            };
            menu.Items.Add(item);
        }
        // Do not dispose from Menu.Closed: the drop-down is still processing
        // its native window messages there, which caused ObjectDisposedException.
        menu.Show(button, new Point(0, button.Height));
    }

    private static string AudioMenuLabel(AudioRoute route)
    {
        switch (route)
        {
            case AudioRoute.Phone: return "手机";
            case AudioRoute.Both: return "电脑 + 手机";
            case AudioRoute.None: return "都不输出";
            default: return "电脑";
        }
    }

    private Control OptionsBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
        var audioLabel = TextLabel("声音输出", 9f, WindowTheme.Faint, true);
        audioLabel.Margin = new Padding(1, 10, 8, 0);
        bar.Controls.Add(audioLabel);
        settingsAudioButton = AudioSelector(true);
        bar.Controls.Add(settingsAudioButton);
        var statsLabel = TextLabel("实时统计", 9f, WindowTheme.Faint, true);
        statsLabel.Margin = new Padding(18, 10, 8, 0);
        bar.Controls.Add(statsLabel);
        statsSwitch = new AntdUI.Switch
        {
            Checked = draft.ShowStats,
            Fill = WindowTheme.Accent,
            Width = 52,
            Height = 30,
            AccessibleName = "显示实时统计",
            Margin = new Padding(0, 5, 6, 0)
        };
        statsSwitch.CheckedChanged += delegate
        {
            draft.ShowStats = statsSwitch.Checked;
            profileStatus.ForeColor = WindowTheme.AccentLight;
            profileStatus.Text = statsSwitch.Checked
                ? "将显示实时 FPS 和网络往返，保存并重连后生效。"
                : "将隐藏实时统计，保存并重连后生效。";
        };
        tooltips.SetToolTip(statsSwitch, "开启后标题栏显示实时 FPS 和网络往返；关闭后不显示。");
        bar.Controls.Add(statsSwitch);
        return bar;
    }

    private void UpdateAudioUi()
    {
        if (audioButton != null)
        {
            audioButton.Text = "声音：" + ConnectionProfile.AudioLabel(draft.Audio);
            audioButton.AccessibleName = "声音输出：" + ConnectionProfile.AudioLabel(draft.Audio);
        }
        if (settingsAudioButton != null)
        {
            settingsAudioButton.Text = "声音：" + ConnectionProfile.AudioLabel(draft.Audio);
            settingsAudioButton.AccessibleName = "声音输出：" + ConnectionProfile.AudioLabel(draft.Audio);
        }
        bool pcAudio = draft.Audio == AudioRoute.Pc || draft.Audio == AudioRoute.Both;
        if (audioBufferSlider != null)
        {
            audioBufferSlider.Enabled = pcAudio;
            if (audioBufferReading != null) audioBufferReading.Enabled = pcAudio;
        }
    }

    private void EnableLowLatency()
    {
        ApplyPreset(StreamPresets.All[1]);
        if (active == null)
        {
            draft.Save();
            SetNotice("已启用游戏低延迟：0ms 视频缓冲、90fps。鼠标保持普通桌面指针，不会被窗口捕获。", WindowTheme.Green);
        }
        else SaveProfile();
    }

    private Control PresetBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = false, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
        var label = TextLabel("场景预设", 9f, WindowTheme.Faint, true);
        label.Margin = new Padding(1, 9, 9, 0);
        bar.Controls.Add(label);
        foreach (var preset in StreamPresets.All)
        {
            var current = preset;
            var button = StyledButton(current.Name, null, delegate { ApplyPreset(current); }, false);
            button.Size = new Size(100, 36);
            button.Margin = new Padding(0, 0, 8, 4);
            tooltips.SetToolTip(button, current.Description);
            bar.Controls.Add(button);
        }
        return bar;
    }

    private void ApplyPreset(StreamPreset preset)
    {
        preset.Apply(draft);
        sizeSlider.Value = draft.Size;
        fpsSlider.Value = draft.Fps;
        bitrateSlider.Value = draft.Bitrate;
        videoBufferSlider.Value = draft.VideoBuffer;
        audioBufferSlider.Value = draft.AudioBuffer;
        sizeReading.Text = draft.Size + " px";
        fpsReading.Text = draft.Fps + " fps";
        bitrateReading.Text = (draft.Bitrate / 10m).ToString("0.0") + " Mbps";
        videoBufferReading.Text = draft.VideoBuffer + " ms";
        audioBufferReading.Text = draft.AudioBuffer + " ms";
        UpdateAudioUi();
        profileStatus.ForeColor = WindowTheme.AccentLight;
        profileStatus.Text = "已套用「" + preset.Name + "」预设，点「" +
            (active == null ? "保存设置" : "保存并重连") + "」生效。";
        AppLog.Info("套用画质预设: " + preset.Name);
    }

    private static TableLayoutPanel SettingCard(string title, string hint)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = WindowTheme.Surface, Padding = new Padding(13, 10, 10, 7), Margin = new Padding(0, 0, 10, 10) };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = TextLabel(title, 10.6f, WindowTheme.Ink, true);
        var description = TextLabel(hint, 8.7f, WindowTheme.Faint);
        card.Controls.Add(heading, 0, 0);
        card.Controls.Add(description, 0, 1);
        card.SetColumnSpan(description, 2);
        return card;
    }

    private static Control SliderCard(string title, string hint, int minimum, int maximum, int value,
        Func<int, string> format, Action<int> update, out AntdUI.Slider slider, out Label reading)
    {
        var card = SettingCard(title, hint);
        var readingControl = new Label { Text = format(value), AutoSize = false, Dock = DockStyle.Fill, ForeColor = WindowTheme.AccentLight, Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold), TextAlign = ContentAlignment.TopRight, Margin = new Padding(0) };
        reading = readingControl;
        card.Controls.Add(readingControl, 1, 0);
        var sliderControl = new AntdUI.Slider
        {
            MinValue = minimum,
            MaxValue = maximum,
            Value = value,
            Fill = WindowTheme.Accent,
            FillHover = WindowTheme.AccentLight,
            TrackColor = Color.FromArgb(75, 85, 119),
            Dock = DockStyle.Fill,
            LineSize = 5,
            ShowValue = false,
            AccessibleName = title
        };
        slider = sliderControl;
        sliderControl.ValueChanged += delegate { readingControl.Text = format(sliderControl.Value); update(sliderControl.Value); };
        card.Controls.Add(sliderControl, 0, 2);
        card.SetColumnSpan(sliderControl, 2);
        return card;
    }

    private void SaveProfile()
    {
        if (active != null && !active.Online) throw new InvalidOperationException("当前设备已离线。");
        draft.Save();
        profileStatus.Text = "连接设置已保存。";
        profileStatus.ForeColor = WindowTheme.Green;
        if (active != null)
        {
            SavedProfile = draft.Copy();
            DialogResult = DialogResult.OK;
            return;
        }
        var selected = Current();
        if (selected == null) selected = rows.FirstOrDefault(item => item.Online);
        if (selected != null && SettingsSaved != null) SettingsSaved(selected, draft.Copy());
    }

    private DeviceRecord SelectedOnlineDevice()
    {
        var row = active != null && active.Online ? active : Current();
        if (row == null || !row.Online) row = rows.FirstOrDefault(item => item.Online);
        return row;
    }

    private async void ProbeConnection()
    {
        if (probeButton.Loading) return;
        var row = SelectedOnlineDevice();
        if (row == null) { profileStatus.Text = "没有在线设备可检查。"; profileStatus.ForeColor = WindowTheme.Amber; return; }
        string serial = row.Serial;
        profileStatus.Text = "正在检查 ADB 连接…";
        profileStatus.ForeColor = WindowTheme.Muted;
        probeButton.Loading = true;
        try
        {
            long[] samples = await Task.Run(delegate
            {
                var readings = new long[3];
                for (int i = 0; i < readings.Length; i++)
                {
                    var timer = Stopwatch.StartNew();
                    if (WirelessMirror.Adb("-s " + serial + " shell echo connected") != "connected")
                        throw new InvalidOperationException("设备未响应。");
                    readings[i] = timer.ElapsedMilliseconds;
                }
                Array.Sort(readings);
                return readings;
            });
            if (!IsDisposed)
            {
                profileStatus.Text = "ADB 命令往返中位数 " + samples[1] + " ms · 不含采集、编码、传输和解码，不能当作画面延迟。";
                profileStatus.ForeColor = WindowTheme.Green;
            }
        }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                profileStatus.Text = "连接检查失败。" + error.Message;
                profileStatus.ForeColor = WindowTheme.Red;
            }
        }
        finally { if (!IsDisposed) probeButton.Loading = false; }
    }

    private async void ProbeNetwork()
    {
        if (networkButton.Loading) return;
        var row = SelectedOnlineDevice();
        if (row == null)
        {
            profileStatus.Text = "没有在线设备可测试。";
            profileStatus.ForeColor = WindowTheme.Amber;
            return;
        }
        networkButton.Loading = true;
        profileStatus.Text = "正在解析手机 IP，并测试局域网往返…";
        profileStatus.ForeColor = WindowTheme.Muted;
        try
        {
            var result = await Task.Run(delegate
            {
                string host = NetworkDiagnostics.ResolveHost(row.Serial);
                if (String.IsNullOrEmpty(host))
                    throw new InvalidOperationException("无法从当前连接解析手机 IP，请检查无线调试并刷新设备。");
                return NetworkDiagnostics.Probe(host, 6, 1000);
            });
            if (!IsDisposed)
            {
                if (result.Received == 0)
                {
                    profileStatus.Text = "Wi-Fi 往返测试失败：" + (result.Error ?? "没有收到回应。");
                    profileStatus.ForeColor = WindowTheme.Red;
                }
                else
                {
                    profileStatus.Text = "Wi-Fi 往返（ICMP）平均 " + result.Average + " ms，最小 " + result.Min +
                        " / 最大 " + result.Max + "，抖动 " + result.Jitter + " ms，丢包 " + result.LossPercent +
                        "%。这不是画面端到端延迟。";
                    profileStatus.ForeColor = result.LossPercent == 0 ? WindowTheme.Green : WindowTheme.Amber;
                    AppLog.Info("Wi-Fi 探测 " + result.Host + ": avg=" + result.Average + "ms jitter=" + result.Jitter +
                        "ms loss=" + result.LossPercent + "%");
                }
            }
        }
        catch (Exception error)
        {
            AppLog.Error("Wi-Fi 往返测试失败", error);
            if (!IsDisposed)
            {
                profileStatus.Text = "Wi-Fi 往返测试失败：" + error.Message;
                profileStatus.ForeColor = WindowTheme.Red;
            }
        }
        finally { if (!IsDisposed) networkButton.Loading = false; }
    }

    private void OpenLogFolder()
    {
        try
        {
            AppPaths.EnsureLogDirectory();
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + AppPaths.LogDirectory + "\"")
            {
                UseShellExecute = true
            });
            profileStatus.Text = "已打开日志目录：" + AppPaths.LogDirectory;
            profileStatus.ForeColor = WindowTheme.Muted;
        }
        catch (Exception error)
        {
            AppLog.Error("打开日志目录失败", error);
            profileStatus.Text = "打开日志目录失败：" + error.Message;
            profileStatus.ForeColor = WindowTheme.Red;
        }
    }

    private DeviceRecord Current() { return devices.SelectedItem as DeviceRecord; }

    private void UpdateButtons()
    {
        var row = Current();
        var selected = devices.SelectedItems.Cast<DeviceRecord>().ToArray();
        connect.Enabled = selected.Any(item => item.Online || GetStreamStatus(item) != null);
        connect.Text = selected.Length > 1 ? "打开所选" :
            row != null && GetStreamStatus(row) != null ? "切到投屏" : "打开投屏";
        rename.Enabled = row != null && row.Id != null;
        history.Enabled = row != null && row.LastUsed != DateTime.MinValue;
        artwork.ShowStatus(row != null && row.Online);
        chosenName.Text = row == null ? "尚未选中设备" : row.Name;
        chosenState.Visible = row != null;
        if (row != null)
        {
            chosenState.Text = GetStreamStatus(row) ?? (row.Online ? "在线" : "离线");
            chosenState.Type = row.Online ? AntdUI.TTypeMini.Success : AntdUI.TTypeMini.Default;
        }
        chosenAddress.Text = row == null ? "等待选择设备" : FriendlySerial(row.Serial);
        chosenDetail.Text = row == null
            ? "先开启手机无线调试，再扫码或输入配对码。"
            : "机型      " + row.Model +
              "\n最近投屏  " + (row.LastUsed == DateTime.MinValue ? "暂无记录" : Relative(row.LastUsed)) +
              "\n连接次数  " + row.Connections;
        detailTip.Text = row == null
            ? "下一步\n手机和电脑连接同一 Wi-Fi，开启无线调试后刷新设备。"
            : row.Online
                ? "下一步\n双击打开一台，或按住 Ctrl 选中多台后点「打开所选」。"
                : "设备当前离线\n检查手机无线调试是否开启，再刷新设备。";
    }

    private static string FriendlySerial(string serial)
    {
        if (String.IsNullOrEmpty(serial)) return "未连接";
        const string suffix = "._adb-tls-connect._tcp";
        if (serial.EndsWith(suffix, StringComparison.Ordinal)) return "无线调试 · 已通过 mDNS 发现";
        return serial;
    }

    private string GetStreamStatus(DeviceRecord device)
    {
        return StreamStatus == null ? null : StreamStatus(device);
    }

    internal void RefreshStreamStatus(DeviceRecord updated = null)
    {
        if (updated != null)
        {
            var row = rows.FirstOrDefault(item => item.Id == updated.Id);
            if (row != null)
            {
                row.LastUsed = updated.LastUsed;
                row.Connections = updated.Connections;
                row.RecentConnections = updated.RecentConnections;
            }
        }
        devices.Invalidate();
        UpdateButtons();
    }

    internal void ShowDevicePage() { ShowPage(0); }
    internal void ReportStream(string message, Color color) { SetNotice(message, color); }

    private void PickSelected()
    {
        var selected = devices.SelectedItems.Cast<DeviceRecord>().ToArray();
        foreach (var row in selected) Pick(row);
    }

    private void Pick(DeviceRecord row)
    {
        if (!row.Online && GetStreamStatus(row) == null)
        {
            SetNotice("这台设备当前离线。请先开启手机无线调试，或刷新列表。", WindowTheme.Amber);
            return;
        }
        if (OpenRequested != null) OpenRequested(row);
    }

    private void RefreshDevices()
    {
        RefreshFromButton();
    }

    private async Task RefreshDevicesAsync(string successMessage = null)
    {
        if (scanning || IsDisposed) return;
        scanning = true;
        statusTimer.Stop();
        refresh.Loading = true;
        refresh.Enabled = false;
        deviceStatus.Text = "正在通过 ADB 查找局域网设备…";
        deviceStatus.ForeColor = WindowTheme.Muted;
        if (rows.Count == 0)
        {
            emptyHint.Text = "正在扫描局域网…\n请确认手机和电脑连接同一个 Wi-Fi。";
            emptyHint.Visible = true;
            emptyHint.BringToFront();
        }
        try
        {
            var updated = await Task.Run(delegate { return directory.Scan(); });
            if (IsDisposed) return;
            if (!updated.Any(item => item.Online) && !IsDisposed)
            {
                AppLog.Info("首次扫描没有在线设备，1.2 秒后自动重试一次");
                await Task.Delay(1200);
                if (IsDisposed) return;
                updated = await Task.Run(delegate { return directory.Scan(); });
                if (IsDisposed) return;
            }
            Fill(updated);
            if (!String.IsNullOrEmpty(successMessage)) SetNotice(successMessage, WindowTheme.Green);
            else if (updated.Count == 0 || !updated.Any(item => item.Online))
                SetNotice("扫描完成，没有发现在线设备。打开无线调试，或使用扫码 / 六位码配对。", WindowTheme.Amber);
            else
            {
                SetStatusText(summary, WindowTheme.Muted);
                statusTimer.Stop();
            }
        }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                SetNotice("扫描失败：" + error.Message, WindowTheme.Red);
                emptyHint.Text = "扫描失败。\n检查手机无线调试和 Wi-Fi 后点「刷新设备」。";
                emptyHint.Visible = rows.Count == 0;
            }
        }
        finally
        {
            scanning = false;
            if (!IsDisposed)
            {
                refresh.Loading = false;
                refresh.Enabled = true;
            }
        }
    }

    private void SetNotice(string text, Color color)
    {
        SetStatusText(text, color);
        statusTimer.Stop();
        statusTimer.Start();
    }

    private void SetStatusText(string text, Color color)
    {
        deviceStatus.Text = text;
        deviceStatus.ForeColor = color;
    }

    private void Rename()
    {
        var row = Current();
        if (row == null || row.Id == null) return;
        using (var dialog = new TextPromptDialog("重命名设备", "输入设备名称，最多 40 个字。", row.Name, 40))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            directory.Rename(row, dialog.Value);
            if (RenameApplied != null) RenameApplied(row);
        }
        devices.Invalidate();
        UpdateButtons();
        SetNotice("设备名称已更新。", WindowTheme.Green);
    }

    private void ShowHistory()
    {
        var row = Current();
        if (row == null || row.LastUsed == DateTime.MinValue) return;
        var times = row.RecentConnections ?? new List<DateTime> { row.LastUsed };
        using (var dialog = new Form
        {
            Text = row.Name + " · 连接记录",
            ClientSize = new Size(430, 430),
            StartPosition = FormStartPosition.CenterParent,
            Font = Font,
            ShowInTaskbar = false,
            BackColor = WindowTheme.Canvas,
            ForeColor = WindowTheme.Ink,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        })
        {
            WindowTheme.Attach(dialog);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20), BackColor = WindowTheme.Canvas };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.Controls.Add(new Label { Text = row.Name + "\n最近 " + times.Count + " 次成功投屏", AutoSize = true, ForeColor = WindowTheme.Ink, Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold) }, 0, 0);
            var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BackColor = WindowTheme.Surface, ForeColor = WindowTheme.Ink, BorderStyle = BorderStyle.None, Font = new Font("Microsoft YaHei UI", 10f) };
            foreach (var time in times) list.Items.Add(time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            layout.Controls.Add(list, 0, 1);
            var close = StyledButton("关闭", "CloseOutlined", delegate { dialog.Close(); }, false);
            close.DialogResult = DialogResult.Cancel;
            close.Anchor = AnchorStyles.Right;
            layout.Controls.Add(close, 0, 2);
            dialog.Controls.Add(layout);
            dialog.ShowDialog(this);
        }
    }

    private void Fill(List<DeviceRecord> updated)
    {
        var previous = Current();
        string previousSerial = previous == null ? null : previous.Serial;
        rows = updated ?? new List<DeviceRecord>();
        devices.BeginUpdate();
        try
        {
            devices.Items.Clear();
            foreach (var row in rows) devices.Items.Add(row);
            int index = -1;
            if (previous != null && previous.Id != null)
                index = rows.FindIndex(row => row.Id == previous.Id);
            if (index < 0 && previousSerial != null)
                index = rows.FindIndex(row => row.Serial == previousSerial);
            if (index < 0) index = rows.FindIndex(row => row.Online);
            if (index < 0 && rows.Count > 0) index = 0;
            devices.SelectedIndex = index;
        }
        finally { devices.EndUpdate(); }
        emptyHint.Visible = rows.Count == 0;
        if (rows.Count == 0) emptyHint.BringToFront();
        int online = rows.Count(item => item.Online);
        summary = rows.Count == 0
            ? "暂无设备。打开手机无线调试，或使用扫码 / 六位码配对。"
            : online + " 台在线   ·   " + (rows.Count - online) + " 台历史离线   ·   双击打开一台，Ctrl 选择多台";
        listCount.Text = rows.Count == 0 ? "未发现设备" : rows.Count + " 台设备";
        if (!scanning) SetStatusText(summary, WindowTheme.Muted);
        UpdateButtons();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.F5 && !scanning)
        {
            RefreshFromButton();
            return true;
        }
        if (keyData == (Keys.Control | Keys.P))
        {
            PairWithCode();
            return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            statusTimer.Dispose();
            tooltips.Dispose();
            deviceNameFont.Dispose();
            smallFont.Dispose();
            tinyFont.Dispose();
            if (phoneIcon != null) phoneIcon.Dispose();
            if (appIcon != null) appIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}

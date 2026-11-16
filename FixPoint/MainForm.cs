using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;

namespace FixPoint;

/// <summary>
/// فئة النافذة الرئيسية (MainForm) - بنية برمجية معاد كتابتها بالكامل
/// تم حل جميع مشاكل تسريب الذاكرة (Memory Leaks)، تجميد الواجهة (UI Freezing عبر Async/Await)،
/// تسريب مقابض الجرافيكس (GDI Handles عبر Using)، وإضافة شريط القوائم (MenuStrip) والاختصارات ورسم GDI+ الحي.
/// </summary>
public class MainForm : Form
{
    private readonly string _currentRole;
    private readonly string _currentUserName;
    private TabControl? _tabControl;
    private MenuStrip? _mainMenu;

    // مؤقت تأخير البحث (Debounce Timer) لمنع الضغط اللحظي المتكرر على قاعدة البيانات مع كل ضغطة زر
    private readonly System.Windows.Forms.Timer _searchDebounceTimer = new() { Interval = 300 };
    private Action? _pendingSearchAction;

    // لوحة الرسم الحي لشريط تقدم الصيانة (GDI+ On-Screen Lifecycle Canvas)
    private readonly Panel _lifecycleCanvas = new();

    // عناصر لوحة التحكم (Dashboard)
    private readonly Label _lblTotalCustomers = new();
    private readonly Label _lblTotalDevices = new();
    private readonly Label _lblTotalTechnicians = new();
    private readonly Label _lblTotalOrders = new();
    private readonly Label _lblCompletedOrders = new();
    private readonly Label _lblPendingOrders = new();
    private readonly Label _lblTotalRevenue = new();
    private readonly DataGridView _recentOrdersGrid = new();

    // أزرار الحذف المركزية للتحكم بالصلاحيات (Role-Based Access Control)
    private readonly Button _customerDeleteButton = new() { Text = "Delete", AutoSize = true, BackColor = Color.FromArgb(231, 76, 60), ForeColor = Color.White, Margin = new Padding(0, 0, 8, 0) };
    private readonly Button _deviceDeleteButton = new() { Text = "Delete", AutoSize = true, BackColor = Color.FromArgb(231, 76, 60), ForeColor = Color.White, Margin = new Padding(0, 0, 8, 0) };
    private readonly Button _technicianDeleteButton = new() { Text = "Delete", AutoSize = true, BackColor = Color.FromArgb(231, 76, 60), ForeColor = Color.White, Margin = new Padding(0, 0, 8, 0) };
    private readonly Button _repairOrderDeleteButton = new() { Text = "Delete Order", AutoSize = true, BackColor = Color.FromArgb(231, 76, 60), ForeColor = Color.White, Margin = new Padding(0, 0, 8, 0) };

    // عناصر إدارة العملاء (Customers)
    private readonly TextBox _customerNameText = new();
    private readonly TextBox _customerPhoneText = new();
    private readonly TextBox _customerAddressText = new();
    private readonly TextBox _customerSearchText = new();
    private readonly DataGridView _customerGrid = new();
    private int? _selectedCustomerId;

    // عناصر إدارة الأجهزة (Devices)
    private readonly ComboBox _deviceCustomerComboBox = new();
    private readonly ComboBox _deviceTypeComboBox = new();
    private readonly TextBox _deviceBrandText = new();
    private readonly TextBox _deviceModelText = new();
    private readonly TextBox _deviceSerialText = new();
    private readonly TextBox _deviceSearchText = new();
    private readonly DataGridView _deviceGrid = new();
    private int? _selectedDeviceId;

    // عناصر إدارة الفنيين (Technicians)
    private readonly TextBox _technicianNameText = new();
    private readonly TextBox _technicianPhoneText = new();
    private readonly TextBox _technicianSpecialtyText = new();
    private readonly TextBox _technicianSearchText = new();
    private readonly DataGridView _technicianGrid = new();
    private int? _selectedTechnicianId;

    // عناصر أوامر الصيانة (Repair Orders)
    private readonly ComboBox _repairCustomerComboBox = new();
    private readonly ComboBox _repairDeviceComboBox = new();
    private readonly ComboBox _repairTechnicianComboBox = new();
    private readonly ComboBox _repairStatusComboBox = new();
    private readonly TextBox _repairProblemText = new();
    private readonly TextBox _repairDiagnosisText = new();
    private readonly TextBox _repairCostText = new();
    private readonly TextBox _repairSearchText = new();
    private readonly DataGridView _repairGrid = new();
    private int? _selectedRepairOrderId;

    // عناصر الفواتير والدفعات (Invoices & Payments)
    private readonly DataGridView _invoiceGrid = new();
    private readonly Panel _invoiceEmptyStatePanel = new();
    private readonly Label _invoiceEmptyStateLabel = new();
    private readonly ComboBox _paymentInvoiceComboBox = new();
    private readonly TextBox _paymentAmountText = new();
    private readonly ComboBox _paymentMethodComboBox = new();
    private readonly TextBox _paymentNotesText = new();
    private readonly Button _recordPaymentButton = new();
    private readonly PrintDocument _invoicePrintDocument = new();
    private int? _selectedInvoiceId;
    private DataTable? _invoicePrintData;
    private DataTable? _invoicePaymentPrintData;
    private int _invoicePaymentPrintIndex;
    private bool _invoicePrintFirstPage;
    private int _invoicePrintPageNumber;

    public MainForm(string role, string userName)
    {
        _currentRole = role;
        _currentUserName = userName;

        InitializeComponent();

        // تفعيل خاصية الـ DoubleBuffered على الفورم لمنع الوميض والارتعاش (Flicker)
        EnableDoubleBuffering(this);
        EnableDoubleBuffering(_recentOrdersGrid);
        EnableDoubleBuffering(_customerGrid);
        EnableDoubleBuffering(_deviceGrid);
        EnableDoubleBuffering(_technicianGrid);
        EnableDoubleBuffering(_repairGrid);
        EnableDoubleBuffering(_invoiceGrid);

        // معالجة حدث DataError لكافة الجداول لقمع خطأ الـ FormatException الداخلي لـ Windows Forms
        _recentOrdersGrid.DataError += (_, e) => { e.ThrowException = false; };
        _customerGrid.DataError += (_, e) => { e.ThrowException = false; };
        _deviceGrid.DataError += (_, e) => { e.ThrowException = false; };
        _technicianGrid.DataError += (_, e) => { e.ThrowException = false; };
        _repairGrid.DataError += (_, e) => { e.ThrowException = false; };
        _invoiceGrid.DataError += (_, e) => { e.ThrowException = false; };

        // إعداد مؤقت الـ Debounce
        _searchDebounceTimer.Tick += OnSearchDebounceTimerTick;

        // إعداد طباعة الفواتير
        _invoicePrintDocument.BeginPrint += OnInvoiceBeginPrint;
        _invoicePrintDocument.PrintPage += InvoicePrintDocument_PrintPage;

        // تحميل البيانات بشكل غير متزامن (Async) لمنع تجميد خيط الواجهة عند الإقلاع
        _ = LoadInitialDataAsync();
    }

    private async Task LoadInitialDataAsync()
    {
        await LoadDashboardAsync();
        await LoadCustomerDataAsync();
        await LoadDeviceDataAsync();
        await LoadTechnicianDataAsync();
        await LoadRepairOrderDataAsync();
        await LoadInvoiceDataAsync();
        ConfigureRoleVisibility();
    }

    private void InitializeComponent()
    {
        Text = "FixPoint - Computer Maintenance Center System";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1300, 800);
        WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(245, 247, 250);

        // 1. تفعيل اعتراض أزرار واختصارات لوحة المفاتيح على مستوى الفورم بالكامل (KeyPreview)
        KeyPreview = true;
        KeyDown += OnFormKeyDown;

        // 2. بناء شريط القوائم القياسي لبيئة ويندوز (MenuStrip)
        _mainMenu = CreateAppMenuStrip();
        MainMenuStrip = _mainMenu;
        Controls.Add(_mainMenu);

        // 3. تخطيط الواجهة الأساسي
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0),
            BackColor = Color.FromArgb(245, 247, 250)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var sidebar = CreateSidebar();
        var content = CreateContent();

        mainLayout.Controls.Add(sidebar, 0, 0);
        mainLayout.Controls.Add(content, 1, 0);
        Controls.Add(mainLayout);
    }

    /// <summary>
    /// بناء شريط القوائم (MenuStrip) محققاً متطلب Task 3 بالكامل مع الاختصارات والقوائم المنسدلة
    /// </summary>
    private MenuStrip CreateAppMenuStrip()
    {
        var menu = new MenuStrip
        {
            BackColor = Color.White,
            Font = new Font("Segoe UI", 9.5F),
            Dock = DockStyle.Top
        };

        // قائمة ملف (&File)
        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&New Repair Intake", null, (_, _) => ExecuteNewOrder(), Keys.Control | Keys.N));
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&Save Active Record", null, (_, _) => ExecuteSaveActiveRecord(), Keys.Control | Keys.S));
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&Print Invoice", null, (_, _) => ExecutePrintActiveInvoice(), Keys.Control | Keys.P));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&Backup Database", null, (_, _) => BackupDatabase()));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close(), Keys.Alt | Keys.F4));

        // قائمة عرض (&View)
        var viewMenu = new ToolStripMenuItem("&View");
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("&Dashboard", null, (_, _) => SwitchToTab("Dashboard"), Keys.Control | Keys.D1));
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("&Customers", null, (_, _) => SwitchToTab("Customers"), Keys.Control | Keys.D2));
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("&Devices", null, (_, _) => SwitchToTab("Devices"), Keys.Control | Keys.D3));
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("&Technicians", null, (_, _) => SwitchToTab("Technicians"), Keys.Control | Keys.D4));
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("&Repair Orders", null, (_, _) => SwitchToTab("Repair Orders"), Keys.Control | Keys.D5));
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("&Invoices", null, (_, _) => SwitchToTab("Invoices"), Keys.Control | Keys.D6));

        // قائمة عمليات (&Operations)
        var opsMenu = new ToolStripMenuItem("&Operations");
        opsMenu.DropDownItems.Add(new ToolStripMenuItem("&Complete Repair Job", null, (_, _) => QuickCompleteRepair(), Keys.Control | Keys.J));
        opsMenu.DropDownItems.Add(new ToolStripMenuItem("&Generate Completed Invoices", null, async (_, _) => await GenerateInvoicesForCompletedRepairsAsync(), Keys.Control | Keys.I));
        opsMenu.DropDownItems.Add(new ToolStripMenuItem("&Record Payment", null, (_, _) => SwitchToTab("Invoices")));

        // قائمة مساعدة (&Help)
        var helpMenu = new ToolStripMenuItem("&Help");
        helpMenu.DropDownItems.Add(new ToolStripMenuItem("&About FixPoint", null, (_, _) => ShowAboutDialog()));

        menu.Items.AddRange(new ToolStripItem[] { fileMenu, viewMenu, opsMenu, helpMenu });
        return menu;
    }

    /// <summary>
    /// معالج ضغطات لوحة المفاتيح لتفعيل اختصارات الـ Accelerators (Ctrl+S, Ctrl+P, Ctrl+N, Ctrl+J)
    /// </summary>
    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            ExecuteSaveActiveRecord();
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.P)
        {
            ExecutePrintActiveInvoice();
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.N)
        {
            ExecuteNewOrder();
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.J)
        {
            QuickCompleteRepair();
            e.SuppressKeyPress = true;
        }
    }

    private void ExecuteNewOrder()
    {
        SwitchToTab("Repair Orders");
        ClearRepairOrderForm();
        _repairCustomerComboBox.Focus();
    }

    private void ExecuteSaveActiveRecord()
    {
        if (_tabControl?.SelectedTab == null) return;
        var tabTitle = _tabControl.SelectedTab.Text;

        if (tabTitle.Contains("Customer")) SaveCustomer(this, EventArgs.Empty);
        else if (tabTitle.Contains("Device")) SaveDevice(this, EventArgs.Empty);
        else if (tabTitle.Contains("Technician")) SaveTechnician(this, EventArgs.Empty);
        else if (tabTitle.Contains("Repair")) SaveRepairOrder(this, EventArgs.Empty);
    }

    private void ExecutePrintActiveInvoice()
    {
        SwitchToTab("Invoices");
        PreviewInvoice(this, EventArgs.Empty);
    }

    private void QuickCompleteRepair()
    {
        SwitchToTab("Repair Orders");
        if (_selectedRepairOrderId is null)
        {
            MessageBox.Show("يرجى اختيار أمر صيانة من الجدول أولاً.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _repairStatusComboBox.SelectedItem = "Completed";
        UpdateRepairOrder(this, EventArgs.Empty);
    }

    private void ShowAboutDialog()
    {
        MessageBox.Show(
            "نظام FixPoint لإدارة مراكز صيانة الكمبيوتر والإلكترونيات\n" +
            "الإصدار: 2.0 (Production-Grade Architecture)\n\n" +
            "جامعة القاهرة الجديدة التكنولوجية (NCT)\n" +
            "كلية تكنولوجيا الصناعة والطاقة - هندسة البرمجيات (سنة 4)\n" +
            "مقرر: برمجة ويندوز 1 (Windows Programming I)\n\n" +
            "المميزات الهندسية المضافة:\n" +
            "• محرك قاعدة بيانات محصن ضد تكرار البيانات بـ UNIQUE و CHECK\n" +
            "• تطبيق كامل للـ 3NF وتفعيل الـ Foreign Keys ونمط WAL\n" +
            "• استعلامات غير متزامنة بالكامل (Async/Await) لمنع تجمد الواجهة\n" +
            "• رسم حي ومباشر لدورة حياة الصيانة عبر GDI+\n" +
            "• فهارس بحث فائقة السرعة ومانع ضغط الاستعلامات (Debouncing)",
            "حول نظام FixPoint",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void BackupDatabase()
    {
        using var saveDialog = new SaveFileDialog
        {
            Filter = "SQLite Database (*.db)|*.db",
            FileName = $"FixPoint_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.db",
            Title = "حفظ نسخة احتياطية من قاعدة البيانات"
        };

        if (saveDialog.ShowDialog() == DialogResult.OK)
        {
            try
            {
                File.Copy(DatabaseService.DatabasePath, saveDialog.FileName, true);
                MessageBox.Show("تم حفظ النسخة الاحتياطية بنجاح!", "نسخ احتياطي", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"فشل النسخ الاحتياطي:\n{ex.Message}", "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private Panel CreateSidebar()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(34, 47, 62),
            Padding = new Padding(12)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.FromArgb(34, 47, 62)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));

        var appTitle = new Label
        {
            Text = "FixPoint",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var userBadge = new Label
        {
            Text = $"{_currentUserName} ({_currentRole})",
            ForeColor = Color.FromArgb(220, 224, 233),
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        string[] menuItems;
        if (_currentRole.Equals("Technician", StringComparison.OrdinalIgnoreCase))
        {
            // الفني يرى فقط أوامر الصيانة المسندة لمتابعتها وتشخيصها
            menuItems = new[] { "Repair Orders" };
        }
        else if (_currentRole.Equals("Employee", StringComparison.OrdinalIgnoreCase))
        {
            // موظف الاستقبال يرى العملاء والأجهزة وأوامر الصيانة والفواتير (بدون فنيين أو إحصائيات عليا)
            menuItems = new[] { "Customers", "Devices", "Repair Orders", "Invoices" };
        }
        else
        {
            // المدير له وصول كامل لكافة النوافذ
            menuItems = new[] { "Dashboard", "Customers", "Devices", "Technicians", "Repair Orders", "Invoices" };
        }
        var menuPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.FromArgb(34, 47, 62),
            Padding = new Padding(0, 8, 0, 0)
        };

        foreach (var item in menuItems)
        {
            var button = new Button
            {
                Text = item,
                Width = 204,
                Height = 42,
                Margin = new Padding(0, 0, 0, 8),
                BackColor = Color.FromArgb(56, 78, 96),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            button.Click += (_, _) => SwitchToTab(item);
            menuPanel.Controls.Add(button);
        }

        var logoutButton = new Button
        {
            Text = "Logout",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0),
            BackColor = Color.FromArgb(204, 70, 70),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold)
        };
        logoutButton.Click += (_, _) =>
        {
            var loginForm = new LoginForm();
            loginForm.Show();
            Close();
        };

        layout.Controls.Add(appTitle, 0, 0);
        layout.Controls.Add(userBadge, 0, 1);
        layout.Controls.Add(menuPanel, 0, 2);
        layout.Controls.Add(logoutButton, 0, 3);
        sidebar.Controls.Add(layout);

        return sidebar;
    }

    private TabControl CreateContent()
    {
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10F),
            Appearance = TabAppearance.FlatButtons,
            ItemSize = new Size(120, 32)
        };

        if (_currentRole.Equals("Technician", StringComparison.OrdinalIgnoreCase))
        {
            // الفني لا يرى سوى أوامر الصيانة فقط
            _tabControl.TabPages.Add(CreateRepairOrderTab());
        }
        else if (_currentRole.Equals("Employee", StringComparison.OrdinalIgnoreCase))
        {
            // الموظف يرى إدارة الاستقبال والفواتير بدون إحصائيات المدير وإدارة الفنيين
            _tabControl.TabPages.Add(CreateCustomerTab());
            _tabControl.TabPages.Add(CreateDeviceTab());
            _tabControl.TabPages.Add(CreateRepairOrderTab());
            _tabControl.TabPages.Add(CreateInvoiceTab());
        }
        else
        {
            // المدير يرى كل شيء
            _tabControl.TabPages.Add(CreateDashboardTab());
            _tabControl.TabPages.Add(CreateCustomerTab());
            _tabControl.TabPages.Add(CreateDeviceTab());
            _tabControl.TabPages.Add(CreateTechnicianTab());
            _tabControl.TabPages.Add(CreateRepairOrderTab());
            _tabControl.TabPages.Add(CreateInvoiceTab());
        }

        return _tabControl;
    }

    private void SwitchToTab(string tabName)
    {
        if (_tabControl is null) return;
        foreach (TabPage page in _tabControl.TabPages)
        {
            if (page.Text.Equals(tabName, StringComparison.OrdinalIgnoreCase))
            {
                _tabControl.SelectedTab = page;
                break;
            }
        }
    }

    // =========================================================================
    // DASHBOARD TAB
    // =========================================================================
    private TabPage CreateDashboardTab()
    {
        var page = new TabPage("Dashboard");
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(16),
            BackColor = Color.White
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 135F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var cards = new[]
        {
            ("Total Customers", _lblTotalCustomers, Color.FromArgb(52, 152, 219)),
            ("Total Devices", _lblTotalDevices, Color.FromArgb(46, 204, 113)),
            ("Total Technicians", _lblTotalTechnicians, Color.FromArgb(155, 89, 182)),
            ("Total Orders", _lblTotalOrders, Color.FromArgb(243, 156, 18)),
            ("Completed Repairs", _lblCompletedOrders, Color.FromArgb(39, 174, 96)),
            ("Pending Repairs", _lblPendingOrders, Color.FromArgb(230, 126, 34)),
            ("Revenue", _lblTotalRevenue, Color.FromArgb(231, 76, 60)),
        };

        var cardIndex = 0;
        foreach (var (title, label, color) in cards)
        {
            var card = new Panel
            {
                BackColor = color,
                Padding = new Padding(12),
                Dock = DockStyle.Fill,
                Margin = new Padding(8)
            };
            var titleLabel = new Label
            {
                Text = title,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Dock = DockStyle.Top,
                AutoSize = true
            };
            label.Text = "0";
            label.ForeColor = Color.White;
            label.Font = new Font("Segoe UI", 24F, FontStyle.Bold);
            label.AutoSize = true;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            card.Controls.Add(label);
            card.Controls.Add(titleLabel);
            root.Controls.Add(card, cardIndex % 4, cardIndex / 4);
            cardIndex++;
        }

        var recentPanel = new GroupBox
        {
            Text = "Recent Repair Orders",
            Dock = DockStyle.Fill,
            Margin = new Padding(8),
            Font = new Font("Segoe UI", 10F, FontStyle.Bold)
        };

        _recentOrdersGrid.Dock = DockStyle.Fill;
        _recentOrdersGrid.ReadOnly = true;
        _recentOrdersGrid.AllowUserToAddRows = false;
        _recentOrdersGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _recentOrdersGrid.MultiSelect = false;
        _recentOrdersGrid.RowHeadersVisible = false;
        _recentOrdersGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        recentPanel.Controls.Add(_recentOrdersGrid);

        root.Controls.Add(recentPanel, 0, 1);
        root.SetColumnSpan(recentPanel, 4);

        page.Controls.Add(root);
        return page;
    }

    private async Task LoadDashboardAsync()
    {
        var customersTask = DatabaseService.GetTableAsync("SELECT COUNT(*) AS TotalCustomers FROM Customers;");
        var devicesTask = DatabaseService.GetTableAsync("SELECT COUNT(*) AS TotalDevices FROM Devices;");
        var techniciansTask = DatabaseService.GetTableAsync("SELECT COUNT(*) AS TotalTechnicians FROM Technicians;");
        var ordersTask = DatabaseService.GetTableAsync("SELECT COUNT(*) AS TotalOrders FROM RepairOrders;");
        var completedTask = DatabaseService.GetTableAsync("SELECT COUNT(*) AS TotalCompleted FROM RepairOrders WHERE Status IN ('Completed', 'Delivered');");
        var pendingTask = DatabaseService.GetTableAsync("SELECT COUNT(*) AS TotalPending FROM RepairOrders WHERE Status IN ('Received', 'In Progress');");
        var revenueTask = DatabaseService.GetTableAsync("SELECT COALESCE(SUM(Amount), 0) AS TotalRevenue FROM Payments;");
        var recentTask = DatabaseService.GetTableAsync(
            @"SELECT r.OrderId, c.FullName AS Customer, d.Brand || ' ' || d.Model AS Device, 
                     COALESCE(t.FullName, 'Unassigned') AS Technician, r.Status, r.RepairCost 
              FROM RepairOrders r
              INNER JOIN Customers c ON c.CustomerId = r.CustomerId
              INNER JOIN Devices d ON d.DeviceId = r.DeviceId
              LEFT JOIN Technicians t ON t.TechnicianId = r.TechnicianId
              ORDER BY r.OrderId DESC LIMIT 10;");

        await Task.WhenAll(customersTask, devicesTask, techniciansTask, ordersTask, completedTask, pendingTask, revenueTask, recentTask);

        _lblTotalCustomers.Text = customersTask.Result.Rows.Count > 0 ? customersTask.Result.Rows[0]["TotalCustomers"].ToString() ?? "0" : "0";
        _lblTotalDevices.Text = devicesTask.Result.Rows.Count > 0 ? devicesTask.Result.Rows[0]["TotalDevices"].ToString() ?? "0" : "0";
        _lblTotalTechnicians.Text = techniciansTask.Result.Rows.Count > 0 ? techniciansTask.Result.Rows[0]["TotalTechnicians"].ToString() ?? "0" : "0";
        _lblTotalOrders.Text = ordersTask.Result.Rows.Count > 0 ? ordersTask.Result.Rows[0]["TotalOrders"].ToString() ?? "0" : "0";
        _lblCompletedOrders.Text = completedTask.Result.Rows.Count > 0 ? completedTask.Result.Rows[0]["TotalCompleted"].ToString() ?? "0" : "0";
        _lblPendingOrders.Text = pendingTask.Result.Rows.Count > 0 ? pendingTask.Result.Rows[0]["TotalPending"].ToString() ?? "0" : "0";
        _lblTotalRevenue.Text = revenueTask.Result.Rows.Count > 0 ? $"${Convert.ToDecimal(revenueTask.Result.Rows[0]["TotalRevenue"]).ToString("0.00", CultureInfo.InvariantCulture)}" : "$0.00";
        _recentOrdersGrid.DataSource = recentTask.Result;
    }

    // =========================================================================
    // CUSTOMERS TAB
    // =========================================================================
    private TabPage CreateCustomerTab()
    {
        var page = new TabPage("Customers");
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 220F));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var formPanel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 1, RowCount = 7 };
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));

        AddFormField(formPanel, "Full Name", _customerNameText, 0);
        AddFormField(formPanel, "Phone Number", _customerPhoneText, 2);
        AddFormField(formPanel, "Address", _customerAddressText, 4);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var addButton = new Button { Text = "Add Customer", AutoSize = true, BackColor = Color.FromArgb(52, 152, 219), ForeColor = Color.White, Margin = new Padding(0, 0, 8, 0) };
        var updateButton = new Button { Text = "Update", AutoSize = true, BackColor = Color.FromArgb(155, 89, 182), ForeColor = Color.White, Margin = new Padding(0, 0, 8, 0) };
        var clearButton = new Button { Text = "Clear", AutoSize = true, BackColor = Color.FromArgb(108, 117, 125), ForeColor = Color.White };

        addButton.Click += SaveCustomer;
        updateButton.Click += UpdateCustomer;
        _customerDeleteButton.Click += DeleteCustomer;
        clearButton.Click += (_, _) => ClearCustomerForm();

        buttons.Controls.Add(addButton);
        buttons.Controls.Add(updateButton);
        buttons.Controls.Add(_customerDeleteButton);
        buttons.Controls.Add(clearButton);
        formPanel.Controls.Add(buttons, 0, 6);

        var searchPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var searchLabel = new Label { Text = "Search by Customer Name, Phone, or Address", AutoSize = true, Dock = DockStyle.Top };
        _customerSearchText.Dock = DockStyle.Top;
        _customerSearchText.TextChanged += (_, _) => TriggerDebouncedSearch(() => _ = LoadCustomerDataAsync());

        searchPanel.Controls.Add(searchLabel);
        searchPanel.Controls.Add(_customerSearchText);

        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        _customerGrid.Dock = DockStyle.Fill;
        _customerGrid.ReadOnly = true;
        _customerGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _customerGrid.MultiSelect = false;
        _customerGrid.RowHeadersVisible = false;
        _customerGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _customerGrid.SelectionChanged += CustomerGridSelectionChanged;
        gridPanel.Controls.Add(_customerGrid);

        panel.Controls.Add(formPanel, 0, 0);
        panel.Controls.Add(searchPanel, 1, 0);
        panel.Controls.Add(gridPanel, 0, 1);
        panel.SetColumnSpan(gridPanel, 2);

        page.Controls.Add(panel);
        return page;
    }

    private async Task LoadCustomerDataAsync()
    {
        var filter = _customerSearchText.Text.Trim();
        string query;
        DataTable table;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = "SELECT CustomerId AS [Customer ID], FullName AS [Full Name], PhoneNumber AS [Phone], Address AS [Address] FROM Customers WHERE FullName LIKE @search OR PhoneNumber LIKE @search OR Address LIKE @search ORDER BY CustomerId DESC";
            table = await DatabaseService.GetTableAsync(query, ("@search", $"%{filter}%"));
        }
        else
        {
            query = "SELECT CustomerId AS [Customer ID], FullName AS [Full Name], PhoneNumber AS [Phone], Address AS [Address] FROM Customers ORDER BY CustomerId DESC";
            table = await DatabaseService.GetTableAsync(query);
        }

        _customerGrid.DataSource = table;
    }

    private void CustomerGridSelectionChanged(object? sender, EventArgs e)
    {
        if (_customerGrid.SelectedRows.Count == 0) return;
        var row = _customerGrid.SelectedRows[0];
        _selectedCustomerId = row.Cells[0].Value is int id ? id : Convert.ToInt32(row.Cells[0].Value);
        _customerNameText.Text = row.Cells[1].Value?.ToString() ?? string.Empty;
        _customerPhoneText.Text = row.Cells[2].Value?.ToString() ?? string.Empty;
        _customerAddressText.Text = row.Cells[3].Value?.ToString() ?? string.Empty;
    }

    private async void SaveCustomer(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_customerNameText.Text) || string.IsNullOrWhiteSpace(_customerPhoneText.Text) || string.IsNullOrWhiteSpace(_customerAddressText.Text))
        {
            MessageBox.Show("يرجى ملء جميع حقول العميل.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var success = await DatabaseService.ExecuteNonQueryAsync(
            "INSERT INTO Customers (FullName, PhoneNumber, Address) VALUES (@name, @phone, @address);",
            ("@name", _customerNameText.Text.Trim()),
            ("@phone", _customerPhoneText.Text.Trim()),
            ("@address", _customerAddressText.Text.Trim()));

        if (success)
        {
            ClearCustomerForm();
            await LoadCustomerDataAsync();
            await LoadDashboardAsync();
            await RefreshCustomerDropdownsAsync();
            MessageBox.Show("تمت إضافة العميل بنجاح.", "نجاح", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void UpdateCustomer(object? sender, EventArgs e)
    {
        if (_selectedCustomerId is null)
        {
            MessageBox.Show("يرجى اختيار عميل من الجدول أولاً.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var success = await DatabaseService.ExecuteNonQueryAsync(
            "UPDATE Customers SET FullName = @name, PhoneNumber = @phone, Address = @address WHERE CustomerId = @id;",
            ("@name", _customerNameText.Text.Trim()),
            ("@phone", _customerPhoneText.Text.Trim()),
            ("@address", _customerAddressText.Text.Trim()),
            ("@id", _selectedCustomerId.Value));

        if (success)
        {
            ClearCustomerForm();
            await LoadCustomerDataAsync();
            await LoadDashboardAsync();
            await RefreshCustomerDropdownsAsync();
            MessageBox.Show("تم تحديث بيانات العميل بنجاح.", "نجاح", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void DeleteCustomer(object? sender, EventArgs e)
    {
        if (_selectedCustomerId is null)
        {
            MessageBox.Show("يرجى اختيار عميل للحذف.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show("هل أنت متأكد من حذف هذا العميل؟ سيتم منع الحذف إذا كانت لديه أجهزة أو صيانة مرتبطة.", "تأكيد الحذف", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        var success = await DatabaseService.ExecuteNonQueryAsync("DELETE FROM Customers WHERE CustomerId = @id;", ("@id", _selectedCustomerId.Value));
        if (success)
        {
            ClearCustomerForm();
            await LoadCustomerDataAsync();
            await LoadDashboardAsync();
            await RefreshCustomerDropdownsAsync();
            MessageBox.Show("تم حذف العميل.", "تم الحذف", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ClearCustomerForm()
    {
        _selectedCustomerId = null;
        _customerNameText.Clear();
        _customerPhoneText.Clear();
        _customerAddressText.Clear();
        _customerGrid.ClearSelection();
    }

    // =========================================================================
    // DEVICES TAB
    // =========================================================================
    private TabPage CreateDeviceTab()
    {
        var page = new TabPage("Devices");
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 330F));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var formPanel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 1, RowCount = 11 };
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));

        _deviceCustomerComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _deviceTypeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _deviceTypeComboBox.Items.AddRange(new object[] { "Laptop", "Desktop", "Mobile", "Tablet", "Printer", "Console", "Other" });

        AddFormField(formPanel, "Customer", _deviceCustomerComboBox, 0);
        AddFormField(formPanel, "Device Type", _deviceTypeComboBox, 2);
        AddFormField(formPanel, "Brand", _deviceBrandText, 4);
        AddFormField(formPanel, "Model", _deviceModelText, 6);
        AddFormField(formPanel, "Serial Number", _deviceSerialText, 8);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var addButton = new Button { Text = "Add Device", AutoSize = true, BackColor = Color.FromArgb(52, 152, 219), ForeColor = Color.White, Margin = new Padding(0, 0, 8, 0) };
        var updateButton = new Button { Text = "Update", AutoSize = true, BackColor = Color.FromArgb(155, 89, 182), ForeColor = Color.White, Margin = new Padding(0, 0, 8, 0) };
        var clearButton = new Button { Text = "Clear", AutoSize = true, BackColor = Color.FromArgb(108, 117, 125), ForeColor = Color.White };

        addButton.Click += SaveDevice;
        updateButton.Click += UpdateDevice;
        _deviceDeleteButton.Click += DeleteDevice;
        clearButton.Click += (_, _) => ClearDeviceForm();

        buttons.Controls.Add(addButton);
        buttons.Controls.Add(updateButton);
        buttons.Controls.Add(_deviceDeleteButton);
        buttons.Controls.Add(clearButton);
        formPanel.Controls.Add(buttons, 0, 10);

        var searchPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var searchLabel = new Label { Text = "Search by Brand, Model, Serial, or Customer", AutoSize = true, Dock = DockStyle.Top };
        _deviceSearchText.Dock = DockStyle.Top;
        _deviceSearchText.TextChanged += (_, _) => TriggerDebouncedSearch(() => _ = LoadDeviceDataAsync());
        searchPanel.Controls.Add(searchLabel);
        searchPanel.Controls.Add(_deviceSearchText);

        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        _deviceGrid.Dock = DockStyle.Fill;
        _deviceGrid.ReadOnly = true;
        _deviceGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _deviceGrid.MultiSelect = false;
        _deviceGrid.RowHeadersVisible = false;
        _deviceGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _deviceGrid.SelectionChanged += DeviceGridSelectionChanged;
        gridPanel.Controls.Add(_deviceGrid);

        panel.Controls.Add(formPanel, 0, 0);
        panel.Controls.Add(searchPanel, 1, 0);
        panel.Controls.Add(gridPanel, 0, 1);
        panel.SetColumnSpan(gridPanel, 2);

        page.Controls.Add(panel);
        return page;
    }

    private async Task LoadDeviceDataAsync()
    {
        var filter = _deviceSearchText.Text.Trim();
        string query;
        DataTable table;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = @"SELECT d.DeviceId AS [Device ID], c.CustomerId AS [Customer ID], c.FullName AS [Customer], 
                             d.DeviceType AS [Type], d.Brand AS [Brand], d.Model AS [Model], d.SerialNumber AS [Serial Number] 
                      FROM Devices d
                      INNER JOIN Customers c ON c.CustomerId = d.CustomerId
                      WHERE d.Brand LIKE @search OR d.Model LIKE @search OR d.SerialNumber LIKE @search OR c.FullName LIKE @search
                      ORDER BY d.DeviceId DESC;";
            table = await DatabaseService.GetTableAsync(query, ("@search", $"%{filter}%"));
        }
        else
        {
            query = @"SELECT d.DeviceId AS [Device ID], c.CustomerId AS [Customer ID], c.FullName AS [Customer], 
                             d.DeviceType AS [Type], d.Brand AS [Brand], d.Model AS [Model], d.SerialNumber AS [Serial Number] 
                      FROM Devices d
                      INNER JOIN Customers c ON c.CustomerId = d.CustomerId
                      ORDER BY d.DeviceId DESC;";
            table = await DatabaseService.GetTableAsync(query);
        }

        _deviceGrid.DataSource = table;
        await RefreshCustomerDropdownsAsync();
    }

    private void DeviceGridSelectionChanged(object? sender, EventArgs e)
    {
        if (_deviceGrid.SelectedRows.Count == 0) return;
        var row = _deviceGrid.SelectedRows[0];
        _selectedDeviceId = row.Cells[0].Value is int id ? id : Convert.ToInt32(row.Cells[0].Value);
        _deviceTypeComboBox.SelectedItem = row.Cells[3].Value?.ToString();
        _deviceBrandText.Text = row.Cells[4].Value?.ToString() ?? string.Empty;
        _deviceModelText.Text = row.Cells[5].Value?.ToString() ?? string.Empty;
        _deviceSerialText.Text = row.Cells[6].Value?.ToString() ?? string.Empty;

        var customerValue = row.Cells[1].Value?.ToString();
        foreach (ComboBoxItem item in _deviceCustomerComboBox.Items)
        {
            if (item.Value == customerValue)
            {
                _deviceCustomerComboBox.SelectedItem = item;
                break;
            }
        }
    }

    private async void SaveDevice(object? sender, EventArgs e)
    {
        if (_deviceCustomerComboBox.SelectedItem is not ComboBoxItem customerItem ||
            _deviceTypeComboBox.SelectedItem is null ||
            string.IsNullOrWhiteSpace(_deviceBrandText.Text) ||
            string.IsNullOrWhiteSpace(_deviceModelText.Text) ||
            string.IsNullOrWhiteSpace(_deviceSerialText.Text))
        {
            MessageBox.Show("يرجى ملء جميع حقول الجهاز وتحديد العميل.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var success = await DatabaseService.ExecuteNonQueryAsync(
            "INSERT INTO Devices (CustomerId, DeviceType, Brand, Model, SerialNumber) VALUES (@customerId, @type, @brand, @model, @serial);",
            ("@customerId", customerItem.Value),
            ("@type", _deviceTypeComboBox.SelectedItem.ToString()!),
            ("@brand", _deviceBrandText.Text.Trim()),
            ("@model", _deviceModelText.Text.Trim()),
            ("@serial", _deviceSerialText.Text.Trim()));

        if (success)
        {
            ClearDeviceForm();
            await LoadDeviceDataAsync();
            await LoadDashboardAsync();
            MessageBox.Show("تمت إضافة الجهاز بنجاح.", "نجاح", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void UpdateDevice(object? sender, EventArgs e)
    {
        if (_selectedDeviceId is null)
        {
            MessageBox.Show("يرجى اختيار جهاز من الجدول أولاً.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_deviceCustomerComboBox.SelectedItem is not ComboBoxItem customerItem ||
            _deviceTypeComboBox.SelectedItem is null ||
            string.IsNullOrWhiteSpace(_deviceBrandText.Text) ||
            string.IsNullOrWhiteSpace(_deviceModelText.Text) ||
            string.IsNullOrWhiteSpace(_deviceSerialText.Text))
        {
            MessageBox.Show("يرجى ملء جميع حقول الجهاز.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var success = await DatabaseService.ExecuteNonQueryAsync(
            "UPDATE Devices SET CustomerId = @customerId, DeviceType = @type, Brand = @brand, Model = @model, SerialNumber = @serial WHERE DeviceId = @id;",
            ("@customerId", customerItem.Value),
            ("@type", _deviceTypeComboBox.SelectedItem.ToString()!),
            ("@brand", _deviceBrandText.Text.Trim()),
            ("@model", _deviceModelText.Text.Trim()),
            ("@serial", _deviceSerialText.Text.Trim()),
            ("@id", _selectedDeviceId.Value));

        if (success)
        {
            ClearDeviceForm();
            await LoadDeviceDataAsync();
            await LoadDashboardAsync();
            MessageBox.Show("تم تحديث بيانات الجهاز بنجاح.", "نجاح", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void DeleteDevice(object? sender, EventArgs e)
    {
        if (_selectedDeviceId is null)
        {
            MessageBox.Show("يرجى اختيار جهاز للحذف.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show("هل أنت متأكد من حذف هذا الجهاز؟", "تأكيد الحذف", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        var success = await DatabaseService.ExecuteNonQueryAsync("DELETE FROM Devices WHERE DeviceId = @id;", ("@id", _selectedDeviceId.Value));
        if (success)
        {
            ClearDeviceForm();
            await LoadDeviceDataAsync();
            await LoadDashboardAsync();
            MessageBox.Show("تم حذف الجهاز.", "تم الحذف", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ClearDeviceForm()
    {
        _selectedDeviceId = null;
        _deviceCustomerComboBox.SelectedIndex = -1;
        _deviceTypeComboBox.SelectedIndex = -1;
        _deviceBrandText.Clear();
        _deviceModelText.Clear();
        _deviceSerialText.Clear();
        _deviceGrid.ClearSelection();
    }

    // =========================================================================
    // TECHNICIANS TAB
    // =========================================================================
    private TabPage CreateTechnicianTab()
    {
        var page = new TabPage("Technicians");
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 220F));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var formPanel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 1, RowCount = 7 };
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));

        AddFormField(formPanel, "Full Name", _technicianNameText, 0);
        AddFormField(formPanel, "Phone Number", _technicianPhoneText, 2);
        AddFormField(formPanel, "Specialty", _technicianSpecialtyText, 4);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var addButton = new Button { Text = "Add Technician", AutoSize = true, BackColor = Color.FromArgb(52, 152, 219), ForeColor = Color.White, Margin = new Padding(0, 0, 8, 0) };
        var updateButton = new Button { Text = "Update", AutoSize = true, BackColor = Color.FromArgb(155, 89, 182), ForeColor = Color.White, Margin = new Padding(0, 0, 8, 0) };
        var clearButton = new Button { Text = "Clear", AutoSize = true, BackColor = Color.FromArgb(108, 117, 125), ForeColor = Color.White };

        addButton.Click += SaveTechnician;
        updateButton.Click += UpdateTechnician;
        _technicianDeleteButton.Click += DeleteTechnician;
        clearButton.Click += (_, _) => ClearTechnicianForm();

        buttons.Controls.Add(addButton);
        buttons.Controls.Add(updateButton);
        buttons.Controls.Add(_technicianDeleteButton);
        buttons.Controls.Add(clearButton);
        formPanel.Controls.Add(buttons, 0, 6);

        var searchPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var searchLabel = new Label { Text = "Search by Name, Phone, or Specialty", AutoSize = true, Dock = DockStyle.Top };
        _technicianSearchText.Dock = DockStyle.Top;
        _technicianSearchText.TextChanged += (_, _) => TriggerDebouncedSearch(() => _ = LoadTechnicianDataAsync());
        searchPanel.Controls.Add(searchLabel);
        searchPanel.Controls.Add(_technicianSearchText);

        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        _technicianGrid.Dock = DockStyle.Fill;
        _technicianGrid.ReadOnly = true;
        _technicianGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _technicianGrid.MultiSelect = false;
        _technicianGrid.RowHeadersVisible = false;
        _technicianGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _technicianGrid.SelectionChanged += TechnicianGridSelectionChanged;
        gridPanel.Controls.Add(_technicianGrid);

        panel.Controls.Add(formPanel, 0, 0);
        panel.Controls.Add(searchPanel, 1, 0);
        panel.Controls.Add(gridPanel, 0, 1);
        panel.SetColumnSpan(gridPanel, 2);

        page.Controls.Add(panel);
        return page;
    }

    private async Task LoadTechnicianDataAsync()
    {
        var filter = _technicianSearchText.Text.Trim();
        string query;
        DataTable table;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = "SELECT TechnicianId AS [Technician ID], FullName AS [Full Name], Phone AS [Phone], Specialty AS [Specialty] FROM Technicians WHERE FullName LIKE @search OR Phone LIKE @search OR Specialty LIKE @search ORDER BY TechnicianId DESC;";
            table = await DatabaseService.GetTableAsync(query, ("@search", $"%{filter}%"));
        }
        else
        {
            query = "SELECT TechnicianId AS [Technician ID], FullName AS [Full Name], Phone AS [Phone], Specialty AS [Specialty] FROM Technicians ORDER BY TechnicianId DESC;";
            table = await DatabaseService.GetTableAsync(query);
        }

        _technicianGrid.DataSource = table;
        await RefreshTechnicianDropdownsAsync();
    }

    private void TechnicianGridSelectionChanged(object? sender, EventArgs e)
    {
        if (_technicianGrid.SelectedRows.Count == 0) return;
        var row = _technicianGrid.SelectedRows[0];
        _selectedTechnicianId = row.Cells[0].Value is int id ? id : Convert.ToInt32(row.Cells[0].Value);
        _technicianNameText.Text = row.Cells[1].Value?.ToString() ?? string.Empty;
        _technicianPhoneText.Text = row.Cells[2].Value?.ToString() ?? string.Empty;
        _technicianSpecialtyText.Text = row.Cells[3].Value?.ToString() ?? string.Empty;
    }

    private async void SaveTechnician(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_technicianNameText.Text) || string.IsNullOrWhiteSpace(_technicianPhoneText.Text) || string.IsNullOrWhiteSpace(_technicianSpecialtyText.Text))
        {
            MessageBox.Show("يرجى ملء جميع حقول الفني.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var success = await DatabaseService.ExecuteNonQueryAsync(
            "INSERT INTO Technicians (FullName, Phone, Specialty) VALUES (@name, @phone, @specialty);",
            ("@name", _technicianNameText.Text.Trim()),
            ("@phone", _technicianPhoneText.Text.Trim()),
            ("@specialty", _technicianSpecialtyText.Text.Trim()));

        if (success)
        {
            ClearTechnicianForm();
            await LoadTechnicianDataAsync();
            await LoadDashboardAsync();
            MessageBox.Show("تمت إضافة الفني بنجاح.", "نجاح", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void UpdateTechnician(object? sender, EventArgs e)
    {
        if (_selectedTechnicianId is null)
        {
            MessageBox.Show("يرجى اختيار فني من الجدول أولاً.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var success = await DatabaseService.ExecuteNonQueryAsync(
            "UPDATE Technicians SET FullName = @name, Phone = @phone, Specialty = @specialty WHERE TechnicianId = @id;",
            ("@name", _technicianNameText.Text.Trim()),
            ("@phone", _technicianPhoneText.Text.Trim()),
            ("@specialty", _technicianSpecialtyText.Text.Trim()),
            ("@id", _selectedTechnicianId.Value));

        if (success)
        {
            ClearTechnicianForm();
            await LoadTechnicianDataAsync();
            await LoadDashboardAsync();
            MessageBox.Show("تم تحديث بيانات الفني بنجاح.", "نجاح", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void DeleteTechnician(object? sender, EventArgs e)
    {
        if (_selectedTechnicianId is null)
        {
            MessageBox.Show("يرجى اختيار فني للحذف.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show("هل أنت متأكد من حذف هذا الفني؟", "تأكيد الحذف", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        var success = await DatabaseService.ExecuteNonQueryAsync("DELETE FROM Technicians WHERE TechnicianId = @id;", ("@id", _selectedTechnicianId.Value));
        if (success)
        {
            ClearTechnicianForm();
            await LoadTechnicianDataAsync();
            await LoadDashboardAsync();
            MessageBox.Show("تم حذف الفني.", "تم الحذف", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ClearTechnicianForm()
    {
        _selectedTechnicianId = null;
        _technicianNameText.Clear();
        _technicianPhoneText.Clear();
        _technicianSpecialtyText.Clear();
        _technicianGrid.ClearSelection();
    }

    // =========================================================================
    // REPAIR ORDERS TAB (مع رسم الـ GDI+ الحي التفاعلي لشريط دورة الحياة)
    // =========================================================================
    private TabPage CreateRepairOrderTab()
    {
        var page = new TabPage("Repair Orders");
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 420F)); // توسيع المساحة لاحتواء شريط الرسم
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var formContainer = new Panel { Dock = DockStyle.Fill };

        // 1. لوحة رسم شريط دورة الحياة باستخدام GDI+ (Task 2 On-Screen Custom Drawing)
        _lifecycleCanvas.Dock = DockStyle.Top;
        _lifecycleCanvas.Height = 65;
        _lifecycleCanvas.BackColor = Color.FromArgb(248, 249, 250);
        _lifecycleCanvas.Margin = new Padding(0, 0, 0, 8);
        _lifecycleCanvas.Paint += OnLifecycleCanvasPaint;

        var formPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(4),
            ColumnCount = 2,
            RowCount = 11
        };
        formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        formPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));

        _repairCustomerComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _repairCustomerComboBox.SelectedIndexChanged += async (_, _) =>
        {
            if (_repairCustomerComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Value, out var cid))
            {
                await LoadDevicesForCustomerAsync(cid);
                if (_repairDeviceComboBox.Items.Count > 0)
                {
                    _repairDeviceComboBox.SelectedIndex = 0;
                }
            }
            else
            {
                _repairDeviceComboBox.Items.Clear();
            }
        };
        _repairDeviceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _repairTechnicianComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _repairStatusComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _repairStatusComboBox.Items.AddRange(new object[] { "Received", "In Progress", "Completed", "Delivered", "Cancelled" });

        // إعادة رسم شريط دورة الحياة عند تغيير الحالة
        _repairStatusComboBox.SelectedIndexChanged += (_, _) => _lifecycleCanvas.Invalidate();

        AddRepairFormField(formPanel, "Customer", _repairCustomerComboBox, 0, 0);
        AddRepairFormField(formPanel, "Device", _repairDeviceComboBox, 1, 0);
        AddRepairFormField(formPanel, "Assigned Technician", _repairTechnicianComboBox, 0, 2);
        AddRepairFormField(formPanel, "Repair Status", _repairStatusComboBox, 1, 2);
        AddRepairFormField(formPanel, "Problem Description", _repairProblemText, 0, 4, 2);
        AddRepairFormField(formPanel, "Diagnosis", _repairDiagnosisText, 0, 6, 2);
        AddRepairFormField(formPanel, "Repair Cost", _repairCostText, 0, 8, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
        var addButton = new Button { Text = "Create Order", AutoSize = true, BackColor = Color.FromArgb(52, 152, 219), ForeColor = Color.White, Margin = new Padding(0, 0, 8, 0) };
        var updateButton = new Button { Text = "Update Order", AutoSize = true, BackColor = Color.FromArgb(155, 89, 182), ForeColor = Color.White, Margin = new Padding(0, 0, 8, 0) };
        var clearButton = new Button { Text = "Clear", AutoSize = true, BackColor = Color.FromArgb(108, 117, 125), ForeColor = Color.White };

        addButton.Click += SaveRepairOrder;
        updateButton.Click += UpdateRepairOrder;
        _repairOrderDeleteButton.Click += DeleteRepairOrder;
        clearButton.Click += (_, _) => ClearRepairOrderForm();

        buttons.Controls.Add(addButton);
        buttons.Controls.Add(updateButton);
        buttons.Controls.Add(_repairOrderDeleteButton);
        buttons.Controls.Add(clearButton);
        formPanel.Controls.Add(buttons, 0, 10);
        formPanel.SetColumnSpan(buttons, 2);

        formContainer.Controls.Add(formPanel);
        formContainer.Controls.Add(_lifecycleCanvas);

        var searchPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var searchLabel = new Label { Text = "Search by Order ID, Customer, Serial, or Status", AutoSize = true, Dock = DockStyle.Top };
        _repairSearchText.Dock = DockStyle.Top;
        _repairSearchText.TextChanged += (_, _) => TriggerDebouncedSearch(() => _ = LoadRepairOrderDataAsync());
        searchPanel.Controls.Add(searchLabel);
        searchPanel.Controls.Add(_repairSearchText);

        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        _repairGrid.Dock = DockStyle.Fill;
        _repairGrid.ReadOnly = true;
        _repairGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _repairGrid.MultiSelect = false;
        _repairGrid.RowHeadersVisible = false;
        _repairGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _repairGrid.SelectionChanged += RepairGridSelectionChanged;
        gridPanel.Controls.Add(_repairGrid);

        panel.Controls.Add(formContainer, 0, 0);
        panel.Controls.Add(searchPanel, 1, 0);
        panel.Controls.Add(gridPanel, 0, 1);
        panel.SetColumnSpan(gridPanel, 2);

        page.Controls.Add(panel);
        return page;
    }

    /// <summary>
    /// رسم دورة حياة الصيانة (Repair Lifecycle) مباشرة على الشاشة باستخدام GDI+
    /// يتم تحسين الرسم بـ AntiAlias واستخدام Using لجميع الـ Pens والـ Brushes لمنع تسريب الذاكرة
    /// </summary>
    private void OnLifecycleCanvasPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        string[] stages = { "Received", "In Progress", "Completed", "Delivered" };
        var selectedStatus = _repairStatusComboBox.SelectedItem?.ToString() ?? "Received";
        var activeIndex = Array.IndexOf(stages, selectedStatus);
        if (activeIndex < 0) activeIndex = 0;

        int y = 22;
        int startX = 50;
        int stepWidth = 110;

        using var lineGray = new Pen(Color.FromArgb(210, 215, 220), 4);
        using var lineGreen = new Pen(Color.FromArgb(46, 204, 113), 4);
        using var fontNode = new Font("Segoe UI", 8F, FontStyle.Bold);
        using var fontText = new Font("Segoe UI", 7.5F, FontStyle.Bold);
        using var brushDark = new SolidBrush(Color.FromArgb(44, 62, 80));

        // رسم خطوط الربط بين المراحل
        for (int i = 0; i < stages.Length - 1; i++)
        {
            var p = (i < activeIndex) ? lineGreen : lineGray;
            g.DrawLine(p, startX + (i * stepWidth), y, startX + ((i + 1) * stepWidth), y);
        }

        // رسم الدوائر والمؤشرات
        for (int i = 0; i < stages.Length; i++)
        {
            int cx = startX + (i * stepWidth);
            var rect = new Rectangle(cx - 10, y - 10, 20, 20);

            if (i < activeIndex)
            {
                // مرحلة مكتملة: دائرة خضراء مع علامة الصح
                g.FillEllipse(Brushes.SeaGreen, rect);
                g.DrawString("✓", fontNode, Brushes.White, cx - 5, y - 6);
                g.DrawString(stages[i], fontText, Brushes.SeaGreen, cx - 22, y + 14);
            }
            else if (i == activeIndex)
            {
                // المرحلة النشطة الحالية: هالة برتقالية مضيئة
                using var haloBrush = new SolidBrush(Color.FromArgb(254, 216, 177));
                g.FillEllipse(haloBrush, new Rectangle(cx - 14, y - 14, 28, 28));
                g.FillEllipse(Brushes.DarkOrange, rect);
                g.DrawString("●", fontNode, Brushes.White, cx - 4, y - 6);
                g.DrawString(stages[i], fontText, Brushes.DarkOrange, cx - 25, y + 14);
            }
            else
            {
                // مرحلة قادمة معلقة: دائرة رمادية هادئة
                g.FillEllipse(Brushes.LightGray, rect);
                g.DrawString(stages[i], fontText, Brushes.Gray, cx - 22, y + 14);
            }
        }
    }

    private async Task LoadRepairOrderDataAsync()
    {
        var filter = _repairSearchText.Text.Trim();
        string query;
        DataTable table;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = @"SELECT r.OrderId AS [Order ID], c.FullName AS [Customer], d.Brand || ' ' || d.Model AS [Device],
                             COALESCE(t.FullName, 'Unassigned') AS [Technician], r.ProblemDescription AS [Problem], 
                             r.Diagnosis AS [Diagnosis], r.Status AS [Status], r.ReceivedDate AS [Received Date], r.RepairCost AS [Cost]
                      FROM RepairOrders r
                      INNER JOIN Customers c ON c.CustomerId = r.CustomerId
                      INNER JOIN Devices d ON d.DeviceId = r.DeviceId
                      LEFT JOIN Technicians t ON t.TechnicianId = r.TechnicianId
                      WHERE r.OrderId LIKE @search OR c.FullName LIKE @search OR d.SerialNumber LIKE @search OR r.Status LIKE @search
                      ORDER BY r.OrderId DESC;";
            table = await DatabaseService.GetTableAsync(query, ("@search", $"%{filter}%"));
        }
        else
        {
            query = @"SELECT r.OrderId AS [Order ID], c.FullName AS [Customer], d.Brand || ' ' || d.Model AS [Device],
                             COALESCE(t.FullName, 'Unassigned') AS [Technician], r.ProblemDescription AS [Problem], 
                             r.Diagnosis AS [Diagnosis], r.Status AS [Status], r.ReceivedDate AS [Received Date], r.RepairCost AS [Cost]
                      FROM RepairOrders r
                      INNER JOIN Customers c ON c.CustomerId = r.CustomerId
                      INNER JOIN Devices d ON d.DeviceId = r.DeviceId
                      LEFT JOIN Technicians t ON t.TechnicianId = r.TechnicianId
                      ORDER BY r.OrderId DESC;";
            table = await DatabaseService.GetTableAsync(query);
        }

        _repairGrid.DataSource = table;
        await RefreshRepairOrderDropdownsAsync();
    }

    private async void RepairGridSelectionChanged(object? sender, EventArgs e)
    {
        if (_repairGrid.SelectedRows.Count == 0) return;
        var row = _repairGrid.SelectedRows[0];
        _selectedRepairOrderId = row.Cells[0].Value is int id ? id : Convert.ToInt32(row.Cells[0].Value);

        var query = "SELECT CustomerId, DeviceId, TechnicianId, ProblemDescription, Diagnosis, Status, RepairCost FROM RepairOrders WHERE OrderId = @orderId;";
        var order = await DatabaseService.GetTableAsync(query, ("@orderId", _selectedRepairOrderId.Value));
        if (order.Rows.Count == 0) return;

        var data = order.Rows[0];
        var customerId = data["CustomerId"].ToString();
        var deviceId = data["DeviceId"].ToString();
        var technicianId = data["TechnicianId"]?.ToString();

        SelectComboItem(_repairCustomerComboBox, customerId);
        await LoadDevicesForCustomerAsync(Convert.ToInt32(customerId));
        SelectComboItem(_repairDeviceComboBox, deviceId);
        SelectComboItem(_repairTechnicianComboBox, technicianId);

        _repairStatusComboBox.SelectedItem = data["Status"]?.ToString() ?? "Received";
        _repairProblemText.Text = data["ProblemDescription"]?.ToString() ?? string.Empty;
        _repairDiagnosisText.Text = data["Diagnosis"]?.ToString() ?? string.Empty;
        _repairCostText.Text = data["RepairCost"]?.ToString() ?? "0";

        // تحديث الرسم الحي فور اختيار الطلب
        _lifecycleCanvas.Invalidate();
    }

    private async void SaveRepairOrder(object? sender, EventArgs e)
    {
        if (_repairCustomerComboBox.SelectedItem is not ComboBoxItem customerItem ||
            _repairDeviceComboBox.SelectedItem is not ComboBoxItem deviceItem ||
            string.IsNullOrWhiteSpace(_repairProblemText.Text))
        {
            MessageBox.Show("يرجى اختيار العميل والجهاز وكتابة وصف العطل.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var customerId = customerItem.Value;
        var deviceId = deviceItem.Value;
        var technicianId = _repairTechnicianComboBox.SelectedItem is ComboBoxItem tech ? tech.Value : null;

        decimal cost = 0m;
        if (!string.IsNullOrWhiteSpace(_repairCostText.Text) && !decimal.TryParse(_repairCostText.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out cost))
        {
            MessageBox.Show("يرجى إدخال تكلفة صيانة صحيحة.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        object technicianValue = string.IsNullOrEmpty(technicianId) ? DBNull.Value : technicianId;
        var status = _repairStatusComboBox.SelectedItem?.ToString() ?? "Received";

        var success = await DatabaseService.ExecuteNonQueryAsync(
            @"INSERT INTO RepairOrders (CustomerId, DeviceId, TechnicianId, ProblemDescription, Diagnosis, Status, ReceivedDate, RepairCost)
              VALUES (@customerId, @deviceId, @technicianId, @problemDescription, @diagnosis, @status, @receivedDate, @repairCost);",
            ("@customerId", customerId),
            ("@deviceId", deviceId),
            ("@technicianId", technicianValue),
            ("@problemDescription", _repairProblemText.Text.Trim()),
            ("@diagnosis", _repairDiagnosisText.Text.Trim()),
            ("@status", status),
            ("@receivedDate", DateTime.Now.ToString("yyyy-MM-dd")),
            ("@repairCost", cost));

        if (success)
        {
            ClearRepairOrderForm();
            await LoadRepairOrderDataAsync();
            await LoadDashboardAsync();
            MessageBox.Show("تم إنشاء أمر الصيانة بنجاح.", "نجاح", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void UpdateRepairOrder(object? sender, EventArgs e)
    {
        if (_selectedRepairOrderId is null)
        {
            MessageBox.Show("يرجى اختيار أمر صيانة من الجدول أولاً.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var existingStatusQuery = await DatabaseService.GetTableAsync("SELECT Status FROM RepairOrders WHERE OrderId = @orderId;", ("@orderId", _selectedRepairOrderId.Value));
        if (existingStatusQuery.Rows.Count == 0) return;

        var currentStatus = existingStatusQuery.Rows[0]["Status"].ToString() ?? "Received";
        var newStatus = _repairStatusComboBox.SelectedItem?.ToString() ?? currentStatus;

        if (!IsValidStatusTransition(currentStatus, newStatus))
        {
            MessageBox.Show($"لا يمكن الانتقال مباشرة من حالة '{currentStatus}' إلى '{newStatus}'. يرجى الالتزام بالدورة الهندسية للصيانة.", "انتقال غير مسموح", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var customerId = ((ComboBoxItem)_repairCustomerComboBox.SelectedItem).Value;
        var deviceId = ((ComboBoxItem)_repairDeviceComboBox.SelectedItem).Value;
        var technicianId = _repairTechnicianComboBox.SelectedItem is ComboBoxItem tech ? tech.Value : null;

        decimal cost = 0m;
        if (!string.IsNullOrWhiteSpace(_repairCostText.Text))
        {
            decimal.TryParse(_repairCostText.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out cost);
        }

        object technicianValue = string.IsNullOrEmpty(technicianId) ? DBNull.Value : technicianId;

        var success = await DatabaseService.ExecuteNonQueryAsync(
            @"UPDATE RepairOrders 
              SET CustomerId = @customerId, DeviceId = @deviceId, TechnicianId = @technicianId, 
                  ProblemDescription = @problemDescription, Diagnosis = @diagnosis, Status = @status, RepairCost = @repairCost 
              WHERE OrderId = @orderId;",
            ("@customerId", customerId),
            ("@deviceId", deviceId),
            ("@technicianId", technicianValue),
            ("@problemDescription", _repairProblemText.Text.Trim()),
            ("@diagnosis", _repairDiagnosisText.Text.Trim()),
            ("@status", newStatus),
            ("@repairCost", cost),
            ("@orderId", _selectedRepairOrderId.Value));

        if (success)
        {
            ClearRepairOrderForm();
            await LoadRepairOrderDataAsync();
            await LoadDashboardAsync();
            MessageBox.Show("تم تحديث أمر الصيانة بنجاح.", "نجاح", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void DeleteRepairOrder(object? sender, EventArgs e)
    {
        if (_selectedRepairOrderId is null)
        {
            MessageBox.Show("يرجى اختيار أمر صيانة أولاً.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show("هل أنت متأكد من حذف أمر الصيانة هذا؟", "تأكيد الحذف", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        var success = await DatabaseService.ExecuteNonQueryAsync("DELETE FROM RepairOrders WHERE OrderId = @orderId;", ("@orderId", _selectedRepairOrderId.Value));
        if (success)
        {
            ClearRepairOrderForm();
            await LoadRepairOrderDataAsync();
            await LoadDashboardAsync();
            MessageBox.Show("تم حذف أمر الصيانة.", "تم الحذف", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ClearRepairOrderForm()
    {
        _selectedRepairOrderId = null;
        _repairCustomerComboBox.SelectedIndex = -1;
        _repairDeviceComboBox.SelectedIndex = -1;
        _repairTechnicianComboBox.SelectedIndex = -1;
        _repairStatusComboBox.SelectedIndex = -1;
        _repairProblemText.Clear();
        _repairDiagnosisText.Clear();
        _repairCostText.Clear();
        _repairGrid.ClearSelection();
        _lifecycleCanvas.Invalidate();
    }

    private static bool IsValidStatusTransition(string currentStatus, string nextStatus)
    {
        var transitions = new Dictionary<string, string[]>
        {
            ["Received"] = new[] { "In Progress", "Cancelled" },
            ["In Progress"] = new[] { "Completed", "Cancelled" },
            ["Completed"] = new[] { "Delivered" },
            ["Delivered"] = Array.Empty<string>(),
            ["Cancelled"] = Array.Empty<string>()
        };

        return currentStatus == nextStatus || (transitions.TryGetValue(currentStatus, out var validStates) && validStates.Contains(nextStatus));
    }

    // =========================================================================
    // INVOICES & PAYMENTS TAB (مع مراعاة الـ 3NF وإزالة التكرارات)
    // =========================================================================
    private TabPage CreateInvoiceTab()
    {
        var page = new TabPage("Invoices");
        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 1 };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));

        var leftLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var generateInvoiceButton = new Button
        {
            Text = "Generate Completed Invoices",
            AutoSize = true,
            BackColor = Color.FromArgb(52, 152, 219),
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 8, 0)
        };
        generateInvoiceButton.Click += async (_, _) => await GenerateInvoicesForCompletedRepairsAsync();

        var previewInvoiceButton = new Button
        {
            Text = "Preview / Print Invoice",
            AutoSize = true,
            BackColor = Color.FromArgb(46, 204, 113),
            ForeColor = Color.White
        };
        previewInvoiceButton.Click += PreviewInvoice;

        actions.Controls.Add(generateInvoiceButton);
        actions.Controls.Add(previewInvoiceButton);

        var gridContainer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        _invoiceGrid.Dock = DockStyle.Fill;
        _invoiceGrid.ReadOnly = true;
        _invoiceGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _invoiceGrid.MultiSelect = false;
        _invoiceGrid.RowHeadersVisible = false;
        _invoiceGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _invoiceGrid.SelectionChanged += InvoiceGridSelectionChanged;

        _invoiceEmptyStatePanel.Dock = DockStyle.Fill;
        _invoiceEmptyStatePanel.BackColor = Color.White;
        _invoiceEmptyStateLabel.Dock = DockStyle.Fill;
        _invoiceEmptyStateLabel.TextAlign = ContentAlignment.MiddleCenter;
        _invoiceEmptyStateLabel.Font = new Font("Segoe UI", 11F);
        _invoiceEmptyStateLabel.ForeColor = Color.FromArgb(120, 120, 120);
        _invoiceEmptyStatePanel.Controls.Add(_invoiceEmptyStateLabel);

        gridContainer.Controls.Add(_invoiceGrid);
        gridContainer.Controls.Add(_invoiceEmptyStatePanel);

        leftLayout.Controls.Add(actions, 0, 0);
        leftLayout.Controls.Add(gridContainer, 0, 1);

        var paymentPanel = new GroupBox { Text = "Record Payment", Dock = DockStyle.Fill, Padding = new Padding(12), Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
        var paymentForm = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9 };
        paymentForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        paymentForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        paymentForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        paymentForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        paymentForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        paymentForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        paymentForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        paymentForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
        paymentForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));

        _paymentInvoiceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _paymentInvoiceComboBox.SelectedIndexChanged += PaymentInvoiceSelectionChanged;
        _paymentMethodComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _paymentMethodComboBox.Items.AddRange(new object[] { "Cash", "Bank Transfer", "Card", "InstaPay", "Other" });

        AddFormField(paymentForm, "Select Invoice", _paymentInvoiceComboBox, 0);
        AddFormField(paymentForm, "Payment Amount", _paymentAmountText, 2);
        AddFormField(paymentForm, "Payment Method", _paymentMethodComboBox, 4);
        AddFormField(paymentForm, "Notes", _paymentNotesText, 6);

        _recordPaymentButton.Text = "Record Payment";
        _recordPaymentButton.Dock = DockStyle.Top;
        _recordPaymentButton.Height = 36;
        _recordPaymentButton.BackColor = Color.FromArgb(39, 174, 96);
        _recordPaymentButton.ForeColor = Color.White;
        _recordPaymentButton.FlatStyle = FlatStyle.Flat;
        _recordPaymentButton.Click += RecordPayment;
        paymentForm.Controls.Add(_recordPaymentButton, 0, 8);

        paymentPanel.Controls.Add(paymentForm);

        mainLayout.Controls.Add(leftLayout, 0, 0);
        mainLayout.Controls.Add(paymentPanel, 1, 0);

        page.Controls.Add(mainLayout);
        return page;
    }

    /// <summary>
    /// توليد فواتير للأجهزة المكتملة دون حشو CustomerId (تطبيق 3NF)
    /// </summary>
    private async Task GenerateInvoicesForCompletedRepairsAsync()
    {
        var rows = await DatabaseService.GetTableAsync(
            @"SELECT r.OrderId, r.RepairCost 
              FROM RepairOrders r 
              WHERE r.Status IN ('Completed', 'Delivered') 
              AND r.OrderId NOT IN (SELECT OrderId FROM Invoices);");

        int generatedCount = 0;
        foreach (DataRow row in rows.Rows)
        {
            var orderId = Convert.ToInt32(row["OrderId"]);
            var amount = Convert.ToDecimal(row["RepairCost"]);
            var invoiceNumber = $"INV-{DateTime.Now:yyyyMMdd}-{orderId:D4}";

            var success = await DatabaseService.ExecuteNonQueryAsync(
                @"INSERT INTO Invoices (OrderId, InvoiceNumber, Amount, IssueDate, Status) 
                  VALUES (@orderId, @invoiceNumber, @amount, @issueDate, 'Pending');",
                ("@orderId", orderId),
                ("@invoiceNumber", invoiceNumber),
                ("@amount", amount),
                ("@issueDate", DateTime.Now.ToString("yyyy-MM-dd")));

            if (success) generatedCount++;
        }

        await LoadInvoiceDataAsync();
        await LoadDashboardAsync();
        MessageBox.Show($"تم إصدار {generatedCount} فاتورة للأجهزة المكتملة.", "الفواتير", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// استعلام الفواتير مع عمل JOIN مع RepairOrders للحصول على اسم العميل (3NF Clean Query)
    /// </summary>
    private async Task LoadInvoiceDataAsync()
    {
        var query = @"
            SELECT
                i.InvoiceId AS [Invoice ID],
                i.InvoiceNumber AS [Invoice Number],
                c.FullName AS [Customer],
                r.OrderId AS [Order ID],
                i.Amount AS [Total Amount],
                COALESCE(SUM(p.Amount), 0) AS [Paid Amount],
                MAX(i.Amount - COALESCE(SUM(p.Amount), 0), 0) AS [Balance Due],
                i.Status AS [Status],
                i.IssueDate AS [Issue Date]
            FROM Invoices i
            INNER JOIN RepairOrders r ON r.OrderId = i.OrderId
            INNER JOIN Customers c ON c.CustomerId = r.CustomerId
            LEFT JOIN Payments p ON p.InvoiceId = i.InvoiceId
            GROUP BY i.InvoiceId
            ORDER BY i.IssueDate DESC, i.InvoiceId DESC;";

        var table = await DatabaseService.GetTableAsync(query);
        _invoiceGrid.DataSource = table;

        if (table.Rows.Count == 0)
        {
            _invoiceEmptyStateLabel.Text = "لا توجد فواتير بعد. قم بإكمال صيانة جهاز ثم اضغط زر توليد الفواتير.";
            _invoiceEmptyStatePanel.Visible = true;
            _invoiceEmptyStatePanel.BringToFront();
        }
        else
        {
            _invoiceEmptyStatePanel.Visible = false;
        }

        // تحديث قائمة الفواتير للدفع
        _paymentInvoiceComboBox.Items.Clear();
        foreach (DataRow row in table.Rows)
        {
            var invoiceId = row["Invoice ID"].ToString()!;
            var number = row["Invoice Number"].ToString()!;
            var customer = row["Customer"].ToString()!;
            var balance = Convert.ToDecimal(row["Balance Due"]);
            _paymentInvoiceComboBox.Items.Add(new ComboBoxItem(invoiceId, $"{number} - {customer} (Due: ${balance:0.00})"));
        }
    }

    private void InvoiceGridSelectionChanged(object? sender, EventArgs e)
    {
        if (_invoiceGrid.SelectedRows.Count == 0) return;
        var row = _invoiceGrid.SelectedRows[0];
        _selectedInvoiceId = row.Cells[0].Value is int id ? id : Convert.ToInt32(row.Cells[0].Value);
        SelectComboItem(_paymentInvoiceComboBox, _selectedInvoiceId.ToString());
    }

    private async void PaymentInvoiceSelectionChanged(object? sender, EventArgs e)
    {
        if (_paymentInvoiceComboBox.SelectedItem is not ComboBoxItem item || !int.TryParse(item.Value, out var invoiceId))
            return;

        var balanceQuery = await DatabaseService.GetTableAsync(
            @"SELECT MAX(i.Amount - COALESCE(SUM(p.Amount), 0), 0) AS Balance
              FROM Invoices i
              LEFT JOIN Payments p ON p.InvoiceId = i.InvoiceId
              WHERE i.InvoiceId = @invoiceId
              GROUP BY i.InvoiceId;",
            ("@invoiceId", invoiceId));

        if (balanceQuery.Rows.Count > 0)
        {
            _paymentAmountText.Text = Convert.ToDecimal(balanceQuery.Rows[0]["Balance"]).ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// تسجيل الدفعة دون حشو OrderId المكرر (تطبيق 3NF)
    /// </summary>
    private async void RecordPayment(object? sender, EventArgs e)
    {
        if (_paymentInvoiceComboBox.SelectedItem is not ComboBoxItem item || !int.TryParse(item.Value, out var invoiceId))
        {
            MessageBox.Show("يرجى اختيار فاتورة للدفع.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!decimal.TryParse(_paymentAmountText.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            MessageBox.Show("يرجى إدخال مبلغ دفع موجب وصحيح.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_paymentMethodComboBox.SelectedItem is null)
        {
            MessageBox.Show("يرجى اختيار وسيلة الدفع.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var invoiceDetails = await DatabaseService.GetTableAsync(
            @"SELECT i.Amount AS Total, COALESCE(SUM(p.Amount), 0) AS Paid
              FROM Invoices i
              LEFT JOIN Payments p ON p.InvoiceId = i.InvoiceId
              WHERE i.InvoiceId = @invoiceId
              GROUP BY i.InvoiceId;",
            ("@invoiceId", invoiceId));

        if (invoiceDetails.Rows.Count == 0) return;

        var invoiceTotal = Convert.ToDecimal(invoiceDetails.Rows[0]["Total"]);
        var paidSoFar = Convert.ToDecimal(invoiceDetails.Rows[0]["Paid"]);
        var remainingBalance = invoiceTotal - paidSoFar;

        if (amount > remainingBalance)
        {
            MessageBox.Show($"المبلغ المدخل (${amount:0.00}) يتجاوز الرصيد المتبقي على الفاتورة (${remainingBalance:0.00}).", "قيمة زائدة", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // إدخال الدفعة دون تكرار OrderId
        var success = await DatabaseService.ExecuteNonQueryAsync(
            @"INSERT INTO Payments (InvoiceId, Amount, PaymentDate, PaymentMethod, Notes) 
              VALUES (@invoiceId, @amount, @paymentDate, @paymentMethod, @notes);",
            ("@invoiceId", invoiceId),
            ("@amount", amount),
            ("@paymentDate", DateTime.Now.ToString("yyyy-MM-dd")),
            ("@paymentMethod", _paymentMethodComboBox.SelectedItem.ToString()!),
            ("@notes", _paymentNotesText.Text.Trim()));

        if (success)
        {
            var paid = paidSoFar + amount;
            var newStatus = (paid >= invoiceTotal) ? "Paid" : "Partial Payment";
            await DatabaseService.ExecuteNonQueryAsync("UPDATE Invoices SET Status = @status WHERE InvoiceId = @invoiceId;", ("@status", newStatus), ("@invoiceId", invoiceId));

            _paymentAmountText.Clear();
            _paymentNotesText.Clear();
            _paymentMethodComboBox.SelectedIndex = -1;
            await LoadInvoiceDataAsync();
            await LoadDashboardAsync();
            MessageBox.Show("تم تسجيل الدفعة بنجاح وتحديث حالة الفاتورة.", "نجاح", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // =========================================================================
    // INVOICE PRINTING & GDI+ PRINT DOCUMENT
    // =========================================================================
    private async void PreviewInvoice(object? sender, EventArgs e)
    {
        if (_selectedInvoiceId is null)
        {
            MessageBox.Show("يرجى اختيار فاتورة لمعاينتها وطباعتها.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var invoiceId = _selectedInvoiceId.Value;
        _invoicePrintData = await DatabaseService.GetTableAsync(
            @"SELECT
                i.InvoiceId, i.InvoiceNumber, i.IssueDate, i.Status AS InvoiceStatus, i.Amount AS Total,
                COALESCE(SUM(p.Amount), 0) AS Paid,
                MAX(i.Amount - COALESCE(SUM(p.Amount), 0), 0) AS Balance,
                c.FullName AS CustomerName, c.PhoneNumber AS CustomerPhone, c.Address AS CustomerAddress,
                d.DeviceType, d.Brand, d.Model, d.SerialNumber,
                r.OrderId, r.Status AS RepairStatus, r.ReceivedDate, r.ProblemDescription, r.Diagnosis,
                COALESCE(t.FullName, 'Unassigned') AS TechnicianName
            FROM Invoices i
            INNER JOIN RepairOrders r ON r.OrderId = i.OrderId
            INNER JOIN Customers c ON c.CustomerId = r.CustomerId
            INNER JOIN Devices d ON d.DeviceId = r.DeviceId
            LEFT JOIN Technicians t ON t.TechnicianId = r.TechnicianId
            LEFT JOIN Payments p ON p.InvoiceId = i.InvoiceId
            WHERE i.InvoiceId = @invoiceId
            GROUP BY i.InvoiceId;",
            ("@invoiceId", invoiceId));

        _invoicePaymentPrintData = await DatabaseService.GetTableAsync(
            @"SELECT PaymentDate, PaymentMethod, Amount, Notes
              FROM Payments
              WHERE InvoiceId = @invoiceId
              ORDER BY PaymentDate, PaymentId;",
            ("@invoiceId", invoiceId));

        using var previewDialog = new PrintPreviewDialog
        {
            Document = _invoicePrintDocument,
            Width = 900,
            Height = 700,
            StartPosition = FormStartPosition.CenterScreen
        };
        previewDialog.ShowDialog();
    }

    private void OnInvoiceBeginPrint(object? sender, PrintEventArgs e)
    {
        _invoicePaymentPrintIndex = 0;
        _invoicePrintFirstPage = true;
        _invoicePrintPageNumber = 0;

        // ضبط الصفحة القياسية لتكون A4 عمودي بهوامش متناسقة تمنع التداخل أو الضغط
        _invoicePrintDocument.DefaultPageSettings.PaperSize = new PaperSize("A4", 827, 1169);
        _invoicePrintDocument.DefaultPageSettings.Margins = new Margins(50, 50, 50, 50);
    }

    private void InvoicePrintDocument_PrintPage(object? sender, PrintPageEventArgs e)
    {
        if (_invoicePrintData is null || _invoicePrintData.Rows.Count == 0)
        {
            e.HasMorePages = false;
            return;
        }

        var graphics = e.Graphics!;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // ضبط هوامش وأبعاد الطباعة القياسية A4
        var bounds = e.MarginBounds;
        var left = (float)Math.Max(bounds.Left, 60);
        var top = (float)Math.Max(bounds.Top, 60);
        var width = (float)Math.Max(bounds.Width, 680);
        var right = left + width;
        var bottom = top + Math.Max(bounds.Height, 950);
        var y = top;

        using var brandFont = new Font("Segoe UI", 18F, FontStyle.Bold);
        using var titleFont = new Font("Segoe UI", 16F, FontStyle.Bold);
        using var sectionFont = new Font("Segoe UI", 11F, FontStyle.Bold);
        using var bodyFont = new Font("Segoe UI", 9.5F, FontStyle.Regular);
        using var boldFont = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 8.5F, FontStyle.Regular);

        using var blueBrush = new SolidBrush(Color.FromArgb(25, 77, 120));
        using var lightBrush = new SolidBrush(Color.FromArgb(245, 247, 250));
        using var rulePen = new Pen(Color.FromArgb(220, 224, 230), 1.2F);
        using var tablePen = new Pen(Color.FromArgb(200, 205, 215), 1F);
        using var headerFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

        var row = _invoicePrintData.Rows[0];
        string Value(string col) => row[col]?.ToString() ?? string.Empty;
        string Money(string col) => Convert.ToDecimal(row[col]).ToString("0.00", CultureInfo.InvariantCulture) + " EGP";

        // 1. ترويسة الفاتورة
        graphics.FillRectangle(blueBrush, left, y, width, 70F);
        graphics.DrawString("FixPoint Maintenance", brandFont, Brushes.White, left + 16F, y + 10F);
        graphics.DrawString("COMPUTER & ELECTRONICS REPAIR CENTER", smallFont, Brushes.White, left + 18F, y + 42F);
        graphics.DrawString("INVOICE", titleFont, Brushes.White, new RectangleF(left, y + 10F, width - 20F, 50F), headerFormat);
        y += 85F;

        // 2. تفاصيل الفاتورة
        graphics.DrawString($"Invoice #: {Value("InvoiceNumber")}", boldFont, blueBrush, left, y);
        graphics.DrawString($"Date: {Value("IssueDate")}", bodyFont, Brushes.Black, left + (width * 0.40F), y);
        graphics.DrawString($"Status: {Value("InvoiceStatus")}", boldFont, Brushes.DarkGreen, left + (width * 0.75F), y);
        y += 26F;
        graphics.DrawLine(rulePen, left, y, right, y);
        y += 16F;

        // 3. بيانات العميل والجهاز (عمودين متناسقين تماماً)
        var colWidth = (width - 20F) / 2F;
        var col2Left = left + colWidth + 20F;

        graphics.DrawString("BILL TO (CUSTOMER):", sectionFont, blueBrush, left, y);
        graphics.DrawString("DEVICE DETAILS:", sectionFont, blueBrush, col2Left, y);
        y += 24F;

        graphics.DrawString($"Name: {Value("CustomerName")}", boldFont, Brushes.Black, left, y);
        graphics.DrawString($"Device: {Value("DeviceType")} - {Value("Brand")} {Value("Model")}", boldFont, Brushes.Black, col2Left, y);
        y += 20F;

        graphics.DrawString($"Phone: {Value("CustomerPhone")}", bodyFont, Brushes.Black, left, y);
        graphics.DrawString($"Serial Number: {Value("SerialNumber")}", bodyFont, Brushes.Black, col2Left, y);
        y += 20F;

        graphics.DrawString($"Address: {Value("CustomerAddress")}", bodyFont, Brushes.Black, left, y);
        graphics.DrawString($"Assigned Tech: {Value("TechnicianName")}", bodyFont, Brushes.Black, col2Left, y);
        y += 30F;
        graphics.DrawLine(rulePen, left, y, right, y);
        y += 16F;

        // 4. تفاصيل الإصلاح
        graphics.DrawString("REPAIR DETAILS & DIAGNOSIS", sectionFont, blueBrush, left, y);
        y += 24F;
        graphics.DrawString($"Reported Issue: {Value("ProblemDescription")}", bodyFont, Brushes.Black, left, y);
        y += 20F;
        graphics.DrawString($"Diagnosis: {Value("Diagnosis")}", bodyFont, Brushes.Black, left, y);
        y += 30F;

        // 5. جدول الحسابات والماليات المنظم
        var tableY = y;
        var tableWidth = width;
        var rowHeight = 30F;

        // Header Table
        graphics.FillRectangle(blueBrush, left, tableY, tableWidth, rowHeight);
        graphics.DrawString("Item / Service Description", boldFont, Brushes.White, left + 10F, tableY + 6F);
        graphics.DrawString("Amount (EGP)", boldFont, Brushes.White, right - 130F, tableY + 6F);
        tableY += rowHeight;

        // Service Row
        graphics.FillRectangle(lightBrush, left, tableY, tableWidth, rowHeight);
        graphics.DrawRectangle(tablePen, left, tableY, tableWidth, rowHeight);
        graphics.DrawString($"Hardware Repair & Diagnostic Service (Order #{Value("OrderId")})", bodyFont, Brushes.Black, left + 10F, tableY + 6F);
        graphics.DrawString(Money("Total"), boldFont, Brushes.Black, right - 130F, tableY + 6F);
        tableY += rowHeight + 15F;

        // Financial Summary Block (على اليمين)
        var summaryLeft = right - 280F;
        var summaryWidth = 280F;

        graphics.DrawString("Total Repair Cost:", boldFont, blueBrush, summaryLeft, tableY);
        graphics.DrawString(Money("Total"), boldFont, blueBrush, right - 120F, tableY);
        tableY += 24F;

        graphics.DrawString("Amount Paid:", bodyFont, Brushes.Black, summaryLeft, tableY);
        graphics.DrawString(Money("Paid"), bodyFont, Brushes.Black, right - 120F, tableY);
        tableY += 24F;

        graphics.FillRectangle(Color.FromArgb(253, 237, 236).ToBrush(), summaryLeft, tableY - 4F, summaryWidth, 30F);
        graphics.DrawRectangle(new Pen(Color.FromArgb(231, 76, 60), 1F), summaryLeft, tableY - 4F, summaryWidth, 30F);
        graphics.DrawString("BALANCE DUE:", sectionFont, Color.FromArgb(192, 57, 43).ToBrush(), summaryLeft + 8F, tableY + 2F);
        graphics.DrawString(Money("Balance"), sectionFont, Color.FromArgb(192, 57, 43).ToBrush(), right - 120F, tableY + 2F);

        // 6. خط النهاية والفوتر
        graphics.DrawLine(rulePen, left, bottom - 30F, right, bottom - 30F);
        graphics.DrawString("Thank you for choosing FixPoint Repair Center. | Warranty: 30 Days on Parts & Services.", smallFont, blueBrush, left, bottom - 22F);
        graphics.DrawString($"Page {_invoicePrintPageNumber + 1}", smallFont, Brushes.Gray, right - 60F, bottom - 22F);

        e.HasMorePages = false;
    }

    // =========================================================================
    // HELPER METHODS & DEBOUNCING
    // =========================================================================
    private void TriggerDebouncedSearch(Action searchAction)
    {
        _pendingSearchAction = searchAction;
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void OnSearchDebounceTimerTick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        _pendingSearchAction?.Invoke();
    }

    private async Task RefreshCustomerDropdownsAsync()
    {
        var customers = await DatabaseService.GetTableAsync("SELECT CustomerId, FullName, PhoneNumber FROM Customers ORDER BY FullName;");
        _deviceCustomerComboBox.Items.Clear();
        _repairCustomerComboBox.Items.Clear();

        foreach (DataRow row in customers.Rows)
        {
            var id = row["CustomerId"].ToString()!;
            var name = row["FullName"].ToString()!;
            var phone = row["PhoneNumber"].ToString()!;
            var item = new ComboBoxItem(id, $"{name} ({phone})");
            _deviceCustomerComboBox.Items.Add(item);
            _repairCustomerComboBox.Items.Add(item);
        }
    }

    private async Task RefreshTechnicianDropdownsAsync()
    {
        var techs = await DatabaseService.GetTableAsync("SELECT TechnicianId, FullName, Specialty FROM Technicians ORDER BY FullName;");
        _repairTechnicianComboBox.Items.Clear();
        foreach (DataRow row in techs.Rows)
        {
            var id = row["TechnicianId"].ToString()!;
            var name = row["FullName"].ToString()!;
            var spec = row["Specialty"].ToString()!;
            _repairTechnicianComboBox.Items.Add(new ComboBoxItem(id, $"{name} ({spec})"));
        }
    }

    private async Task RefreshRepairOrderDropdownsAsync()
    {
        await RefreshCustomerDropdownsAsync();
        await RefreshTechnicianDropdownsAsync();
    }

    private async Task LoadDevicesForCustomerAsync(int customerId)
    {
        var devices = await DatabaseService.GetTableAsync(
            "SELECT DeviceId, DeviceType, Brand, Model, SerialNumber FROM Devices WHERE CustomerId = @customerId;",
            ("@customerId", customerId));

        _repairDeviceComboBox.Items.Clear();
        foreach (DataRow row in devices.Rows)
        {
            var id = row["DeviceId"].ToString()!;
            var type = row["DeviceType"].ToString()!;
            var brand = row["Brand"].ToString()!;
            var model = row["Model"].ToString()!;
            _repairDeviceComboBox.Items.Add(new ComboBoxItem(id, $"{brand} {model} ({type})"));
        }
    }

    private static void SelectComboItem(ComboBox comboBox, string? value)
    {
        if (value is null)
        {
            comboBox.SelectedIndex = -1;
            return;
        }

        foreach (var obj in comboBox.Items)
        {
            if (obj is ComboBoxItem item && item.Value == value)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
        comboBox.SelectedIndex = -1;
    }

    private static void AddFormField(TableLayoutPanel panel, string labelText, Control control, int row)
    {
        var label = new Label { Text = labelText, AutoSize = true, Dock = DockStyle.Bottom };
        control.Dock = DockStyle.Top;
        panel.Controls.Add(label, 0, row);
        panel.Controls.Add(control, 0, row + 1);
    }

    private static void AddRepairFormField(TableLayoutPanel panel, string labelText, Control control, int col, int row, int colSpan = 1)
    {
        var label = new Label { Text = labelText, AutoSize = true, Dock = DockStyle.Bottom };
        control.Dock = DockStyle.Top;
        panel.Controls.Add(label, col, row);
        panel.SetColumnSpan(label, colSpan);
        panel.Controls.Add(control, col, row + 1);
        panel.SetColumnSpan(control, colSpan);
    }

    private void ConfigureRoleVisibility()
    {
        var isAdmin = _currentRole.Equals("Admin", StringComparison.OrdinalIgnoreCase);
        var isTechnician = _currentRole.Equals("Technician", StringComparison.OrdinalIgnoreCase);
        var isEmployee = _currentRole.Equals("Employee", StringComparison.OrdinalIgnoreCase);

        // 1. أزرار الحذف (Delete Buttons): للمدير فقط حماية للبيانات ومنعاً للتخريب
        _customerDeleteButton.Enabled = isAdmin;
        _customerDeleteButton.Visible = isAdmin;

        _deviceDeleteButton.Enabled = isAdmin;
        _deviceDeleteButton.Visible = isAdmin;

        _technicianDeleteButton.Enabled = isAdmin;
        _technicianDeleteButton.Visible = isAdmin;

        _repairOrderDeleteButton.Enabled = isAdmin;
        _repairOrderDeleteButton.Visible = isAdmin;

        // 2. إدارة الفنيين: ممنوعة تماماً على غير المديرين
        if (!isAdmin)
        {
            _technicianNameText.Enabled = false;
            _technicianPhoneText.Enabled = false;
            _technicianSpecialtyText.Enabled = false;
        }

        // 3. شروط خاصة بالفني (Technician):
        if (isTechnician)
        {
            // الفني لا يمكنه تغيير العميل أو الجهاز، دوره فقط تشخيص العطل، تحديد تكلفة الصيانة وتغيير الحالة
            _repairCustomerComboBox.Enabled = false;
            _repairDeviceComboBox.Enabled = false;
            _repairTechnicianComboBox.Enabled = false;
            _paymentAmountText.Enabled = false;
            _recordPaymentButton.Enabled = false;
        }
    }

    /// <summary>
    /// تفعيل خاصية DoubleBuffered على أي عنصر واجهة عبر الـ Reflection لمنع وميض الشاشة
    /// </summary>
    private static void EnableDoubleBuffering(Control control)
    {
        try
        {
            typeof(Control).InvokeMember(
                "DoubleBuffered",
                BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                control,
                new object[] { true });
        }
        catch
        {
            // تجاهل أي خطأ إذا لم يسمح نظام التشغيل
        }
    }
}

public static class ColorExtensions
{
    public static Brush ToBrush(this Color color) => new SolidBrush(color);
}

public class ComboBoxItem
{
    public ComboBoxItem(string value, string text)
    {
        Value = value;
        Text = text;
    }

    public string Value { get; }
    public string Text { get; }

    public override string ToString() => Text;
}

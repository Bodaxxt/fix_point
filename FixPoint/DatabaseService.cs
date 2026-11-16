using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;

namespace FixPoint;

/// <summary>
/// فئة خدمة قاعدة البيانات (Database Service Layer)
/// تم إعادة بنائها وفقاً لأعلى معايير هندسة البرمجيات ومبادئ ACID
/// تدعم المعاملات (Transactions)، القيود الصارمة (Constraints)، الفهارس (Indexes)، والعمليات غير المتزامنة (Async/Await)
/// </summary>
public static class DatabaseService
{
    public static string DatabasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FixPoint", "fixpoint.db");

    public static string ConnectionString => $"Data Source={DatabasePath}";

    /// <summary>
    /// إنشاء وفتح اتصال SQLite مع تفعيل فحص المفاتيح الخارجية ونمط WAL إجبارياً
    /// </summary>
    public static SqliteConnection CreateOpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var pragmaCmd = connection.CreateCommand();
        // تفعيل المفاتيح الأجنبية ونمط كتابة الـ WAL لتحسين الأداء ومنع قفل قاعدة البيانات
        pragmaCmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
        pragmaCmd.ExecuteNonQuery();

        return connection;
    }

    /// <summary>
    /// فتح اتصال غير متزامن لتفادي تجميد خيط واجهة المستخدم (UI Thread)
    /// </summary>
    public static async Task<SqliteConnection> CreateOpenConnectionAsync()
    {
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
        await pragmaCmd.ExecuteNonQueryAsync();

        return connection;
    }

    /// <summary>
    /// تهيئة الجداول والـ Schema الكاملة داخل Transaction واحدة لحماية سلامة البيانات ومنع استهلاك القرص
    /// </summary>
    public static void Initialize()
    {
        try
        {
            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();

            var schemaStatements = new[]
            {
                // 1. جدول المستخدمين والصلاحيات
                @"CREATE TABLE IF NOT EXISTS Users (
                    UserId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL UNIQUE,
                    PasswordHash TEXT NOT NULL,
                    Role TEXT NOT NULL DEFAULT 'Employee',
                    FullName TEXT NOT NULL
                );",

                // 2. جدول العملاء (رقم الهاتف فريد لمنع تكرار نفس العميل)
                @"CREATE TABLE IF NOT EXISTS Customers (
                    CustomerId INTEGER PRIMARY KEY AUTOINCREMENT,
                    FullName TEXT NOT NULL,
                    PhoneNumber TEXT NOT NULL UNIQUE,
                    Address TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
                );",

                // 3. جدول الأجهزة (السيريال نمبر فريد عالمياً لمنع التضارب)
                @"CREATE TABLE IF NOT EXISTS Devices (
                    DeviceId INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerId INTEGER NOT NULL,
                    DeviceType TEXT NOT NULL,
                    Brand TEXT NOT NULL,
                    Model TEXT NOT NULL,
                    SerialNumber TEXT NOT NULL UNIQUE,
                    CreatedAt TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                    FOREIGN KEY(CustomerId) REFERENCES Customers(CustomerId) ON DELETE RESTRICT
                );",

                // 4. جدول الفنيين (رقم تليفون الفني فريد)
                @"CREATE TABLE IF NOT EXISTS Technicians (
                    TechnicianId INTEGER PRIMARY KEY AUTOINCREMENT,
                    FullName TEXT NOT NULL,
                    Phone TEXT NOT NULL UNIQUE,
                    Specialty TEXT NOT NULL
                );",

                // 5. جدول أوامر الصيانة (حصر الحالات ومنع التكلفة السالبة)
                @"CREATE TABLE IF NOT EXISTS RepairOrders (
                    OrderId INTEGER PRIMARY KEY AUTOINCREMENT,
                    CustomerId INTEGER NOT NULL,
                    DeviceId INTEGER NOT NULL,
                    TechnicianId INTEGER,
                    ProblemDescription TEXT NOT NULL,
                    Diagnosis TEXT,
                    Status TEXT NOT NULL DEFAULT 'Received'
                        CHECK (Status IN ('Received', 'In Progress', 'Completed', 'Delivered', 'Cancelled')),
                    ReceivedDate TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                    RepairCost REAL NOT NULL DEFAULT 0.0 CHECK (RepairCost >= 0),
                    InvoiceNumber TEXT UNIQUE,
                    FOREIGN KEY(CustomerId) REFERENCES Customers(CustomerId) ON DELETE RESTRICT,
                    FOREIGN KEY(DeviceId) REFERENCES Devices(DeviceId) ON DELETE RESTRICT,
                    FOREIGN KEY(TechnicianId) REFERENCES Technicians(TechnicianId) ON DELETE SET NULL
                );",

                // 6. جدول الفواتير (تطبيق 3NF بحذف تكرار CustomerId لأنها مستنتجة من أمر الصيانة)
                @"CREATE TABLE IF NOT EXISTS Invoices (
                    InvoiceId INTEGER PRIMARY KEY AUTOINCREMENT,
                    OrderId INTEGER NOT NULL UNIQUE,
                    InvoiceNumber TEXT NOT NULL UNIQUE,
                    Amount REAL NOT NULL CHECK (Amount >= 0),
                    IssueDate TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                    Status TEXT NOT NULL DEFAULT 'Pending'
                        CHECK (Status IN ('Pending', 'Partial Payment', 'Paid', 'Cancelled')),
                    FOREIGN KEY(OrderId) REFERENCES RepairOrders(OrderId) ON DELETE RESTRICT
                );",

                // 7. جدول الدفعات (تطبيق 3NF بحذف OrderId لأن الفاتورة مرتبطة به بالفعل)
                @"CREATE TABLE IF NOT EXISTS Payments (
                    PaymentId INTEGER PRIMARY KEY AUTOINCREMENT,
                    InvoiceId INTEGER NOT NULL,
                    Amount REAL NOT NULL CHECK (Amount > 0),
                    PaymentDate TEXT NOT NULL DEFAULT (datetime('now', 'localtime')),
                    PaymentMethod TEXT NOT NULL CHECK (PaymentMethod IN ('Cash', 'Card', 'Bank Transfer', 'InstaPay', 'Other')),
                    Notes TEXT,
                    FOREIGN KEY(InvoiceId) REFERENCES Invoices(InvoiceId) ON DELETE RESTRICT
                );",

                // 8. فهارس الأداء العالي (B-Tree Indexes) لتسريع عمليات الـ JOIN والبحث
                @"CREATE INDEX IF NOT EXISTS idx_customers_phone ON Customers(PhoneNumber);",
                @"CREATE INDEX IF NOT EXISTS idx_devices_customer ON Devices(CustomerId);",
                @"CREATE INDEX IF NOT EXISTS idx_devices_serial ON Devices(SerialNumber);",
                @"CREATE INDEX IF NOT EXISTS idx_repairs_status ON RepairOrders(Status);",
                @"CREATE INDEX IF NOT EXISTS idx_repairs_customer ON RepairOrders(CustomerId);",
                @"CREATE INDEX IF NOT EXISTS idx_repairs_device ON RepairOrders(DeviceId);",
                @"CREATE INDEX IF NOT EXISTS idx_payments_invoice ON Payments(InvoiceId);"
            };

            foreach (var statement in schemaStatements)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }

            transaction.Commit();

            EnsureDefaultAdmin();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ أثناء تهيئة قاعدة البيانات:\n{ex.Message}", "خطأ في النظام", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static void EnsureDefaultAdmin()
    {
        try
        {
            using var connection = CreateOpenConnection();
            using var countCommand = connection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(*) FROM Users;";
            var userCount = Convert.ToInt32(countCommand.ExecuteScalar());

            if (userCount > 0)
            {
                return;
            }

            // إضافة حسابات افتراضية متعددة لاختبار تعدد الأدوار الثلاثة (Admin vs Employee vs Technician)
            var defaultUsers = new[]
            {
                ("admin", "Admin@123", "Admin", "System Administrator (Manager)"),
                ("employee", "Emp@123", "Employee", "Front-Desk Receptionist"),
                ("tech", "Tech@123", "Technician", "Eng. Mostafa (Technician)")
            };

            foreach (var (user, pass, role, name) in defaultUsers)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.CommandText = "INSERT INTO Users (Username, PasswordHash, Role, FullName) VALUES (@username, @passwordHash, @role, @fullName);";
                insertCommand.Parameters.AddWithValue("@username", user);
                insertCommand.Parameters.AddWithValue("@passwordHash", HashPassword(pass));
                insertCommand.Parameters.AddWithValue("@role", role);
                insertCommand.Parameters.AddWithValue("@fullName", name);
                insertCommand.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ في إنشاء الحسابات الافتراضية:\n{ex.Message}", "تحذير", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public static bool ValidateLogin(string username, string password, out string role, out string fullName)
    {
        role = string.Empty;
        fullName = string.Empty;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Role, FullName FROM Users WHERE Username = @username AND PasswordHash = @passwordHash;";
            command.Parameters.AddWithValue("@username", username.Trim());
            command.Parameters.AddWithValue("@passwordHash", HashPassword(password));

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                role = reader.GetString(0);
                fullName = reader.GetString(1);
                return true;
            }
        }
        catch (SqliteException ex)
        {
            MessageBox.Show($"فشل الاتصال بقاعدة البيانات أثناء تسجيل الدخول:\n{ex.Message}", "خطأ تسجيل الدخول", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        return false;
    }

    /// <summary>
    /// جلب جدول بيانات متزامن (مع معالجة الأخطاء والـ Parameters)
    /// </summary>
    public static DataTable GetTable(string query, params (string Name, object Value)[] parameters)
    {
        var table = new DataTable();
        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = query;
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
            }

            using var reader = command.ExecuteReader();
            table.Load(reader);
        }
        catch (SqliteException ex)
        {
            HandleSqliteException(ex);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ عام أثناء استرجاع البيانات:\n{ex.Message}", "خطأ في النظام", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        return table;
    }

    /// <summary>
    /// جلب جدول بيانات غير متزامن (Async) لضمان بقاء الواجهة سريعة وغير متجمدة
    /// </summary>
    public static async Task<DataTable> GetTableAsync(string query, params (string Name, object Value)[] parameters)
    {
        return await Task.Run(() =>
        {
            var table = new DataTable();
            try
            {
                using var connection = CreateOpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = query;
                foreach (var parameter in parameters)
                {
                    command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
                }

                using var reader = command.ExecuteReader();
                table.Load(reader);
            }
            catch (SqliteException ex)
            {
                HandleSqliteException(ex);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ عام أثناء استرجاع البيانات:\n{ex.Message}", "خطأ في النظام", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return table;
        });
    }

    /// <summary>
    /// تنفيذ استعلام تعديل/إضافة/حذف متزامن مع اصطياد انتهاكات القيود (Constraints)
    /// </summary>
    public static bool ExecuteNonQuery(string query, params (string Name, object Value)[] parameters)
    {
        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = query;

            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
            }

            command.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException ex)
        {
            HandleSqliteException(ex);
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"خطأ غير متوقع أثناء حفظ البيانات:\n{ex.Message}", "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>
    /// تنفيذ استعلام تعديل/إضافة/حذف غير متزامن (Async)
    /// </summary>
    public static async Task<bool> ExecuteNonQueryAsync(string query, params (string Name, object Value)[] parameters)
    {
        return await Task.Run(() => ExecuteNonQuery(query, parameters));
    }

    /// <summary>
    /// معالجة مخصصة لأخطاء SQLite وتحويلها إلى رسائل مفهومة للمستخدم (خاصة رمز الخطأ 19 للقيود)
    /// </summary>
    private static void HandleSqliteException(SqliteException ex)
    {
        if (ex.SqliteErrorCode == 19) // SQLite Constraint Error
        {
            var message = ex.Message;
            if (message.Contains("Customers.PhoneNumber"))
            {
                MessageBox.Show("رقم الهاتف هذا مسجل بالفعل لعميل آخر! لا يمكن تكرار نفس رقم الهاتف.", "تنبيه: تكرار بيانات", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (message.Contains("Devices.SerialNumber"))
            {
                MessageBox.Show("الرقم التسلسلي (Serial Number) مسجل بالفعل لجهاز آخر في النظام!", "تنبيه: تكرار جهاز", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (message.Contains("Technicians.Phone"))
            {
                MessageBox.Show("رقم هاتف الفني مسجل بالفعل لفني آخر!", "تنبيه: تكرار فني", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (message.Contains("FOREIGN KEY"))
            {
                MessageBox.Show("لا يمكن إتمام هذه العملية لأن هناك بيانات أخرى مرتبطة بها أو أن العنصر المشار إليه غير موجود!", "خطأ في الارتباط", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (message.Contains("CHECK"))
            {
                MessageBox.Show("القيمة المدخلة تنتهك قواعد الصحة (مثال: مبلغ مالي سالب أو حالة صيانة غير صحيحة)!", "خطأ في صحة البيانات", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show($"تم رفض العملية بسبب انتهاك قيود صحة البيانات:\n{ex.Message}", "تنبيه قيود قاعدة البيانات", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show($"خطأ في قاعدة البيانات:\n{ex.Message}", "خطأ قاعدة بيانات", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

using System.Drawing;
using System.Windows.Forms;

namespace FixPoint;

public class LoginForm : Form
{
    private readonly TextBox _txtUsername = new();
    private readonly TextBox _txtPassword = new();
    private readonly CheckBox _chkShowPassword = new();
    private readonly Button _btnLogin = new();
    private readonly Label _lblStatus = new();

    public LoginForm()
    {
        InitializeComponent();
        DatabaseService.Initialize();
    }

    private void InitializeComponent()
    {
        Text = "FixPoint - Login";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(430, 420);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(30),
            BackColor = Color.FromArgb(245, 247, 250)
        };

        var titleLabel = new Label
        {
            Text = "FixPoint",
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            ForeColor = Color.FromArgb(25, 77, 120),
            AutoSize = true,
            Location = new Point(0, 10)
        };

        var subtitleLabel = new Label
        {
            Text = "Computer Maintenance Center",
            Font = new Font("Segoe UI", 11F),
            ForeColor = Color.FromArgb(85, 85, 85),
            AutoSize = true,
            Location = new Point(0, 55)
        };

        var usernameLabel = new Label
        {
            Text = "Username",
            AutoSize = true,
            Location = new Point(0, 95)
        };

        _txtUsername.Location = new Point(0, 120);
        _txtUsername.Size = new Size(350, 30);
        _txtUsername.Font = new Font("Segoe UI", 11F);

        var passwordLabel = new Label
        {
            Text = "Password",
            AutoSize = true,
            Location = new Point(0, 165)
        };

        _txtPassword.Location = new Point(0, 190);
        _txtPassword.Size = new Size(350, 30);
        _txtPassword.Font = new Font("Segoe UI", 11F);
        _txtPassword.UseSystemPasswordChar = true;

        _chkShowPassword.Text = "Show Password";
        _chkShowPassword.AutoSize = true;
        _chkShowPassword.Location = new Point(0, 230);
        _chkShowPassword.CheckedChanged += (_, _) =>
        {
            _txtPassword.UseSystemPasswordChar = !_chkShowPassword.Checked;
        };

        _btnLogin.Text = "Login";
        _btnLogin.BackColor = Color.FromArgb(25, 77, 120);
        _btnLogin.ForeColor = Color.White;
        _btnLogin.FlatStyle = FlatStyle.Flat;
        _btnLogin.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        _btnLogin.Size = new Size(350, 40);
        _btnLogin.Location = new Point(0, 270);
        _btnLogin.Click += LoginButton_Click;

        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = Color.DarkRed;
        _lblStatus.Location = new Point(0, 325);
        _lblStatus.Text = string.Empty;

        panel.Controls.Add(titleLabel);
        panel.Controls.Add(subtitleLabel);
        panel.Controls.Add(usernameLabel);
        panel.Controls.Add(_txtUsername);
        panel.Controls.Add(passwordLabel);
        panel.Controls.Add(_txtPassword);
        panel.Controls.Add(_chkShowPassword);
        panel.Controls.Add(_btnLogin);
        panel.Controls.Add(_lblStatus);
        Controls.Add(panel);

        AcceptButton = _btnLogin;

        var btnCancel = new Button { Visible = false };
        btnCancel.Click += (_, _) => Application.Exit();
        panel.Controls.Add(btnCancel);
        CancelButton = btnCancel;

        _txtUsername.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _txtUsername.AutoCompleteSource = AutoCompleteSource.CustomSource;
        var userSources = new AutoCompleteStringCollection();
        userSources.AddRange(new[] { "admin", "employee", "tech" });
        _txtUsername.AutoCompleteCustomSource = userSources;
    }

    private void LoginButton_Click(object? sender, EventArgs e)
    {
        var username = _txtUsername.Text.Trim();
        var password = _txtPassword.Text;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            _lblStatus.Text = "Username and password are required.";
            return;
        }

        if (DatabaseService.ValidateLogin(username, password, out var role, out var fullName))
        {
            var mainForm = new MainForm(role, fullName);
            mainForm.Show();
            Hide();
            mainForm.FormClosed += (_, _) => Application.Exit();
            return;
        }

        _lblStatus.Text = "Invalid username or password.";
    }
}
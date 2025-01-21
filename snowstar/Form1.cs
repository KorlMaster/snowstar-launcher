using System;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

// LAUNCHER BY KARLMASTER

namespace snowstar
{
    public partial class Form1 : Form
    {
        bool launch_ok = false;

        public Form1()
        {
            InitializeComponent();
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            // Load settings from config
            string kofiValue = ConfigurationManager.AppSettings["kofi"];
            string kofiLink = ConfigurationManager.AppSettings["kofi_link"];

            if (kofiValue == "1")
            {
                pictureBox2.Visible = true;
            }
            else
            {
                pictureBox2.Visible = false;
            }

            button_login.Enabled = false;
            textBox_USERNAME.Focus();
            this.ActiveControl = textBox_USERNAME;

            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.WindowState = FormWindowState.Normal;

            textBox_PASSWORD.UseSystemPasswordChar = true;
            this.KeyPreview = true;
            this.KeyDown += new KeyEventHandler(Form1_KeyDown);

            ServerStatusLabel.Text = string.Empty;
            CheckingStatusLabel.Text = string.Empty;

            string serverAddress = LoadServerAddress("server.cfg");
            int port = 10002;

            if (string.IsNullOrEmpty(serverAddress))
            {
                ServerStatusLabel.ForeColor = Color.DarkRed;
                ServerStatusLabel.Text = "Failed to load server address!";
                return;
            }

            ServerStatusLabel.Text = "Checking...";

            bool isOnline = await CheckServerStatusAsync(serverAddress, port);

            if (isOnline)
            {
                ServerStatusLabel.ForeColor = Color.DarkGreen;
                ServerStatusLabel.Text = "ONLINE";
                launch_ok = true;
                button_login.Enabled = true;
            }
            else
            {
                ServerStatusLabel.ForeColor = Color.DarkRed;
                ServerStatusLabel.Text = "OFFLINE";
            }
        }

        private string LoadServerAddress(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    MessageBox.Show("server.cfg not found!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }

                string[] lines = File.ReadAllLines(filePath);
                foreach (string line in lines)
                {
                    if (line.Trim().StartsWith("SERVER_GAME"))
                    {
                        int startIndex = line.IndexOf('"') + 1;
                        int endIndex = line.LastIndexOf('"');
                        if (startIndex > 0 && endIndex > startIndex)
                        {
                            return line.Substring(startIndex, endIndex - startIndex);
                        }
                    }
                }

                MessageBox.Show("SERVER_GAME not found in server.cfg!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to read server.cfg: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (launch_ok == true)
                    button_login_Click(this, EventArgs.Empty);
            }
        }

        private async Task<bool> CheckServerStatusAsync(string serverAddress, int port)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(serverAddress, port);
                    var timeoutTask = Task.Delay(10000);

                    var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                    if (completedTask == timeoutTask)
                    {
                        return false;
                    }

                    return client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        static string sha256(string password)
        {
            using (var crypt = new System.Security.Cryptography.SHA256Managed())
            {
                byte[] crypto = crypt.ComputeHash(Encoding.UTF8.GetBytes(password));
                return BitConverter.ToString(crypto).Replace("-", "").ToLowerInvariant();
            }
        }

        private async void button_login_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(textBox_USERNAME.Text) || string.IsNullOrWhiteSpace(textBox_PASSWORD.Text))
            {
                MessageBox.Show("Please fill in both username and password.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            button_login.Enabled = false;
            CheckingStatusLabel.Text = "Verifying files...";

            bool filesValid = await Task.Run(() => VerifyPatchlist());

            if (!filesValid)
            {
                MessageBox.Show("File integrity check failed!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                button_login.Enabled = true;
                return;
            }

            try
            {
                string username = textBox_USERNAME.Text;
                string password = textBox_PASSWORD.Text;

                StartGame(username, password);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                button_login.Enabled = true;
            }
        }

        private void StartGame(string username, string password)
        {
            string gamePath = Path.Combine(Directory.GetCurrentDirectory(), "game.exe");

            if (!File.Exists(gamePath))
            {
                MessageBox.Show("game.exe not found!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                string arguments = $"-karlmaster_to {(char)'a'}{sha256(password)}{username}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = gamePath,
                    Arguments = arguments,
                    UseShellExecute = true
                });
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start the game: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool VerifyPatchlist()
        {
            try
            {
                string[] patchlist = Encoding.UTF8.GetString(Properties.Resources.files)
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                int totalFiles = patchlist.Length;
                int checkedFiles = 0;

                foreach (string entry in patchlist)
                {
                    string[] parts = entry.Split('|');
                    if (parts.Length != 2)
                    {
                        MessageBox.Show("Invalid entry in files checking list", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }

                    string relativePath = parts[0];
                    string expectedHash = parts[1];

                    string filePath = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
                    if (!File.Exists(filePath))
                    {
                        MessageBox.Show($"File not found: {relativePath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }

                    string actualHash = ComputeFileHash(filePath);
                    if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show($"Hash mismatch for file: {relativePath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }

                    checkedFiles++;
                    float progress = (float)checkedFiles / totalFiles * 100;
                    CheckingStatusLabel.Text = $" | Checking files.. {progress:F2}%";
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error verifying patchlist: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private string ComputeFileHash(string filePath)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {
            string kofiLink = ConfigurationManager.AppSettings["kofi_link"] ?? "https://ko-fi.com/karlmaster";
            Process.Start(new ProcessStartInfo(kofiLink) { UseShellExecute = true });
        }
    }
}

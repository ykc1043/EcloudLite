using System.Drawing;
using System.Windows.Forms;
using EcloudLite.Infrastructure;

namespace EcloudLite.UI
{
    internal sealed class AboutForm : Form
    {
        public AboutForm()
        {
            Text = "关于 " + AppInfo.ProductName;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 300);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.White;

            Label title = new Label
            {
                Text = AppInfo.ProductName,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(25, 34, 45),
                AutoSize = true,
                Location = new Point(24, 20)
            };
            Controls.Add(title);

            Label version = new Label
            {
                Text = "Lite " + AppInfo.LiteVersion,
                ForeColor = Color.FromArgb(74, 85, 99),
                AutoSize = true,
                Location = new Point(27, 58)
            };
            Controls.Add(version);

            TableLayoutPanel versions = new TableLayoutPanel
            {
                Location = new Point(24, 91),
                Size = new Size(512, 112),
                ColumnCount = 2,
                RowCount = 3,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            versions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            versions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            AddVersionRow(versions, 0, "客户端基于", AppInfo.ClientBaseline);
            AddVersionRow(versions, 1, "移动云电脑", AppInfo.CloudComputerVersion);
            AddVersionRow(versions, 2, "桌面协议", AppInfo.DesktopProtocolVersion);
            Controls.Add(versions);

            Label notice = new Label
            {
                Text = "非官方客户端，仅用于兼容性研究和已授权账号。",
                ForeColor = Color.FromArgb(94, 101, 110),
                Location = new Point(27, 220),
                Size = new Size(390, 24)
            };
            Controls.Add(notice);

            Button close = new Button
            {
                Text = "关闭",
                DialogResult = DialogResult.OK,
                Size = new Size(88, 30),
                Location = new Point(448, 250),
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                UseVisualStyleBackColor = true
            };
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
        }

        private static void AddVersionRow(TableLayoutPanel panel, int row, string name, string value)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            panel.Controls.Add(new Label
            {
                Text = name,
                ForeColor = Color.FromArgb(55, 65, 77),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, row);
            panel.Controls.Add(new Label
            {
                Text = value,
                ForeColor = Color.FromArgb(25, 34, 45),
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            }, 1, row);
        }
    }
}

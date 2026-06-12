using System;

namespace Mari_Downloads
{
    public static class ScrollableMessageBox
    {
        public static DialogResult Show(
            string text,
            string title = "Message",
            MessageBoxButtons buttons = MessageBoxButtons.OK
            )
        {


            Form form = new Form();
            form.Text = title;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.Size = new Size(700, 500);
            form.MinimumSize = new Size(400, 300);
            form.Icon = new Icon(Path.Combine(Application.StartupPath, "media", "icon.ico"));

            TextBox textBox = new TextBox();
            textBox.Multiline = true;
            textBox.ReadOnly = true;
            textBox.ScrollBars = ScrollBars.Vertical;
            textBox.Dock = DockStyle.Fill;
            textBox.Text = text;

            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Bottom;
            panel.FlowDirection = FlowDirection.RightToLeft;
            panel.Height = 50;
            panel.Padding = new Padding(10);

            DialogResult result = DialogResult.None;

            void AddButton(string label, DialogResult dialogResult)
            {
                Button btn = new Button();
                btn.Text = label;
                btn.DialogResult = dialogResult;
                btn.AutoSize = true;
                btn.Padding = new Padding(10, 5, 10, 5);

                btn.Click += (_, __) =>
                {
                    result = dialogResult;
                    form.Close();
                };

                panel.Controls.Add(btn);

                if (form.AcceptButton == null)
                    form.AcceptButton = btn;
            }

            switch (buttons)
            {
                case MessageBoxButtons.OK:
                    AddButton("OK", DialogResult.OK);
                    break;

                case MessageBoxButtons.OKCancel:
                    AddButton("Cancelar", DialogResult.Cancel);
                    AddButton("OK", DialogResult.OK);
                    break;

                case MessageBoxButtons.YesNo:
                    AddButton("No", DialogResult.No);
                    AddButton("Yes", DialogResult.Yes);
                    break;

                case MessageBoxButtons.YesNoCancel:
                    AddButton("Cancel", DialogResult.Cancel);
                    AddButton("No", DialogResult.No);
                    AddButton("Yes", DialogResult.Yes);
                    break;
            }

            form.Controls.Add(textBox);
            form.Controls.Add(panel);
            AppCustomization.ColorComponents(form, Properties.Settings.Default.MainBackColor, Properties.Settings.Default.MainForeColor);
            AppCustomization.FontChange(form, Properties.Settings.Default.MainFont);

            form.ShowDialog();

            return result;
        }
        public class OutputForm : Form
        {
            private readonly TextBox _outputBox;

            public OutputForm()
            {
                Text = "Output";
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(700, 500);
                MinimumSize = new Size(400, 300);

                try
                {
                    Icon = new Icon(Path.Combine(Application.StartupPath, "media", "icon.ico"));
                }
                catch { }

                _outputBox = new TextBox()
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    Dock = DockStyle.Fill,
                    WordWrap = false
                };

                Controls.Add(_outputBox);

                AppCustomization.ColorComponents(
                    this,
                    Properties.Settings.Default.MainBackColor,
                    Properties.Settings.Default.MainForeColor);

                AppCustomization.FontChange(
                    this,
                    Properties.Settings.Default.MainFont);
            }

            public void SetText(string text)
            {
                if (InvokeRequired)
                {
                    BeginInvoke(() => SetText(text));
                    return;
                }

                _outputBox.Text = text;

                _outputBox.SelectionStart = _outputBox.TextLength;
                _outputBox.ScrollToCaret();
            }

            public void AppendLine(string line)
            {
                if (InvokeRequired)
                {
                    BeginInvoke(() => AppendLine(line));
                    return;
                }

                _outputBox.AppendText(line + Environment.NewLine);

                _outputBox.SelectionStart = _outputBox.TextLength;
                _outputBox.ScrollToCaret();
            }
        }
    }
}

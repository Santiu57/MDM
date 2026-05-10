using System;
using System.Collections.Generic;
using System.Text;

namespace Mari_Downloads
{
    internal class RowFormat
{
        public static Control[] Color(string text, Func<Color> getter, Action<Color> setter)
        {
            Label Text = new Label { Text = text, Size = new Size(180, 35), TextAlign = ContentAlignment.MiddleLeft };

            Panel Preview = new Panel
            {
                Size = new Size(35, 35),
                BackColor = getter()
            };

            Button change = new Button { Text = "Change", Size = new Size(100, 35) };

            change.Click += (s, e) =>
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    cd.Color = getter();

                    if (cd.ShowDialog() == DialogResult.OK)
                    {
                        setter(cd.Color);
                        Preview.BackColor = cd.Color;
                        Properties.Settings.Default.Save();
                    }
                }
            };

            return [Text, Preview, change];
        }
        public static Control[] Argument(Manager.Argument arg, Action onChanged)
        {
            CheckBox enabled = new CheckBox { Checked = arg.Enabled, Size = new Size(25, 35), CheckAlign = ContentAlignment.TopLeft };
            Label name = new Label { Text = arg.Name, TextAlign = ContentAlignment.MiddleLeft, AutoSize = true };

            enabled.CheckedChanged += (s, e) => { arg.Enabled = enabled.Checked; onChanged(); };

            // Construir el control de valor según el tipo
            Control[] valueControls = arg.InputType switch
            {
                Manager.Argument.ControlType.FilePath => BuildFilePath(arg, onChanged),
                Manager.Argument.ControlType.FolderPath => BuildFolderPath(arg, onChanged),
                Manager.Argument.ControlType.DropDown => BuildDropDown(arg, onChanged),
                _ => BuildTextBox(arg, onChanged)
            };

            return new Control[] { enabled, name }.Concat(valueControls).ToArray();
        }

        public static Control[] BuildTextBox(Manager.Argument arg, Action onChanged)
        {
            TextBox tb = new TextBox { Text = arg.Value, Size = new Size(250, 30) };
            tb.TextChanged += (s, e) => { arg.Value = tb.Text; onChanged(); };
            return new Control[] { tb };
        }

        public static Control[] BuildFilePath(Manager.Argument arg, Action onChanged)
        {
            TextBox tb = new TextBox { Text = arg.Value, Size = new Size(200, 30) };
            Button btn = new Button { BackgroundImage = Media.Get("Folder"), BackgroundImageLayout = ImageLayout.Stretch, Size = new Size(35, 28) };

            tb.TextChanged += (s, e) => { arg.Value = tb.Text; onChanged(); };

            btn.Click += (s, e) =>
            {
                using var ofd = new OpenFileDialog();
                if (!string.IsNullOrWhiteSpace(arg.Value))
                    ofd.InitialDirectory = Path.GetDirectoryName(arg.Value);

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    tb.Text = ofd.FileName;
                    arg.Value = ofd.FileName;
                    onChanged();
                }
            };

            return [tb, btn];
        }

        public static Control[] BuildFolderPath(Manager.Argument arg, Action onChanged)
        {
            TextBox tb = new TextBox { Text = arg.Value, Size = new Size(200, 28) };
            Button btn = new Button { BackgroundImage = Media.Get("Folder"), BackgroundImageLayout = ImageLayout.Stretch, Size = new Size(35, 28) };

            tb.TextChanged += (s, e) => { arg.Value = tb.Text; onChanged(); };

            btn.Click += (s, e) =>
            {
                using var fbd = new FolderBrowserDialog();
                if (!string.IsNullOrWhiteSpace(arg.Value))
                    fbd.InitialDirectory = arg.Value;

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    tb.Text = fbd.SelectedPath;
                    arg.Value = fbd.SelectedPath;
                    onChanged();
                }
            };

            return [tb, btn];
        }

        private static Control[] BuildDropDown(Manager.Argument arg, Action onChanged)
        {
            ComboBox cb = new ComboBox
            {
                Size = new Size(200, 35),
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            // Las opciones visibles son las keys del mapa, o Options si no hay mapa
            string[] displayOptions = arg.OptionMap.Count > 0
                ? arg.OptionMap.Keys.ToArray()
                : arg.Options;

            foreach (var opt in displayOptions)
                cb.Items.Add(opt);

            // Seleccionar el item cuyo value real coincide con arg.Value
            string currentDisplay = arg.OptionMap.Count > 0
                ? arg.OptionMap.FirstOrDefault(kv => kv.Value == arg.Value).Key ?? displayOptions.FirstOrDefault()
                : (displayOptions.Contains(arg.Value) ? arg.Value : displayOptions.FirstOrDefault());

            cb.SelectedItem = currentDisplay;
            if (cb.SelectedItem != null)
                arg.Value = arg.OptionMap.Count > 0
                    ? arg.OptionMap[cb.SelectedItem.ToString()]
                    : cb.SelectedItem.ToString();

            cb.SelectedIndexChanged += (s, e) =>
            {
                if (cb.SelectedItem == null) return;
                arg.Value = arg.OptionMap.Count > 0
                    ? arg.OptionMap[cb.SelectedItem.ToString()]
                    : cb.SelectedItem.ToString();
                onChanged();
            };

            return [cb];
        }
    }
}

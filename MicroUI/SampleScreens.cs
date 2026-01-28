using MicroUI.Core;
using System;
using System.Collections.Generic;

namespace MicroUI.Samples
{
    public static class SampleScreens
    {
        public static Action<MControl>? Navigate;

        public static MControl CreateMainMenu()
        {
            var panel = new MPanel();
            
            var label = new MLabel { Text = "MicroUI Demos", X = 350, Y = 20, FontSize = 24, Width = 200 };
            panel.Add(label);

            string[] names = { "Dashboard (Code)", "Chart (Code)", "Login (Code)", "Settings (Code)", "Anim (Code)", 
                               "Extended Controls", "XML 1", "XML 2", "XML 3", "XML 4" };
            
            int col = 0; int row = 0;
            for(int i=0; i<names.Length; i++)
            {
                var btn = new MButton { Text = names[i], X = 50 + (col * 220), Y = 80 + (row * 60), Width = 200 };
                int index = i;
                btn.OnClick += (s) => LoadSample(index);
                panel.Add(btn);

                col++;
                if (col > 2) { col = 0; row++; }
            }

            return panel;
        }

        private static void LoadSample(int index)
        {
            MControl? screen = null;
            try {
                switch(index)
                {
                    case 0: screen = CreateDashboard(); break;
                    case 1: screen = CreateChart(); break;
                    case 2: screen = CreateLogin(); break;
                    case 3: screen = CreateSettings(); break;
                    case 4: screen = CreateAnim(); break;
                    case 5: screen = CreateExtendedControls(); break;
                    case 6: screen = MicroUI.Services.XmlLoader.LoadFromFile("Layouts/Screen1.xml"); break;
                    case 7: screen = MicroUI.Services.XmlLoader.LoadFromFile("Layouts/Screen2.xml"); break;
                    case 8: screen = MicroUI.Services.XmlLoader.LoadFromFile("Layouts/Screen3.xml"); break;
                    case 9: screen = MicroUI.Services.XmlLoader.LoadFromFile("Layouts/Screen4.xml"); break;
                }
            } catch (Exception ex) {
                var p = new MPanel();
                p.Add(new MLabel { Text = "Error: " + ex.Message, X=20, Y=20, Width=500 });
                screen = p;
            }

            if (screen != null)
            {
                var backBtn = new MButton { Text = "Back", X = 700, Y = 430, Width = 80, Height = 30, Background = Color.Red };
                backBtn.OnClick += (s) => Navigate?.Invoke(CreateMainMenu());
                screen.Add(backBtn);
                
                Navigate?.Invoke(screen);
            }
        }

        // --- CODE SAMPLES ---

        private static MControl CreateDashboard()
        {
            var p = new MPanel();
            p.Add(new MLabel { Text = "Dashboard", X = 20, Y = 20, FontSize=20, Width=200 });
            var gauge1 = new MGauge { Title = "Temp", Value = 45, Max = 100, X = 50, Y = 100 };
            var gauge2 = new MGauge { Title = "RPM", Value = 2500, Max = 8000, X = 250, Y = 100 };
            var btn = new MButton { Text = "Update Data", X = 50, Y = 300, Width = 150 };
            btn.OnClick += (s) => {
                var rnd = new Random();
                gauge1.Value = rnd.Next(0, 100);
                gauge2.Value = rnd.Next(0, 8000);
            };
            p.Add(gauge1); p.Add(gauge2); p.Add(btn);
            return p;
        }

        private static MControl CreateChart()
        {
            var p = new MPanel();
            p.Add(new MLabel { Text = "Realtime Chart", X = 20, Y = 20, FontSize=20, Width=200 });
            var chart = new MChart { Title="CPU Usage", X = 50, Y = 80, Width=600, Height=300 };
            chart.DataPoints = new List<double> { 10, 20, 15, 40, 35, 60, 50, 80, 70, 90 };
            var btn = new MButton { Text = "Add Point", X = 50, Y = 400 };
            btn.OnClick += (s) => {
                var rnd = new Random();
                chart.DataPoints.Add(rnd.Next(10, 100));
                if (chart.DataPoints.Count > 20) chart.DataPoints.RemoveAt(0);
            };
            p.Add(chart); p.Add(btn);
            return p;
        }

        private static MControl CreateLogin()
        {
            var p = new MPanel();
            var box = new MPanel { X = 200, Y = 100, Width = 400, Height = 250, Background = Color.FromArgb(255, 50, 50, 50) };
            
            box.Add(new MLabel { Text = "USER LOGIN", X = 150, Y = 20, Width = 150 });
            box.Add(new MLabel { Text = "Username:", X = 50, Y = 80, Width = 100 });
            box.Add(new MTextBox { Text = "", X = 150, Y = 75, Width = 200 }); 
            
            box.Add(new MLabel { Text = "Password:", X = 50, Y = 130, Width = 100 });
            box.Add(new MPasswordBox { Text = "", X = 150, Y = 125, Width = 200 });

            var btnLogin = new MButton { Text = "LOGIN", X = 150, Y = 190 };
            btnLogin.OnClick += (s) => MessageBox.Show("Login", "Login Successful!", p, () => btnLogin.Text = "Done");
            
            box.Add(btnLogin);
            p.Add(box);
            return p;
        }

        private static MControl CreateSettings()
        {
            var p = new MPanel();
            p.Add(new MLabel { Text = "Settings", X = 20, Y = 20, FontSize=20, Width=200 });
            int y = 80;
            foreach(var theme in Enum.GetValues(typeof(ThemeType)))
            {
                var btn = new MButton { Text = "Theme: " + theme.ToString(), X = 50, Y = y, Width = 200 };
                btn.OnClick += (s) => {
                    ThemeManager.CurrentTheme = (ThemeType)theme;
                };
                p.Add(btn);
                y += 60;
            }
            return p;
        }

        private static MControl CreateAnim()
        {
            var p = new MPanel();
            p.Add(new MLabel { Text = "Animation Demo", X = 20, Y = 20, FontSize=20, Width=200 });
            var movingBtn = new MButton { Text = "Catch Me", X = 50, Y = 100 };
            movingBtn.OnClick += (s) => {
                var rnd = new Random();
                movingBtn.AnimateTo(rnd.Next(50, 600), rnd.Next(100, 350));
            };
            p.Add(movingBtn);
            return p;
        }

        private static MControl CreateExtendedControls()
        {
            var p = new MPanel();
            p.Add(new MLabel { Text = "New Controls Demo", X = 20, Y = 10, FontSize=20, Width=250 });

            // Column 1
            p.Add(new MCheckBox { Text = "Enable Wi-Fi", X = 20, Y = 60, Checked = true });
            p.Add(new MCheckBox { Text = "Enable Bluetooth", X = 20, Y = 100 });
            
            p.Add(new MRadioButton { Text = "Option A", GroupName="G1", X = 20, Y = 140, Checked = true });
            p.Add(new MRadioButton { Text = "Option B", GroupName="G1", X = 20, Y = 180 });

            p.Add(new MLabel { Text = "Brightness:", X = 20, Y = 230 });
            var slider = new MSlider { X = 20, Y = 260, Value = 50, Max = 100 };
            p.Add(slider);

            p.Add(new MLabel { Text = "Download:", X = 20, Y = 300 });
            var prog = new MProgressBar { X = 20, Y = 330, Value = 30 };
            p.Add(prog);
            
            var btnSim = new MButton { Text = "Simulate DL", X = 240, Y = 330, Width=100 };
            btnSim.OnClick += (s) => prog.Value = (prog.Value + 10) % 100;
            p.Add(btnSim);

            // Column 2
            p.Add(new MLabel { Text = "Select Mode:", X = 400, Y = 60 });
            var combo = new MComboBox { X = 400, Y = 90 };
            combo.Items = new List<string> { "Silent", "Vibrate", "Ring", "Loud" };
            p.Add(combo);

            p.Add(new MLabel { Text = "Logs:", X = 400, Y = 140 });
            var list = new MListBox { X = 400, Y = 170, Height=100 };
            list.Items = new List<string> { "System Boot", "Network OK", "User Login", "Update Check", "Error 404" };
            p.Add(list);

            p.Add(new MToggleSwitch { X = 400, Y = 300, Checked = true });
            p.Add(new MLabel { Text = "Power Save", X = 470, Y = 300 });

            // Image Placeholder
            p.Add(new MImage { X = 600, Y = 60, Source = "logo.png" });
            
            // Text Block
            p.Add(new MTextBlock { Text = "This is a multiline text block.\nIt supports newlines and wrapping logic in a real engine.", X = 600, Y = 200, Width=150, Height=100 });

            return p;
        }
    }
}

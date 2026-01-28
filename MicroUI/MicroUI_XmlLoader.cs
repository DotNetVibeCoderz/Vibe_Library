using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.IO;
using MicroUI.Core;

namespace MicroUI.Services
{
    public static class XmlLoader
    {
        public static MControl LoadFromFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"XML file not found: {path}");
            string content = File.ReadAllText(path);
            return LoadFromXml(content);
        }

        public static MControl LoadFromXml(string xmlContent)
        {
            try 
            {
                XElement root = XElement.Parse(xmlContent);
                return ParseElement(root);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading XML: " + ex.Message);
                return new MPanel(); // Return empty panel on error
            }
        }

        private static MControl ParseElement(XElement element)
        {
            MControl control = null;
            string type = element.Name.LocalName;

            // Simple Factory
            switch (type)
            {
                case "Panel": control = new MPanel(); break;
                case "Button": control = new MButton(); break;
                case "Label": control = new MLabel(); break;
                case "TextBlock": control = new MTextBlock(); break;
                case "Gauge": control = new MGauge(); break;
                case "Chart": control = new MChart(); break;
                case "CheckBox": control = new MCheckBox(); break;
                case "RadioButton": control = new MRadioButton(); break;
                case "TextBox": control = new MTextBox(); break;
                case "PasswordBox": control = new MPasswordBox(); break;
                case "ProgressBar": control = new MProgressBar(); break;
                case "Slider": control = new MSlider(); break;
                case "ToggleSwitch": control = new MToggleSwitch(); break;
                case "Image": control = new MImage(); break;
                case "ListBox": control = new MListBox(); break;
                case "ComboBox": control = new MComboBox(); break;
                default: control = new MPanel(); break; // Fallback
            }

            // Parse Attributes
            foreach (var attr in element.Attributes())
            {
                ApplyAttribute(control, attr.Name.LocalName, attr.Value);
            }

            // Parse Children
            foreach (var child in element.Elements())
            {
                control.Add(ParseElement(child));
            }

            return control;
        }

        private static void ApplyAttribute(MControl control, string name, string value)
        {
            try {
                switch (name)
                {
                    case "X": control.X = double.Parse(value); break;
                    case "Y": control.Y = double.Parse(value); break;
                    case "Width": control.Width = double.Parse(value); break;
                    case "Height": control.Height = double.Parse(value); break;
                    case "Text": 
                        if (control is MButton b) b.Text = value;
                        if (control is MLabel l) l.Text = value;
                        if (control is MCheckBox cb) cb.Text = value;
                        if (control is MRadioButton rb) rb.Text = value;
                        if (control is MTextBox tb) tb.Text = value;
                        if (control is MTextBlock tbl) tbl.Text = value;
                        break;
                    case "Checked":
                         if (control is MCheckBox cb2) cb2.Checked = bool.Parse(value);
                         if (control is MRadioButton rb2) rb2.Checked = bool.Parse(value);
                         if (control is MToggleSwitch ts) ts.Checked = bool.Parse(value);
                         break;
                    case "Value":
                        if (control is MGauge g) g.Value = double.Parse(value);
                        if (control is MProgressBar pb) pb.Value = double.Parse(value);
                        if (control is MSlider sl) sl.Value = double.Parse(value);
                        break;
                    case "Max":
                         if (control is MProgressBar pb2) pb2.Max = double.Parse(value);
                         if (control is MSlider sl2) sl2.Max = double.Parse(value);
                         if (control is MGauge g2) g2.Max = double.Parse(value);
                         break;
                    case "Title":
                        if (control is MGauge g3) g3.Title = value;
                        if (control is MChart c) c.Title = value;
                        break;
                    case "Source":
                        if (control is MImage img) img.Source = value;
                        break;
                    case "Items": // Comma separated items for List/Combo
                        if (control is MListBox lb) lb.Items = new List<string>(value.Split(','));
                        if (control is MComboBox cmb) cmb.Items = new List<string>(value.Split(','));
                        break;
                    case "Background":
                        if (value.StartsWith("#"))
                        {
                            var r = Convert.ToByte(value.Substring(1, 2), 16);
                            var gVal = Convert.ToByte(value.Substring(3, 2), 16);
                            var bVal = Convert.ToByte(value.Substring(5, 2), 16);
                            control.Background = MicroUI.Core.Color.FromArgb(255, r, gVal, bVal);
                        }
                        break;
                }
            }
            catch { }
        }
    }
}

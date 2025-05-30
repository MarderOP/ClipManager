using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json.Nodes;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Clip
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class Backup : Window
    {
        string jsonData;
        public Backup(string json)
        {
            InitializeComponent();
            JsonData.Text = json;
            jsonData = json;
        }

        private async void SaveBackupToFile(object sender, RoutedEventArgs e)
        {
            try
            {
                string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupJsonPath = Path.Combine(downloadsPath, $"clips_backup_{timestamp}.json");
                await File.WriteAllTextAsync(backupJsonPath, jsonData);
                Debug.WriteLine($"Backup JSON saved at: {backupJsonPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save backup JSON: {ex.Message}");
            }
        }
    }
}

using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using Windows.Storage;
using Windows.Media.Core;
using Windows.Media.Playback;
using WinRT.Interop;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using Windows.Foundation;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.UI;
using System.IO;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media;
namespace Clip
{
    public sealed partial class MainWindow : Window
    {
        private MediaPlayer mediaPlayer;
        private StorageFile videoFile;
        private double videoDurationInSeconds;
        private DispatcherTimer timelineTimer;
        private int VideoTimeOffset = 10;
        private int VideoTimestampOffset = 10;
        private bool isFullscreen = false;
        private TypedEventHandler<MediaPlaybackSession, object> positionChangedHandler;
        private Clip selectedClipForEditing;
        public MainWindow()
        {
            this.InitializeComponent();
            timelineTimer = new DispatcherTimer();
            timelineTimer.Interval = TimeSpan.FromMilliseconds(100);
            timelineTimer.Tick += TimelineTimer_Tick;
            timelineTimer.Tick += InitialTimestampLoad;
            timelineTimer.Start();
            Body.KeyDown += Window_KeyDown;
            this.Closed += BackupSave;
        }
        private void BackupSave(object sender, WindowEventArgs e)
        {
            string data = ExportTreeToJson();
            if (data == null || data=="{ }")
            {
                Backup backup = new(data);
                backup.Activate();
            }   
        }
        private async Task ExportClipsFromJsonAsync(string json)
        {
            Debug.WriteLine("Starting ExportClipsFromJsonAsync...");

            string videoPath = videoFile?.Path;
            if (string.IsNullOrEmpty(videoPath))
            {
                Debug.WriteLine("No video loaded. videoFile.Path is null or empty.");
                return;
            }

            Debug.WriteLine($"Video path: {videoPath}");

            string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string clipsRootPath = Path.Combine(downloadsPath, "Clips");
            Directory.CreateDirectory(clipsRootPath);
            Debug.WriteLine($"Clips root directory created at: {clipsRootPath}");
            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupJsonPath = Path.Combine(downloadsPath, $"clips_backup_{timestamp}.json");
                await File.WriteAllTextAsync(backupJsonPath, json);
                Debug.WriteLine($"Backup JSON saved at: {backupJsonPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save backup JSON: {ex.Message}");
            }
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (var folder in doc.RootElement.EnumerateObject())
            {
                string folderName = SanitizeFileName(folder.Name);
                string folderPath = Path.Combine(clipsRootPath, folderName);
                Directory.CreateDirectory(folderPath);
                Debug.WriteLine($"Processing folder: {folderName} at path: {folderPath}");

                foreach (var item in folder.Value.EnumerateObject())
                {
                    var value = item.Value;
                    Debug.WriteLine($"  Checking item: {item.Name}");

                    if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("begin", out _))
                    {
                        Debug.WriteLine($"    Detected regular clip: {item.Name}");
                        await ExtractClipAsync(videoPath, folderPath, item.Name, value);
                    }
                    else if (value.ValueKind == JsonValueKind.Object)
                    {
                        Debug.WriteLine($"    Detected compilation: {item.Name}");
                        string compilationName = SanitizeFileName(item.Name);
                        string compilationPath = Path.Combine(folderPath, compilationName);
                        Directory.CreateDirectory(compilationPath);
                        Debug.WriteLine($"    Created compilation folder: {compilationPath}");

                        foreach (var subItem in value.EnumerateObject())
                        {
                            Debug.WriteLine($"      Sub-clip: {subItem.Name}");
                            await ExtractClipAsync(videoPath, compilationPath, subItem.Name, subItem.Value);
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"    Skipped unknown value kind for item: {item.Name}");
                    }
                }
            }

            Debug.WriteLine("All clips exported.");
        }
        private async Task ExtractClipAsync(string videoPath, string outputFolder, string title, JsonElement clipData)
        {
            string? begin = clipData.GetProperty("begin").GetString();
            string? end = clipData.GetProperty("end").GetString();
            if (!TryParseTime(begin, out var startTime) || !TryParseTime(end, out var endTime))
            {
                Debug.WriteLine($"    Invalid time format: {begin} or {end}");
                return;
            }

            Debug.WriteLine($"  Extracting clip: {title}");
            Debug.WriteLine($"    Begin: {startTime}, End: {endTime}");

            string safeTitle = SanitizeFileName(title);
            string outputFile = Path.Combine(outputFolder, $"{safeTitle}.mp4");

            Debug.WriteLine($"    Output path: {outputFile}");
            string ffmpegPath = "ffmpeg";
            double duration = (endTime - startTime).TotalSeconds;
            string arguments = $"-y -ss {startTime.TotalSeconds} -i \"{videoPath}\" -t {duration} " +
                   "-c:v copy -c:a copy " +
                   $"\"{outputFile}\"";
            Debug.WriteLine($"    FFmpeg command: {ffmpegPath} {arguments}");
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            string errorOutput = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Debug.WriteLine($"    FFmpeg exited with code {process.ExitCode}");
            if (!string.IsNullOrEmpty(errorOutput))
            {
                Debug.WriteLine($"    FFmpeg stderr: {errorOutput}");
            }

            if (process.ExitCode == 0)
            {
                Debug.WriteLine($"    Successfully exported: {outputFile}");
            }
            else
            {
                Debug.WriteLine($"    Failed to export: {outputFile}");
            }
        }
        private string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
        private void Window_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.A:
                    BackwardsButton_Click(sender, e);
                    break;
                case VirtualKey.D:
                    ForwardsButton_Click(sender, e);
                    break;
                case VirtualKey.Space:
                    PauseButton_Click(sender, e);
                    break;
                case VirtualKey.P:
                    PauseButton_Click(sender, e);
                    break;

                case VirtualKey.F:
                    ToggleFullscreen();
                    break;

                case VirtualKey.F11:
                    ToggleTrueFullscreen(); 
                    break;
                case VirtualKey.N:
                    throw new Exception("Intentional crash for backup system testing.");
            }
        }
        private void ToggleFullscreen()
        {
            if (isFullscreen)
            {
                LeftPanel.Visibility = Visibility.Visible;
                RightPanel.Visibility = Visibility.Visible;
                //Header.Visibility = Visibility.Visible;
                VideoPlayback.HorizontalAlignment = HorizontalAlignment.Stretch;
                VideoPlayback.VerticalAlignment = VerticalAlignment.Stretch;
                VideoPlayback.Width = double.NaN;
                VideoPlayback.Height = double.NaN;
                MainPanel.HorizontalAlignment = HorizontalAlignment.Center;
                MainPanel.VerticalAlignment = VerticalAlignment.Center;
                BodyContent.ColumnDefinitions[1].Width = new GridLength(0.75, GridUnitType.Star);  
                BodyContent.ColumnDefinitions[0].Width = new GridLength(0.20, GridUnitType.Star); 
                BodyContent.ColumnDefinitions[2].Width = new GridLength(0.05, GridUnitType.Star);
                isFullscreen = false;
            }
            else
            {
                LeftPanel.Visibility = Visibility.Collapsed;
                RightPanel.Visibility = Visibility.Collapsed;
                //Header.Visibility = Visibility.Collapsed;
                BodyContent.ColumnDefinitions[0].Width = new GridLength(0); 
                BodyContent.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star); 
                BodyContent.ColumnDefinitions[2].Width = new GridLength(0);  
                var window = this.AppWindow;
                if (window != null)
                {
                    VideoPlayback.Width = window.ClientSize.Width;
                    VideoPlayback.Height = window.ClientSize.Height;
                }
                MainPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
                MainPanel.VerticalAlignment = VerticalAlignment.Stretch;

                isFullscreen = true;
            }
        }
        private AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId myWndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(myWndId);
        }
        private void ToggleTrueFullscreen()
        {
            var _appWindow = GetAppWindowForCurrentWindow();
            if (_appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
            {
                _appWindow.SetPresenter(AppWindowPresenterKind.Default);
                if (isFullscreen)
                {
                    ToggleFullscreen();
                    ToggleFullscreen();
                }
            }
            else
            {
                _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                if (isFullscreen)
                {
                    ToggleFullscreen();
                    ToggleFullscreen();
                }
            }
        }
        private string ExportTreeToJson()
        {
            var result = new Dictionary<string, object>();

            foreach (var folderNode in FolderTreeView.RootNodes)
            {
                string folderName = folderNode.Content as string;

                if (string.IsNullOrEmpty(folderName))
                {
                    Debug.WriteLine("Skipped folderNode: Content is null or empty");
                    continue;
                }
                Debug.WriteLine($"Folder Name: {folderName}");

                var folderContent = new Dictionary<string, object>();

                if (Folders.ContainsKey(folderName))
                {
                    var folder = Folders[folderName];
                    foreach (var clip in folder.Clips)
                    {
                        folderContent[clip.Title] = new
                        {
                            title = clip.Title,
                            begin = clip.Begin,
                            end = clip.End
                        };
                    }
                    foreach (var subFolder in folder.SubFolders)
                    {
                        var compilationContent = new Dictionary<string, object>();

                        foreach (var clip in subFolder.Clips)
                        {
                            compilationContent[clip.Title] = new
                            {
                                title = clip.Title,
                                begin = clip.Begin,
                                end = clip.End
                            };
                        }
                        if (compilationContent.Count > 0)
                        {
                            folderContent[subFolder.Name] = compilationContent;
                        }
                    }
                    if (folderContent.Count > 0)
                    {
                        result[folderName] = folderContent;
                    }
                }
                else
                {
                    Debug.WriteLine($"Folder {folderName} not found in Folders collection.");
                }
            }
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            Debug.WriteLine("Final JSON:\n" + json);
            return json;
        }
        private async void ExportJsonButton_Click(object sender, RoutedEventArgs e)
        {
            string json = ExportTreeToJson();
            await ExportClipsFromJsonAsync(json);
        }

        public class Folder
        {
            public string Name { get; set; }
            public List<Clip> Clips { get; set; } = new List<Clip>(); 
            public List<Folder> SubFolders { get; set; } = new List<Folder>(); 
        }

        public class Clip
        {
            public string Title { get; set; }
            public string Begin { get; set; }
            public string End { get; set; }
        }

        public Dictionary<string, Folder> Folders { get; set; } = new Dictionary<string, Folder>();
        private void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            var node = args.InvokedItem as TreeViewNode;
            if (node == null || !node.Content.ToString().StartsWith("🎬")) return;
            var clipTitle = node.Content.ToString().Replace("🎬 ", "").Split('(')[0].Trim();
            foreach (var folder in Folders.Values)
            {
                var clip = FindClipRecursive(folder, clipTitle);
                if (clip != null)
                {
                    string defaultBegin = "0:00";
                    string defaultEnd = SecondsToTimeFormat((int)videoDurationInSeconds);
                    if (clip.Begin != defaultBegin || clip.End != defaultEnd)
                    {
                        BeginTimeInput.Text = clip.Begin;
                        EndTimeInput.Text = clip.End;
                    }
                    selectedClipForEditing = clip;
                    break;
                }
            }
        }
        private static Clip FindClipRecursive(Folder folder, string title)
        {
            var match = folder.Clips.FirstOrDefault(c => c.Title.StartsWith(title));
            if (match != null) return match;

            foreach (var subfolder in folder.SubFolders)
            {
                var result = FindClipRecursive(subfolder, title);
                if (result != null) return result;
            }
            return null;
        }
        private async void SaveTimestamp_Click(object sender, RoutedEventArgs e)
        {
            if (selectedClipForEditing == null)
            {
                await ShowInfoDialog("No Clip Selected", "Please select a clip to edit before saving.");
                return;
            }

            if (!ValidateTimes(out TimeSpan beginTime, out TimeSpan endTime))
            {
                await ShowInfoDialog("Invalid Time", "Please enter a valid time format and make sure the end time is after the start time.");
                return;
            }

            var selectedClip = selectedClipForEditing;
            string newBegin = BeginTimeInput.Text.Trim();
            string newEnd = EndTimeInput.Text.Trim();

            Debug.WriteLine($"Saving new Begin: {newBegin}, End: {newEnd} for Clip: {selectedClip.Title}");
            selectedClip.Begin = newBegin;
            selectedClip.End = newEnd;
            var parentNode = FolderTreeView.RootNodes
                .SelectMany(n => n.Children)
                .FirstOrDefault(n => n.Content.ToString().Contains(selectedClip.Title));

            if (parentNode != null)
            {
                parentNode.Content = $"🎬 {selectedClip.Title.Split('(')[0].Trim()}";
                Debug.WriteLine($"Clip and TreeViewNode updated: {selectedClip.Title}");
            }

            await ShowInfoDialog("Success", "✅ Timestamp saved successfully.");
        }
        private void UpdateFolderList()
        {
            FolderTreeView.RootNodes.Clear();

            foreach (var folder in Folders.Values)
            {
                var folderNode = CreateFolderNode(folder);
                FolderTreeView.RootNodes.Add(folderNode);
            }
        }

        private TreeViewNode CreateFolderNode(Folder folder)
        {
            var node = new TreeViewNode { Content = folder.Name };
            foreach (var clip in folder.Clips)
            {
                var clipNode = new TreeViewNode { Content = $"🎬 {clip.Title}" };
                node.Children.Add(clipNode);
            }
            foreach (var subFolder in folder.SubFolders)
            {
                var subNode = CreateFolderNode(subFolder); 
                node.Children.Add(subNode);
            }
            return node;
        }
        private async void CreateFolder_Click(object sender, RoutedEventArgs e)
        {
            var inputDialog = new ContentDialog
            {
                Title = "Enter Folder Name",
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel"
            };

            var inputBox = new TextBox();
            inputDialog.Content = inputBox;
            inputDialog.XamlRoot = this.Content.XamlRoot;
            var result = await inputDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                string folderName = inputBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(folderName))
                {
                    await ShowInfoDialog("Invalid name", "Folder name cannot be empty.");
                    return;
                }

                if (Folders.ContainsKey(folderName))
                {
                    await ShowInfoDialog("Duplicate folder", $"A folder named \"{folderName}\" already exists.");
                    return;
                }

                Folders.Add(folderName, new Folder { Name = folderName });
                UpdateFolderList();
            }
        }
        private async Task ShowInfoDialog(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };

            await dialog.ShowAsync();
        }
        private async void CreateTimestamp_Click(object sender, RoutedEventArgs e)
        {
            if (Folders.Count == 0)
            {
                await ShowInfoDialog("No Folders", "Create a folder first.");
                return;
            }

            string selectedFolder = null;
            if (Folders.Count > 1)
            {
                var folderNames = Folders.Keys.ToList();
                var folderDialog = new ContentDialog
                {
                    Title = "Select Folder",
                    PrimaryButtonText = "OK",
                    CloseButtonText = "Cancel",
                    Content = new ComboBox
                    {
                        ItemsSource = folderNames,
                        SelectedIndex = 0
                    },
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await folderDialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                    return;

                selectedFolder = ((ComboBox)folderDialog.Content).SelectedItem?.ToString();
            }
            else
            {
                selectedFolder = Folders.Keys.First();
            }

            if (string.IsNullOrEmpty(selectedFolder))
                return;

            string selectedTarget = "<Main Folder>";
            var folder = Folders[selectedFolder];
            if (folder.SubFolders.Any())
            {
                var targets = new List<string> { "<Main Folder>" };
                targets.AddRange(folder.SubFolders.Select(f => f.Name));

                var targetDialog = new ContentDialog
                {
                    Title = "Select Target Location",
                    PrimaryButtonText = "OK",
                    CloseButtonText = "Cancel",
                    Content = new ComboBox
                    {
                        ItemsSource = targets,
                        SelectedIndex = 0
                    },
                    XamlRoot = this.Content.XamlRoot
                };

                var targetResult = await targetDialog.ShowAsync();
                if (targetResult != ContentDialogResult.Primary)
                    return;

                selectedTarget = ((ComboBox)targetDialog.Content).SelectedItem?.ToString() ?? "<Main Folder>";
            }

            string title = null;
            while (true)
            {
                var titleBox = new TextBox { PlaceholderText = "Enter title" };
                var titleDialog = new ContentDialog
                {
                    Title = "Enter Timestamp Title",
                    PrimaryButtonText = "Create",
                    CloseButtonText = "Cancel",
                    Content = titleBox,
                    XamlRoot = this.Content.XamlRoot
                };
                var result = await titleDialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                    return;

                title = titleBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(title))
                {
                    await ShowInfoDialog("Invalid Input", "Title cannot be empty.");
                    continue;
                }
                if (!IsValidTitle(title))
                {
                    await ShowInfoDialog("Invalid Title", "Title can only contain letters, numbers, spaces, and underscores.");
                    continue;
                }

                bool isDuplicate = false;
                if (selectedTarget == "<Main Folder>")
                {
                    isDuplicate = folder.Clips.Any(c => c.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    var subFolder = folder.SubFolders.FirstOrDefault(f => f.Name == selectedTarget);
                    if (subFolder != null)
                        isDuplicate = subFolder.Clips.Any(c => c.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
                }

                if (isDuplicate)
                {
                    await ShowInfoDialog("Duplicate Timestamp", $"\"{title}\" already exists in \"{selectedTarget}\".");
                    continue;
                }

                break;
            }

            string begin = "0:00";
            string end = SecondsToTimeFormat((int)videoDurationInSeconds);
            Debug.WriteLine(end);
            if (selectedTarget == "<Main Folder>")
            {
                folder.Clips.Add(new Clip { Title = title, Begin = begin, End = end });
            }
            else
            {
                var targetSub = folder.SubFolders.FirstOrDefault(f => f.Name == selectedTarget);
                if (targetSub != null)
                {
                    targetSub.Clips.Add(new Clip { Title = title, Begin = begin, End = end });
                }
                else
                {
                    await ShowInfoDialog("Error", "Subfolder not found.");
                    return;
                }
            }

            UpdateFolderList();
        }
        private async void CreateCompilation_Click(object sender, RoutedEventArgs e)
        {
            if (Folders.Count == 0)
            {
                await ShowInfoDialog("No Folders Available", "Create a folder before adding a compilation.");
                return;
            }
            string selectedFolder = null;
            if (Folders.Count > 1)
            {
                var folderDialog = new ContentDialog
                {
                    Title = "Select Folder for Compilation",
                    PrimaryButtonText = "Next",
                    CloseButtonText = "Cancel",
                    Content = new ComboBox
                    {
                        ItemsSource = Folders.Keys.ToList(),
                        SelectedIndex = 0
                    },
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await folderDialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                    return;

                selectedFolder = ((ComboBox)folderDialog.Content).SelectedItem?.ToString();
            }
            else
            {
                selectedFolder = Folders.Keys.First();
            }
            if (string.IsNullOrEmpty(selectedFolder))
                return;
            string compilationName = null;
            while (true)
            {
                var nameBox = new TextBox { PlaceholderText = "Enter Compilation Name" };
                var nameDialog = new ContentDialog
                {
                    Title = "Enter Compilation Name",
                    PrimaryButtonText = "Create",
                    CloseButtonText = "Cancel",
                    Content = nameBox,
                    XamlRoot = this.Content.XamlRoot
                };

                var nameResult = await nameDialog.ShowAsync();
                if (nameResult != ContentDialogResult.Primary)
                    return;

                compilationName = nameBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(compilationName))
                {
                    await ShowInfoDialog("Invalid Name", "Compilation name cannot be empty.");
                    continue;
                }

                bool exists = Folders[selectedFolder].SubFolders.Any(sf => sf.Name.Equals(compilationName, StringComparison.OrdinalIgnoreCase));
                if (exists)
                {
                    await ShowInfoDialog("Duplicate Name", $"A compilation named \"{compilationName}\" already exists in \"{selectedFolder}\".");
                    continue;
                }

                break;
            }
            Folders[selectedFolder].SubFolders.Add(new Folder { Name = compilationName });
            UpdateFolderList();
        }

        private async Task DeleteFolder()
        {
            var folderNames = Folders.Keys.ToList();
            if (folderNames.Count == 0)
            {
                await ShowInfoDialog("No Folders", "Create a folder first before deleting one.");
                return;
            }

            var folderDialog = new ContentDialog
            {
                Title = "Select Folder to Delete",
                PrimaryButtonText = "Next",
                CloseButtonText = "Cancel"
            };

            var folderCombo = new ComboBox { ItemsSource = folderNames, PlaceholderText = "Select folder" };
            folderDialog.Content = folderCombo;
            folderDialog.XamlRoot = this.Content.XamlRoot;
            
            var folderResult = await folderDialog.ShowAsync();
            if (folderResult != ContentDialogResult.Primary || folderCombo.SelectedItem == null)
            {
                await ShowInfoDialog("No Folder Selected", "Please select a folder.");
                return;
            }

            string selectedFolder = folderCombo.SelectedItem.ToString();
            var confirmationDialog = new ContentDialog
            {
                Title = "Delete Folder",
                Content = $"Are you sure you want to delete the folder \"{selectedFolder}\"?",
                PrimaryButtonText = "Delete",
                SecondaryButtonText = "Cancel"
            };
            confirmationDialog.XamlRoot = this.Content.XamlRoot;
            var confirmResult = await confirmationDialog.ShowAsync();
            if (confirmResult == ContentDialogResult.Primary)
            {
                // Remove folder
                Folders.Remove(selectedFolder);
                UpdateFolderList(); 
            }
        }
        private async Task DeleteCompilation()
        {
            var folderNames = Folders.Keys.ToList();
            if (folderNames.Count == 0)
            {
                await ShowInfoDialog("No Folders", "Create a folder first before deleting a compilation.");
                return;
            }

            var folderDialog = new ContentDialog
            {
                Title = "Select Folder",
                PrimaryButtonText = "Next",
                CloseButtonText = "Cancel"
            };

            var folderCombo = new ComboBox { ItemsSource = folderNames, PlaceholderText = "Select folder" };
            folderDialog.Content = folderCombo;
            folderDialog.XamlRoot = this.Content.XamlRoot;

            var folderResult = await folderDialog.ShowAsync();
            if (folderResult != ContentDialogResult.Primary || folderCombo.SelectedItem == null)
            {
                await ShowInfoDialog("No Folder Selected", "Please select a folder.");
                return;
            }

            string selectedFolder = folderCombo.SelectedItem.ToString();
            var compilations = Folders[selectedFolder].SubFolders.Select(f => f.Name).ToList();

            if (compilations.Count == 0)
            {
                await ShowInfoDialog("No Compilations", "There are no compilations in this folder.");
                return;
            }

            var compilationDialog = new ContentDialog
            {
                Title = "Select Compilation to Delete",
                PrimaryButtonText = "Next",
                CloseButtonText = "Cancel"
            };
            compilationDialog.XamlRoot = this.Content.XamlRoot;
            var compilationCombo = new ComboBox { ItemsSource = compilations, PlaceholderText = "Select compilation" };
            compilationDialog.Content = compilationCombo;
            compilationDialog.XamlRoot = this.Content.XamlRoot;

            var compilationResult = await compilationDialog.ShowAsync();
            if (compilationResult != ContentDialogResult.Primary || compilationCombo.SelectedItem == null)
            {
                await ShowInfoDialog("No Compilation Selected", "Please select a compilation.");
                return;
            }
            string selectedCompilation = compilationCombo.SelectedItem.ToString();
            var confirmDialog = new ContentDialog
            {
                Title = "Delete Compilation",
                Content = $"Are you sure you want to delete the compilation \"{selectedCompilation}\"?",
                PrimaryButtonText = "Delete",
                SecondaryButtonText = "Cancel"
            };
            confirmDialog.XamlRoot = this.Content.XamlRoot;
            var confirmResult = await confirmDialog.ShowAsync();
            if (confirmResult == ContentDialogResult.Primary)
            {
                var targetFolder = Folders[selectedFolder].SubFolders.FirstOrDefault(f => f.Name == selectedCompilation);
                if (targetFolder != null)
                {
                    Folders[selectedFolder].SubFolders.Remove(targetFolder);
                    UpdateFolderList();
                }
            }
        }
        private async Task DeleteTimestamp()
        {
            var folderNames = Folders.Keys.ToList();
            if (folderNames.Count == 0)
            {
                await ShowInfoDialog("No Folders", "Create a folder first before deleting a timestamp.");
                return;
            }

            var folderDialog = new ContentDialog
            {
                Title = "Select Folder",
                PrimaryButtonText = "Next",
                CloseButtonText = "Cancel"
            };

            var folderCombo = new ComboBox { ItemsSource = folderNames, PlaceholderText = "Select folder" };
            folderDialog.Content = folderCombo;
            folderDialog.XamlRoot = this.Content.XamlRoot;

            var folderResult = await folderDialog.ShowAsync();
            if (folderResult != ContentDialogResult.Primary || folderCombo.SelectedItem == null)
            {
                await ShowInfoDialog("No Folder Selected", "Please select a folder.");
                return;
            }

            string selectedFolder = folderCombo.SelectedItem.ToString();
            var options = new List<string> { "<Main Folder>" };
            options.AddRange(Folders[selectedFolder].SubFolders.Select(f => f.Name));

            var targetDialog = new ContentDialog
            {
                Title = "Select Timestamp Location",
                PrimaryButtonText = "Next",
                CloseButtonText = "Cancel"
            };

            var targetCombo = new ComboBox { ItemsSource = options, PlaceholderText = "Select folder or compilation" };
            targetDialog.Content = targetCombo;
            targetDialog.XamlRoot = this.Content.XamlRoot;

            var targetResult = await targetDialog.ShowAsync();
            if (targetResult != ContentDialogResult.Primary || targetCombo.SelectedItem == null)
            {
                await ShowInfoDialog("No Target Selected", "Please select a target location.");
                return;
            }

            string target = targetCombo.SelectedItem.ToString();
            var timestampDialog = new ContentDialog
            {
                Title = "Select Timestamp to Delete",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel"
            };

            var timestamps = target == "<Main Folder>" ? Folders[selectedFolder].Clips : Folders[selectedFolder].SubFolders.FirstOrDefault(f => f.Name == target)?.Clips ?? new List<Clip>();

            if (timestamps.Count == 0)
            {
                await ShowInfoDialog("No Timestamps", "There are no timestamps in this location.");
                return;
            }

            var timestampCombo = new ComboBox { ItemsSource = timestamps.Select(c => c.Title), PlaceholderText = "Select timestamp" };
            timestampDialog.Content = timestampCombo;
            timestampDialog.XamlRoot = this.Content.XamlRoot;

            var timestampResult = await timestampDialog.ShowAsync();
            if (timestampResult != ContentDialogResult.Primary || timestampCombo.SelectedItem == null)
            {
                await ShowInfoDialog("No Timestamp Selected", "Please select a timestamp.");
                return;
            }
            string selectedTimestamp = timestampCombo.SelectedItem.ToString();
            var confirmDialog = new ContentDialog
            {
                Title = "Delete Timestamp",
                Content = $"Are you sure you want to delete the timestamp \"{selectedTimestamp}\"?",
                PrimaryButtonText = "Delete",
                SecondaryButtonText = "Cancel"
            };
            confirmDialog.XamlRoot = this.Content.XamlRoot;
            var confirmResult = await confirmDialog.ShowAsync();
            if (confirmResult == ContentDialogResult.Primary)
            {
                var targetFolder = Folders[selectedFolder];
                var targetCompilation = target == "<Main Folder>" ? null : target;

                if (targetCompilation == null)
                {
                    targetFolder.Clips.RemoveAll(c => c.Title == selectedTimestamp);
                }
                else
                {
                    var subFolder = targetFolder.SubFolders.FirstOrDefault(f => f.Name == targetCompilation);
                    if (subFolder != null)
                    {
                        subFolder.Clips.RemoveAll(c => c.Title == selectedTimestamp);
                    }
                }

                UpdateFolderList();
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var options = new List<string> { "Delete Folder", "Delete Compilation", "Delete Timestamp" };

            var deleteDialog = new ContentDialog
            {
                Title = "Select Deletion Type",
                PrimaryButtonText = "Next",
                CloseButtonText = "Cancel"
            };

            var deleteCombo = new ComboBox { ItemsSource = options, PlaceholderText = "Select what to delete" };
            deleteDialog.Content = deleteCombo;
            deleteDialog.XamlRoot = this.Content.XamlRoot;

            var result = await deleteDialog.ShowAsync();
            if (result != ContentDialogResult.Primary || deleteCombo.SelectedItem == null)
            {
                await ShowInfoDialog("No Option Selected", "Please select what you want to delete.");
                return;
            }

            string selectedOption = deleteCombo.SelectedItem.ToString();

            // Delegate the logic based on selection
            switch (selectedOption)
            {
                case "Delete Folder":
                    await DeleteFolder();
                    break;
                case "Delete Compilation":
                    await DeleteCompilation();
                    break;
                case "Delete Timestamp":
                    await DeleteTimestamp();
                    break;
                default:
                    await ShowInfoDialog("Invalid Option", "Please select a valid option.");
                    break;
            }
        }
        private async void LoadVideoHandle(object sender, RoutedEventArgs e)
        {
            videoFile = await PickFileAsync();
            if (videoFile != null)
            {
                mediaPlayer = new MediaPlayer();
                var mediaSource = MediaSource.CreateFromStorageFile(videoFile);
                VideoPlayback.SetMediaPlayer(mediaPlayer);
                mediaPlayer.Source = mediaSource;
                mediaPlayer.Play();
                mediaPlayer.MediaOpened += (sender, args) =>
                {
                    videoDurationInSeconds = mediaPlayer.PlaybackSession.NaturalDuration.TotalSeconds;
                };
                LoadVideoButton.Visibility = Visibility.Collapsed;
                LeftPanelContent.Visibility = Visibility.Visible;
                VideoManipulation.Visibility = Visibility.Visible;
                TimestampButtons.Visibility = Visibility.Visible;
                TimestampManipulation.Visibility = Visibility.Visible;
            }
            else
            {
                LoadVideoButton.Content = "No file selected!";
            }
        }
        private async Task<StorageFile> PickFileAsync()
        {
            var filePicker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary
            };
            filePicker.FileTypeFilter.Add(".mp4");
            filePicker.FileTypeFilter.Add(".ts");

            var hwnd = WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);

            return await filePicker.PickSingleFileAsync();
        }
        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateTimes(out TimeSpan beginTime, out TimeSpan endTime))
                return;

            mediaPlayer.Pause();
            mediaPlayer.PlaybackSession.Position = beginTime;
            if (positionChangedHandler != null)
            {
                mediaPlayer.PlaybackSession.PositionChanged -= positionChangedHandler;
            }
            positionChangedHandler = (s, args) =>
            {
                if (mediaPlayer.PlaybackSession.Position >= endTime)
                {
                    mediaPlayer.PlaybackSession.Position = beginTime;
                    mediaPlayer.Play();
                }
            };

            mediaPlayer.PlaybackSession.PositionChanged += positionChangedHandler;
            mediaPlayer.Play();
        }
        private static string SecondsToTimeFormat(int seconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            Debug.WriteLine(time);
            if (time.Hours > 0)
                return $"{time.Hours}:{time.Minutes:D2}:{time.Seconds:D2}";
            else
                return $"{time.Minutes}:{time.Seconds:D2}";
        }
        private static int TimeToSeconds(TimeSpan time)
        {
            return (int)time.TotalSeconds;
        }
        private void ChangeBeginTimeForward(object sender, RoutedEventArgs e)
        {
            if (!TryParseTime(BeginTimeInput.Text, out TimeSpan beginTime) ||
                !TryParseTime(EndTimeInput.Text, out TimeSpan endTime))
                return;

            int beginSeconds = TimeToSeconds(beginTime);
            int endSeconds = TimeToSeconds(endTime);
            int newBeginSeconds = beginSeconds + VideoTimestampOffset;

            if (newBeginSeconds >= endSeconds)
                newBeginSeconds = Math.Max(endSeconds - 1, 0); 

            BeginTimeInput.Text = SecondsToTimeFormat(newBeginSeconds);
        }
        private void ChangeBeginTimeBackward(object sender, RoutedEventArgs e)
        {
            if (!TryParseTime(BeginTimeInput.Text, out TimeSpan beginTime) ||
                !TryParseTime(EndTimeInput.Text, out TimeSpan endTime))
                return;

            int beginSeconds = TimeToSeconds(beginTime);
            int newBeginSeconds = beginSeconds - VideoTimestampOffset;

            if (newBeginSeconds < 0)
                newBeginSeconds = 0;

            BeginTimeInput.Text = SecondsToTimeFormat(newBeginSeconds);
        }
        private void ChangeEndTimeForward(object sender, RoutedEventArgs e)
        {
            if (!TryParseTime(BeginTimeInput.Text, out TimeSpan beginTime) ||
                !TryParseTime(EndTimeInput.Text, out TimeSpan endTime))
                return;

            int beginSeconds = TimeToSeconds(beginTime);
            int endSeconds = TimeToSeconds(endTime);
            int newEndSeconds = endSeconds + VideoTimestampOffset;

            if (newEndSeconds > videoDurationInSeconds)
                newEndSeconds = (int)videoDurationInSeconds;

            if (newEndSeconds <= beginSeconds)
                newEndSeconds = beginSeconds + 1;

            EndTimeInput.Text = SecondsToTimeFormat(newEndSeconds);
        }
        private void ChangeEndTimeBackward(object sender, RoutedEventArgs e)
        {
            if (!TryParseTime(BeginTimeInput.Text, out TimeSpan beginTime) ||
                !TryParseTime(EndTimeInput.Text, out TimeSpan endTime))
                return;

            int beginSeconds = TimeToSeconds(beginTime);
            int endSeconds = TimeToSeconds(endTime);
            int newEndSeconds = endSeconds - VideoTimestampOffset;

            if (newEndSeconds <= beginSeconds)
                newEndSeconds = beginSeconds + 1;

            EndTimeInput.Text = SecondsToTimeFormat(newEndSeconds);
        }
        private bool ValidateTimes(out TimeSpan beginTime, out TimeSpan endTime)
        {
            beginTime = TimeSpan.Zero;
            endTime = TimeSpan.Zero;
            string beginText = BeginTimeInput.Text;
            string endText = EndTimeInput.Text;
            if (!IsValidTimeFormat(beginText) || !IsValidTimeFormat(endText))
            {
                return false;
            }
            if (!TryParseTime(beginText, out beginTime) || !TryParseTime(endText, out endTime))
            {
                return false;
            }
            if (beginTime >= endTime)
            {
                return false;
            }
            if (beginTime.TotalSeconds > videoDurationInSeconds || endTime.TotalSeconds > videoDurationInSeconds)
            {
                return false;
            }
            return true;
        }
        private static bool TryParseTime(string time, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            var parts = time.Split(':');
            Debug.WriteLine($"Parsing time: {time}");
            if (parts.Length == 2)
            {
                if (parts[0].Length == 1) 
                {
                    time = "0:" + time;
                    parts = time.Split(':');
                }
                int minutes = int.Parse(parts[0]);
                int seconds = int.Parse(parts[1]);
                result = new TimeSpan(0, minutes, seconds);
                Debug.WriteLine($"Parsed time: {result}");
                return true;
            }
            if (parts.Length == 3)
            {
                int hours = int.Parse(parts[0]);
                int minutes = int.Parse(parts[1]);
                int seconds = int.Parse(parts[2]);
                result = new TimeSpan(hours, minutes, seconds);
                Debug.WriteLine($"Parsed time: {result}");
                return true;
            }

            return false;
        }
        private void TimelineTimer_Tick(object sender, object e)
        {
            if (VideoPlayback?.MediaPlayer?.PlaybackSession == null)
                return;

            var session = VideoPlayback.MediaPlayer.PlaybackSession;

            TimeSpan position = session.Position;
            TimeSpan duration = session.NaturalDuration;

            if (duration.TotalSeconds > 0)
            {
                string current = position.ToString(@"hh\:mm\:ss\.fff");
                string total = duration.ToString(@"hh\:mm\:ss\.fff");
                Timeline.Text = $"{current} - {total}";
            }
            else
            {
                Timeline.Text = "00:00:00.000 - 00:00:00.000";
            }
        }
        private void InitialTimestampLoad(object sender, object e)
        {
            if (VideoPlayback?.MediaPlayer?.PlaybackSession == null)
                return;
            var session = VideoPlayback.MediaPlayer.PlaybackSession;
            TimeSpan duration = session.NaturalDuration;
            if (duration.TotalSeconds > 0)
            {
                string total = duration.ToString(@"hh\:mm\:ss");
                BeginTimeInput.Text = "0:00";
                EndTimeInput.Text = total;
                timelineTimer.Tick -= InitialTimestampLoad;
            }
        }
        private void TimeChange_LostFocus(object sender, RoutedEventArgs e)
        {
            string input = TimeChange.Text.Trim();
            int defaultValue = 10;
            if (string.IsNullOrEmpty(input) || !IsValidNumber(input))
            {
                TimeChange.Text = $"{defaultValue}s";
            }
            else
            {
                string numberPart = input.TrimEnd('s');

                if (int.TryParse(numberPart, out int validValue))
                {
                    VideoTimeOffset = validValue;
                    TimeChange.Text = (numberPart).ToString() + "s";
                }
            }
        }
        private void TimestampChange_LostFocus(object sender, RoutedEventArgs e)
        {
            string input = TimestampChange.Text.Trim();
            int defaultValue = 10;
            if (string.IsNullOrEmpty(input) || !IsValidNumber(input))
            {
                TimestampChange.Text = $"{defaultValue}s";
            }
            else
            {
                string numberPart = input.TrimEnd('s');

                if (int.TryParse(numberPart, out int validValue))
                {
                    VideoTimestampOffset = validValue;
                    TimestampChange.Text = (numberPart).ToString() + "s";
                }
            }
        }
        private void BackwardsButton_Click(object sender, RoutedEventArgs e)
        {
            var currentPosition = VideoPlayback.MediaPlayer.Position;
            var newPosition = currentPosition - TimeSpan.FromSeconds(VideoTimeOffset);
            if (newPosition < TimeSpan.Zero)
            {
                newPosition = TimeSpan.Zero;
            }

            VideoPlayback.MediaPlayer.Position = newPosition;
        }

        private void ForwardsButton_Click(object sender, RoutedEventArgs e)
        {
            var currentPosition = VideoPlayback.MediaPlayer.Position;
            var newPosition = currentPosition + TimeSpan.FromSeconds(VideoTimeOffset);
            if (newPosition > VideoPlayback.MediaPlayer.PlaybackSession.NaturalDuration)
            {
                newPosition = VideoPlayback.MediaPlayer.PlaybackSession.NaturalDuration;
            }

            VideoPlayback.MediaPlayer.Position = newPosition;
        }
        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            var mediaPlayer = VideoPlayback.MediaPlayer;
            if (mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                mediaPlayer.Pause();
            }
            else
            {
                mediaPlayer.Play();
            }
        }
        static bool IsValidTitle(string title)
        {
            string pattern = @"^[a-zA-Z0-9 _]*$";
            return Regex.IsMatch(title, pattern);
        }
        private static bool IsValidNumber(string time)
        {
            return Regex.IsMatch(time, @"^\d+s?$");
        }
        private static bool IsValidTimeFormat(string time)
        {
            return Regex.IsMatch(time, @"^(\d{1,2}):(\d{1,2}):?(\d{1,2})?$");
        }

    }
}

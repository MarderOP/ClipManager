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
namespace Clip
{
    public sealed partial class MainWindow : Window
    {
        private MediaPlayer mediaPlayer;
        private StorageFile videoFile;
        private double videoDurationInSeconds;
        private DispatcherTimer timelineTimer;
        private int VideoTimeOffset;
        private int VideoTimestampOffset;
        private bool isFullscreen = false;
        private Thickness originalMargin;
        private Visibility originalUIVisibility;
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
        }
        private void Window_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.A:
                case VirtualKey.GamepadDPadLeft:
                    BackwardsButton_Click(sender, e);
                    break;

                case VirtualKey.D:
                case VirtualKey.GamepadDPadRight:
                    ForwardsButton_Click(sender, e);
                    break;

                case VirtualKey.Space:
                case VirtualKey.P:
                    PauseButton_Click(sender, e);
                    break;

                case VirtualKey.F:
                    ToggleFullscreen();
                    break;

                case VirtualKey.F11:
                    ToggleTrueFullscreen(); 
                    break;
            }
        }
        private void ToggleFullscreen()
        {
            if (isFullscreen)
            {
                LeftPanel.Visibility = Visibility.Visible;
                RightPanel.Visibility = Visibility.Visible;
                Header.Visibility = Visibility.Visible;
                VideoPlayback.HorizontalAlignment = HorizontalAlignment.Stretch;
                VideoPlayback.VerticalAlignment = VerticalAlignment.Stretch;
                VideoPlayback.Width = double.NaN;
                VideoPlayback.Height = double.NaN;
                MainPanel.HorizontalAlignment = HorizontalAlignment.Center;
                MainPanel.VerticalAlignment = VerticalAlignment.Center;
                BodyContent.ColumnDefinitions[1].Width = new GridLength(0.7, GridUnitType.Star);  
                BodyContent.ColumnDefinitions[0].Width = new GridLength(0.15, GridUnitType.Star); 
                BodyContent.ColumnDefinitions[2].Width = new GridLength(0.15, GridUnitType.Star);

                isFullscreen = false;
            }
            else
            {
                LeftPanel.Visibility = Visibility.Collapsed;
                RightPanel.Visibility = Visibility.Collapsed;
                Header.Visibility = Visibility.Collapsed;
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
            }
            else
            {
                _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen); 
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
        private void ExportJsonButton_Click(object sender, RoutedEventArgs e)
        {
            ExportTreeToJson();
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

            public override string ToString()
            {
                return Title;
            }
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
                    selectedClipForEditing = clip;
                    BeginTimeInput.Text = clip.Begin;
                    EndTimeInput.Text = clip.End;
                    break;
                }
            }
        }
        private Clip FindClipRecursive(Folder folder, string title)
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
            if (selectedClipForEditing == null) return;
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
                UpdateConfirmationText.Visibility = Visibility.Visible;
                await Task.Delay(3000);
                UpdateConfirmationText.Visibility = Visibility.Collapsed;
            }
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

            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputBox.Text))
            {
                string folderName = inputBox.Text.Trim();
                if (!Folders.ContainsKey(folderName))
                {
                    Folders.Add(folderName, new Folder { Name = folderName });
                    UpdateFolderList();
                }
            }
        }

        private async void CreateTimestamp_Click(object sender, RoutedEventArgs e)
        {
            var folderNames = Folders.Keys.ToList();
            var folderDialog = new ContentDialog
            {
                Title = "Select Folder",
                PrimaryButtonText = "Next",
                CloseButtonText = "Cancel"
            };

            var folderCombo = new ComboBox { ItemsSource = folderNames };
            folderDialog.Content = folderCombo;
            folderDialog.XamlRoot = this.Content.XamlRoot;

            var folderResult = await folderDialog.ShowAsync();

            if (folderResult != ContentDialogResult.Primary || folderCombo.SelectedItem == null)
                return;
            string selectedFolder = folderCombo.SelectedItem.ToString();
            var options = new List<string> { "<Main Folder>" };
            options.AddRange(Folders[selectedFolder].SubFolders.Select(f => f.Name));

            var targetDialog = new ContentDialog
            {
                Title = "Select Target",
                PrimaryButtonText = "Next",
                CloseButtonText = "Cancel"
            };

            var targetCombo = new ComboBox { ItemsSource = options };
            targetDialog.Content = targetCombo;
            targetDialog.XamlRoot = this.Content.XamlRoot;

            var targetResult = await targetDialog.ShowAsync();

            if (targetResult != ContentDialogResult.Primary || targetCombo.SelectedItem == null)
                return;
            string target = targetCombo.SelectedItem.ToString();
            var nameDialog = new ContentDialog
            {
                Title = "Enter Timestamp Title",
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel"
            };

            var inputBox = new TextBox { PlaceholderText = "Enter title" };
            nameDialog.Content = inputBox;
            nameDialog.XamlRoot = this.Content.XamlRoot;

            var nameResult = await nameDialog.ShowAsync();

            if (nameResult == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputBox.Text))
            {
                
                string begin = "00:00:00";
                string end = SecondsToTimeFormat((int)videoDurationInSeconds);
                string title = inputBox.Text.Trim();
                var clip = new Clip
                {
                    Title = title ,
                    Begin = begin,
                    End = end
                };
                if (target == "<Main Folder>")
                {

                    Folders[selectedFolder].Clips.Add(clip);
                }
                else
                {
                    var targetSub = Folders[selectedFolder].SubFolders.FirstOrDefault(f => f.Name == target);
                    if (targetSub != null)
                    {
                        targetSub.Clips.Add(clip);
                    }
                }
                UpdateFolderList();
            }
        }
        private async void CreateCompilation_Click(object sender, RoutedEventArgs e)
        {
            var inputDialog = new ContentDialog
            {
                Title = "Enter Compilation Name",
                PrimaryButtonText = "Next",
                CloseButtonText = "Cancel"
            };

            var inputBox = new TextBox { PlaceholderText = "Compilation name" };
            inputDialog.Content = inputBox;
            inputDialog.XamlRoot = this.Content.XamlRoot;

            var inputResult = await inputDialog.ShowAsync();

            if (inputResult == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputBox.Text))
            {
                string compilationName = inputBox.Text.Trim();
                var folderNames = Folders.Keys.ToList();
                var folderDialog = new ContentDialog
                {
                    Title = "Select Folder for Compilation",
                    PrimaryButtonText = "Create",
                    CloseButtonText = "Cancel"
                };

                var comboBox = new ComboBox { ItemsSource = folderNames };
                folderDialog.Content = comboBox;
                folderDialog.XamlRoot = this.Content.XamlRoot;

                var folderResult = await folderDialog.ShowAsync();

                if (folderResult == ContentDialogResult.Primary && comboBox.SelectedItem != null)
                {
                    string selectedFolder = comboBox.SelectedItem.ToString();
                    Folders[selectedFolder].SubFolders.Add(new Folder { Name = compilationName });
                    UpdateFolderList();
                }
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
        private string SecondsToTimeFormat(int seconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(seconds);

            if (time.Hours > 0)
                return $"{time.Hours}:{time.Minutes:D2}:{time.Seconds:D2}";
            else
                return $"{time.Minutes}:{time.Seconds:D2}";
        }

        private int TimeToSeconds(TimeSpan time)
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
            int newBeginSeconds = beginSeconds + 10;

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
            int newBeginSeconds = beginSeconds - 10;

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
            int newEndSeconds = endSeconds + 10;

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
            int newEndSeconds = endSeconds - 10;

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
        private bool TryParseTime(string time, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            var parts = time.Split(':');

            Debug.WriteLine($"Parsing time: {time}");

            // Case 1: M:SS format (Minutes:Seconds)
            if (parts.Length == 2)
            {
                int minutes = int.Parse(parts[0]);
                int seconds = int.Parse(parts[1]);
                result = new TimeSpan(0, minutes, seconds);
                Debug.WriteLine($"Parsed time: {result}");
                return true;
            }

            // Case 2: MM:SS format (Hours:Minutes)
            if (parts.Length == 2 && parts[0].Length == 1)
            {
                int minutes = int.Parse(parts[0]);
                int seconds = int.Parse(parts[1]);
                result = new TimeSpan(0, minutes, seconds);
                Debug.WriteLine($"Parsed time: {result}");
                return true;
            }

            // Case 3: HH:MM:SS format (Hours:Minutes:Seconds)
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
            // Move backwards by VideoTimeOffset seconds
            var currentPosition = VideoPlayback.MediaPlayer.Position;
            var newPosition = currentPosition - TimeSpan.FromSeconds(VideoTimeOffset);

            // Ensure the position doesn't go below zero
            if (newPosition < TimeSpan.Zero)
            {
                newPosition = TimeSpan.Zero;
            }

            VideoPlayback.MediaPlayer.Position = newPosition;
        }

        private void ForwardsButton_Click(object sender, RoutedEventArgs e)
        {
            // Move forwards by VideoTimeOffset seconds
            var currentPosition = VideoPlayback.MediaPlayer.Position;
            var newPosition = currentPosition + TimeSpan.FromSeconds(VideoTimeOffset);

            // Ensure the position doesn't exceed the video length
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
        private bool IsValidNumber(string time)
        {
            return Regex.IsMatch(time, @"^\d+s?$");
        }
        private bool IsValidTimeFormat(string time)
        {
            return Regex.IsMatch(time, @"^(\d{1,2}):(\d{1,2}):?(\d{1,2})?$");
        }

    }
}

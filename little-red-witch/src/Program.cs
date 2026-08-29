using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]
[assembly: AssemblyInformationalVersion("1.2.0")]

namespace LittleRedWitch
{
    internal static class Program
    {
        private static Mutex instanceMutex;

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            instanceMutex = new Mutex(true, @"Local\LittleRedWitchDesktopPet", out createdNew);

            if (!createdNew)
            {
                MessageBox.Show("小紅巫已經在桌面上了。", "小紅巫", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                PetWindow window = new PetWindow();
                app.MainWindow = window;
                window.Show();
                app.Run();
            }
            finally
            {
                instanceMutex.ReleaseMutex();
                instanceMutex.Dispose();
            }
        }
    }

    internal enum PetState
    {
        Idle,
        WalkRight,
        WalkLeft,
        Wave,
        Jump
    }

    internal sealed class AnimationClip
    {
        public readonly int Row;
        public readonly int[] Durations;
        public readonly bool Loop;

        public AnimationClip(int row, bool loop, params int[] durations)
        {
            Row = row;
            Loop = loop;
            Durations = durations;
        }
    }

    internal sealed class SpriteAtlas
    {
        public const int CellWidth = 192;
        public const int CellHeight = 208;

        private readonly BitmapSource atlas;
        private readonly Dictionary<int, ImageSource> frameCache = new Dictionary<int, ImageSource>();

        public SpriteAtlas()
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                "LittleRedWitch.Resources.spritesheet.png");

            if (stream == null)
            {
                throw new InvalidOperationException("找不到內嵌的寵物圖集。");
            }

            using (stream)
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                atlas = image;
            }

            if (atlas.PixelWidth != 1536 || atlas.PixelHeight != 2288)
            {
                throw new InvalidOperationException("寵物圖集尺寸不正確，預期為 1536×2288。");
            }
        }

        public ImageSource GetFrame(int row, int column)
        {
            int key = (row * 8) + column;
            ImageSource cached;
            if (frameCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            CroppedBitmap frame = new CroppedBitmap(
                atlas,
                new Int32Rect(column * CellWidth, row * CellHeight, CellWidth, CellHeight));
            frame.Freeze();
            frameCache[key] = frame;
            return frame;
        }
    }

    internal sealed class PetWindow : Window
    {
        private const string StartupValueName = "LittleRedWitchDesktopPet";

        private readonly SpriteAtlas atlas;
        private readonly Image sprite;
        private readonly DispatcherTimer frameTimer;
        private readonly DispatcherTimer motionTimer;
        private readonly DispatcherTimer decisionTimer;
        private readonly DispatcherTimer pointerTimer;
        private readonly Random random = new Random();
        private readonly Dictionary<PetState, AnimationClip> clips;

        private PetState currentState = PetState.Idle;
        private AnimationClip currentClip;
        private int currentFrame;
        private bool isRoaming = true;
        private bool isDragging;
        private bool isLooking;
        private double walkSpeed;
        private double scale = 0.9;

        private MenuItem roamingItem;
        private MenuItem topmostItem;
        private MenuItem startupItem;
        private MenuItem autoUpdatesItem;
        private MenuItem checkUpdatesItem;
        private bool isCheckingForUpdates;
        private readonly Dictionary<MenuItem, double> sizeItems = new Dictionary<MenuItem, double>();

        public PetWindow()
        {
            Title = "小紅巫";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.Manual;

            atlas = new SpriteAtlas();
            sprite = new Image();
            sprite.Stretch = Stretch.Uniform;
            sprite.SnapsToDevicePixels = true;
            sprite.ToolTip = "小紅巫｜拖曳移動、雙擊揮手、右鍵開啟選單";
            RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.HighQuality);
            Content = sprite;

            clips = CreateClips();

            frameTimer = new DispatcherTimer(DispatcherPriority.Render);
            frameTimer.Tick += OnFrameTick;

            motionTimer = new DispatcherTimer(DispatcherPriority.Render);
            motionTimer.Interval = TimeSpan.FromMilliseconds(30);
            motionTimer.Tick += OnMotionTick;

            decisionTimer = new DispatcherTimer(DispatcherPriority.Background);
            decisionTimer.Interval = TimeSpan.FromMilliseconds(3000);
            decisionTimer.Tick += OnDecisionTick;

            pointerTimer = new DispatcherTimer(DispatcherPriority.Background);
            pointerTimer.Interval = TimeSpan.FromMilliseconds(120);
            pointerTimer.Tick += OnPointerTick;

            ContextMenu = BuildContextMenu();
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            Loaded += OnLoaded;
            Closed += OnClosed;

            SetScale(scale);
            StartState(PetState.Idle);
        }

        private static Dictionary<PetState, AnimationClip> CreateClips()
        {
            Dictionary<PetState, AnimationClip> result = new Dictionary<PetState, AnimationClip>();
            result[PetState.Idle] = new AnimationClip(0, true, 280, 110, 110, 140, 140, 320);
            result[PetState.WalkRight] = new AnimationClip(1, true, 120, 120, 120, 120, 120, 120, 120, 220);
            result[PetState.WalkLeft] = new AnimationClip(2, true, 120, 120, 120, 120, 120, 120, 120, 220);
            result[PetState.Wave] = new AnimationClip(3, false, 140, 140, 140, 280);
            result[PetState.Jump] = new AnimationClip(4, false, 140, 140, 140, 140, 280);
            return result;
        }

        private ContextMenu BuildContextMenu()
        {
            ContextMenu menu = new ContextMenu();

            MenuItem greeting = new MenuItem();
            greeting.Header = "揮揮手";
            greeting.Click += delegate { PlayOneShot(PetState.Wave); };
            menu.Items.Add(greeting);

            MenuItem jump = new MenuItem();
            jump.Header = "跳一下";
            jump.Click += delegate { PlayOneShot(PetState.Jump); };
            menu.Items.Add(jump);

            roamingItem = new MenuItem();
            roamingItem.Click += OnToggleRoaming;
            menu.Items.Add(roamingItem);

            MenuItem reset = new MenuItem();
            reset.Header = "回到右下角";
            reset.Click += delegate { MoveToBottomRight(); StartState(PetState.Idle); };
            menu.Items.Add(reset);

            menu.Items.Add(new Separator());

            MenuItem sizeMenu = new MenuItem();
            sizeMenu.Header = "尺寸";
            sizeMenu.Items.Add(CreateSizeItem("口袋（25%）", 0.25));
            sizeMenu.Items.Add(CreateSizeItem("超迷你（35%）", 0.35));
            sizeMenu.Items.Add(CreateSizeItem("迷你（50%）", 0.50));
            sizeMenu.Items.Add(CreateSizeItem("小小（65%）", 0.65));
            sizeMenu.Items.Add(CreateSizeItem("小（75%）", 0.75));
            sizeMenu.Items.Add(CreateSizeItem("中（90%）", 0.90));
            sizeMenu.Items.Add(CreateSizeItem("大（115%）", 1.15));
            sizeMenu.Items.Add(CreateSizeItem("特大（140%）", 1.40));
            menu.Items.Add(sizeMenu);

            topmostItem = new MenuItem();
            topmostItem.Header = "永遠置頂";
            topmostItem.IsCheckable = true;
            topmostItem.Click += delegate { Topmost = topmostItem.IsChecked; };
            menu.Items.Add(topmostItem);

            startupItem = new MenuItem();
            startupItem.Header = "開機自動啟動";
            startupItem.IsCheckable = true;
            startupItem.Click += OnToggleStartup;
            menu.Items.Add(startupItem);

            menu.Items.Add(new Separator());

            autoUpdatesItem = new MenuItem();
            autoUpdatesItem.Header = "自動檢查更新";
            autoUpdatesItem.IsCheckable = true;
            autoUpdatesItem.Click += delegate
            {
                UpdateService.SetAutoCheckEnabled(autoUpdatesItem.IsChecked);
            };
            menu.Items.Add(autoUpdatesItem);

            checkUpdatesItem = new MenuItem();
            checkUpdatesItem.Header = "檢查更新";
            checkUpdatesItem.Click += delegate { BeginUpdateCheck(true); };
            menu.Items.Add(checkUpdatesItem);

            menu.Items.Add(new Separator());

            MenuItem about = new MenuItem();
            about.Header = "關於小紅巫";
            about.Click += delegate
            {
                MessageBox.Show(
                    "小紅巫 1.2\n\n透明桌面寵物，不需要 Codex。\n拖曳可以移動，雙擊會向你揮手。\n支援八種尺寸與 GitHub 自動更新。",
                    "關於小紅巫",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            };
            menu.Items.Add(about);

            MenuItem exit = new MenuItem();
            exit.Header = "關閉小紅巫";
            exit.Click += delegate { Close(); };
            menu.Items.Add(exit);

            menu.Opened += delegate
            {
                roamingItem.Header = isRoaming ? "暫停漫遊" : "繼續漫遊";
                topmostItem.IsChecked = Topmost;
                startupItem.IsChecked = IsStartupEnabled();
                autoUpdatesItem.IsChecked = UpdateService.GetAutoCheckEnabled();
                UpdateSizeChecks();
            };

            return menu;
        }

        private MenuItem CreateSizeItem(string title, double itemScale)
        {
            MenuItem item = new MenuItem();
            item.Header = title;
            item.IsCheckable = true;
            item.Click += delegate { SetScale(itemScale); };
            sizeItems[item] = itemScale;
            return item;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            MoveToBottomRight();
            motionTimer.Start();
            decisionTimer.Start();
            pointerTimer.Start();

            if (UpdateService.ShouldRunAutomaticCheck())
            {
                BeginUpdateCheck(false);
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            frameTimer.Stop();
            motionTimer.Stop();
            decisionTimer.Stop();
            pointerTimer.Stop();
        }

        private void OnFrameTick(object sender, EventArgs e)
        {
            currentFrame++;
            if (currentFrame >= currentClip.Durations.Length)
            {
                if (currentClip.Loop)
                {
                    currentFrame = 0;
                }
                else
                {
                    StartState(PetState.Idle);
                    return;
                }
            }

            ShowAnimationFrame();
        }

        private void OnMotionTick(object sender, EventArgs e)
        {
            if (!isRoaming || isDragging)
            {
                return;
            }

            if (currentState != PetState.WalkLeft && currentState != PetState.WalkRight)
            {
                return;
            }

            Rect area = SystemParameters.WorkArea;
            Left += walkSpeed;

            if (Left <= area.Left)
            {
                Left = area.Left;
                BeginWalk(true);
            }
            else if (Left + ActualWidth >= area.Right)
            {
                Left = area.Right - ActualWidth;
                BeginWalk(false);
            }

            Top = area.Bottom - ActualHeight - 4;
        }

        private void OnDecisionTick(object sender, EventArgs e)
        {
            if (!isRoaming || isDragging || currentState == PetState.Wave || currentState == PetState.Jump)
            {
                return;
            }

            int choice = random.Next(100);
            if (choice < 35)
            {
                StartState(PetState.Idle);
            }
            else if (choice < 75)
            {
                BeginWalk(random.Next(2) == 0);
            }
            else if (choice < 90)
            {
                PlayOneShot(PetState.Wave);
            }
            else
            {
                PlayOneShot(PetState.Jump);
            }

            decisionTimer.Interval = TimeSpan.FromMilliseconds(random.Next(2400, 4600));
        }

        private void OnPointerTick(object sender, EventArgs e)
        {
            if (isDragging || currentState != PetState.Idle)
            {
                return;
            }

            NativePoint cursor;
            if (!NativeMethods.GetCursorPos(out cursor))
            {
                return;
            }

            Point local = PointFromScreen(new Point(cursor.X, cursor.Y));
            double dx = local.X - (ActualWidth / 2.0);
            double dy = local.Y - (ActualHeight / 2.0);
            double distance = Math.Sqrt((dx * dx) + (dy * dy));

            if (distance < 85 || distance > 650)
            {
                if (isLooking)
                {
                    isLooking = false;
                    StartState(PetState.Idle);
                }
                return;
            }

            double angle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
            if (angle < 0)
            {
                angle += 360.0;
            }

            int directionIndex = ((int)Math.Round(angle / 22.5)) % 16;
            int row = directionIndex < 8 ? 9 : 10;
            int column = directionIndex < 8 ? directionIndex : directionIndex - 8;

            isLooking = true;
            frameTimer.Stop();
            sprite.Source = atlas.GetFrame(row, column);
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                PlayOneShot(PetState.Wave);
                return;
            }

            isDragging = true;
            isLooking = false;
            StartState(PetState.Idle);
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                isDragging = false;
                ClampToWorkArea();
            }
        }

        private void OnToggleRoaming(object sender, RoutedEventArgs e)
        {
            isRoaming = !isRoaming;
            if (!isRoaming)
            {
                StartState(PetState.Idle);
            }
            roamingItem.Header = isRoaming ? "暫停漫遊" : "繼續漫遊";
        }

        private void OnToggleStartup(object sender, RoutedEventArgs e)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null)
                    {
                        throw new InvalidOperationException("無法開啟 Windows 啟動設定。");
                    }

                    if (startupItem.IsChecked)
                    {
                        string executable = Assembly.GetExecutingAssembly().Location;
                        key.SetValue(StartupValueName, "\"" + executable + "\"");
                    }
                    else
                    {
                        key.DeleteValue(StartupValueName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                startupItem.IsChecked = !startupItem.IsChecked;
                MessageBox.Show("無法更新開機啟動設定：\n" + ex.Message, "小紅巫", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BeginUpdateCheck(bool manual)
        {
            if (isCheckingForUpdates)
            {
                if (manual)
                {
                    MessageBox.Show("小紅巫正在檢查更新。", "小紅巫更新", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            isCheckingForUpdates = true;
            checkUpdatesItem.IsEnabled = false;
            checkUpdatesItem.Header = "正在檢查更新…";

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateCheckResult result = null;
                Exception failure = null;
                try
                {
                    result = UpdateService.CheckLatestRelease();
                    UpdateService.RecordSuccessfulCheck();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    CompleteUpdateCheck(manual, result, failure);
                }));
            });
        }

        private void CompleteUpdateCheck(bool manual, UpdateCheckResult result, Exception failure)
        {
            isCheckingForUpdates = false;
            checkUpdatesItem.IsEnabled = true;
            checkUpdatesItem.Header = "檢查更新";

            if (failure != null)
            {
                if (manual)
                {
                    MessageBox.Show(
                        "目前無法檢查更新：\n" + failure.Message,
                        "小紅巫更新",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return;
            }

            if (result == null || !result.UpdateAvailable)
            {
                if (manual)
                {
                    MessageBox.Show(
                        "你使用的已經是最新版（v" + UpdateService.CurrentVersion + "）。",
                        "小紅巫更新",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return;
            }

            MessageBoxResult choice = MessageBox.Show(
                "發現新版小紅巫 " + result.Release.TagName + "。\n\n要現在下載並安裝嗎？",
                "小紅巫有新版本",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (choice == MessageBoxResult.Yes)
            {
                BeginUpdateDownload(result.Release);
            }
        }

        private void BeginUpdateDownload(UpdateRelease release)
        {
            isCheckingForUpdates = true;
            checkUpdatesItem.IsEnabled = false;
            checkUpdatesItem.Header = "正在下載更新…";

            ThreadPool.QueueUserWorkItem(delegate
            {
                PreparedUpdate prepared = null;
                Exception failure = null;
                try
                {
                    prepared = UpdateService.DownloadAndPrepare(release);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    isCheckingForUpdates = false;
                    checkUpdatesItem.IsEnabled = true;
                    checkUpdatesItem.Header = "檢查更新";

                    if (failure != null)
                    {
                        MessageBox.Show(
                            "更新下載或驗證失敗：\n" + failure.Message,
                            "小紅巫更新",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }

                    try
                    {
                        UpdateService.LaunchUpdater(prepared);
                        Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "無法啟動更新程式：\n" + ex.Message,
                            "小紅巫更新",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }));
            });
        }

        private void PlayOneShot(PetState state)
        {
            isLooking = false;
            StartState(state);
        }

        private void BeginWalk(bool right)
        {
            if (!isRoaming)
            {
                return;
            }

            isLooking = false;
            walkSpeed = right ? 1.8 : -1.8;
            StartState(right ? PetState.WalkRight : PetState.WalkLeft);
        }

        private void StartState(PetState state)
        {
            isLooking = false;
            currentState = state;
            currentClip = clips[state];
            currentFrame = 0;
            ShowAnimationFrame();
            frameTimer.Start();
        }

        private void ShowAnimationFrame()
        {
            sprite.Source = atlas.GetFrame(currentClip.Row, currentFrame);
            frameTimer.Interval = TimeSpan.FromMilliseconds(currentClip.Durations[currentFrame]);
        }

        private void SetScale(double newScale)
        {
            scale = newScale;
            Width = SpriteAtlas.CellWidth * scale;
            Height = SpriteAtlas.CellHeight * scale;
            ClampToWorkArea();
            UpdateSizeChecks();
        }

        private void UpdateSizeChecks()
        {
            if (sizeItems.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<MenuItem, double> entry in sizeItems)
            {
                entry.Key.IsChecked = Math.Abs(scale - entry.Value) < 0.01;
            }
        }

        private void MoveToBottomRight()
        {
            Rect area = SystemParameters.WorkArea;
            Left = area.Right - Width - 24;
            Top = area.Bottom - Height - 4;
        }

        private void ClampToWorkArea()
        {
            if (!IsLoaded)
            {
                return;
            }

            Rect area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, Math.Min(Left, area.Right - ActualWidth));
            Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - ActualHeight));
        }

        private static bool IsStartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", false))
            {
                return key != null && key.GetValue(StartupValueName) != null;
            }
        }
    }

    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out NativePoint point);
    }
}

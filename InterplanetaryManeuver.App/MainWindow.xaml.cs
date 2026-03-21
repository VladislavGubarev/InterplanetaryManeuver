using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using InterplanetaryManeuver.App.ViewModels;
using Wpf.Ui.Controls;

namespace InterplanetaryManeuver.App;

public partial class MainWindow : FluentWindow
{
    private bool _pseudoMaximized;
    private Rect _restoreBounds;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Loaded += (_, _) => SelectSection(0);
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Maximized)
            {
                // Безрамочное окно может перекрывать панель задач, поэтому
                // принудительно делаем «псевдо-разворачивание» по рабочей области.
                WindowState = WindowState.Normal;
                if (!_pseudoMaximized)
                    ToggleMaximize();
            }
        };
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton item)
            return;

        if (item.Tag is null || !int.TryParse(item.Tag.ToString(), out int index))
            return;

        SelectSection(index);
    }

    private void SelectSection(int index)
    {
        MainTabs.SelectedIndex = index;
        SimulationButton.IsChecked = index == 0;
        OptimizationButton.IsChecked = index == 1;
        AnimationButton.IsChecked = index == 2;
        SandboxButton.IsChecked = index == 3;

        switch (index)
        {
            case 0:
                PageHeaderTitle.Text = "Симуляция";
                PageHeaderSubtitle.Text = "Расчёт траекторий, метрики пролёта у Юпитера и графики состояния системы.";
                break;
            case 1:
                PageHeaderTitle.Text = "Оптимизация";
                PageHeaderSubtitle.Text = "Перебор параметров гравитационного манёвра и поиск лучшего решения по приросту скорости.";
                break;
            case 2:
                PageHeaderTitle.Text = "Анимация";
                PageHeaderSubtitle.Text = "Живая орбитальная сцена для покадрового просмотра движения тел и аппарата.";
                break;
            case 3:
                PageHeaderTitle.Text = "Песочница";
                PageHeaderSubtitle.Text = "Редактор своей системы тел с запуском RK-45 и мгновенным просмотром результата.";
                break;
        }
    }

    private void ToggleMaximize()
    {
        if (_pseudoMaximized)
        {
            _pseudoMaximized = false;
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            return;
        }

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        _restoreBounds = new Rect(Left, Top, Width, Height);
        var work = GetWorkingAreaInDips();

        _pseudoMaximized = true;
        Left = work.Left;
        Top = work.Top;
        Width = work.Width;
        Height = work.Height;
    }

    private Rect GetWorkingAreaInDips()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return new Rect(
                SystemParameters.WorkArea.Left,
                SystemParameters.WorkArea.Top,
                SystemParameters.WorkArea.Width,
                SystemParameters.WorkArea.Height);
        }

        var waPx = GetWorkingAreaPx(hwnd);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
            return new Rect(waPx.Left, waPx.Top, waPx.Width, waPx.Height);

        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var topLeft = fromDevice.Transform(new Point(waPx.Left, waPx.Top));
        var bottomRight = fromDevice.Transform(new Point(waPx.Right, waPx.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private static NativeRect GetWorkingAreaPx(IntPtr hwnd)
    {
        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return new NativeRect(
                (int)SystemParameters.WorkArea.Left,
                (int)SystemParameters.WorkArea.Top,
                (int)SystemParameters.WorkArea.Right,
                (int)SystemParameters.WorkArea.Bottom);
        }

        return new NativeRect(
            monitorInfo.rcWork.Left,
            monitorInfo.rcWork.Top,
            monitorInfo.rcWork.Right,
            monitorInfo.rcWork.Bottom);
    }

    private readonly record struct NativeRect(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    private static class NativeMethods
    {
        public const int MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}

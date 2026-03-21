using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using InterplanetaryManeuver.App.Models;
using InterplanetaryManeuver.App.Mvvm;
using InterplanetaryManeuver.App.Services;
using Microsoft.Win32;
using PhysicsSim.Core;
using PhysicsSim.Core.Ode;

namespace InterplanetaryManeuver.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly SolidColorBrush SunBrush = new(Color.FromRgb(0xFF, 0xD2, 0x4A));
    private static readonly SolidColorBrush JupiterBrush = new(Color.FromRgb(0x5A, 0xE4, 0xFF));
    private static readonly SolidColorBrush SaturnBrush = new(Color.FromRgb(0xFF, 0xB4, 0x5A));
    private static readonly SolidColorBrush SpacecraftBrush = new(Color.FromRgb(0xEE, 0xF2, 0xFF));
    private static readonly SolidColorBrush VxBrush = new(Color.FromRgb(0x7E, 0xE0, 0x9C));
    private static readonly SolidColorBrush VyBrush = new(Color.FromRgb(0xFF, 0x90, 0x6E));
    private static readonly SolidColorBrush VzBrush = new(Color.FromRgb(0xC5, 0x9B, 0xFF));

    private readonly ScenarioFactory _scenarioFactory = new(new HorizonsEphemerisService());

    private SimulationPreset? _selectedPreset;
    private double _durationDays = 420;
    private double _outputStepHours = 6;
    private double _absTol = 1e3;
    private double _relTol = 1e-9;
    private string _epochText = DateTime.UtcNow.ToString("yyyy-MM-dd 00:00", CultureInfo.InvariantCulture);
    private double _phaseAngleDeg = -35.0;
    private double _headingAngleDeg = 11.0;
    private double _vInfinityKms = 9.5;
    private bool _isRunning;
    private string _statusText = "Готово.";
    private string _metricsText = "Симуляция еще не запускалась.";
    private string _reportText = "Запустите симуляцию, и здесь появится отчет.";
    private string _optimizationText = "Оптимизация еще не запускалась.";
    private bool _hasResults;
    private IReadOnlyList<LineSeries> _orbitSeries = Array.Empty<LineSeries>();
    private IReadOnlyList<LineSeries> _speedSeries = Array.Empty<LineSeries>();
    private IReadOnlyList<LineSeries> _speedComponentSeries = Array.Empty<LineSeries>();

    private double _optPhaseMinDeg = -70;
    private double _optPhaseMaxDeg = 30;
    private int _optPhaseSamples = 7;
    private double _optHeadingMinDeg = -16;
    private double _optHeadingMaxDeg = 18;
    private int _optHeadingSamples = 8;
    private double _optVInfinityMinKms = 6.5;
    private double _optVInfinityMaxKms = 13.0;
    private int _optVInfinitySamples = 6;
    private EditableBody? _selectedCustomBody;
    private string _sandboxText = "Песочница еще не запускалась.";
    private AnimationSceneData? _animationScene;
    private int _animationFrameIndex;
    private int _animationFrameCount;
    private bool _isAnimationPlaying;
    private double _animationSpeedMultiplier = 1.0;

    private SimulationResult? _lastResult;
    private SimulationScenario? _lastScenario;
    private IntegrationSettings? _lastSettings;
    private FlybyMetrics? _lastFlybyMetrics;

    private CancellationTokenSource? _cts;
    private readonly DispatcherTimer _animationTimer;
    private readonly RelayCommand _runCommand;
    private readonly RelayCommand _optimizeCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _saveReportCommand;
    private readonly RelayCommand _exportCsvCommand;
    private readonly RelayCommand _runSandboxCommand;
    private readonly RelayCommand _addBodyCommand;
    private readonly RelayCommand _removeBodyCommand;
    private readonly RelayCommand _saveBodiesCommand;
    private readonly RelayCommand _loadBodiesCommand;
    private readonly RelayCommand _toggleAnimationCommand;
    private readonly RelayCommand _resetAnimationCommand;
    private readonly RelayCommand<EditableBody> _removeCustomBodyItemCommand;

    public MainViewModel()
    {
        FreezeBrushes();

        Presets = new ObservableCollection<SimulationPreset>(SimulationPreset.CreateDefaults());
        CustomBodies = new ObservableCollection<EditableBody>(CreateDefaultCustomBodies());
        CustomBodies.CollectionChanged += (_, _) =>
        {
            _runSandboxCommand?.RaiseCanExecuteChanged();
            _saveBodiesCommand?.RaiseCanExecuteChanged();
        };
        SelectedPreset = Presets.FirstOrDefault();

        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(45)
        };
        _animationTimer.Tick += (_, _) => AdvanceAnimationFrame();
        UpdateAnimationSpeed();

        _runCommand = new RelayCommand(() => _ = RunAsync(), () => !IsRunning);
        _optimizeCommand = new RelayCommand(() => _ = OptimizeAsync(), CanOptimize);
        _cancelCommand = new RelayCommand(Cancel, () => IsRunning);
        _saveReportCommand = new RelayCommand(SaveReport, () => HasResults && !IsRunning);
        _exportCsvCommand = new RelayCommand(ExportCsv, () => HasResults && !IsRunning);
        _runSandboxCommand = new RelayCommand(() => _ = RunSandboxAsync(), () => !IsRunning && CustomBodies.Count > 0);
        _addBodyCommand = new RelayCommand(AddCustomBody, () => !IsRunning);
        _removeBodyCommand = new RelayCommand(RemoveSelectedCustomBody, () => !IsRunning && SelectedCustomBody is not null);
        _saveBodiesCommand = new RelayCommand(SaveCustomBodies, () => CustomBodies.Count > 0);
        _loadBodiesCommand = new RelayCommand(LoadCustomBodies, () => !IsRunning);
        _toggleAnimationCommand = new RelayCommand(ToggleAnimationPlayback, () => AnimationFrameCount > 1);
        _resetAnimationCommand = new RelayCommand(ResetAnimation, () => AnimationFrameCount > 0);
        _removeCustomBodyItemCommand = new RelayCommand<EditableBody>(RemoveCustomBodyItem, body => !IsRunning && body is not null);

        RunCommand = _runCommand;
        OptimizeCommand = _optimizeCommand;
        CancelCommand = _cancelCommand;
        SaveReportCommand = _saveReportCommand;
        ExportCsvCommand = _exportCsvCommand;
        RunSandboxCommand = _runSandboxCommand;
        AddBodyCommand = _addBodyCommand;
        RemoveBodyCommand = _removeBodyCommand;
        SaveBodiesCommand = _saveBodiesCommand;
        LoadBodiesCommand = _loadBodiesCommand;
        ToggleAnimationCommand = _toggleAnimationCommand;
        ResetAnimationCommand = _resetAnimationCommand;
        RemoveCustomBodyItemCommand = _removeCustomBodyItemCommand;
    }

    public ObservableCollection<SimulationPreset> Presets { get; }
    public ObservableCollection<EditableBody> CustomBodies { get; }

    public SimulationPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!SetProperty(ref _selectedPreset, value))
                return;

            ResetOutputs();
            _optimizeCommand?.RaiseCanExecuteChanged();
        }
    }

    public double DurationDays
    {
        get => _durationDays;
        set => SetProperty(ref _durationDays, value);
    }

    public double OutputStepHours
    {
        get => _outputStepHours;
        set => SetProperty(ref _outputStepHours, value);
    }

    public double AbsTol
    {
        get => _absTol;
        set => SetProperty(ref _absTol, value);
    }

    public double RelTol
    {
        get => _relTol;
        set => SetProperty(ref _relTol, value);
    }

    public string EpochText
    {
        get => _epochText;
        set => SetProperty(ref _epochText, value);
    }

    public double PhaseAngleDeg
    {
        get => _phaseAngleDeg;
        set => SetProperty(ref _phaseAngleDeg, value);
    }

    public double HeadingAngleDeg
    {
        get => _headingAngleDeg;
        set => SetProperty(ref _headingAngleDeg, value);
    }

    public double VInfinityKms
    {
        get => _vInfinityKms;
        set => SetProperty(ref _vInfinityKms, value);
    }

    public double OptPhaseMinDeg
    {
        get => _optPhaseMinDeg;
        set => SetProperty(ref _optPhaseMinDeg, value);
    }

    public double OptPhaseMaxDeg
    {
        get => _optPhaseMaxDeg;
        set => SetProperty(ref _optPhaseMaxDeg, value);
    }

    public int OptPhaseSamples
    {
        get => _optPhaseSamples;
        set => SetProperty(ref _optPhaseSamples, value);
    }

    public double OptHeadingMinDeg
    {
        get => _optHeadingMinDeg;
        set => SetProperty(ref _optHeadingMinDeg, value);
    }

    public double OptHeadingMaxDeg
    {
        get => _optHeadingMaxDeg;
        set => SetProperty(ref _optHeadingMaxDeg, value);
    }

    public int OptHeadingSamples
    {
        get => _optHeadingSamples;
        set => SetProperty(ref _optHeadingSamples, value);
    }

    public double OptVInfinityMinKms
    {
        get => _optVInfinityMinKms;
        set => SetProperty(ref _optVInfinityMinKms, value);
    }

    public double OptVInfinityMaxKms
    {
        get => _optVInfinityMaxKms;
        set => SetProperty(ref _optVInfinityMaxKms, value);
    }

    public int OptVInfinitySamples
    {
        get => _optVInfinitySamples;
        set => SetProperty(ref _optVInfinitySamples, value);
    }

    public EditableBody? SelectedCustomBody
    {
        get => _selectedCustomBody;
        set
        {
            if (!SetProperty(ref _selectedCustomBody, value))
                return;

            _removeBodyCommand?.RaiseCanExecuteChanged();
        }
    }

    public string SandboxText
    {
        get => _sandboxText;
        private set => SetProperty(ref _sandboxText, value);
    }

    public AnimationSceneData? AnimationScene
    {
        get => _animationScene;
        private set => SetProperty(ref _animationScene, value);
    }

    public int AnimationFrameIndex
    {
        get => _animationFrameIndex;
        set
        {
            int clamped = Math.Clamp(value, 0, Math.Max(0, AnimationFrameCount - 1));
            if (!SetProperty(ref _animationFrameIndex, clamped))
                return;

            RaisePropertyChanged(nameof(AnimationFrameLabel));
        }
    }

    public int AnimationFrameCount
    {
        get => _animationFrameCount;
        private set
        {
            if (!SetProperty(ref _animationFrameCount, value))
                return;

            _toggleAnimationCommand?.RaiseCanExecuteChanged();
            _resetAnimationCommand?.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(AnimationFrameLabel));
            RaisePropertyChanged(nameof(AnimationFrameMax));
        }
    }

    public bool IsAnimationPlaying
    {
        get => _isAnimationPlaying;
        private set
        {
            if (!SetProperty(ref _isAnimationPlaying, value))
                return;

            _toggleAnimationCommand?.RaiseCanExecuteChanged();
        }
    }

    public double AnimationSpeedMultiplier
    {
        get => _animationSpeedMultiplier;
        set
        {
            double clamped = Math.Clamp(value, 0.25, 4.0);
            if (!SetProperty(ref _animationSpeedMultiplier, clamped))
                return;

            UpdateAnimationSpeed();
            RaisePropertyChanged(nameof(AnimationSpeedLabel));
        }
    }

    public string AnimationFrameLabel => AnimationFrameCount == 0
        ? "Кадров нет"
        : $"Кадр {AnimationFrameIndex + 1} / {AnimationFrameCount}";

    public int AnimationFrameMax => Math.Max(0, AnimationFrameCount - 1);

    public string AnimationSpeedLabel => $"Скорость: {AnimationSpeedMultiplier:F2}x";

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value))
                return;

            _runCommand.RaiseCanExecuteChanged();
            _optimizeCommand.RaiseCanExecuteChanged();
            _cancelCommand.RaiseCanExecuteChanged();
            _saveReportCommand.RaiseCanExecuteChanged();
            _exportCsvCommand.RaiseCanExecuteChanged();
            _runSandboxCommand.RaiseCanExecuteChanged();
            _addBodyCommand.RaiseCanExecuteChanged();
            _removeBodyCommand.RaiseCanExecuteChanged();
            _loadBodiesCommand.RaiseCanExecuteChanged();
            _removeCustomBodyItemCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string MetricsText
    {
        get => _metricsText;
        private set => SetProperty(ref _metricsText, value);
    }

    public string ReportText
    {
        get => _reportText;
        private set => SetProperty(ref _reportText, value);
    }

    public string OptimizationText
    {
        get => _optimizationText;
        private set => SetProperty(ref _optimizationText, value);
    }

    public bool HasResults
    {
        get => _hasResults;
        private set
        {
            if (!SetProperty(ref _hasResults, value))
                return;

            _saveReportCommand.RaiseCanExecuteChanged();
            _exportCsvCommand.RaiseCanExecuteChanged();
        }
    }

    public IReadOnlyList<LineSeries> OrbitSeries
    {
        get => _orbitSeries;
        private set => SetProperty(ref _orbitSeries, value);
    }

    public IReadOnlyList<LineSeries> SpeedSeries
    {
        get => _speedSeries;
        private set => SetProperty(ref _speedSeries, value);
    }

    public IReadOnlyList<LineSeries> SpeedComponentSeries
    {
        get => _speedComponentSeries;
        private set => SetProperty(ref _speedComponentSeries, value);
    }

    public ICommand RunCommand { get; }
    public ICommand OptimizeCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SaveReportCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand RunSandboxCommand { get; }
    public ICommand AddBodyCommand { get; }
    public ICommand RemoveBodyCommand { get; }
    public ICommand SaveBodiesCommand { get; }
    public ICommand LoadBodiesCommand { get; }
    public ICommand ToggleAnimationCommand { get; }
    public ICommand ResetAnimationCommand { get; }
    public ICommand RemoveCustomBodyItemCommand { get; }

    private static void FreezeBrushes()
    {
        SunBrush.Freeze();
        JupiterBrush.Freeze();
        SaturnBrush.Freeze();
        SpacecraftBrush.Freeze();
        VxBrush.Freeze();
        VyBrush.Freeze();
        VzBrush.Freeze();
    }

    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "Отмена...";
    }

    private bool CanOptimize()
    {
        return !IsRunning && SelectedPreset?.Kind == SimulationPresetKind.JupiterFlyby;
    }

    private async Task RunAsync()
    {
        if (SelectedPreset is null)
        {
            StatusText = "Выберите пресет.";
            return;
        }

        if (!TryParseEpoch(out DateTime epochUtc, out string epochError))
        {
            StatusText = "Ошибка.";
            MetricsText = epochError;
            return;
        }

        SetBusyState("Загрузка эфемерид и запуск RK-45...");

        try
        {
            FlybySetup? flybySetup = SelectedPreset.Kind == SimulationPresetKind.JupiterFlyby ? GetCurrentFlybySetup() : null;
            SimulationScenario scenario = await BuildScenarioAsync(epochUtc, flybySetup, _cts!.Token);
            IntegrationSettings settings = CreateIntegrationSettings();
            SimulationResult result = await SimulateAsync(scenario, settings, _cts.Token);

            ApplySimulationOutputs(result, scenario, settings);
            OptimizationText = "Оптимизация еще не запускалась.";
            StatusText = $"Готово. Точек: {result.SampleCount:n0}";
        }
        catch (OperationCanceledException)
        {
            ApplyCanceledState();
        }
        catch (Exception ex)
        {
            ApplyErrorState(ex);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task OptimizeAsync()
    {
        if (SelectedPreset?.Kind != SimulationPresetKind.JupiterFlyby)
        {
            OptimizationText = "Оптимизация доступна только для сценария гравиманевра у Юпитера.";
            return;
        }

        if (!TryParseEpoch(out DateTime epochUtc, out string epochError))
        {
            StatusText = "Ошибка.";
            OptimizationText = epochError;
            return;
        }

        OptimizationSettings opt = CreateOptimizationSettings();
        if (opt.TotalSamples <= 0)
        {
            OptimizationText = "Параметры сетки оптимизации заданы неверно.";
            return;
        }

        SetBusyState($"Оптимизация: {opt.TotalSamples} траекторий...");
        OptimizationText = "Подготовка перебора...";

        try
        {
            var top = new List<OptimizationCandidate>();
            SimulationResult? bestResult = null;
            SimulationScenario? bestScenario = null;
            FlybyMetrics? bestMetrics = null;
            double bestScore = double.NegativeInfinity;
            IntegrationSettings settings = CreateIntegrationSettings();

            int candidateIndex = 0;
            foreach (double phase in Sweep(opt.PhaseMinDeg, opt.PhaseMaxDeg, opt.PhaseSamples))
            {
                foreach (double heading in Sweep(opt.HeadingMinDeg, opt.HeadingMaxDeg, opt.HeadingSamples))
                {
                    foreach (double vInf in Sweep(opt.VInfinityMinKms, opt.VInfinityMaxKms, opt.VInfinitySamples))
                    {
                        _cts!.Token.ThrowIfCancellationRequested();
                        candidateIndex++;

                        var flyby = new FlybySetup
                        {
                            StartDistanceMultiplier = 1.20,
                            PhaseAngleDeg = phase,
                            HeadingAngleDeg = heading,
                            VInfinityKms = vInf,
                        };

                        SimulationScenario scenario = await BuildScenarioAsync(epochUtc, flyby, _cts.Token);
                        SimulationResult result = await SimulateAsync(scenario, settings, _cts.Token);
                        FlybyMetrics metrics = BuildFlybyMetrics(result, scenario);
                        double score = ScoreCandidate(metrics);

                        var candidate = new OptimizationCandidate
                        {
                            Index = candidateIndex,
                            PhaseAngleDeg = phase,
                            HeadingAngleDeg = heading,
                            VInfinityKms = vInf,
                            DeltaVGainKms = metrics.DeltaVGainHeliocentric / 1000.0,
                            MinJupiterDistanceKm = metrics.MinDistanceToJupiter / 1000.0,
                            MinSaturnDistanceAu = metrics.MinDistanceToSaturn / AstronomyConstants.AstronomicalUnit,
                            Score = score,
                        };

                        InsertTopCandidate(top, candidate, 10);

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestResult = result;
                            bestScenario = scenario;
                            bestMetrics = metrics;
                            PhaseAngleDeg = phase;
                            HeadingAngleDeg = heading;
                            VInfinityKms = vInf;
                        }

                        OptimizationText = BuildOptimizationProgressText(candidateIndex, opt.TotalSamples, top);
                        StatusText = $"Оптимизация: {candidateIndex}/{opt.TotalSamples}";
                    }
                }
            }

            if (bestResult is null || bestScenario is null)
                throw new InvalidOperationException("Не удалось получить ни одной траектории.");

            ApplySimulationOutputs(bestResult, bestScenario, settings, bestMetrics);
            OptimizationText = BuildOptimizationSummary(top, bestMetrics, bestScore);
            StatusText = $"Оптимизация завершена. Лучший score = {bestScore:F3}";
        }
        catch (OperationCanceledException)
        {
            ApplyCanceledState();
            OptimizationText = "Оптимизация отменена.";
        }
        catch (Exception ex)
        {
            ApplyErrorState(ex);
            OptimizationText = ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RunSandboxAsync()
    {
        if (CustomBodies.Count == 0)
        {
            SandboxText = "Добавьте хотя бы одно тело.";
            return;
        }

        if (!TryParseEpoch(out DateTime epochUtc, out string epochError))
        {
            StatusText = "Ошибка.";
            SandboxText = epochError;
            return;
        }

        SetBusyState("Запуск песочницы...");
        SandboxText = "Выполняется...";

        try
        {
            SimulationScenario scenario = CreateCustomScenario(epochUtc);
            IntegrationSettings settings = CreateIntegrationSettings();
            SimulationResult result = await SimulateAsync(scenario, settings, _cts!.Token);
            ApplySimulationOutputs(result, scenario, settings);
            SandboxText = BuildSandboxSummary(scenario, result);
            StatusText = $"Песочница готова. Точек: {result.SampleCount:n0}";
        }
        catch (OperationCanceledException)
        {
            ApplyCanceledState();
            SandboxText = "Песочница отменена.";
        }
        catch (Exception ex)
        {
            ApplyErrorState(ex);
            SandboxText = ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void AddCustomBody()
    {
        var body = new EditableBody
        {
            Name = $"Тело {CustomBodies.Count + 1}",
            Mass = 1e22,
            XAu = 2.0 + 0.2 * CustomBodies.Count,
            VxKms = 0,
            VyKms = 15,
            VzKms = 0,
        };
        CustomBodies.Add(body);
        SelectedCustomBody = body;
        _runSandboxCommand.RaiseCanExecuteChanged();
        _saveBodiesCommand.RaiseCanExecuteChanged();
    }

    private void RemoveSelectedCustomBody()
    {
        if (SelectedCustomBody is null)
            return;

        CustomBodies.Remove(SelectedCustomBody);
        SelectedCustomBody = CustomBodies.FirstOrDefault();
        _runSandboxCommand.RaiseCanExecuteChanged();
        _saveBodiesCommand.RaiseCanExecuteChanged();
    }

    private void RemoveCustomBodyItem(EditableBody? body)
    {
        if (body is null)
            return;

        if (ReferenceEquals(SelectedCustomBody, body))
            SelectedCustomBody = null;

        CustomBodies.Remove(body);
        if (SelectedCustomBody is null)
            SelectedCustomBody = CustomBodies.FirstOrDefault();
        _runSandboxCommand.RaiseCanExecuteChanged();
        _saveBodiesCommand.RaiseCanExecuteChanged();
    }

    private void SaveCustomBodies()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Сохранить набор тел",
            Filter = "JSON (*.json)|*.json|Все файлы (*.*)|*.*",
            FileName = $"sandbox_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            AddExtension = true,
            OverwritePrompt = true,
        };

        if (dlg.ShowDialog() != true)
            return;

        string json = System.Text.Json.JsonSerializer.Serialize(CustomBodies, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(dlg.FileName, json, Encoding.UTF8);
        SandboxText = $"Набор тел сохранен: {Path.GetFileName(dlg.FileName)}";
    }

    private void LoadCustomBodies()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Загрузить набор тел",
            Filter = "JSON (*.json)|*.json|Все файлы (*.*)|*.*",
            Multiselect = false,
        };

        if (dlg.ShowDialog() != true)
            return;

        string json = File.ReadAllText(dlg.FileName, Encoding.UTF8);
        List<EditableBody>? bodies = System.Text.Json.JsonSerializer.Deserialize<List<EditableBody>>(json);
        if (bodies is null || bodies.Count == 0)
            throw new InvalidOperationException("Файл не содержит тел.");

        CustomBodies.Clear();
        foreach (EditableBody body in bodies)
            CustomBodies.Add(body);

        SelectedCustomBody = CustomBodies.FirstOrDefault();
        SandboxText = $"Загружено тел: {CustomBodies.Count}";
        _runSandboxCommand.RaiseCanExecuteChanged();
        _saveBodiesCommand.RaiseCanExecuteChanged();
    }

    private void ToggleAnimationPlayback()
    {
        if (AnimationFrameCount <= 1)
            return;

        if (IsAnimationPlaying)
        {
            _animationTimer.Stop();
            IsAnimationPlaying = false;
            return;
        }

        _animationTimer.Start();
        IsAnimationPlaying = true;
    }

    private void ResetAnimation()
    {
        _animationTimer?.Stop();
        IsAnimationPlaying = false;
        AnimationFrameIndex = 0;
    }

    private void AdvanceAnimationFrame()
    {
        if (AnimationFrameCount <= 1)
        {
            ResetAnimation();
            return;
        }

        if (AnimationFrameIndex >= AnimationFrameCount - 1)
        {
            _animationTimer?.Stop();
            IsAnimationPlaying = false;
            return;
        }

        AnimationFrameIndex++;
    }

    private void UpdateAnimationSpeed()
    {
        double milliseconds = 45.0 / Math.Max(0.25, AnimationSpeedMultiplier);
        _animationTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 12.0, 180.0));
    }

    private void SetBusyState(string status)
    {
        IsRunning = true;
        StatusText = status;
        MetricsText = "Выполняется...";
        ReportText = "Выполняется...";

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
    }

    private async Task<SimulationScenario> BuildScenarioAsync(DateTime epochUtc, FlybySetup? flybySetup, CancellationToken cancellationToken)
    {
        return await _scenarioFactory.CreateAsync(SelectedPreset!.Kind, epochUtc, flybySetup, cancellationToken);
    }

    private async Task<SimulationResult> SimulateAsync(
        SimulationScenario scenario,
        IntegrationSettings settings,
        CancellationToken cancellationToken)
    {
        double t0 = 0;
        double t1 = DurationDays * 86400.0;
        double outDt = OutputStepHours * 3600.0;

        var system = new NBodySystem(
            scenario.GravitationalConstant,
            scenario.Bodies,
            scenario.ToBarycentricFrame);

        return await Task.Run(
            () => NBodySimulator.Simulate(system, t0, t1, outDt, settings, cancellationToken),
            cancellationToken);
    }

    private IntegrationSettings CreateIntegrationSettings()
    {
        double outDt = OutputStepHours * 3600.0;
        return new IntegrationSettings
        {
            AbsTol = AbsTol,
            RelTol = RelTol,
            InitialStep = Math.Min(3600.0, outDt),
            MinStep = 1e-3,
            MaxStep = Math.Max(3600.0, 5 * outDt),
        };
    }

    private FlybySetup GetCurrentFlybySetup()
    {
        return new FlybySetup
        {
            StartDistanceMultiplier = 1.20,
            PhaseAngleDeg = PhaseAngleDeg,
            HeadingAngleDeg = HeadingAngleDeg,
            VInfinityKms = VInfinityKms,
        };
    }

    private OptimizationSettings CreateOptimizationSettings()
    {
        return new OptimizationSettings
        {
            PhaseMinDeg = OptPhaseMinDeg,
            PhaseMaxDeg = OptPhaseMaxDeg,
            PhaseSamples = Math.Max(1, OptPhaseSamples),
            HeadingMinDeg = OptHeadingMinDeg,
            HeadingMaxDeg = OptHeadingMaxDeg,
            HeadingSamples = Math.Max(1, OptHeadingSamples),
            VInfinityMinKms = OptVInfinityMinKms,
            VInfinityMaxKms = OptVInfinityMaxKms,
            VInfinitySamples = Math.Max(1, OptVInfinitySamples),
        };
    }

    private bool TryParseEpoch(out DateTime epochUtc, out string error)
    {
        if (DateTime.TryParse(
                EpochText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out epochUtc))
        {
            error = string.Empty;
            return true;
        }

        error = "Не удалось разобрать эпоху. Используйте формат yyyy-MM-dd HH:mm";
        return false;
    }

    private void ApplySimulationOutputs(
        SimulationResult result,
        SimulationScenario scenario,
        IntegrationSettings settings,
        FlybyMetrics? flybyMetrics = null)
    {
        BuildPlots(result, scenario);
        FlybyMetrics? metrics = flybyMetrics ?? TryBuildFlybyMetrics(result, scenario);
        BuildMetrics(result, scenario, metrics);

        _lastResult = result;
        _lastScenario = scenario;
        _lastSettings = settings;
        _lastFlybyMetrics = metrics;
        ReportText = BuildReport(result, scenario, settings, metrics, 0, DurationDays * 86400.0, OutputStepHours * 3600.0);
        UpdateAnimationScene(result, scenario);
        HasResults = true;
    }

    private void BuildPlots(SimulationResult result, SimulationScenario scenario)
    {
        int sunIndex = Math.Clamp(scenario.SunIndex, 0, result.BodyCount - 1);
        int scIndex = scenario.SpacecraftIndex;

        var orbit = new List<LineSeries>();
        for (int body = 0; body < result.BodyCount; body++)
        {
            var pts = new Point[result.SampleCount];
            for (int i = 0; i < result.SampleCount; i++)
            {
                Vector3d p = result.Positions[i][body] - result.Positions[i][sunIndex];
                pts[i] = new Point(
                    p.X / AstronomyConstants.AstronomicalUnit,
                    p.Y / AstronomyConstants.AstronomicalUnit);
            }

            orbit.Add(new LineSeries
            {
                Name = result.BodyNames[body],
                Points = pts,
                Stroke = PickBrush(result.BodyNames[body]),
                Thickness = body == scIndex ? 2.4 : 2.0,
            });
        }

        OrbitSeries = orbit;

        if (scIndex < 0 || scIndex >= result.BodyCount)
        {
            SpeedSeries = Array.Empty<LineSeries>();
            SpeedComponentSeries = Array.Empty<LineSeries>();
            return;
        }

        var speedPts = new Point[result.SampleCount];
        var vxPts = new Point[result.SampleCount];
        var vyPts = new Point[result.SampleCount];
        var vzPts = new Point[result.SampleCount];

        for (int i = 0; i < result.SampleCount; i++)
        {
            double days = result.Times[i] / 86400.0;
            Vector3d heliocentricVelocity = result.Velocities[i][scIndex] - result.Velocities[i][sunIndex];
            speedPts[i] = new Point(days, heliocentricVelocity.Length() / 1000.0);
            vxPts[i] = new Point(days, heliocentricVelocity.X / 1000.0);
            vyPts[i] = new Point(days, heliocentricVelocity.Y / 1000.0);
            vzPts[i] = new Point(days, heliocentricVelocity.Z / 1000.0);
        }

        SpeedSeries =
        [
            new LineSeries
            {
                Name = "Скорость КА |v| (км/с)",
                Points = speedPts,
                Stroke = SpacecraftBrush,
                Thickness = 2.2,
            },
        ];

        SpeedComponentSeries =
        [
            new LineSeries { Name = "Vx (км/с)", Points = vxPts, Stroke = VxBrush, Thickness = 1.9 },
            new LineSeries { Name = "Vy (км/с)", Points = vyPts, Stroke = VyBrush, Thickness = 1.9 },
            new LineSeries { Name = "Vz (км/с)", Points = vzPts, Stroke = VzBrush, Thickness = 1.9 },
        ];
    }

    private void BuildMetrics(SimulationResult result, SimulationScenario scenario, FlybyMetrics? flybyMetrics)
    {
        if (scenario.SpacecraftIndex < 0 || scenario.SpacecraftIndex >= result.BodyCount)
        {
            MetricsText = "В этом пресете нет космического аппарата.";
            return;
        }

        int sunIndex = Math.Clamp(scenario.SunIndex, 0, result.BodyCount - 1);
        double v0 = (result.Velocities[0][scenario.SpacecraftIndex] - result.Velocities[0][sunIndex]).Length();
        double v1 = (result.Velocities[^1][scenario.SpacecraftIndex] - result.Velocities[^1][sunIndex]).Length();

        var sb = new StringBuilder();
        sb.AppendLine("Система отсчета: гелиоцентрическая относительно Солнца.");
        sb.AppendLine($"Эпоха начальных данных: {scenario.EpochUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"v0: {v0 / 1000.0:F3} км/с");
        sb.AppendLine($"v1: {v1 / 1000.0:F3} км/с");

        if (flybyMetrics is not null)
        {
            sb.AppendLine($"Δv на входе/выходе SOI Юпитера: {flybyMetrics.DeltaVGainHeliocentric / 1000.0:F3} км/с");
            sb.AppendLine($"Мин. расстояние до Юпитера: {flybyMetrics.MinDistanceToJupiter / 1000.0:n0} км");
            sb.AppendLine($"Радиус SOI Юпитера: {flybyMetrics.JupiterSoiRadius / 1000.0:n0} км");
            if (double.IsFinite(flybyMetrics.MinDistanceToSaturn))
                sb.AppendLine($"Мин. расстояние до Сатурна: {flybyMetrics.MinDistanceToSaturn / AstronomyConstants.AstronomicalUnit:F4} а.е.");
            sb.AppendLine($"|v∞| до Юпитера: {flybyMetrics.InitialJupiterRelativeSpeed / 1000.0:F3} км/с");
            sb.AppendLine($"|v∞| после Юпитера: {flybyMetrics.FinalJupiterRelativeSpeed / 1000.0:F3} км/с");
        }

        MetricsText = sb.ToString().TrimEnd();
    }

    private FlybyMetrics? TryBuildFlybyMetrics(SimulationResult result, SimulationScenario scenario)
    {
        if (scenario.SpacecraftIndex < 0 || scenario.JupiterIndex < 0)
            return null;

        return BuildFlybyMetrics(result, scenario);
    }

    private FlybyMetrics BuildFlybyMetrics(SimulationResult result, SimulationScenario scenario)
    {
        return FlybyAnalysis.Compute(
            result,
            scenario.SunIndex,
            scenario.JupiterIndex,
            scenario.SpacecraftIndex,
            scenario.JupiterSoiRadius,
            scenario.SaturnIndex);
    }

    private static double ScoreCandidate(FlybyMetrics metrics)
    {
        if (!metrics.HasSphereOfInfluenceCrossing)
            return -1e9;

        double deltaVGainKms = metrics.DeltaVGainHeliocentric / 1000.0;
        double saturnMissAu = metrics.MinDistanceToSaturn / AstronomyConstants.AstronomicalUnit;
        double jupiterPenalty = Math.Max(0.0, (2.0 * AstronomyConstants.JupiterMeanRadius - metrics.MinDistanceToJupiter) / AstronomyConstants.JupiterMeanRadius);
        return deltaVGainKms - saturnMissAu - 5.0 * jupiterPenalty;
    }

    private static IEnumerable<double> Sweep(double min, double max, int samples)
    {
        if (samples <= 1)
        {
            yield return min;
            yield break;
        }

        double step = (max - min) / (samples - 1);
        for (int i = 0; i < samples; i++)
            yield return min + i * step;
    }

    private static void InsertTopCandidate(List<OptimizationCandidate> top, OptimizationCandidate candidate, int capacity)
    {
        top.Add(candidate);
        top.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        if (top.Count > capacity)
            top.RemoveRange(capacity, top.Count - capacity);
    }

    private static string BuildOptimizationProgressText(int current, int total, IReadOnlyList<OptimizationCandidate> top)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Проверено траекторий: {current} / {total}");
        if (top.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Текущий топ:");
            foreach (OptimizationCandidate candidate in top)
                sb.AppendLine(candidate.ToString());
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildOptimizationSummary(IReadOnlyList<OptimizationCandidate> top, FlybyMetrics? bestMetrics, double bestScore)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Лучший score: {bestScore:F3}");
        if (bestMetrics is not null)
        {
            sb.AppendLine($"Δv на SOI Юпитера: {bestMetrics.DeltaVGainHeliocentric / 1000.0:F3} км/с");
            sb.AppendLine($"Мин. дистанция до Сатурна: {bestMetrics.MinDistanceToSaturn / AstronomyConstants.AstronomicalUnit:F4} а.е.");
            sb.AppendLine($"Мин. дистанция до Юпитера: {bestMetrics.MinDistanceToJupiter / 1000.0:n0} км");
        }

        if (top.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Топ-10:");
            foreach (OptimizationCandidate candidate in top)
                sb.AppendLine(candidate.ToString());
        }

        return sb.ToString().TrimEnd();
    }

    private static Brush PickBrush(string name)
    {
        if (name.Equals("Sun", StringComparison.OrdinalIgnoreCase) || name.Equals("Солнце", StringComparison.OrdinalIgnoreCase))
            return SunBrush;
        if (name.Equals("Jupiter", StringComparison.OrdinalIgnoreCase) || name.Equals("Юпитер", StringComparison.OrdinalIgnoreCase))
            return JupiterBrush;
        if (name.Equals("Saturn", StringComparison.OrdinalIgnoreCase) || name.Equals("Сатурн", StringComparison.OrdinalIgnoreCase))
            return SaturnBrush;
        if (name.Equals("Spacecraft", StringComparison.OrdinalIgnoreCase) || name.Equals("КА", StringComparison.OrdinalIgnoreCase))
            return SpacecraftBrush;
        return Brushes.White;
    }

    private void SaveReport()
    {
        if (_lastResult is null || _lastScenario is null || _lastSettings is null)
            return;

        var dlg = new SaveFileDialog
        {
            Title = "Сохранить отчет",
            Filter = "Markdown (*.md)|*.md|Текст (*.txt)|*.txt|Все файлы (*.*)|*.*",
            FileName = $"otchet_{DateTime.Now:yyyyMMdd_HHmmss}.md",
            AddExtension = true,
            OverwritePrompt = true,
        };

        if (dlg.ShowDialog() != true)
            return;

        File.WriteAllText(dlg.FileName, ReportText, Encoding.UTF8);
        StatusText = "Отчет сохранен.";
    }

    private void ExportCsv()
    {
        if (_lastResult is null)
            return;

        var dlg = new SaveFileDialog
        {
            Title = "Экспорт CSV (траектории)",
            Filter = "CSV (*.csv)|*.csv|Все файлы (*.*)|*.*",
            FileName = $"traektorii_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            AddExtension = true,
            OverwritePrompt = true,
        };

        if (dlg.ShowDialog() != true)
            return;

        using var sw = new StreamWriter(dlg.FileName, false, Encoding.UTF8);
        sw.WriteLine(BuildCsvHeader(_lastResult.BodyNames));

        for (int i = 0; i < _lastResult.SampleCount; i++)
        {
            sw.Write(_lastResult.Times[i].ToString("R", CultureInfo.InvariantCulture));
            for (int body = 0; body < _lastResult.BodyCount; body++)
            {
                Vector3d p = _lastResult.Positions[i][body];
                Vector3d v = _lastResult.Velocities[i][body];
                sw.Write($",{p.X:R},{p.Y:R},{p.Z:R},{v.X:R},{v.Y:R},{v.Z:R}");
            }

            sw.WriteLine();
        }

        StatusText = "CSV сохранен.";
    }

    private static string BuildCsvHeader(string[] bodyNames)
    {
        var sb = new StringBuilder();
        sb.Append("t_s");
        foreach (string name in bodyNames)
        {
            string safe = name.Replace(',', '_').Replace('\n', ' ').Replace('\r', ' ');
            sb.Append($",x_{safe}_m,y_{safe}_m,z_{safe}_m,vx_{safe}_mps,vy_{safe}_mps,vz_{safe}_mps");
        }

        return sb.ToString();
    }

    private static string BuildReport(
        SimulationResult result,
        SimulationScenario scenario,
        IntegrationSettings settings,
        FlybyMetrics? flybyMetrics,
        double t0,
        double t1,
        double outputDt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Отчет симуляции");
        sb.AppendLine();
        sb.AppendLine($"Сценарий: {scenario.Name}");
        sb.AppendLine($"Эпоха начальных данных: {scenario.EpochUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"Время моделирования: t0 = {t0:R} c, t1 = {t1:R} c, шаг вывода = {outputDt:R} c");
        sb.AppendLine($"G = {scenario.GravitationalConstant:R} м^3/(кг·с^2)");
        sb.AppendLine($"СК: {(scenario.ToBarycentricFrame ? "барицентрическая" : "как задано")}");
        sb.AppendLine($"Эфемериды: {(scenario.UsesEphemerides ? "NASA/JPL Horizons API" : "нет")}");
        sb.AppendLine();

        sb.AppendLine("## Модель");
        sb.AppendLine("Используется ньютоновская гравитация для N тел:");
        sb.AppendLine("r_i' = v_i");
        sb.AppendLine("v_i' = ОЈ_{j!=i} G*m_j*(r_j - r_i)/|r_j - r_i|^3");
        sb.AppendLine();

        sb.AppendLine("## Численный метод");
        sb.AppendLine("Интегратор: адаптивный Dormand-Prince RK 5(4).");
        sb.AppendLine("scale = AbsTol + RelTol * max(|y|, |y_new|)");
        sb.AppendLine("err = || (y_new - y_embedded4) / scale ||_RMS");
        sb.AppendLine($"AbsTol = {settings.AbsTol:R}");
        sb.AppendLine($"RelTol = {settings.RelTol:R}");
        sb.AppendLine($"h0 = {settings.InitialStep:R} c, h_min = {settings.MinStep:R} c, h_max = {settings.MaxStep:R} c");
        sb.AppendLine();

        sb.AppendLine("## Начальные условия");
        for (int i = 0; i < scenario.Bodies.Count; i++)
        {
            BodyState body = scenario.Bodies[i];
            sb.AppendLine($"{i}: {body.Name}, m = {body.Mass:R} кг, r0 = ({body.Position.X:R}, {body.Position.Y:R}, {body.Position.Z:R}) м, v0 = ({body.Velocity.X:R}, {body.Velocity.Y:R}, {body.Velocity.Z:R}) м/с");
        }
        sb.AppendLine();

        sb.AppendLine("## Результаты");
        if (scenario.SpacecraftIndex >= 0 && flybyMetrics is not null)
        {
            sb.AppendLine($"Δv на входе/выходе SOI Юпитера: {flybyMetrics.DeltaVGainHeliocentric / 1000.0:F6} км/с");
            sb.AppendLine($"Мин. расстояние до Юпитера: {flybyMetrics.MinDistanceToJupiter / 1000.0:n0} км");
            sb.AppendLine($"Мин. расстояние до Сатурна: {flybyMetrics.MinDistanceToSaturn / AstronomyConstants.AstronomicalUnit:F6} а.е.");
            sb.AppendLine($"|v∞| до Юпитера: {flybyMetrics.InitialJupiterRelativeSpeed / 1000.0:F6} км/с");
            sb.AppendLine($"|v∞| после Юпитера: {flybyMetrics.FinalJupiterRelativeSpeed / 1000.0:F6} км/с");
        }
        else
        {
            sb.AppendLine("КА отсутствует в этом сценарии.");
        }
        sb.AppendLine();

        sb.AppendLine("## Экспорт");
        sb.AppendLine("CSV содержит t и (x,y,z,vx,vy,vz) для каждого тела в SI.");
        return sb.ToString().TrimEnd();
    }

    private SimulationScenario CreateCustomScenario(DateTime epochUtc)
    {
        var bodies = CustomBodies.Select(static body => body.ToBodyState()).ToList();
        if (bodies.Count == 0)
            throw new InvalidOperationException("В песочнице нет тел.");

        int sunIndex = FindBodyIndex(bodies, "солнце");
        if (sunIndex < 0)
            sunIndex = 0;

        int jupiterIndex = FindBodyIndex(bodies, "юпитер");
        int saturnIndex = FindBodyIndex(bodies, "сатурн");
        int spacecraftIndex = FindSpacecraftIndex(bodies);

        return new SimulationScenario
        {
            Name = "Песочница",
            Bodies = bodies,
            SunIndex = sunIndex,
            JupiterIndex = jupiterIndex,
            SaturnIndex = saturnIndex,
            SpacecraftIndex = spacecraftIndex,
            EpochUtc = epochUtc,
            UsesEphemerides = false,
            JupiterSoiRadius = AstronomyConstants.JupiterSemiMajorAxis * 0.064,
            ToBarycentricFrame = true,
        };
    }

    private static int FindBodyIndex(IReadOnlyList<BodyState> bodies, string token)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            if (bodies[i].Name.Contains(token, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static int FindSpacecraftIndex(IReadOnlyList<BodyState> bodies)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            if (bodies[i].Name.Contains("ка", StringComparison.OrdinalIgnoreCase) ||
                bodies[i].Name.Contains("spacecraft", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        for (int i = 0; i < bodies.Count; i++)
        {
            if (Math.Abs(bodies[i].Mass) < 1e-9)
                return i;
        }

        return -1;
    }

    private void UpdateAnimationScene(SimulationResult result, SimulationScenario scenario)
    {
        Brush[] brushes = result.BodyNames
            .Select(PickBrush)
            .Select(static brush =>
            {
                if (brush.CanFreeze && !brush.IsFrozen)
                    brush.Freeze();
                return brush;
            })
            .ToArray();

        AnimationScene = new AnimationSceneData
        {
            Positions = result.Positions,
            BodyNames = result.BodyNames,
            BodyBrushes = brushes,
            CenterBodyIndex = Math.Clamp(scenario.SunIndex, 0, result.BodyCount - 1),
        };
        AnimationFrameCount = result.SampleCount;
        AnimationFrameIndex = 0;
        ResetAnimation();
    }

    private static IEnumerable<EditableBody> CreateDefaultCustomBodies()
    {
        const double g = 6.67430e-11;
        double rJ = AstronomyConstants.JupiterSemiMajorAxis;
        double rS = AstronomyConstants.SaturnSemiMajorAxis;
        double vJ = Math.Sqrt(g * AstronomyConstants.SolarMass / rJ) / 1000.0;
        double vS = Math.Sqrt(g * AstronomyConstants.SolarMass / rS) / 1000.0;

        return
        [
            new EditableBody
            {
                Name = "Солнце",
                Mass = AstronomyConstants.SolarMass,
                XAu = 0,
                YAu = 0,
                ZAu = 0,
                VxKms = 0,
                VyKms = 0,
                VzKms = 0,
            },
            new EditableBody
            {
                Name = "Юпитер",
                Mass = AstronomyConstants.JupiterMass,
                XAu = AstronomyConstants.JupiterSemiMajorAxis / AstronomyConstants.AstronomicalUnit,
                YAu = 0,
                ZAu = 0,
                VxKms = 0,
                VyKms = vJ,
                VzKms = 0,
            },
            new EditableBody
            {
                Name = "Сатурн",
                Mass = AstronomyConstants.SaturnMass,
                XAu = AstronomyConstants.SaturnSemiMajorAxis / AstronomyConstants.AstronomicalUnit,
                YAu = 0,
                ZAu = 0,
                VxKms = 0,
                VyKms = vS,
                VzKms = 0,
            },
            new EditableBody
            {
                Name = "КА",
                Mass = 0,
                XAu = AstronomyConstants.JupiterSemiMajorAxis / AstronomyConstants.AstronomicalUnit - 0.18,
                YAu = -0.06,
                ZAu = 0,
                VxKms = 12,
                VyKms = vJ + 4,
                VzKms = 0,
            },
        ];
    }

    private static string BuildSandboxSummary(SimulationScenario scenario, SimulationResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Песочница рассчитана.");
        sb.AppendLine($"Тел в системе: {scenario.Bodies.Count}");
        sb.AppendLine($"Точек: {result.SampleCount:n0}");
        sb.AppendLine("Список тел:");
        foreach (BodyState body in scenario.Bodies)
            sb.AppendLine($"- {body.Name}, m = {body.Mass:E3} кг");
        return sb.ToString().TrimEnd();
    }

    private void ResetOutputs()
    {
        HasResults = false;
        OrbitSeries = Array.Empty<LineSeries>();
        SpeedSeries = Array.Empty<LineSeries>();
        SpeedComponentSeries = Array.Empty<LineSeries>();
        MetricsText = "Симуляция еще не запускалась.";
        ReportText = "Запустите симуляцию, и здесь появится отчет.";
        OptimizationText = "Оптимизация еще не запускалась.";
        SandboxText = "Песочница еще не запускалась.";
        ResetAnimation();
        AnimationScene = null;
        AnimationFrameCount = 0;
        StatusText = "Готово.";
        _lastResult = null;
        _lastScenario = null;
        _lastSettings = null;
        _lastFlybyMetrics = null;
    }

    private void ApplyCanceledState()
    {
        ResetAnimation();
        StatusText = "Отменено.";
        MetricsText = "Отменено.";
        ReportText = "Отменено.";
        HasResults = false;
    }

    private void ApplyErrorState(Exception ex)
    {
        ResetAnimation();
        StatusText = "Ошибка.";
        MetricsText = ex.Message;
        ReportText = ex.Message;
        HasResults = false;
    }
}


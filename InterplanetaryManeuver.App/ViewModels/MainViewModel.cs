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
    private const double SaturnMissPenaltyWeight = 40.0;

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
    private double _idealStartDistanceKm = 3_000_000.0;
    private double _idealSafeRadiusKm = 120_000.0;
    private double _idealPlanetSpeedKms = 13.07;
    private bool _isRunning;
    private string _statusText = "Готово.";
    private string _metricsText = "Симуляция еще не запускалась.";
    private string _reportText = "Запустите симуляцию, и здесь появится отчет.";
    private string _optimizationText = "Оптимизация еще не запускалась.";
    private bool _hasResults;
    private IReadOnlyList<LineSeries> _orbitSeries = Array.Empty<LineSeries>();
    private IReadOnlyList<LineSeries> _speedSeries = Array.Empty<LineSeries>();
    private IReadOnlyList<LineSeries> _speedComponentSeries = Array.Empty<LineSeries>();
    private string _orbitPlotTitle = "Траектории (относительно Солнца)";
    private string _orbitPlotXLabel = "X (а.е.)";
    private string _orbitPlotYLabel = "Y (а.е.)";
    private string _speedPlotTitle = "Скорость аппарата";
    private string _speedPlotXLabel = "t (сутки)";
    private string _speedPlotYLabel = "v (км/с)";
    private string _speedComponentPlotTitle = "Компоненты скорости";
    private string _speedComponentPlotXLabel = "t (сутки)";
    private string _speedComponentPlotYLabel = "Vx, Vy, Vz (км/с)";

    private double _optPhaseMinDeg = -70;
    private double _optPhaseMaxDeg = 30;
    private int _optPhaseSamples = 7;
    private double _optHeadingMinDeg = -16;
    private double _optHeadingMaxDeg = 18;
    private int _optHeadingSamples = 8;
    private double _optVInfinityMinKms = 6.5;
    private double _optVInfinityMaxKms = 13.0;
    private int _optVInfinitySamples = 6;
    private bool _useLocalRefinement = true;
    private int _localIterations = 8;
    private double _gradientNormTolerance = 0.05;
    private double _phaseDerivativeStepDeg = 0.5;
    private double _headingDerivativeStepDeg = 0.25;
    private double _vInfinityDerivativeStepKms = 0.05;
    private double _phaseMoveStepDeg = 1.0;
    private double _headingMoveStepDeg = 0.5;
    private double _vInfinityMoveStepKms = 0.1;
    private EditableBody? _selectedCustomBody;
    private string _sandboxText = "Песочница еще не запускалась.";
    private AnimationSceneData? _animationScene;
    private int _animationFrameIndex;
    private int _animationFrameCount;
    private bool _isAnimationPlaying;
    private double _animationSpeedMultiplier = 1.0;
    private string _animationStatusText = "Запустите расчёт, чтобы увидеть статус пролёта.";
    private int _animationViewResetVersion;

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
    private readonly RelayCommand _resetAnimationViewCommand;
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
        _saveReportCommand = new RelayCommand(SaveReport, () => HasResults && !IsRunning && !string.IsNullOrWhiteSpace(ReportText));
        _exportCsvCommand = new RelayCommand(ExportCsv, () => HasResults && !IsRunning && _lastResult is not null);
        _runSandboxCommand = new RelayCommand(() => _ = RunSandboxAsync(), () => !IsRunning && CustomBodies.Count > 0);
        _addBodyCommand = new RelayCommand(AddCustomBody, () => !IsRunning);
        _removeBodyCommand = new RelayCommand(RemoveSelectedCustomBody, () => !IsRunning && SelectedCustomBody is not null);
        _saveBodiesCommand = new RelayCommand(SaveCustomBodies, () => CustomBodies.Count > 0);
        _loadBodiesCommand = new RelayCommand(LoadCustomBodies, () => !IsRunning);
        _toggleAnimationCommand = new RelayCommand(ToggleAnimationPlayback, () => AnimationFrameCount > 1);
        _resetAnimationCommand = new RelayCommand(ResetAnimation, () => AnimationFrameCount > 0);
        _resetAnimationViewCommand = new RelayCommand(ResetAnimationView);
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
        ResetAnimationViewCommand = _resetAnimationViewCommand;
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
            RaisePropertyChanged(nameof(IsIdealFlybyPreset));
            RaisePropertyChanged(nameof(IsNumericalFlybyPreset));
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

    public double IdealStartDistanceKm
    {
        get => _idealStartDistanceKm;
        set => SetProperty(ref _idealStartDistanceKm, Math.Max(1000.0, value));
    }

    public double IdealSafeRadiusKm
    {
        get => _idealSafeRadiusKm;
        set => SetProperty(ref _idealSafeRadiusKm, Math.Max(1000.0, value));
    }

    public double IdealPlanetSpeedKms
    {
        get => _idealPlanetSpeedKms;
        set => SetProperty(ref _idealPlanetSpeedKms, Math.Max(0.01, value));
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

    public bool UseLocalRefinement
    {
        get => _useLocalRefinement;
        set => SetProperty(ref _useLocalRefinement, value);
    }

    public int LocalIterations
    {
        get => _localIterations;
        set => SetProperty(ref _localIterations, Math.Max(1, value));
    }

    public double GradientNormTolerance
    {
        get => _gradientNormTolerance;
        set => SetProperty(ref _gradientNormTolerance, Math.Max(1e-6, value));
    }

    public double PhaseDerivativeStepDeg
    {
        get => _phaseDerivativeStepDeg;
        set => SetProperty(ref _phaseDerivativeStepDeg, Math.Max(1e-4, value));
    }

    public double HeadingDerivativeStepDeg
    {
        get => _headingDerivativeStepDeg;
        set => SetProperty(ref _headingDerivativeStepDeg, Math.Max(1e-4, value));
    }

    public double VInfinityDerivativeStepKms
    {
        get => _vInfinityDerivativeStepKms;
        set => SetProperty(ref _vInfinityDerivativeStepKms, Math.Max(1e-4, value));
    }

    public double PhaseMoveStepDeg
    {
        get => _phaseMoveStepDeg;
        set => SetProperty(ref _phaseMoveStepDeg, Math.Max(1e-4, value));
    }

    public double HeadingMoveStepDeg
    {
        get => _headingMoveStepDeg;
        set => SetProperty(ref _headingMoveStepDeg, Math.Max(1e-4, value));
    }

    public double VInfinityMoveStepKms
    {
        get => _vInfinityMoveStepKms;
        set => SetProperty(ref _vInfinityMoveStepKms, Math.Max(1e-4, value));
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

    public string AnimationStatusText
    {
        get => _animationStatusText;
        private set => SetProperty(ref _animationStatusText, value);
    }

    public int AnimationViewResetVersion
    {
        get => _animationViewResetVersion;
        private set => SetProperty(ref _animationViewResetVersion, value);
    }

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

    public string OrbitPlotTitle
    {
        get => _orbitPlotTitle;
        private set => SetProperty(ref _orbitPlotTitle, value);
    }

    public string OrbitPlotXLabel
    {
        get => _orbitPlotXLabel;
        private set => SetProperty(ref _orbitPlotXLabel, value);
    }

    public string OrbitPlotYLabel
    {
        get => _orbitPlotYLabel;
        private set => SetProperty(ref _orbitPlotYLabel, value);
    }

    public string SpeedPlotTitle
    {
        get => _speedPlotTitle;
        private set => SetProperty(ref _speedPlotTitle, value);
    }

    public string SpeedPlotXLabel
    {
        get => _speedPlotXLabel;
        private set => SetProperty(ref _speedPlotXLabel, value);
    }

    public string SpeedPlotYLabel
    {
        get => _speedPlotYLabel;
        private set => SetProperty(ref _speedPlotYLabel, value);
    }

    public string SpeedComponentPlotTitle
    {
        get => _speedComponentPlotTitle;
        private set => SetProperty(ref _speedComponentPlotTitle, value);
    }

    public string SpeedComponentPlotXLabel
    {
        get => _speedComponentPlotXLabel;
        private set => SetProperty(ref _speedComponentPlotXLabel, value);
    }

    public string SpeedComponentPlotYLabel
    {
        get => _speedComponentPlotYLabel;
        private set => SetProperty(ref _speedComponentPlotYLabel, value);
    }

    public bool IsIdealFlybyPreset => SelectedPreset?.Kind == SimulationPresetKind.IdealFlyby;

    public bool IsNumericalFlybyPreset =>
        SelectedPreset?.Kind == SimulationPresetKind.JupiterFlyby ||
        SelectedPreset?.Kind == SimulationPresetKind.ExtendedJupiterFlyby;

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
    public ICommand ResetAnimationViewCommand { get; }
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
        return !IsRunning &&
               (SelectedPreset?.Kind == SimulationPresetKind.JupiterFlyby ||
                SelectedPreset?.Kind == SimulationPresetKind.ExtendedJupiterFlyby);
    }

    private async Task RunAsync()
    {
        if (SelectedPreset is null)
        {
            StatusText = "Выберите пресет.";
            return;
        }

        if (SelectedPreset.Kind == SimulationPresetKind.IdealFlyby)
        {
            SetBusyState("Вычисляется идеальный flyby...");

            try
            {
                IdealFlybyResult ideal = ComputeIdealFlyby();
                ApplyIdealFlybyOutputs(ideal);
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
            FlybySetup? flybySetup =
                SelectedPreset.Kind == SimulationPresetKind.JupiterFlyby ||
                SelectedPreset.Kind == SimulationPresetKind.ExtendedJupiterFlyby
                    ? GetCurrentFlybySetup()
                    : null;
            SimulationScenario scenario = await BuildScenarioAsync(epochUtc, flybySetup, _cts!.Token);
            IntegrationSettings settings = CreateIntegrationSettings();
            SimulationResult result = await SimulateAsync(scenario, settings, _cts.Token);

            ApplySimulationOutputs(result, scenario, settings);
            OptimizationText = "Оптимизация еще не запускалась.";
            StatusText = BuildRunStatus(result, _lastFlybyMetrics, result.SampleCount);
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
        if (SelectedPreset?.Kind != SimulationPresetKind.JupiterFlyby &&
            SelectedPreset?.Kind != SimulationPresetKind.ExtendedJupiterFlyby)
        {
            OptimizationText = "Оптимизация доступна только для численных сценариев flyby у Юпитера.";
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
            int validCount = 0;
            int collisionCount = 0;
            int lowFlybyCount = 0;
            int noSoiCount = 0;
            int noReturnCount = 0;

            int candidateIndex = 0;
            foreach (double phase in Sweep(opt.PhaseMinDeg, opt.PhaseMaxDeg, opt.PhaseSamples))
            {
                foreach (double heading in Sweep(opt.HeadingMinDeg, opt.HeadingMaxDeg, opt.HeadingSamples))
                {
                    foreach (double vInf in Sweep(opt.VInfinityMinKms, opt.VInfinityMaxKms, opt.VInfinitySamples))
                    {
                        _cts!.Token.ThrowIfCancellationRequested();
                        candidateIndex++;

                        var evaluation = await EvaluateFlybyCandidateAsync(epochUtc, settings, phase, heading, vInf, _cts.Token);
                        SimulationScenario scenario = evaluation.Scenario;
                        SimulationResult result = evaluation.Result;
                        FlybyMetrics metrics = evaluation.Metrics;
                        double score = evaluation.Score;

                        OptimizationCandidate candidate = CreateOptimizationCandidate(candidateIndex, phase, heading, vInf, metrics, score);
                        RegisterOptimizationCandidate(metrics, candidate, top, ref validCount, ref collisionCount, ref lowFlybyCount, ref noSoiCount, ref noReturnCount);

                        if (metrics.IsFeasibleFlyby && score > bestScore)
                        {
                            bestScore = score;
                            bestResult = result;
                            bestScenario = scenario;
                            bestMetrics = metrics;
                            PhaseAngleDeg = phase;
                            HeadingAngleDeg = heading;
                            VInfinityKms = vInf;
                        }

                        OptimizationText = BuildOptimizationProgressText(candidateIndex, opt.TotalSamples, top, validCount, collisionCount, lowFlybyCount, noSoiCount, noReturnCount);
                        StatusText = $"Оптимизация: {candidateIndex}/{opt.TotalSamples}";
                    }
                }
            }

            if (opt.UseLocalRefinement && bestScenario is not null && bestResult is not null && bestMetrics is not null)
            {
                (double phase, double heading, double vInf, SimulationScenario scenario, SimulationResult result, FlybyMetrics metrics, double score, int extraEvaluations) =
                    await RefineBestCandidateAsync(epochUtc, settings, opt, PhaseAngleDeg, HeadingAngleDeg, VInfinityKms, bestScore, _cts!.Token);

                candidateIndex += extraEvaluations;
                OptimizationCandidate refinedCandidate = CreateOptimizationCandidate(candidateIndex, phase, heading, vInf, metrics, score);
                RegisterOptimizationCandidate(metrics, refinedCandidate, top, ref validCount, ref collisionCount, ref lowFlybyCount, ref noSoiCount, ref noReturnCount);

                if (metrics.IsFeasibleFlyby && score > bestScore)
                {
                    bestScore = score;
                    bestScenario = scenario;
                    bestResult = result;
                    bestMetrics = metrics;
                    PhaseAngleDeg = phase;
                    HeadingAngleDeg = heading;
                    VInfinityKms = vInf;
                }
            }

            if (bestResult is null || bestScenario is null)
                throw new InvalidOperationException("Не удалось получить ни одной допустимой траектории.");

            ApplySimulationOutputs(bestResult, bestScenario, settings, bestMetrics);
            OptimizationText = BuildOptimizationSummary(top, bestMetrics, bestScore, validCount, collisionCount, lowFlybyCount, noSoiCount, noReturnCount);
            StatusText = $"Оптимизация завершена. Лучший score = {bestScore:F3} (учтён промах по Сатурну)";
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
            RadiusKm = 1000,
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

    private void ResetAnimationView()
    {
        AnimationViewResetVersion++;
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

        StopCondition? stopCondition = null;
        if (scenario.SpacecraftIndex >= 0 && scenario.JupiterIndex >= 0)
        {
            double startDistance = (scenario.Bodies[scenario.SpacecraftIndex].Position - scenario.Bodies[scenario.JupiterIndex].Position).Length();
            double minDistanceSeen = startDistance;
            bool outbound = false;
            double tolerance = Math.Max(1.0, startDistance * 1e-6);

            stopCondition = (_, state) =>
            {
                int scBase = scenario.SpacecraftIndex * 6;
                int jBase = scenario.JupiterIndex * 6;
                double dx = state[scBase + 0] - state[jBase + 0];
                double dy = state[scBase + 1] - state[jBase + 1];
                double dz = state[scBase + 2] - state[jBase + 2];
                double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                if (distance < minDistanceSeen - tolerance)
                    minDistanceSeen = distance;
                else if (distance > minDistanceSeen + tolerance)
                    outbound = true;

                if (outbound && distance >= startDistance - tolerance)
                    return "Возврат на исходное расстояние от Юпитера";

                return null;
            };
        }

        return await Task.Run(
            () => NBodySimulator.Simulate(system, t0, t1, outDt, settings, scenario.BodyCollisionRadii, stopCondition, cancellationToken),
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

    private async Task<(SimulationScenario Scenario, SimulationResult Result, FlybyMetrics Metrics, double Score)> EvaluateFlybyCandidateAsync(
        DateTime epochUtc,
        IntegrationSettings settings,
        double phaseDeg,
        double headingDeg,
        double vInfinityKms,
        CancellationToken cancellationToken)
    {
        var flyby = new FlybySetup
        {
            StartDistanceMultiplier = 1.20,
            PhaseAngleDeg = phaseDeg,
            HeadingAngleDeg = headingDeg,
            VInfinityKms = vInfinityKms,
        };

        SimulationScenario scenario = await BuildScenarioAsync(epochUtc, flyby, cancellationToken);
        SimulationResult result = await SimulateAsync(scenario, settings, cancellationToken);
        FlybyMetrics metrics = BuildFlybyMetrics(result, scenario);
        double score = ScoreCandidate(metrics);
        return (scenario, result, metrics, score);
    }

    private static OptimizationCandidate CreateOptimizationCandidate(
        int index,
        double phaseDeg,
        double headingDeg,
        double vInfinityKms,
        FlybyMetrics metrics,
        double score)
    {
        return new OptimizationCandidate
        {
            Index = index,
            PhaseAngleDeg = phaseDeg,
            HeadingAngleDeg = headingDeg,
            VInfinityKms = vInfinityKms,
            DeltaVGainKms = metrics.DeltaVGainHeliocentric / 1000.0,
            MinJupiterDistanceKm = metrics.MinDistanceToJupiter / 1000.0,
            MinSaturnDistanceAu = metrics.MinDistanceToSaturn / AstronomyConstants.AstronomicalUnit,
            Score = score,
            Status = DescribeFlybyStatus(metrics),
        };
    }

    private static void RegisterOptimizationCandidate(
        FlybyMetrics metrics,
        OptimizationCandidate candidate,
        List<OptimizationCandidate> top,
        ref int validCount,
        ref int collisionCount,
        ref int lowFlybyCount,
        ref int noSoiCount,
        ref int noReturnCount)
    {
        if (metrics.HasJupiterCollision)
        {
            collisionCount++;
            return;
        }

        if (!metrics.HasSphereOfInfluenceCrossing)
        {
            noSoiCount++;
            return;
        }

        if (!metrics.HasReturnToInitialDistance)
        {
            noReturnCount++;
            return;
        }

        validCount++;
        if (metrics.HasDangerouslyLowJupiterFlyby)
            lowFlybyCount++;

        InsertTopCandidate(top, candidate, 10);
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
            UseLocalRefinement = UseLocalRefinement,
            LocalIterations = LocalIterations,
            GradientNormTolerance = GradientNormTolerance,
            PhaseDerivativeStepDeg = PhaseDerivativeStepDeg,
            HeadingDerivativeStepDeg = HeadingDerivativeStepDeg,
            VInfinityDerivativeStepKms = VInfinityDerivativeStepKms,
            PhaseMoveStepDeg = PhaseMoveStepDeg,
            HeadingMoveStepDeg = HeadingMoveStepDeg,
            VInfinityMoveStepKms = VInfinityMoveStepKms,
        };
    }

    private async Task<(double PhaseDeg, double HeadingDeg, double VInfinityKms, SimulationScenario Scenario, SimulationResult Result, FlybyMetrics Metrics, double Score, int Evaluations)> RefineBestCandidateAsync(
        DateTime epochUtc,
        IntegrationSettings settings,
        OptimizationSettings opt,
        double startPhaseDeg,
        double startHeadingDeg,
        double startVInfinityKms,
        double startScore,
        CancellationToken cancellationToken)
    {
        double phase = startPhaseDeg;
        double heading = startHeadingDeg;
        double vInf = startVInfinityKms;

        double phaseMove = opt.PhaseMoveStepDeg;
        double headingMove = opt.HeadingMoveStepDeg;
        double vInfMove = opt.VInfinityMoveStepKms;

        var current = await EvaluateFlybyCandidateAsync(epochUtc, settings, phase, heading, vInf, cancellationToken);
        int evaluations = 1;
        double currentScore = current.Score;
        if (double.IsFinite(startScore))
            currentScore = Math.Max(currentScore, startScore);

        for (int iter = 0; iter < opt.LocalIterations; iter++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double gradPhase = await EstimatePartialDerivativeAsync(epochUtc, settings, phase, heading, vInf, opt.PhaseDerivativeStepDeg, 0, opt, cancellationToken);
            double gradHeading = await EstimatePartialDerivativeAsync(epochUtc, settings, phase, heading, vInf, opt.HeadingDerivativeStepDeg, 1, opt, cancellationToken);
            double gradVInf = await EstimatePartialDerivativeAsync(epochUtc, settings, phase, heading, vInf, opt.VInfinityDerivativeStepKms, 2, opt, cancellationToken);
            evaluations += 6;

            double gradNorm = Math.Sqrt(gradPhase * gradPhase + gradHeading * gradHeading + gradVInf * gradVInf);
            if (gradNorm < opt.GradientNormTolerance)
                break;

            double nextPhase = Clamp(phase + Math.Sign(gradPhase) * phaseMove, opt.PhaseMinDeg, opt.PhaseMaxDeg);
            double nextHeading = Clamp(heading + Math.Sign(gradHeading) * headingMove, opt.HeadingMinDeg, opt.HeadingMaxDeg);
            double nextVInf = Clamp(vInf + Math.Sign(gradVInf) * vInfMove, opt.VInfinityMinKms, opt.VInfinityMaxKms);

            if (Math.Abs(nextPhase - phase) < 1e-9 &&
                Math.Abs(nextHeading - heading) < 1e-9 &&
                Math.Abs(nextVInf - vInf) < 1e-9)
            {
                break;
            }

            var trial = await EvaluateFlybyCandidateAsync(epochUtc, settings, nextPhase, nextHeading, nextVInf, cancellationToken);
            evaluations++;

            if (trial.Metrics.IsFeasibleFlyby && trial.Score > currentScore)
            {
                phase = nextPhase;
                heading = nextHeading;
                vInf = nextVInf;
                current = trial;
                currentScore = trial.Score;
            }
            else
            {
                phaseMove *= 0.5;
                headingMove *= 0.5;
                vInfMove *= 0.5;
                if (phaseMove < 1e-3 && headingMove < 1e-3 && vInfMove < 1e-4)
                    break;
            }
        }

        return (phase, heading, vInf, current.Scenario, current.Result, current.Metrics, current.Score, evaluations);
    }

    private async Task<double> EstimatePartialDerivativeAsync(
        DateTime epochUtc,
        IntegrationSettings settings,
        double phaseDeg,
        double headingDeg,
        double vInfinityKms,
        double step,
        int parameterIndex,
        OptimizationSettings opt,
        CancellationToken cancellationToken)
    {
        (double phase, double heading, double vInf) plusPoint = parameterIndex switch
        {
            0 => (Clamp(phaseDeg + step, opt.PhaseMinDeg, opt.PhaseMaxDeg), headingDeg, vInfinityKms),
            1 => (phaseDeg, Clamp(headingDeg + step, opt.HeadingMinDeg, opt.HeadingMaxDeg), vInfinityKms),
            2 => (phaseDeg, headingDeg, Clamp(vInfinityKms + step, opt.VInfinityMinKms, opt.VInfinityMaxKms)),
            _ => throw new ArgumentOutOfRangeException(nameof(parameterIndex)),
        };

        (double phase, double heading, double vInf) minusPoint = parameterIndex switch
        {
            0 => (Clamp(phaseDeg - step, opt.PhaseMinDeg, opt.PhaseMaxDeg), headingDeg, vInfinityKms),
            1 => (phaseDeg, Clamp(headingDeg - step, opt.HeadingMinDeg, opt.HeadingMaxDeg), vInfinityKms),
            2 => (phaseDeg, headingDeg, Clamp(vInfinityKms - step, opt.VInfinityMinKms, opt.VInfinityMaxKms)),
            _ => throw new ArgumentOutOfRangeException(nameof(parameterIndex)),
        };

        double dx = parameterIndex switch
        {
            0 => plusPoint.phase - minusPoint.phase,
            1 => plusPoint.heading - minusPoint.heading,
            2 => plusPoint.vInf - minusPoint.vInf,
            _ => 0.0,
        };

        if (Math.Abs(dx) < 1e-12)
            return 0.0;

        var plus = await EvaluateFlybyCandidateAsync(epochUtc, settings, plusPoint.phase, plusPoint.heading, plusPoint.vInf, cancellationToken);
        var minus = await EvaluateFlybyCandidateAsync(epochUtc, settings, minusPoint.phase, minusPoint.heading, minusPoint.vInf, cancellationToken);
        return (plus.Score - minus.Score) / dx;
    }

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

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

    private void ApplyIdealFlybyOutputs(IdealFlybyResult result)
    {
        OrbitSeries = result.OrbitSeries;
        SpeedSeries = result.SpeedSeries;
        SpeedComponentSeries = result.SpeedComponentSeries;
        MetricsText = result.MetricsText;
        ReportText = result.ReportText;
        StatusText = result.StatusText;
        OptimizationText = "Для аналитического режима используется отдельная формульная оценка без численной оптимизации.";
        AnimationScene = null;
        AnimationFrameCount = 0;
        AnimationFrameIndex = 0;
        AnimationStatusText = "Аналитический режим не использует покадровую анимацию N-body.";
        OrbitPlotTitle = "Идеальная траектория flyby (система Юпитера)";
        OrbitPlotXLabel = "X (млн км)";
        OrbitPlotYLabel = "Y (млн км)";
        SpeedPlotTitle = "Скорость в идеальной модели";
        SpeedPlotXLabel = "Прогресс вдоль траектории";
        SpeedPlotYLabel = "v (км/с)";
        SpeedComponentPlotTitle = "Гелиоцентрическая скорость в идеальной модели";
        SpeedComponentPlotXLabel = "Прогресс вдоль траектории";
        SpeedComponentPlotYLabel = "Vx, Vy и |v| (км/с)";
        _lastResult = null;
        _lastScenario = null;
        _lastSettings = null;
        _lastFlybyMetrics = null;
        HasResults = true;
    }

    private IdealFlybyResult ComputeIdealFlyby()
    {
        double r0 = IdealStartDistanceKm * 1000.0;
        double q = IdealSafeRadiusKm * 1000.0;
        double vp = IdealPlanetSpeedKms * 1000.0;
        double rJ = AstronomyConstants.JupiterMeanRadius;

        if (q <= rJ)
            throw new InvalidOperationException($"Для идеальной модели радиус пролёта R должен быть больше радиуса Юпитера ({rJ / 1000.0:n0} км).");
        if (r0 <= q)
            throw new InvalidOperationException("Для идеальной модели стартовое расстояние r0 должно быть больше радиуса пролёта R.");

        double mu = 6.67430e-11 * AstronomyConstants.JupiterMass;
        double cosNu = Clamp(2.0 * q / r0 - 1.0, -1.0, 1.0);
        double nuLimit = Math.Acos(cosNu);
        const int sampleCount = 240;

        var rawPoints = new Point[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            double t = i / (double)(sampleCount - 1);
            double nu = -nuLimit + (2.0 * nuLimit * t);
            double r = 2.0 * q / (1.0 + Math.Cos(nu));
            rawPoints[i] = new Point(r * Math.Cos(nu), r * Math.Sin(nu));
        }

        var tangents = new Vector[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            Point prev = rawPoints[Math.Max(0, i - 1)];
            Point next = rawPoints[Math.Min(sampleCount - 1, i + 1)];
            Vector tangent = next - prev;
            tangent.Normalize();
            tangents[i] = tangent;
        }

        double endAngle = Math.Atan2(tangents[^1].Y, tangents[^1].X);
        double rotation = -endAngle;

        var trajectory = new Point[sampleCount];
        var jupiterCircle = BuildCircleSeries(rJ / 1_000_000.0, 128);
        var safeCircle = BuildCircleSeries(q / 1_000_000.0, 128);
        var vRelPoints = new Point[sampleCount];
        var vHelioPoints = new Point[sampleCount];
        var vxHelioPoints = new Point[sampleCount];
        var vyHelioPoints = new Point[sampleCount];

        Vector startTangent = default;
        Vector endTangent = default;
        double initialHeliocentricSpeed = 0.0;
        double finalHeliocentricSpeed = 0.0;
        double maxRelativeSpeed = 0.0;

        for (int i = 0; i < sampleCount; i++)
        {
            double progress = i / (double)(sampleCount - 1);
            Point rotatedPoint = Rotate(rawPoints[i], rotation);
            Vector tangent = Rotate(tangents[i], rotation);
            double radius = Math.Sqrt(rawPoints[i].X * rawPoints[i].X + rawPoints[i].Y * rawPoints[i].Y);
            double vRel = Math.Sqrt(2.0 * mu / radius);
            Vector vHelio = new(tangent.X * vRel + vp, tangent.Y * vRel);

            trajectory[i] = new Point(rotatedPoint.X / 1_000_000.0, rotatedPoint.Y / 1_000_000.0);
            vRelPoints[i] = new Point(progress, vRel / 1000.0);
            vHelioPoints[i] = new Point(progress, vHelio.Length / 1000.0);
            vxHelioPoints[i] = new Point(progress, vHelio.X / 1000.0);
            vyHelioPoints[i] = new Point(progress, vHelio.Y / 1000.0);
            maxRelativeSpeed = Math.Max(maxRelativeSpeed, vRel);

            if (i == 0)
            {
                startTangent = tangent;
                initialHeliocentricSpeed = vHelio.Length;
            }

            if (i == sampleCount - 1)
            {
                endTangent = tangent;
                finalHeliocentricSpeed = vHelio.Length;
            }
        }

        double turnAngleDeg = Math.Acos(Clamp((startTangent.X * endTangent.X + startTangent.Y * endTangent.Y), -1.0, 1.0)) * 180.0 / Math.PI;
        double vStartRelative = Math.Sqrt(2.0 * mu / r0);
        double deltaV = finalHeliocentricSpeed - initialHeliocentricSpeed;

        var orbitSeries =
            new List<LineSeries>
            {
                new()
                {
                    Name = "Юпитер",
                    Points = jupiterCircle,
                    Stroke = JupiterBrush,
                    Thickness = 2.0,
                },
                new()
                {
                    Name = "Граница R",
                    Points = safeCircle,
                    Stroke = SaturnBrush,
                    Thickness = 1.6,
                },
                new()
                {
                    Name = "Идеальная парабола",
                    Points = trajectory,
                    Stroke = SpacecraftBrush,
                    Thickness = 2.6,
                },
            };

        var speedSeries =
            new List<LineSeries>
            {
                new()
                {
                    Name = "|v| в системе Юпитера",
                    Points = vRelPoints,
                    Stroke = JupiterBrush,
                    Thickness = 2.0,
                },
                new()
                {
                    Name = "|v| в гелиоцентрической системе",
                    Points = vHelioPoints,
                    Stroke = SpacecraftBrush,
                    Thickness = 2.2,
                },
            };

        var componentSeries =
            new List<LineSeries>
            {
                new() { Name = "Vx (гелиоцентрическая)", Points = vxHelioPoints, Stroke = VxBrush, Thickness = 1.8 },
                new() { Name = "Vy (гелиоцентрическая)", Points = vyHelioPoints, Stroke = VyBrush, Thickness = 1.8 },
                new() { Name = "|v| (гелиоцентрическая)", Points = vHelioPoints, Stroke = SpacecraftBrush, Thickness = 2.0 },
            };

        var metrics = new StringBuilder();
        metrics.AppendLine("Режим: идеальный аналитический flyby.");
        metrics.AppendLine("Траектория: парабола в системе Юпитера.");
        metrics.AppendLine($"Стартовое расстояние r0: {IdealStartDistanceKm:n0} км");
        metrics.AppendLine($"Минимальный радиус пролёта R: {IdealSafeRadiusKm:n0} км");
        metrics.AppendLine($"Скорость Юпитера vp: {IdealPlanetSpeedKms:F3} км/с");
        metrics.AppendLine($"|v| на старте в системе Юпитера: {vStartRelative / 1000.0:F3} км/с");
        metrics.AppendLine($"|v| в перицентре в системе Юпитера: {maxRelativeSpeed / 1000.0:F3} км/с");
        metrics.AppendLine($"Гелиоцентрическая скорость на старте: {initialHeliocentricSpeed / 1000.0:F3} км/с");
        metrics.AppendLine($"Гелиоцентрическая скорость после пролёта: {finalHeliocentricSpeed / 1000.0:F3} км/с");
        metrics.AppendLine($"Прирост Δv: {deltaV / 1000.0:F3} км/с");
        metrics.AppendLine($"Угол поворота траектории: {turnAngleDeg:F2}°");

        var report = new StringBuilder();
        report.AppendLine("# Идеальный flyby");
        report.AppendLine();
        report.AppendLine("## Постановка");
        report.AppendLine("Используется аналитическая идеализация гравитационного манёвра у Юпитера.");
        report.AppendLine("В системе Юпитера траектория аппарата моделируется параболой, а гелиоцентрическая скорость получается сложением относительной скорости аппарата и скорости Юпитера.");
        report.AppendLine();
        report.AppendLine("## Формулы");
        report.AppendLine("v(r) = sqrt(2 * G * M_J / r)");
        report.AppendLine("r(ν) = 2R / (1 + cos(ν))");
        report.AppendLine("v_helio = v_rel + v_planet");
        report.AppendLine("Δv = |v_helio,out| - |v_helio,in|");
        report.AppendLine();
        report.AppendLine("## Параметры");
        report.AppendLine($"r0 = {IdealStartDistanceKm:F3} км");
        report.AppendLine($"R = {IdealSafeRadiusKm:F3} км");
        report.AppendLine($"vp = {IdealPlanetSpeedKms:F6} км/с");
        report.AppendLine();
        report.AppendLine("## Результаты");
        report.AppendLine($"Парабола ограничена участком от -ν0 до +ν0, где ν0 = arccos(2R/r0 - 1).");
        report.AppendLine($"Стартовая скорость в системе Юпитера: {vStartRelative / 1000.0:F6} км/с.");
        report.AppendLine($"Максимальная скорость в перицентре: {maxRelativeSpeed / 1000.0:F6} км/с.");
        report.AppendLine($"Гелиоцентрическая скорость на входе: {initialHeliocentricSpeed / 1000.0:F6} км/с.");
        report.AppendLine($"Гелиоцентрическая скорость на выходе: {finalHeliocentricSpeed / 1000.0:F6} км/с.");
        report.AppendLine($"Прирост скорости: {deltaV / 1000.0:F6} км/с.");
        report.AppendLine($"Угол поворота траектории: {turnAngleDeg:F6}°.");
        report.AppendLine();
        report.AppendLine("## Интерпретация");
        report.AppendLine("В идеальной модели скорость аппарата в системе Юпитера возрастает при приближении к перицентру, а прирост гелиоцентрической скорости определяется главным образом поворотом вектора скорости.");
        report.AppendLine("Этот режим нужен для аналитической защиты и сравнения с более реалистичной численной N-body моделью.");

        return new IdealFlybyResult
        {
            OrbitSeries = orbitSeries,
            SpeedSeries = speedSeries,
            SpeedComponentSeries = componentSeries,
            StatusText = $"Идеальный flyby рассчитан. Δv = {deltaV / 1000.0:F3} км/с",
            MetricsText = metrics.ToString().TrimEnd(),
            ReportText = report.ToString().TrimEnd(),
        };
    }

    private void BuildPlots(SimulationResult result, SimulationScenario scenario)
    {
        OrbitPlotTitle = "Траектории (относительно Солнца)";
        OrbitPlotXLabel = "X (а.е.)";
        OrbitPlotYLabel = "Y (а.е.)";
        SpeedPlotTitle = "Скорость аппарата";
        SpeedPlotXLabel = "t (сутки)";
        SpeedPlotYLabel = "v (км/с)";
        SpeedComponentPlotTitle = "Компоненты скорости";
        SpeedComponentPlotXLabel = "t (сутки)";
        SpeedComponentPlotYLabel = "Vx, Vy, Vz (км/с)";

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

    private static Point Rotate(Point point, double angle)
    {
        double c = Math.Cos(angle);
        double s = Math.Sin(angle);
        return new Point(point.X * c - point.Y * s, point.X * s + point.Y * c);
    }

    private static Vector Rotate(Vector vector, double angle)
    {
        double c = Math.Cos(angle);
        double s = Math.Sin(angle);
        return new Vector(vector.X * c - vector.Y * s, vector.X * s + vector.Y * c);
    }

    private static IReadOnlyList<Point> BuildCircleSeries(double radius, int samples)
    {
        var points = new Point[samples + 1];
        for (int i = 0; i <= samples; i++)
        {
            double angle = 2.0 * Math.PI * i / samples;
            points[i] = new Point(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }

        return points;
    }

    private void BuildMetrics(SimulationResult result, SimulationScenario scenario, FlybyMetrics? flybyMetrics)
    {
        if (scenario.SpacecraftIndex < 0 || scenario.SpacecraftIndex >= result.BodyCount)
        {
            MetricsText = result.Collision is not null
                ? $"В этом пресете нет космического аппарата.{Environment.NewLine}Общая коллизия: {DescribeCollision(result.Collision)}"
                : "В этом пресете нет космического аппарата.";
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
        if (result.Collision is not null)
            sb.AppendLine($"Общая коллизия: {DescribeCollision(result.Collision)}");

        if (flybyMetrics is not null)
        {
            sb.AppendLine($"Статус пролёта: {DescribeFlybyStatus(flybyMetrics)}");
            sb.AppendLine($"Начальное расстояние до Юпитера r0: {flybyMetrics.InitialDistanceToJupiter / 1000.0:n0} км");
            sb.AppendLine($"Δv при возврате на то же расстояние r0: {flybyMetrics.DeltaVGainHeliocentric / 1000.0:F3} км/с");
            sb.AppendLine($"Мин. расстояние до Юпитера: {flybyMetrics.MinDistanceToJupiter / 1000.0:n0} км");
            sb.AppendLine($"Высота ближайшего подхода над Юпитером: {Math.Max(0.0, flybyMetrics.ClosestApproachAltitudeToJupiter) / 1000.0:n0} км");
            sb.AppendLine($"Радиус SOI Юпитера: {flybyMetrics.JupiterSoiRadius / 1000.0:n0} км");
            if (double.IsFinite(flybyMetrics.MinDistanceToSaturn))
                sb.AppendLine($"Мин. расстояние до Сатурна: {flybyMetrics.MinDistanceToSaturn / AstronomyConstants.AstronomicalUnit:F4} а.е.");
            sb.AppendLine($"|v∞| до Юпитера: {flybyMetrics.InitialJupiterRelativeSpeed / 1000.0:F3} км/с");
            sb.AppendLine($"|v∞| после Юпитера: {flybyMetrics.FinalJupiterRelativeSpeed / 1000.0:F3} км/с");
            if (flybyMetrics.HasJupiterCollision)
            {
                sb.AppendLine("Ограничение нарушено: КА вошёл в радиус Юпитера.");
                sb.AppendLine($"Кадр столкновения: {flybyMetrics.JupiterCollisionIndex + 1}");
            }
            else if (!flybyMetrics.HasReturnToInitialDistance)
                sb.AppendLine("Ограничение нарушено: после облёта аппарат не вернулся на исходное удаление r0.");
            else if (flybyMetrics.HasDangerouslyLowJupiterFlyby)
                sb.AppendLine("Предупреждение: пролёт ниже рекомендованной границы 2Rj.");
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
        if (metrics.HasJupiterCollision)
            return -1e12;

        if (!metrics.HasSphereOfInfluenceCrossing)
            return -1e9;

        if (!metrics.HasReturnToInitialDistance)
            return -1e10;

        double deltaVGainKms = metrics.DeltaVGainHeliocentric / 1000.0;
        double saturnMissAu = metrics.MinDistanceToSaturn / AstronomyConstants.AstronomicalUnit;
        return deltaVGainKms - SaturnMissPenaltyWeight * saturnMissAu;
    }

    private static string DescribeFlybyStatus(FlybyMetrics metrics)
    {
        if (metrics.HasJupiterCollision)
            return "Столкновение с Юпитером";
        if (!metrics.HasSphereOfInfluenceCrossing)
            return "Нет корректного входа/выхода из SOI";
        if (!metrics.HasReturnToInitialDistance)
            return "Не достигнуто исходное расстояние от Юпитера";
        if (metrics.HasDangerouslyLowJupiterFlyby)
            return "Пролёт допустим, но слишком низкий";
        return "Корректный пролёт";
    }

    private static string BuildRunStatus(SimulationResult result, FlybyMetrics? metrics, int sampleCount)
    {
        if (result.Collision is not null)
            return $"Столкновение: {result.Collision.BodyAName} и {result.Collision.BodyBName}. Точек: {sampleCount:n0}";

        if (metrics is null)
            return $"Готово. Точек: {sampleCount:n0}";

        if (metrics.HasJupiterCollision)
            return $"Сценарий недопустим: столкновение с Юпитером. Точек: {sampleCount:n0}";
        if (!metrics.HasSphereOfInfluenceCrossing)
            return $"Пролёт не засчитан: нет корректного пересечения SOI. Точек: {sampleCount:n0}";
        if (!metrics.HasReturnToInitialDistance)
            return $"Сценарий недопустим: не достигнуто исходное расстояние r0. Точек: {sampleCount:n0}";
        if (metrics.HasDangerouslyLowJupiterFlyby)
            return $"Пролёт слишком низкий у Юпитера. Точек: {sampleCount:n0}";

        return $"Готово. Точек: {sampleCount:n0}";
    }

    private static string BuildAnimationStatusText(SimulationResult? result, FlybyMetrics? metrics)
    {
        if (result?.Collision is not null)
        {
            var sbCollision = new StringBuilder();
            sbCollision.AppendLine($"Событие: {DescribeCollision(result.Collision)}.");
            sbCollision.AppendLine($"Момент: t = {result.Collision.Time / 86400.0:F3} суток.");
            sbCollision.AppendLine($"Сближение: {result.Collision.Distance / 1000.0:n0} км при пороге {result.Collision.ThresholdDistance / 1000.0:n0} км.");
            sbCollision.AppendLine("Анимация остановлена в момент первого пересечения радиусов тел.");
            return sbCollision.ToString().TrimEnd();
        }

        if (metrics is null)
            return "Нет данных flyby для описания статуса пролёта.";

        var sb = new StringBuilder();
        sb.AppendLine($"Статус: {DescribeFlybyStatus(metrics)}.");
        sb.AppendLine($"Начальное расстояние r0: {metrics.InitialDistanceToJupiter / 1000.0:n0} км.");
        sb.AppendLine($"Минимальное расстояние до Юпитера: {metrics.MinDistanceToJupiter / 1000.0:n0} км.");
        sb.AppendLine($"Высота ближайшего пролёта: {Math.Max(0.0, metrics.ClosestApproachAltitudeToJupiter) / 1000.0:n0} км.");
        if (metrics.HasJupiterCollision)
        {
            sb.AppendLine("Ход пролёта прерван: аппарат пересёк радиус Юпитера.");
            sb.AppendLine($"Номер кадра до столкновения: {metrics.JupiterCollisionIndex + 1}.");
        }
        else if (!metrics.HasSphereOfInfluenceCrossing)
            sb.AppendLine("Гравитационный манёвр не сформировался.");
        else if (!metrics.HasReturnToInitialDistance)
            sb.AppendLine("Ход пролёта прерван: после облёта аппарат не вернулся на исходное расстояние r0.");
        else if (metrics.HasDangerouslyLowJupiterFlyby)
            sb.AppendLine("Есть предупреждение: минимальная высота ниже рекомендованного порога 2Rj.");
        else
            sb.AppendLine("Траектория пригодна для дальнейшего анализа и оптимизации.");

        return sb.ToString().TrimEnd();
    }

    private static string DescribeCollision(CollisionEvent collision)
    {
        return $"{collision.BodyAName} ↔ {collision.BodyBName}";
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

    private static string BuildOptimizationProgressText(int current, int total, IReadOnlyList<OptimizationCandidate> top, int validCount, int collisionCount, int lowFlybyCount, int noSoiCount, int noReturnCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Проверено траекторий: {current} / {total}");
        sb.AppendLine($"Score = Δv - {SaturnMissPenaltyWeight:F0} * rSaturnMin");
        sb.AppendLine($"Допустимых: {validCount}, столкновений: {collisionCount}, низких пролётов: {lowFlybyCount}, без входа в SOI: {noSoiCount}, без возврата на r0: {noReturnCount}");
        if (top.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Текущий топ:");
            foreach (OptimizationCandidate candidate in top)
                sb.AppendLine(candidate.ToString());
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildOptimizationSummary(IReadOnlyList<OptimizationCandidate> top, FlybyMetrics? bestMetrics, double bestScore, int validCount, int collisionCount, int lowFlybyCount, int noSoiCount, int noReturnCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Лучший score: {bestScore:F3}");
        sb.AppendLine($"Формула score: Δv - {SaturnMissPenaltyWeight:F0} * rSaturnMin, где Δv в км/с, rSaturnMin в а.е.");
        sb.AppendLine($"Допустимых траекторий: {validCount}");
        sb.AppendLine($"Столкновений с Юпитером: {collisionCount}");
        sb.AppendLine($"Низких пролётов (< 2Rj): {lowFlybyCount}");
        sb.AppendLine($"Без корректного пересечения SOI: {noSoiCount}");
        sb.AppendLine($"Без возврата на исходное расстояние r0: {noReturnCount}");
        if (bestMetrics is not null)
        {
            sb.AppendLine($"Статус лучшего кандидата: {DescribeFlybyStatus(bestMetrics)}");
            sb.AppendLine($"Δv при возврате на r0: {bestMetrics.DeltaVGainHeliocentric / 1000.0:F3} км/с");
            sb.AppendLine($"Мин. дистанция до Сатурна: {bestMetrics.MinDistanceToSaturn / AstronomyConstants.AstronomicalUnit:F4} а.е.");
            sb.AppendLine($"Мин. дистанция до Юпитера: {bestMetrics.MinDistanceToJupiter / 1000.0:n0} км");
            sb.AppendLine($"Высота ближайшего подхода: {Math.Max(0.0, bestMetrics.ClosestApproachAltitudeToJupiter) / 1000.0:n0} км");
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
        if (!HasResults || string.IsNullOrWhiteSpace(ReportText))
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
        sb.AppendLine("v_i' = Σ_{j!=i} G*m_j*(r_j - r_i)/|r_j - r_i|^3");
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

        sb.AppendLine("## Ограничения на траекторию");
        sb.AppendLine($"Столкновение с Юпитером: d_min <= Rj, где Rj = {AstronomyConstants.JupiterMeanRadius:R} м.");
        sb.AppendLine($"Низкий пролёт: d_min < 2Rj = {AstronomyConstants.JupiterLowFlybyDistance:R} м.");
        sb.AppendLine("Для оптимизации недопустимы варианты без корректного входа/выхода из SOI Юпитера, без возврата на исходное расстояние r0 и варианты со столкновением.");
        sb.AppendLine("Для общей системы тел используется критерий столкновения d_ij <= R_i + R_j.");
        sb.AppendLine();

        sb.AppendLine("## Результаты");
        if (result.Collision is not null)
            sb.AppendLine($"Общая коллизия: {DescribeCollision(result.Collision)} на t = {result.Collision.Time:R} c.");
        if (scenario.SpacecraftIndex >= 0 && flybyMetrics is not null)
        {
            sb.AppendLine($"Статус пролёта: {DescribeFlybyStatus(flybyMetrics)}");
            sb.AppendLine($"Начальное расстояние до Юпитера r0: {flybyMetrics.InitialDistanceToJupiter / 1000.0:F6} км");
            sb.AppendLine($"Δv при возврате на исходное расстояние r0: {flybyMetrics.DeltaVGainHeliocentric / 1000.0:F6} км/с");
            sb.AppendLine($"Мин. расстояние до Юпитера: {flybyMetrics.MinDistanceToJupiter / 1000.0:n0} км");
            sb.AppendLine($"Высота ближайшего подхода над Юпитером: {Math.Max(0.0, flybyMetrics.ClosestApproachAltitudeToJupiter) / 1000.0:n0} км");
            sb.AppendLine($"Мин. расстояние до Сатурна: {flybyMetrics.MinDistanceToSaturn / AstronomyConstants.AstronomicalUnit:F6} а.е.");
            sb.AppendLine($"|v∞| до Юпитера: {flybyMetrics.InitialJupiterRelativeSpeed / 1000.0:F6} км/с");
            sb.AppendLine($"|v∞| после Юпитера: {flybyMetrics.FinalJupiterRelativeSpeed / 1000.0:F6} км/с");
            if (flybyMetrics.HasJupiterCollision)
                sb.AppendLine($"Кадр столкновения: {flybyMetrics.JupiterCollisionIndex + 1}");
            else if (flybyMetrics.HasReturnToInitialDistance)
                sb.AppendLine($"Кадр возврата на r0: {flybyMetrics.EqualDistanceIndex + 1}");
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
        var collisionRadii = CustomBodies.Select(static body => body.ToCollisionRadiusMeters()).ToList();
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
            BodyCollisionRadii = collisionRadii,
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
        int frameLimit = result.SampleCount;
        if (_lastFlybyMetrics?.HasJupiterCollision == true && _lastFlybyMetrics.JupiterCollisionIndex >= 0)
            frameLimit = Math.Min(result.SampleCount, _lastFlybyMetrics.JupiterCollisionIndex + 1);

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
            Positions = result.Positions.Take(frameLimit).ToArray(),
            BodyNames = result.BodyNames,
            BodyBrushes = brushes,
            CenterBodyIndex = Math.Clamp(scenario.SunIndex, 0, result.BodyCount - 1),
        };
        AnimationStatusText = BuildAnimationStatusText(result, _lastFlybyMetrics);
        AnimationFrameCount = frameLimit;
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
                RadiusKm = AstronomyConstants.SolarRadius / 1000.0,
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
                RadiusKm = AstronomyConstants.JupiterMeanRadius / 1000.0,
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
                RadiusKm = AstronomyConstants.SaturnMeanRadius / 1000.0,
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
                RadiusKm = 0,
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
        if (result.Collision is not null)
            sb.AppendLine($"Столкновение: {result.Collision.BodyAName} ↔ {result.Collision.BodyBName} на t = {result.Collision.Time / 86400.0:F3} суток");
        sb.AppendLine("Список тел:");
        for (int i = 0; i < scenario.Bodies.Count; i++)
        {
            BodyState body = scenario.Bodies[i];
            double radiusKm = i < scenario.BodyCollisionRadii.Count ? scenario.BodyCollisionRadii[i] / 1000.0 : 0.0;
            sb.AppendLine($"- {body.Name}, m = {body.Mass:E3} кг, R = {radiusKm:n0} км");
        }
        return sb.ToString().TrimEnd();
    }

    private void ResetOutputs()
    {
        HasResults = false;
        OrbitSeries = Array.Empty<LineSeries>();
        SpeedSeries = Array.Empty<LineSeries>();
        SpeedComponentSeries = Array.Empty<LineSeries>();
        OrbitPlotTitle = "Траектории (относительно Солнца)";
        OrbitPlotXLabel = "X (а.е.)";
        OrbitPlotYLabel = "Y (а.е.)";
        SpeedPlotTitle = "Скорость аппарата";
        SpeedPlotXLabel = "t (сутки)";
        SpeedPlotYLabel = "v (км/с)";
        SpeedComponentPlotTitle = "Компоненты скорости";
        SpeedComponentPlotXLabel = "t (сутки)";
        SpeedComponentPlotYLabel = "Vx, Vy, Vz (км/с)";
        MetricsText = "Симуляция еще не запускалась.";
        ReportText = "Запустите симуляцию, и здесь появится отчет.";
        OptimizationText = "Оптимизация еще не запускалась.";
        SandboxText = "Песочница еще не запускалась.";
        AnimationStatusText = "Запустите расчёт, чтобы увидеть статус пролёта.";
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
        AnimationStatusText = "Расчёт отменён.";
        HasResults = false;
    }

    private void ApplyErrorState(Exception ex)
    {
        ResetAnimation();
        StatusText = "Ошибка.";
        MetricsText = ex.Message;
        ReportText = ex.Message;
        AnimationStatusText = ex.Message;
        HasResults = false;
    }
}


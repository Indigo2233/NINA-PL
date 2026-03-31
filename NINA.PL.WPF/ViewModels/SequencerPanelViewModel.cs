using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NINA.PL.AutoFocus;
using NINA.PL.Capture;
using NINA.PL.Core;
using NINA.PL.Guider;
using NINA.PL.Sequencer;
using NINA.PL.Sequencer.Conditions;
using NINA.PL.Sequencer.Instructions;
using NINA.PL.Sequencer.Triggers;

namespace NINA.PL.WPF.ViewModels;

public enum SequencerDropMode
{
    Before,
    After,
    Inside,
}

public sealed partial class SequencerPanelViewModel : ObservableObject, IDisposable
{
    private readonly CameraMediator _camera;
    private readonly MountMediator _mount;
    private readonly FocuserMediator _focuser;
    private readonly FilterWheelMediator _filterWheel;
    private readonly FlatDeviceMediator? _flatDevice;
    private readonly SwitchMediator? _switchHub;
    private readonly RotatorMediator? _rotator;
    private readonly CaptureEngine _captureEngine;
    private readonly AutoFocusEngine _autoFocusEngine;
    private readonly PlanetaryGuider _guider;
    private readonly SettingsPanelViewModel? _settings;
    private CancellationTokenSource? _runCts;
    private int _stepCounter;
    private SequenceNodeViewModel? _lastRunningVm;
    private readonly DispatcherTimer _elapsedTimer;
    private DateTime _runStartedUtc;

    public SequencerPanelViewModel(
        CameraMediator camera,
        MountMediator mount,
        FocuserMediator focuser,
        FilterWheelMediator filterWheel,
        CaptureEngine captureEngine,
        AutoFocusEngine autoFocusEngine,
        PlanetaryGuider guider,
        FlatDeviceMediator? flatDevice = null,
        SwitchMediator? switchHub = null,
        RotatorMediator? rotator = null,
        SettingsPanelViewModel? settings = null)
    {
        _camera = camera;
        _mount = mount;
        _focuser = focuser;
        _filterWheel = filterWheel;
        _flatDevice = flatDevice;
        _switchHub = switchHub;
        _settings = settings;
        _rotator = rotator;
        _captureEngine = captureEngine;
        _autoFocusEngine = autoFocusEngine;
        _guider = guider;

        StartSectionNodes.CollectionChanged += OnRootNodesCollectionChanged;
        TargetSectionNodes.CollectionChanged += OnRootNodesCollectionChanged;
        EndSectionNodes.CollectionChanged += OnRootNodesCollectionChanged;
        RootNodes.CollectionChanged += OnRootNodesCollectionChanged;

        foreach (InstructionTemplate t in BuildInstructionTemplates())
            AvailableInstructions.Add(t);

        foreach (ConditionTemplate c in BuildConditionTemplates())
            AvailableConditions.Add(c);

        ContainerTemplates.Add(new ContainerTemplate
        {
            Name = "Sequential Container",
            Icon = "📋",
            NodeType = SequenceNodeType.SequentialContainer,
        });
        ContainerTemplates.Add(new ContainerTemplate
        {
            Name = "Parallel Container",
            Icon = "⚡",
            NodeType = SequenceNodeType.ParallelContainer,
        });
        ContainerTemplates.Add(new ContainerTemplate
        {
            Name = "DSO Target",
            Icon = "🌟",
            NodeType = SequenceNodeType.DsoContainer,
        });

        if (AvailableInstructions.Count > 0)
            SelectedInstructionTemplate = AvailableInstructions[0];

        GroupedInstructions = CollectionViewSource.GetDefaultView(AvailableInstructions);
        GroupedInstructions.GroupDescriptions.Add(new PropertyGroupDescription(nameof(InstructionTemplate.Category)));

        GroupedConditions = CollectionViewSource.GetDefaultView(AvailableConditions);
        GroupedConditions.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConditionTemplate.Category)));
        GroupedConditions.Filter = o => o is ConditionTemplate ct && ct.ConditionFactory is not null;

        var triggerSource = new CollectionViewSource { Source = AvailableConditions };
        GroupedTriggers = triggerSource.View;
        GroupedTriggers.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConditionTemplate.Category)));
        GroupedTriggers.Filter = o => o is ConditionTemplate ct && ct.TriggerFactory is not null;

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _elapsedTimer.Tick += (_, _) =>
        {
            if (!IsRunning)
                return;
            ElapsedTime = (DateTime.UtcNow - _runStartedUtc).ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
        };
    }

    public ObservableCollection<SequenceNodeViewModel> StartSectionNodes { get; } = new();

    public ObservableCollection<SequenceNodeViewModel> TargetSectionNodes { get; } = new();

    public ObservableCollection<SequenceNodeViewModel> EndSectionNodes { get; } = new();

    public ObservableCollection<SequenceNodeViewModel> RootNodes { get; } = new();

    public ObservableCollection<SequenceNodeViewModel> GlobalTriggers { get; } = new();

    public ObservableCollection<UserTemplate> UserTemplates { get; } = new();

    public ObservableCollection<SavedTarget> SavedTargets { get; } = new();

    public ObservableCollection<InstructionTemplate> AvailableInstructions { get; } = new();

    public ObservableCollection<ConditionTemplate> AvailableConditions { get; } = new();

    public ObservableCollection<ContainerTemplate> ContainerTemplates { get; } = new();

    public ICollectionView GroupedInstructions { get; }

    public ICollectionView GroupedConditions { get; }

    public ICollectionView GroupedTriggers { get; }

    [ObservableProperty]
    private InstructionTemplate? selectedInstructionTemplate;

    [ObservableProperty]
    private ConditionTemplate? selectedConditionTemplate;

    [ObservableProperty]
    private int paletteTabIndex;

    [ObservableProperty]
    private SequenceNodeViewModel? selectedNode;

    [ObservableProperty]
    private int loopCount = 1;

    [ObservableProperty]
    private bool useTimeLimit;

    [ObservableProperty]
    private double timeLimitMinutes = 60;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private string executionStatus = "Idle";

    [ObservableProperty]
    private int currentStepIndex;

    [ObservableProperty]
    private int totalSteps;

    [ObservableProperty]
    private double progressPercent;

    [ObservableProperty]
    private string elapsedTime = "00:00:00";

    [ObservableProperty]
    private SequenceNodeViewModel? draggedNode;

    public string EstimatedDuration => ComputeEstimatedDuration();

    private void OnRootNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (SequenceNodeViewModel n in e.NewItems)
                WireTree(n, null);
        }

        OnTreeChanged();
    }

    private void WireTree(SequenceNodeViewModel node, SequenceNodeViewModel? parent)
    {
        node.Parent = parent;
        node.Children.CollectionChanged -= OnNodeChildrenChanged;
        node.Children.CollectionChanged += OnNodeChildrenChanged;
        foreach (SequenceNodeViewModel c in node.Children)
            WireTree(c, node);
    }

    private void OnNodeChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            var parent = sender as ObservableCollection<SequenceNodeViewModel>;
            SequenceNodeViewModel? pNode = FindParentOfCollection(parent);
            foreach (SequenceNodeViewModel n in e.NewItems)
                WireTree(n, pNode);
        }

        OnTreeChanged();
    }

    private SequenceNodeViewModel? FindParentOfCollection(ObservableCollection<SequenceNodeViewModel>? coll)
    {
        if (coll is null)
            return null;
        foreach (var sectionNodes in AllSections())
        {
            foreach (SequenceNodeViewModel r in sectionNodes)
            {
                SequenceNodeViewModel? f = FindParentRecursive(r, coll);
                if (f is not null)
                    return f;
            }
        }

        return null;
    }

    private IEnumerable<ObservableCollection<SequenceNodeViewModel>> AllSections()
    {
        yield return StartSectionNodes;
        yield return TargetSectionNodes;
        yield return EndSectionNodes;
        yield return RootNodes;
    }

    private static SequenceNodeViewModel? FindParentRecursive(SequenceNodeViewModel node, ObservableCollection<SequenceNodeViewModel> target)
    {
        if (ReferenceEquals(node.Children, target))
            return node;
        foreach (SequenceNodeViewModel c in node.Children)
        {
            SequenceNodeViewModel? f = FindParentRecursive(c, target);
            if (f is not null)
                return f;
        }

        return null;
    }

    private void OnTreeChanged()
    {
        RefreshNestingLevels();
        RefreshStepNumbers();
        OnPropertyChanged(nameof(EstimatedDuration));
        RunSequenceCommand.NotifyCanExecuteChanged();
    }

    private void RefreshNestingLevels()
    {
        void Walk(SequenceNodeViewModel n, int level)
        {
            n.NestingLevel = level;
            foreach (SequenceNodeViewModel c in n.Children)
                Walk(c, level + 1);
        }

        foreach (var section in AllSections())
            foreach (SequenceNodeViewModel r in section)
                Walk(r, 0);
    }

    private void RefreshStepNumbers()
    {
        int i = 1;
        void Walk(SequenceNodeViewModel n)
        {
            n.StepNumber = i++;
            foreach (SequenceNodeViewModel c in n.Children)
                Walk(c);
        }

        foreach (var section in AllSections())
            foreach (SequenceNodeViewModel r in section)
                Walk(r);
    }

    partial void OnLoopCountChanged(int value) => OnPropertyChanged(nameof(EstimatedDuration));

    partial void OnUseTimeLimitChanged(bool value) => OnPropertyChanged(nameof(EstimatedDuration));

    partial void OnTimeLimitMinutesChanged(double value) => OnPropertyChanged(nameof(EstimatedDuration));

    partial void OnIsRunningChanged(bool value)
    {
        RunSequenceCommand.NotifyCanExecuteChanged();
        StopSequenceCommand.NotifyCanExecuteChanged();
    }

    private bool CanRunSequence() => !IsRunning && RootNodes.Count > 0;

    private bool CanStopSequence() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanRunSequence))]
    private async Task RunSequence()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();

        VisitAllNodes(vm => vm.Status = SequenceItemStatus.Pending);

        SequenceContainer root = BuildSequenceContainer();
        int plannedTotal = ComputeTotalStepCount();
        TotalSteps = plannedTotal;
        _stepCounter = 0;
        CurrentStepIndex = 0;
        ProgressPercent = 0;
        ExecutionStatus = "Starting…";
        IsRunning = true;
        _runStartedUtc = DateTime.UtcNow;
        ElapsedTime = "00:00:00";
        _elapsedTimer.Start();

        var context = new SequenceContext
        {
            Camera = _camera,
            Mount = _mount,
            Focuser = _focuser,
            FilterWheel = _filterWheel,
            CaptureEngine = _captureEngine,
            AutoFocusEngine = _autoFocusEngine,
            Guider = _guider,
            FlatDevice = _flatDevice,
            SwitchHub = _switchHub,
            Rotator = _rotator,
            Latitude = _settings?.ObserverLatitude ?? 40,
            Longitude = _settings?.ObserverLongitude ?? -74,
        };

        using var engine = new SequenceEngine
        {
            RootContainer = root,
        };

        void OnItemStarted(object? s, ISequenceItem item)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _stepCounter++;
                CurrentStepIndex = _stepCounter;
                ProgressPercent = TotalSteps > 0 ? 100.0 * _stepCounter / TotalSteps : 0;

                SequenceNodeViewModel? vm = FindNodeByItem(item);
                if (vm is not null)
                {
                    if (_lastRunningVm is not null && _lastRunningVm != vm && _lastRunningVm.Status == SequenceItemStatus.Running)
                        _lastRunningVm.Status = SequenceItemStatus.Completed;
                    vm.Status = SequenceItemStatus.Running;
                    _lastRunningVm = vm;
                    ExecutionStatus = $"Running: {vm.DisplayName}…";
                }
                else
                {
                    ExecutionStatus = $"Running: {item.Name}…";
                }
            });
        }

        void OnItemCompleted(object? s, ISequenceItem item)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                SequenceNodeViewModel? vm = FindNodeByItem(item);
                if (vm is not null && vm.Status == SequenceItemStatus.Running)
                    vm.Status = SequenceItemStatus.Completed;
            });
        }

        void OnSequenceFailed(object? s, string message)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ExecutionStatus = $"Failed: {message}";
                if (_lastRunningVm is not null)
                    _lastRunningVm.Status = SequenceItemStatus.Failed;
            });
        }

        void OnSequenceCompleted(object? s, EventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (!ExecutionStatus.StartsWith("Failed", StringComparison.Ordinal) && ExecutionStatus != "Stopped")
                    ExecutionStatus = "Completed";
                ProgressPercent = TotalSteps > 0 ? 100 : 0;
                IsRunning = false;
                _elapsedTimer.Stop();
            });
        }

        engine.ItemStarted += OnItemStarted;
        engine.ItemCompleted += OnItemCompleted;
        engine.SequenceFailed += OnSequenceFailed;
        engine.SequenceCompleted += OnSequenceCompleted;

        try
        {
            await engine.RunAsync(context, _runCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ExecutionStatus = "Stopped";
                VisitAllNodes(vm =>
                {
                    if (vm.Status == SequenceItemStatus.Running)
                        vm.Status = SequenceItemStatus.Skipped;
                });
                _elapsedTimer.Stop();
            });
        }
        finally
        {
            engine.ItemStarted -= OnItemStarted;
            engine.ItemCompleted -= OnItemCompleted;
            engine.SequenceFailed -= OnSequenceFailed;
            engine.SequenceCompleted -= OnSequenceCompleted;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                IsRunning = false;
                _elapsedTimer.Stop();
                if (ExecutionStatus.StartsWith("Running", StringComparison.Ordinal))
                    ExecutionStatus = "Idle";
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopSequence))]
    private void StopSequence()
    {
        _runCts?.Cancel();
    }

    [RelayCommand]
    private void SetPaletteTab(string? index)
    {
        if (int.TryParse(index, out int idx))
            PaletteTabIndex = idx;
    }

    [RelayCommand]
    private void AddInstruction(object? parameter)
    {
        switch (parameter)
        {
            case InstructionTemplate it:
                SelectedInstructionTemplate = it;
                InsertInstruction(SequenceItemViewModelFactory.FromTemplate(it));
                return;
            case ConditionTemplate ct:
                SelectedConditionTemplate = ct;
                InsertInstruction(SequenceItemViewModelFactory.FromConditionTemplate(ct));
                return;
        }

        if (SelectedInstructionTemplate is null)
            return;

        InsertInstruction(SequenceItemViewModelFactory.FromTemplate(SelectedInstructionTemplate));
    }

    private void InsertInstruction(SequenceNodeViewModel node)
    {
        if (SelectedNode?.IsContainer == true)
            SelectedNode.Children.Add(node);
        else
            TargetSectionNodes.Add(node);

        SelectedNode = node;
    }

    [RelayCommand]
    private void AddInstructionToSection(string? section)
    {
        if (SelectedInstructionTemplate is null)
            return;

        SequenceNodeViewModel node = SequenceItemViewModelFactory.FromTemplate(SelectedInstructionTemplate);
        var target = section switch
        {
            "Start" => StartSectionNodes,
            "End" => EndSectionNodes,
            _ => TargetSectionNodes,
        };
        target.Add(node);
        SelectedNode = node;
    }

    [RelayCommand]
    private void AddContainer(object? parameter)
    {
        ContainerTemplate? template = parameter switch
        {
            ContainerTemplate ct => ct,
            string s when s.Equals("Sequential", StringComparison.OrdinalIgnoreCase) =>
                ContainerTemplates.FirstOrDefault(t => t.NodeType == SequenceNodeType.SequentialContainer),
            string s when s.Equals("Parallel", StringComparison.OrdinalIgnoreCase) =>
                ContainerTemplates.FirstOrDefault(t => t.NodeType == SequenceNodeType.ParallelContainer),
            string s when s.Equals("DSO", StringComparison.OrdinalIgnoreCase) =>
                ContainerTemplates.FirstOrDefault(t => t.NodeType == SequenceNodeType.DsoContainer),
            _ => null,
        };
        if (template is null)
            return;
        SequenceNodeViewModel node = SequenceItemViewModelFactory.FromContainerTemplate(template);
        TargetSectionNodes.Add(node);
        SelectedNode = node;
    }

    [RelayCommand]
    private void AddConditionToContainer(SequenceNodeViewModel? container)
    {
        if (container is null || !container.IsContainer || SelectedConditionTemplate is null)
            return;
        if (SelectedConditionTemplate.ConditionFactory is not null)
        {
            var node = SequenceItemViewModelFactory.FromConditionTemplate(SelectedConditionTemplate);
            container.Conditions.Add(node);
            node.Parent = container;
            SelectedNode = node;
        }
    }

    [RelayCommand]
    private void AddTriggerToContainer(SequenceNodeViewModel? container)
    {
        if (container is null || !container.IsContainer || SelectedConditionTemplate is null)
            return;
        if (SelectedConditionTemplate.TriggerFactory is not null)
        {
            var node = SequenceItemViewModelFactory.FromConditionTemplate(SelectedConditionTemplate);
            container.Triggers.Add(node);
            node.Parent = container;
            SelectedNode = node;
        }
    }

    [RelayCommand]
    private void AddGlobalTrigger()
    {
        if (SelectedConditionTemplate?.TriggerFactory is null)
            return;
        var node = SequenceItemViewModelFactory.FromConditionTemplate(SelectedConditionTemplate);
        GlobalTriggers.Add(node);
        SelectedNode = node;
    }

    [RelayCommand]
    private void AddChildInstruction(SequenceNodeViewModel? container)
    {
        if (container is null || !container.IsContainer || SelectedInstructionTemplate is null)
            return;
        SequenceNodeViewModel node = SequenceItemViewModelFactory.FromTemplate(SelectedInstructionTemplate);
        container.Children.Add(node);
        SelectedNode = node;
    }

    [RelayCommand]
    private void SelectNode(SequenceNodeViewModel? node)
    {
        if (node is not null)
            SelectedNode = node;
    }

    [RelayCommand]
    private void RemoveNode(SequenceNodeViewModel? vm)
    {
        vm ??= SelectedNode;
        if (vm is null)
            return;
        ObservableCollection<SequenceNodeViewModel> coll = GetParentCollection(vm);
        if (!coll.Contains(vm))
            return;
        coll.Remove(vm);
        if (SelectedNode == vm)
            SelectedNode = null;
    }

    [RelayCommand]
    private void MoveUp(SequenceNodeViewModel? vm)
    {
        vm ??= SelectedNode;
        if (vm is null)
            return;
        ObservableCollection<SequenceNodeViewModel>? coll = GetParentCollection(vm);
        if (coll is null)
            return;
        int i = coll.IndexOf(vm);
        if (i <= 0)
            return;
        coll.Move(i, i - 1);
    }

    [RelayCommand]
    private void MoveDown(SequenceNodeViewModel? vm)
    {
        vm ??= SelectedNode;
        if (vm is null)
            return;
        ObservableCollection<SequenceNodeViewModel>? coll = GetParentCollection(vm);
        if (coll is null)
            return;
        int i = coll.IndexOf(vm);
        if (i < 0 || i >= coll.Count - 1)
            return;
        coll.Move(i, i + 1);
    }

    [RelayCommand]
    private void DuplicateNode(SequenceNodeViewModel? vm)
    {
        vm ??= SelectedNode;
        if (vm is null)
            return;
        SequenceNodeViewModel copy = SequenceItemViewModelFactory.CloneNode(vm);
        ObservableCollection<SequenceNodeViewModel>? coll = GetParentCollection(vm);
        int i = coll?.IndexOf(vm) ?? -1;
        if (coll is null)
            return;
        if (i < 0)
            coll.Add(copy);
        else
            coll.Insert(i + 1, copy);

        SelectedNode = copy;
    }

    [RelayCommand]
    private void SaveAsTemplate(SequenceNodeViewModel? vm)
    {
        if (vm is null)
            return;
        SequenceNodeViewModel copy = SequenceItemViewModelFactory.CloneNode(vm);
        string name = vm.DisplayName;
        UserTemplate? existing = UserTemplates.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            UserTemplates.Remove(existing);

        UserTemplates.Add(new UserTemplate { Name = name, Node = copy });
    }

    [RelayCommand]
    private void AddFromTemplate(UserTemplate? template)
    {
        if (template is null)
            return;
        SequenceNodeViewModel copy = SequenceItemViewModelFactory.CloneNode(template.Node);
        TargetSectionNodes.Add(copy);
        SelectedNode = copy;
    }

    [RelayCommand]
    private void RemoveTemplate(UserTemplate? template)
    {
        if (template is not null)
            UserTemplates.Remove(template);
    }

    [RelayCommand]
    private void SaveTarget(SequenceNodeViewModel? vm)
    {
        if (vm is null || vm.NodeType != SequenceNodeType.DsoContainer)
            return;
        vm.ApplyPropertiesToItem();
        string name = vm.DisplayName;
        double ra = 0, dec = 0, pa = 0;
        if (vm.Item is DeepSkyObjectContainer dso)
        {
            name = !string.IsNullOrWhiteSpace(dso.TargetName) ? dso.TargetName : name;
            ra = dso.RA;
            dec = dso.Dec;
            pa = dso.PositionAngle;
        }

        SequenceNodeViewModel copy = SequenceItemViewModelFactory.CloneNode(vm);
        SavedTarget? existing = SavedTargets.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            SavedTargets.Remove(existing);

        SavedTargets.Add(new SavedTarget
        {
            Name = name,
            RA = ra,
            Dec = dec,
            PositionAngle = pa,
            Node = copy,
        });
    }

    [RelayCommand]
    private void AddFromTarget(SavedTarget? target)
    {
        if (target?.Node is null)
            return;
        SequenceNodeViewModel copy = SequenceItemViewModelFactory.CloneNode(target.Node);
        TargetSectionNodes.Add(copy);
        SelectedNode = copy;
    }

    [RelayCommand]
    private void RemoveTarget(SavedTarget? target)
    {
        if (target is not null)
            SavedTargets.Remove(target);
    }

    [RelayCommand]
    private void ToggleAdvancedSettings(SequenceNodeViewModel? vm)
    {
        if (vm is not null)
            vm.ShowAdvancedSettings = !vm.ShowAdvancedSettings;
    }

    [RelayCommand]
    private void ClearAll()
    {
        RootNodes.Clear();
        StartSectionNodes.Clear();
        TargetSectionNodes.Clear();
        EndSectionNodes.Clear();
        SelectedNode = null;
    }

    [RelayCommand]
    private void DragStart(SequenceNodeViewModel? node)
    {
        DraggedNode = node;
    }

    /// <summary>Used from code-behind for drag-drop when commands are awkward.</summary>
    public void DropAt(SequenceNodeViewModel? target, SequencerDropMode mode)
    {
        SequenceNodeViewModel? drag = DraggedNode;
        if (drag is null || target is null)
            return;
        if (ReferenceEquals(drag, target))
            return;
        if (mode == SequencerDropMode.Inside && !target.IsContainer)
            return;
        if (IsStrictDescendant(drag, target))
            return;

        ObservableCollection<SequenceNodeViewModel> targetColl;
        SequenceNodeViewModel? newParent;
        int insertIndex;

        switch (mode)
        {
            case SequencerDropMode.Inside:
                targetColl = target.Children;
                newParent = target;
                insertIndex = targetColl.Count;
                break;
            default:
                newParent = target.Parent;
                targetColl = newParent?.Children ?? FindSectionCollection(target);
                insertIndex = targetColl.IndexOf(target);
                if (insertIndex < 0)
                    return;
                if (mode == SequencerDropMode.After)
                    insertIndex++;
                break;
        }

        ObservableCollection<SequenceNodeViewModel> dragColl = GetParentCollection(drag);
        int dragIdx = dragColl.IndexOf(drag);
        if (dragIdx < 0)
            return;

        bool sameList = ReferenceEquals(dragColl, targetColl);
        dragColl.RemoveAt(dragIdx);
        drag.Parent = null;
        if (sameList && dragIdx < insertIndex)
            insertIndex--;

        if (insertIndex < 0)
            insertIndex = 0;
        if (insertIndex > targetColl.Count)
            insertIndex = targetColl.Count;

        drag.Parent = newParent;
        targetColl.Insert(insertIndex, drag);
        WireTree(drag, newParent);
        ClearDragVisuals();
    }

    public void AddInstructionToNode(InstructionTemplate template, SequenceNodeViewModel target, SequencerDropMode mode = SequencerDropMode.After)
    {
        var node = SequenceItemViewModelFactory.FromTemplate(template);
        InsertNodeRelativeTo(node, target, mode);
    }

    public void AddConditionToNode(ConditionTemplate template, SequenceNodeViewModel target, SequencerDropMode mode = SequencerDropMode.After)
    {
        var node = SequenceItemViewModelFactory.FromConditionTemplate(template);
        InsertNodeRelativeTo(node, target, mode);
    }

    private void InsertNodeRelativeTo(SequenceNodeViewModel node, SequenceNodeViewModel target, SequencerDropMode mode = SequencerDropMode.After)
    {
        if (mode == SequencerDropMode.Inside && target.IsContainer)
        {
            target.Children.Add(node);
            node.Parent = target;
        }
        else if (target.Parent is { } parent)
        {
            int idx = parent.Children.IndexOf(target);
            int insertIdx = mode == SequencerDropMode.Before ? idx : idx + 1;
            if (insertIdx < 0) insertIdx = 0;
            if (insertIdx > parent.Children.Count) insertIdx = parent.Children.Count;
            parent.Children.Insert(insertIdx, node);
            node.Parent = parent;
        }
        else
        {
            ObservableCollection<SequenceNodeViewModel> coll = FindSectionCollection(target);
            int idx = coll.IndexOf(target);
            int insertIdx = mode == SequencerDropMode.Before ? idx : idx + 1;
            if (insertIdx < 0) insertIdx = 0;
            if (insertIdx > coll.Count) insertIdx = coll.Count;
            coll.Insert(insertIdx, node);
            node.Parent = null;
        }
        RefreshStepNumbers();
        SelectedNode = node;
    }

    private ObservableCollection<SequenceNodeViewModel> FindSectionCollection(SequenceNodeViewModel node)
    {
        if (StartSectionNodes.Contains(node))
            return StartSectionNodes;
        if (EndSectionNodes.Contains(node))
            return EndSectionNodes;
        if (TargetSectionNodes.Contains(node))
            return TargetSectionNodes;
        if (RootNodes.Contains(node))
            return RootNodes;
        return TargetSectionNodes;
    }

    public void ClearDragVisuals()
    {
        VisitAllNodes(n =>
        {
            n.IsDragOver = false;
            n.IsDropTarget = false;
            n.ShowDropBefore = false;
            n.ShowDropAfter = false;
            n.ShowDropInside = false;
        });
        DraggedNode = null;
    }

    public void SetDragOverTarget(SequenceNodeViewModel? target)
    {
        VisitAllNodes(n => n.IsDragOver = target is not null && ReferenceEquals(n, target));
    }

    public void SetDropIndicator(SequenceNodeViewModel? target, SequencerDropMode mode)
    {
        VisitAllNodes(n =>
        {
            bool match = target is not null && ReferenceEquals(n, target);
            n.IsDragOver = match;
            n.ShowDropBefore = match && mode == SequencerDropMode.Before;
            n.ShowDropAfter = match && mode == SequencerDropMode.After;
            n.ShowDropInside = match && mode == SequencerDropMode.Inside;
        });
    }

    private static bool IsStrictDescendant(SequenceNodeViewModel ancestor, SequenceNodeViewModel? node)
    {
        for (SequenceNodeViewModel? p = node?.Parent; p is not null; p = p.Parent)
        {
            if (ReferenceEquals(p, ancestor))
                return true;
        }

        return false;
    }

    [RelayCommand]
    private void SaveSequence()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "NINA-PL sequence (*.json)|*.json|All files|*.*",
            DefaultExt = ".json",
            FileName = "sequence.json",
        };
        if (dlg.ShowDialog() != true)
            return;

        VisitAllNodes(vm => vm.ApplyPropertiesToItem());

        var model = new SequencePersistenceModel
        {
            LoopCount = LoopCount,
            UseTimeLimit = UseTimeLimit,
            TimeLimitMinutes = TimeLimitMinutes,
        };

        foreach (SequenceNodeViewModel vm in RootNodes)
        {
            var entry = new SequenceItemPersistence();
            SaveNodeVm(vm, entry);
            model.Items.Add(entry);
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(model, options);
        File.WriteAllText(dlg.FileName, json);
    }

    private static void SaveNodeVm(SequenceNodeViewModel vm, SequenceItemPersistence entry)
    {
        vm.ApplyPropertiesToItem();
        entry.Type = vm.NodeType switch
        {
            SequenceNodeType.SequentialContainer => nameof(SequenceContainer),
            SequenceNodeType.ParallelContainer => nameof(ParallelContainer),
            SequenceNodeType.DsoContainer => nameof(DeepSkyObjectContainer),
            SequenceNodeType.Instruction => vm.Item.GetType().Name,
            _ => vm.Item.GetType().Name,
        };
        if (vm.Item is ConditionInstruction ci)
            entry.InnerConditionType = ci.Condition.GetType().Name;
        if (vm.Item is TriggerInstruction ti)
            entry.InnerTriggerType = ti.Trigger.GetType().Name;

        foreach (SequenceItemPropertyViewModel p in vm.Properties)
            entry.Properties[p.Name] = p.ValueAsString;

        if (!vm.IsContainer)
            return;

        entry.Children = new List<SequenceItemPersistence>();
        foreach (SequenceNodeViewModel ch in vm.Children)
        {
            var child = new SequenceItemPersistence();
            SaveNodeVm(ch, child);
            entry.Children!.Add(child);
        }
    }

    [RelayCommand]
    private void LoadSequence()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "NINA-PL sequence (*.json)|*.json|All files|*.*",
        };
        if (dlg.ShowDialog() != true)
            return;

        string json = File.ReadAllText(dlg.FileName);
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        SequencePersistenceModel? model = JsonSerializer.Deserialize<SequencePersistenceModel>(json, jsonOptions);
        if (model is null)
            return;

        LoopCount = model.LoopCount;
        UseTimeLimit = model.UseTimeLimit;
        TimeLimitMinutes = model.TimeLimitMinutes;

        RootNodes.Clear();
        Dictionary<string, InstructionTemplate> byType = AvailableInstructions
            .ToDictionary(t => t.Factory().GetType().Name, t => t);

        foreach (SequenceItemPersistence entry in model.Items)
        {
            SequenceNodeViewModel? node = LoadPersistenceEntry(entry, byType);
            if (node is not null)
            {
                RootNodes.Add(node);
                WireTree(node, null);
            }
        }

        OnTreeChanged();
    }

    private SequenceNodeViewModel? LoadPersistenceEntry(
        SequenceItemPersistence entry,
        Dictionary<string, InstructionTemplate> byType)
    {
        if (entry.Type == nameof(SequenceContainer))
        {
            SequenceNodeViewModel vm = SequenceItemViewModelFactory.FromSequentialContainer();
            ApplyLoadedProperties(vm.Item, entry.Properties);
            if (entry.Children is not null)
            {
                foreach (SequenceItemPersistence ch in entry.Children)
                {
                    SequenceNodeViewModel? child = LoadPersistenceEntry(ch, byType);
                    if (child is not null)
                        vm.Children.Add(child);
                }
            }

            return vm;
        }

        if (entry.Type == nameof(ParallelContainer))
        {
            SequenceNodeViewModel vm = SequenceItemViewModelFactory.FromParallelContainer();
            ApplyLoadedProperties(vm.Item, entry.Properties);
            if (entry.Children is not null)
            {
                foreach (SequenceItemPersistence ch in entry.Children)
                {
                    SequenceNodeViewModel? child = LoadPersistenceEntry(ch, byType);
                    if (child is not null)
                        vm.Children.Add(child);
                }
            }

            return vm;
        }

        if (entry.Type == nameof(DeepSkyObjectContainer))
        {
            SequenceNodeViewModel vm = SequenceItemViewModelFactory.FromDeepSkyObjectContainer();
            ApplyLoadedProperties(vm.Item, entry.Properties);
            if (entry.Children is not null)
            {
                foreach (SequenceItemPersistence ch in entry.Children)
                {
                    SequenceNodeViewModel? child = LoadPersistenceEntry(ch, byType);
                    if (child is not null)
                        vm.Children.Add(child);
                }
            }

            return vm;
        }

        if (entry.Type == nameof(ConditionInstruction) && !string.IsNullOrEmpty(entry.InnerConditionType))
        {
            ConditionTemplate? ct = AvailableConditions.FirstOrDefault(t =>
                t.ConditionFactory is not null &&
                t.ConditionFactory().GetType().Name == entry.InnerConditionType);
            if (ct is null || ct.ConditionFactory is null)
                return null;

            ISequenceCondition cond = ct.ConditionFactory();
            ApplyLoadedPropertiesToCondition(cond, entry.Properties);
            var gate = new ConditionInstruction
            {
                Name = ct.Name,
                Description = string.Empty,
                Condition = cond,
            };
            return SequenceItemViewModelFactory.FromItem(gate);
        }

        if (entry.Type == nameof(TriggerInstruction) && !string.IsNullOrEmpty(entry.InnerTriggerType))
        {
            ConditionTemplate? tt = AvailableConditions.FirstOrDefault(t =>
                t.TriggerFactory is not null &&
                t.TriggerFactory().GetType().Name == entry.InnerTriggerType);
            if (tt is null || tt.TriggerFactory is null)
                return null;

            ISequenceTrigger trig = tt.TriggerFactory();
            var wrapped = new TriggerInstruction
            {
                Name = tt.Name,
                Description = string.Empty,
                Trigger = trig,
            };
            ApplyLoadedProperties(wrapped, entry.Properties);
            ApplyLoadedPropertiesToTrigger(trig, entry.Properties);
            return SequenceItemViewModelFactory.FromItem(wrapped);
        }

        if (!byType.TryGetValue(entry.Type, out InstructionTemplate? template))
            return null;

        ISequenceItem item = template.Factory();
        ApplyLoadedProperties(item, entry.Properties);
        return SequenceItemViewModelFactory.FromItem(item);
    }

    private static void ApplyLoadedProperties(ISequenceItem item, Dictionary<string, string> properties)
    {
        foreach (KeyValuePair<string, string> kv in properties)
        {
            PropertyInfo? prop = item.GetType().GetProperty(kv.Key, BindingFlags.Public | BindingFlags.Instance);
            if (prop?.CanWrite != true)
                continue;

            var pvm = new SequenceItemPropertyViewModel(kv.Key, kv.Value, prop.PropertyType);
            pvm.ApplyToItem(item);
        }
    }

    private static void ApplyLoadedPropertiesToCondition(ISequenceCondition condition, Dictionary<string, string> properties)
    {
        foreach (KeyValuePair<string, string> kv in properties)
        {
            PropertyInfo? prop = condition.GetType().GetProperty(kv.Key, BindingFlags.Public | BindingFlags.Instance);
            if (prop?.CanWrite != true)
                continue;

            var pvm = new SequenceItemPropertyViewModel(kv.Key, kv.Value, prop.PropertyType);
            pvm.ApplyToCondition(condition);
        }
    }

    private static void ApplyLoadedPropertiesToTrigger(ISequenceTrigger trigger, Dictionary<string, string> properties)
    {
        foreach (KeyValuePair<string, string> kv in properties)
        {
            PropertyInfo? prop = trigger.GetType().GetProperty(kv.Key, BindingFlags.Public | BindingFlags.Instance);
            if (prop?.CanWrite != true)
                continue;

            var pvm = new SequenceItemPropertyViewModel(kv.Key, kv.Value, prop.PropertyType);
            pvm.ApplyToTrigger(trigger);
        }
    }

    private SequenceContainer BuildSequenceContainer()
    {
        VisitAllNodes(vm => vm.ApplyPropertiesToItem());

        var root = new SequenceContainer
        {
            Name = "Root",
            Description = "User sequence",
        };

        var startSection = new SequenceContainer { Name = "Sequence Start" };
        foreach (SequenceNodeViewModel vm in StartSectionNodes)
            startSection.Items.Add(BuildSequenceItem(vm));
        if (startSection.Items.Count > 0)
            root.Items.Add(startSection);

        foreach (SequenceNodeViewModel vm in TargetSectionNodes)
            root.Items.Add(BuildSequenceItem(vm));

        var endSection = new SequenceContainer { Name = "Sequence End" };
        foreach (SequenceNodeViewModel vm in EndSectionNodes)
            endSection.Items.Add(BuildSequenceItem(vm));
        if (endSection.Items.Count > 0)
            root.Items.Add(endSection);

        foreach (SequenceNodeViewModel vm in RootNodes)
            root.Items.Add(BuildSequenceItem(vm));

        if (LoopCount > 1)
        {
            root.Conditions.Add(new LoopCondition
            {
                Name = "Loop",
                MaxIterations = LoopCount,
            });
        }
        else if (UseTimeLimit)
        {
            root.Conditions.Add(new LoopCondition
            {
                Name = "SinglePass",
                MaxIterations = 1,
            });
        }

        if (UseTimeLimit)
        {
            root.Conditions.Add(new TimeCondition
            {
                Name = "TimeLimit",
                MaxDuration = TimeSpan.FromMinutes(TimeLimitMinutes),
            });
        }

        return root;
    }

    private static ISequenceItem BuildSequenceItem(SequenceNodeViewModel vm)
    {
        vm.ApplyPropertiesToItem();
        switch (vm.NodeType)
        {
            case SequenceNodeType.Instruction:
                return vm.Item;
            case SequenceNodeType.SequentialContainer:
            {
                var seq = new SequenceContainer
                {
                    Name = vm.Item.Name,
                    Description = vm.Item.Description,
                };
                if (vm.Item is SequenceContainer ssrc)
                    seq.IsEnabled = ssrc.IsEnabled;
                foreach (SequenceNodeViewModel ch in vm.Children)
                    seq.Items.Add(BuildSequenceItem(ch));
                return seq;
            }
            case SequenceNodeType.ParallelContainer:
            {
                var par = new ParallelContainer
                {
                    Name = vm.Item.Name,
                    Description = vm.Item.Description,
                };
                if (vm.Item is ParallelContainer psrc)
                    par.IsEnabled = psrc.IsEnabled;
                foreach (SequenceNodeViewModel ch in vm.Children)
                    par.Items.Add(BuildSequenceItem(ch));
                return par;
            }
            case SequenceNodeType.DsoContainer:
            {
                if (vm.Item is not DeepSkyObjectContainer src)
                    throw new InvalidOperationException();

                var dso = new DeepSkyObjectContainer
                {
                    Name = src.Name,
                    Description = src.Description,
                    TargetName = src.TargetName,
                    RA = src.RA,
                    Dec = src.Dec,
                    PositionAngle = src.PositionAngle,
                    IsEnabled = src.IsEnabled,
                };
                foreach (SequenceNodeViewModel ch in vm.Children)
                    dso.Items.Add(BuildSequenceItem(ch));
                return dso;
            }
            default:
                throw new InvalidOperationException();
        }
    }

    private int ComputeTotalStepCount()
    {
        int n = CountInstructionLeaves();
        if (n == 0)
            return 0;
        int loops = LoopCount > 1 ? LoopCount : 1;
        return n * loops;
    }

    private int CountInstructionLeaves()
    {
        int n = 0;
        void Walk(SequenceNodeViewModel node)
        {
            if (node.NodeType == SequenceNodeType.Instruction)
                n++;
            foreach (SequenceNodeViewModel c in node.Children)
                Walk(c);
        }

        foreach (SequenceNodeViewModel r in RootNodes)
            Walk(r);
        return n;
    }

    private string ComputeEstimatedDuration()
    {
        double seconds = 0;
        void Walk(SequenceNodeViewModel node)
        {
            if (node.NodeType == SequenceNodeType.Instruction)
            {
                node.ApplyPropertiesToItem();
                seconds += EstimateStepSeconds(node.Item);
            }

            foreach (SequenceNodeViewModel c in node.Children)
                Walk(c);
        }

        foreach (SequenceNodeViewModel r in RootNodes)
            Walk(r);

        int loops = LoopCount > 1 ? LoopCount : 1;
        seconds *= loops;

        if (seconds <= 0)
            return "—";

        return TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.CurrentCulture);
    }

    private static double EstimateStepSeconds(ISequenceItem item)
    {
        return item switch
        {
            WaitInstruction w => w.Seconds,
            CaptureVideoInstruction c when c.TimeLimitSeconds > 0 => c.TimeLimitSeconds,
            CaptureVideoInstruction c when c.FrameLimit > 0 => c.FrameLimit / 30.0,
            CaptureVideoInstruction => 60,
            RunAutoFocusInstruction => 45,
            SlewInstruction => 30,
            StartGuidingInstruction => 2,
            StopGuidingInstruction => 1,
            ChangeFilterInstruction => 5,
            AnnotationInstruction => 0,
            ConditionInstruction => 0,
            OpenFlatCoverInstruction => 2,
            CloseFlatCoverInstruction => 2,
            SetFlatBrightnessInstruction => 1,
            ToggleFlatLightInstruction => 1,
            ToggleSwitchInstruction => 1,
            SetSwitchValueInstruction => 1,
            ParkMountInstruction => 30,
            UnparkMountInstruction => 15,
            CoolCameraInstruction cc => cc.DurationMinutes * 60,
            WarmCameraInstruction wc => wc.DurationMinutes * 60,
            MoveRotatorInstruction => 20,
            DitherGuidingInstruction d => Math.Max(1, d.DitherPixels * 0.1 + d.SettleTimeSeconds),
            TakeExposureInstruction t => Math.Max(0.1, (t.ExposureSeconds + 1.0) * Math.Max(1, t.TotalExposureCount)),
            WaitForAltitudeInstruction => 60,
            WaitForTimeInstruction => 60,
            WaitForTwilightInstruction => 120,
            MeridianFlipInstruction mf => 60 + mf.PauseBeforeFlipSeconds + mf.SettleTimeSeconds,
            SetTrackingInstruction => 5,
            CenterTargetInstruction c => 30 * Math.Max(1, c.Iterations),
            ExternalScriptInstruction e => Math.Max(1, e.TimeoutSeconds),
            SendNotificationInstruction => 1,
            SetReadoutModeInstruction => 2,
            MoveFocuserAbsoluteInstruction => 8,
            MoveFocuserRelativeInstruction => 5,
            ConnectEquipmentInstruction => 30,
            DisconnectEquipmentInstruction => 15,
            SlewScopeToAltAzInstruction => 45,
            WaitForTimeSpanInstruction w => w.Hours * 3600 + w.Minutes * 60 + w.Seconds,
            MessageBoxInstruction => 0,
            SaveSequenceInstruction => 2,
            TriggerInstruction => 0,
            DeepSkyObjectContainer => 0,
            SequenceContainer => 0,
            ParallelContainer => 0,
            _ => 5,
        };
    }

    private SequenceNodeViewModel? FindNodeByItem(ISequenceItem item)
    {
        SequenceNodeViewModel? Walk(SequenceNodeViewModel n)
        {
            if (ReferenceEquals(n.Item, item))
                return n;
            foreach (SequenceNodeViewModel c in n.Children)
            {
                SequenceNodeViewModel? f = Walk(c);
                if (f is not null)
                    return f;
            }

            return null;
        }

        foreach (var section in AllSections())
        {
            foreach (SequenceNodeViewModel r in section)
            {
                SequenceNodeViewModel? f = Walk(r);
                if (f is not null)
                    return f;
            }
        }

        return null;
    }

    private void VisitAllNodes(Action<SequenceNodeViewModel> action)
    {
        void Visit(SequenceNodeViewModel n)
        {
            action(n);
            foreach (SequenceNodeViewModel c in n.Children)
                Visit(c);
        }

        foreach (SequenceNodeViewModel r in StartSectionNodes)
            Visit(r);
        foreach (SequenceNodeViewModel r in TargetSectionNodes)
            Visit(r);
        foreach (SequenceNodeViewModel r in EndSectionNodes)
            Visit(r);
        foreach (SequenceNodeViewModel r in RootNodes)
            Visit(r);
    }

    public ObservableCollection<SequenceNodeViewModel> GetParentCollection(SequenceNodeViewModel node)
    {
        if (node.Parent is not null)
        {
            if (node.Parent.Conditions.Contains(node))
                return node.Parent.Conditions;
            if (node.Parent.Triggers.Contains(node))
                return node.Parent.Triggers;
            if (node.Parent.Children.Contains(node))
                return node.Parent.Children;
        }

        if (StartSectionNodes.Contains(node))
            return StartSectionNodes;
        if (EndSectionNodes.Contains(node))
            return EndSectionNodes;
        if (TargetSectionNodes.Contains(node))
            return TargetSectionNodes;

        foreach (var section in AllSections())
            foreach (var container in section)
            {
                if (container.Conditions.Contains(node))
                    return container.Conditions;
                if (container.Triggers.Contains(node))
                    return container.Triggers;
            }

        return RootNodes;
    }

    public ObservableCollection<SequenceNodeViewModel> GetCollectionForNode(SequenceNodeViewModel node)
    {
        if (node.Parent is not null)
            return node.Parent.Children;
        return FindSectionCollection(node);
    }

    private static IReadOnlyList<InstructionTemplate> BuildInstructionTemplates() =>
        new InstructionTemplate[]
        {
            new()
            {
                Name = "Capture Video",
                Icon = "📹",
                Category = "Capture",
                Factory = () => new CaptureVideoInstruction(),
            },
            new()
            {
                Name = "Cool Camera",
                Icon = "❄",
                Category = "Camera",
                Factory = () => new CoolCameraInstruction(),
            },
            new()
            {
                Name = "Warm Camera",
                Icon = "🌡",
                Category = "Camera",
                Factory = () => new WarmCameraInstruction(),
            },
            new()
            {
                Name = "Change Filter",
                Icon = "🔄",
                Category = "Equipment",
                Factory = () => new ChangeFilterInstruction(),
            },
            new()
            {
                Name = "Run AutoFocus",
                Icon = "🎯",
                Category = "Equipment",
                Factory = () => new RunAutoFocusInstruction(),
            },
            new()
            {
                Name = "Slew to Target",
                Icon = "🔭",
                Category = "Mount",
                Factory = () => new SlewInstruction(),
            },
            new()
            {
                Name = "Park Mount",
                Icon = "🅿",
                Category = "Mount",
                Factory = () => new ParkMountInstruction(),
            },
            new()
            {
                Name = "Unpark Mount",
                Icon = "▶",
                Category = "Mount",
                Factory = () => new UnparkMountInstruction(),
            },
            new()
            {
                Name = "Start Guiding",
                Icon = "▶",
                Category = "Guiding",
                Factory = () => new StartGuidingInstruction(),
            },
            new()
            {
                Name = "Stop Guiding",
                Icon = "⏹",
                Category = "Guiding",
                Factory = () => new StopGuidingInstruction(),
            },
            new()
            {
                Name = "Dither",
                Icon = "🎲",
                Category = "Guiding",
                Factory = () => new DitherGuidingInstruction(),
            },
            new()
            {
                Name = "Open Flat Cover",
                Icon = "🔆",
                Category = "Flat Panel",
                Factory = () => new OpenFlatCoverInstruction(),
            },
            new()
            {
                Name = "Close Flat Cover",
                Icon = "🔅",
                Category = "Flat Panel",
                Factory = () => new CloseFlatCoverInstruction(),
            },
            new()
            {
                Name = "Set Brightness",
                Icon = "💡",
                Category = "Flat Panel",
                Factory = () => new SetFlatBrightnessInstruction(),
            },
            new()
            {
                Name = "Toggle Light",
                Icon = "🔦",
                Category = "Flat Panel",
                Factory = () => new ToggleFlatLightInstruction(),
            },
            new()
            {
                Name = "Toggle Switch",
                Icon = "🔌",
                Category = "Power",
                Factory = () => new ToggleSwitchInstruction(),
            },
            new()
            {
                Name = "Set Switch Value",
                Icon = "🎚",
                Category = "Power",
                Factory = () => new SetSwitchValueInstruction(),
            },
            new()
            {
                Name = "Move Rotator",
                Icon = "🔃",
                Category = "Rotator",
                Factory = () => new MoveRotatorInstruction(),
            },
            new()
            {
                Name = "Wait",
                Icon = "⏳",
                Category = "Utility",
                Factory = () => new WaitInstruction(),
            },
            new()
            {
                Name = "Annotation",
                Icon = "📝",
                Category = "Utility",
                Factory = () => new AnnotationInstruction(),
            },
            new()
            {
                Name = "Move Focuser (Abs)",
                Icon = "🔭",
                Category = "Focuser",
                Factory = () => new MoveFocuserAbsoluteInstruction(),
            },
            new()
            {
                Name = "Move Focuser (Rel)",
                Icon = "🔭",
                Category = "Focuser",
                Factory = () => new MoveFocuserRelativeInstruction(),
            },
            new()
            {
                Name = "Connect Equipment",
                Icon = "🔗",
                Category = "Utility",
                Factory = () => new ConnectEquipmentInstruction(),
            },
            new()
            {
                Name = "Disconnect Equipment",
                Icon = "🔌",
                Category = "Utility",
                Factory = () => new DisconnectEquipmentInstruction(),
            },
            new()
            {
                Name = "Slew Alt/Az",
                Icon = "📡",
                Category = "Telescope",
                Factory = () => new SlewScopeToAltAzInstruction(),
            },
            new()
            {
                Name = "Wait Duration",
                Icon = "⏱",
                Category = "Utility",
                Factory = () => new WaitForTimeSpanInstruction(),
            },
            new()
            {
                Name = "Message",
                Icon = "💬",
                Category = "Utility",
                Factory = () => new MessageBoxInstruction(),
            },
            new()
            {
                Name = "Save Sequence",
                Icon = "💾",
                Category = "Utility",
                Factory = () => new SaveSequenceInstruction(),
            },
            new()
            {
                Name = "Dew Heater",
                Icon = "💧",
                Category = "Camera",
                Factory = () => new DewHeaterInstruction(),
            },
            new()
            {
                Name = "Take Many Exposures",
                Icon = "📸",
                Category = "Capture",
                Factory = () => new TakeManyExposuresInstruction(),
            },
            new()
            {
                Name = "Take Subframe",
                Icon = "🔲",
                Category = "Capture",
                Factory = () => new TakeSubframeExposureInstruction(),
            },
            new()
            {
                Name = "Smart Exposure",
                Icon = "🧠",
                Category = "Capture",
                Factory = () => new SmartExposureInstruction(),
            },
            new()
            {
                Name = "Open Dome Shutter",
                Icon = "🏠",
                Category = "Dome",
                Factory = () => new OpenDomeShutterInstruction(),
            },
            new()
            {
                Name = "Close Dome Shutter",
                Icon = "🏠",
                Category = "Dome",
                Factory = () => new CloseDomeShutterInstruction(),
            },
            new()
            {
                Name = "Park Dome",
                Icon = "🅿",
                Category = "Dome",
                Factory = () => new ParkDomeInstruction(),
            },
            new()
            {
                Name = "Slew Dome Azimuth",
                Icon = "🧭",
                Category = "Dome",
                Factory = () => new SlewDomeAzimuthInstruction(),
            },
            new()
            {
                Name = "Synchronize Dome",
                Icon = "🔄",
                Category = "Dome",
                Factory = () => new SynchronizeDomeInstruction(),
            },
            new()
            {
                Name = "Enable Dome Sync",
                Icon = "🔗",
                Category = "Dome",
                Factory = () => new EnableDomeSyncInstruction(),
            },
            new()
            {
                Name = "Trained Flat Exposure",
                Icon = "🔆",
                Category = "Flat Panel",
                Factory = () => new TrainedFlatExposureInstruction(),
            },
            new()
            {
                Name = "Trained Dark Exposure",
                Icon = "🌑",
                Category = "Flat Panel",
                Factory = () => new TrainedDarkExposureInstruction(),
            },
            new()
            {
                Name = "Find Home",
                Icon = "🏠",
                Category = "Mount",
                Factory = () => new FindHomeInstruction(),
            },
            new()
            {
                Name = "Slew And Center",
                Icon = "🎯",
                Category = "Mount",
                Factory = () => new SlewAndCenterInstruction(),
            },
            new()
            {
                Name = "Slew Center Rotate",
                Icon = "🔄",
                Category = "Mount",
                Factory = () => new SlewCenterRotateInstruction(),
            },
            new()
            {
                Name = "Solve And Sync",
                Icon = "📡",
                Category = "Mount",
                Factory = () => new SolveAndSyncInstruction(),
            },
            new()
            {
                Name = "Move Focuser By Temp",
                Icon = "🌡",
                Category = "Focuser",
                Factory = () => new MoveFocuserByTempInstruction(),
            },
            new()
            {
                Name = "Solve And Rotate",
                Icon = "🔃",
                Category = "Rotator",
                Factory = () => new SolveAndRotateInstruction(),
            },
            new()
            {
                Name = "Wait Until Safe",
                Icon = "🛡",
                Category = "Safety",
                Factory = () => new WaitUntilSafeInstruction(),
            },
            new()
            {
                Name = "Wait If Moon Alt",
                Icon = "🌙",
                Category = "Utility",
                Factory = () => new WaitIfMoonAltitudeInstruction(),
            },
            new()
            {
                Name = "Wait If Sun Alt",
                Icon = "☀",
                Category = "Utility",
                Factory = () => new WaitIfSunAltitudeInstruction(),
            },
            new()
            {
                Name = "Wait Until Above Horizon",
                Icon = "⛰",
                Category = "Utility",
                Factory = () => new WaitUntilAboveHorizonInstruction(),
            },
            new()
            {
                Name = "Restore Guiding",
                Icon = "▶",
                Category = "Guiding",
                Factory = () => new RestoreGuidingInstruction(),
            },
        };

    private static IReadOnlyList<ConditionTemplate> BuildConditionTemplates() =>
        new ConditionTemplate[]
        {
            new()
            {
                Name = "Loop N Times",
                Icon = "🔁",
                Category = "Flow",
                ConditionFactory = () => new LoopCondition { Name = "Loop", MaxIterations = 5 },
            },
            new()
            {
                Name = "Altitude Limit",
                Icon = "📐",
                Category = "Sky",
                ConditionFactory = () => new AltitudeCondition { Name = "Altitude", MinAltitude = 20 },
            },
            new()
            {
                Name = "Autofocus on Filter Change",
                Icon = "🔭",
                Category = "Autofocus",
                TriggerFactory = () => new AutofocusOnFilterChangeTrigger(),
            },
            new()
            {
                Name = "AF After Exposures",
                Icon = "🎯",
                Category = "Trigger",
                TriggerFactory = () => new AutofocusAfterExposuresTrigger(),
            },
            new()
            {
                Name = "AF After Time",
                Icon = "🕐",
                Category = "Trigger",
                TriggerFactory = () => new AutofocusAfterTimeTrigger(),
            },
            new()
            {
                Name = "Dither After Exposures",
                Icon = "🎲",
                Category = "Trigger",
                TriggerFactory = () => new DitherAfterExposuresTrigger(),
            },
            new()
            {
                Name = "Meridian Flip",
                Icon = "🔀",
                Category = "Trigger",
                TriggerFactory = () => new MeridianFlipTrigger(),
            },
            new()
            {
                Name = "Loop For Time Span",
                Icon = "⏱",
                Category = "Flow",
                ConditionFactory = () => new TimeSpanCondition { Name = "TimeSpan", MaxSeconds = 3600 },
            },
            new()
            {
                Name = "Loop Until Time",
                Icon = "🕐",
                Category = "Flow",
                ConditionFactory = () => new LoopUntilTimeCondition { Name = "Until Time" },
            },
            new()
            {
                Name = "Loop Until Alt Below",
                Icon = "📐",
                Category = "Sky",
                ConditionFactory = () => new LoopUntilAltitudeBelowCondition { Name = "Alt Below", TargetAltitude = 30 },
            },
            new()
            {
                Name = "Loop While Above Horizon",
                Icon = "⛰",
                Category = "Sky",
                ConditionFactory = () => new LoopWhileAboveHorizonCondition { Name = "Above Horizon" },
            },
            new()
            {
                Name = "Loop While Safe",
                Icon = "🛡",
                Category = "Safety",
                ConditionFactory = () => new LoopWhileSafeCondition(),
            },
            new()
            {
                Name = "Loop While Unsafe",
                Icon = "⚠",
                Category = "Safety",
                ConditionFactory = () => new LoopWhileUnsafeCondition(),
            },
            new()
            {
                Name = "AF After HFR Increase",
                Icon = "📈",
                Category = "Trigger",
                TriggerFactory = () => new AutofocusAfterHFRTrigger(),
            },
            new()
            {
                Name = "AF After Temp Change",
                Icon = "🌡",
                Category = "Trigger",
                TriggerFactory = () => new AutofocusAfterTemperatureChangeTrigger(),
            },
            new()
            {
                Name = "Restore Guiding",
                Icon = "▶",
                Category = "Trigger",
                TriggerFactory = () => new RestoreGuidingTrigger(),
            },
            new()
            {
                Name = "Center After Drift",
                Icon = "🎯",
                Category = "Trigger",
                TriggerFactory = () => new CenterAfterDriftTrigger(),
            },
            new()
            {
                Name = "Sync Dome",
                Icon = "🏠",
                Category = "Trigger",
                TriggerFactory = () => new SynchronizeDomeTrigger(),
            },
        };

    public void Dispose()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _elapsedTimer.Stop();
        RootNodes.CollectionChanged -= OnRootNodesCollectionChanged;
    }
}

internal sealed class SequencePersistenceModel
{
    public int LoopCount { get; set; } = 1;

    public bool UseTimeLimit { get; set; }

    public double TimeLimitMinutes { get; set; } = 60;

    public List<SequenceItemPersistence> Items { get; set; } = new();
}

internal sealed class SequenceItemPersistence
{
    public string Type { get; set; } = "";

    public string? InnerConditionType { get; set; }

    public string? InnerTriggerType { get; set; }

    public Dictionary<string, string> Properties { get; set; } = new();

    public List<SequenceItemPersistence>? Children { get; set; }
}

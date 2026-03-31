using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using NINA.PL.Capture;
using NINA.PL.Core;
using NINA.PL.Guider;
using NINA.PL.Sequencer;
using NINA.PL.Sequencer.Conditions;
using NINA.PL.Sequencer.Instructions;
using NINA.PL.Sequencer.Triggers;

namespace NINA.PL.WPF.ViewModels;

public enum SequenceItemStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped,
}

public enum SequenceNodeType
{
    Instruction,
    SequentialContainer,
    ParallelContainer,
    DsoContainer,
}

public sealed partial class SequenceNodeViewModel : ObservableObject
{
    public SequenceNodeViewModel(
        ISequenceItem item,
        string displayName,
        string icon,
        string category,
        SequenceNodeType nodeType = SequenceNodeType.Instruction)
    {
        Item = item;
        DisplayName = displayName;
        Icon = icon;
        Category = category;
        NodeType = nodeType;
        Properties = new ObservableCollection<SequenceItemPropertyViewModel>(
            SequenceItemViewModelFactory.BuildPropertiesForItem(item));
        IsEnabled = ReadItemIsEnabled(item);
    }

    public ISequenceItem Item { get; }

    [ObservableProperty]
    private bool isEnabled = true;

    partial void OnIsEnabledChanged(bool value)
    {
        PropertyInfo? p = Item.GetType().GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.Instance);
        if (p?.CanWrite == true && p.PropertyType == typeof(bool))
            p.SetValue(Item, value);
    }

    private static bool ReadItemIsEnabled(ISequenceItem item)
    {
        PropertyInfo? p = item.GetType().GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.Instance);
        if (p?.PropertyType == typeof(bool) && p.CanRead)
            return (bool)(p.GetValue(item) ?? true);
        return true;
    }

    public string DisplayName { get; }

    public string Icon { get; }

    public string Category { get; }

    public SequenceNodeType NodeType { get; }

    [ObservableProperty]
    private bool isExpanded = true;

    [ObservableProperty]
    private int stepNumber;

    [ObservableProperty]
    private SequenceItemStatus status = SequenceItemStatus.Pending;

    [ObservableProperty]
    private bool isDragOver;

    [ObservableProperty]
    private bool isDropTarget;

    [ObservableProperty]
    private bool showDropBefore;

    [ObservableProperty]
    private bool showDropAfter;

    [ObservableProperty]
    private bool showDropInside;

    [ObservableProperty]
    private int nestingLevel;

    public bool IsContainer => NodeType != SequenceNodeType.Instruction;

    public ObservableCollection<SequenceNodeViewModel> Children { get; } = new();

    public ObservableCollection<SequenceItemPropertyViewModel> Properties { get; }

    public SequenceNodeViewModel? Parent { get; set; }

    /// <summary>Writes all property editors back to <see cref="Item"/>.</summary>
    public void ApplyPropertiesToItem()
    {
        if (Item is TriggerInstruction ti)
        {
            foreach (SequenceItemPropertyViewModel p in Properties)
            {
                if (ti.GetType().GetProperty(p.Name, BindingFlags.Public | BindingFlags.Instance) is { CanWrite: true })
                    p.ApplyToItem(Item);
                else
                    p.ApplyToTrigger(ti.Trigger);
            }

            return;
        }

        foreach (SequenceItemPropertyViewModel p in Properties)
            p.ApplyToItem(Item);
    }
}

public sealed partial class SequenceItemPropertyViewModel : ObservableObject
{
    public SequenceItemPropertyViewModel(string name, string valueAsString, Type propertyType)
    {
        Name = name;
        ValueAsString = valueAsString;
        PropertyType = propertyType;
    }

    public string Name { get; }

    [ObservableProperty]
    private string valueAsString;

    public Type PropertyType { get; }

    public void ApplyToItem(ISequenceItem item)
    {
        PropertyInfo? prop = item.GetType().GetProperty(Name, BindingFlags.Public | BindingFlags.Instance);
        if (prop?.CanWrite != true)
            return;

        object? value = ConvertValue(ValueAsString, prop.PropertyType, Name);
        if (value is null && Nullable.GetUnderlyingType(prop.PropertyType) is null && prop.PropertyType.IsValueType)
            return;

        prop.SetValue(item, value);
    }

    public void ApplyToCondition(ISequenceCondition condition)
    {
        PropertyInfo? prop = condition.GetType().GetProperty(Name, BindingFlags.Public | BindingFlags.Instance);
        if (prop?.CanWrite != true)
            return;

        object? value = ConvertValue(ValueAsString, prop.PropertyType, Name);
        if (value is null && Nullable.GetUnderlyingType(prop.PropertyType) is null && prop.PropertyType.IsValueType)
            return;

        prop.SetValue(condition, value);
    }

    public void ApplyToTrigger(ISequenceTrigger trigger)
    {
        PropertyInfo? prop = trigger.GetType().GetProperty(Name, BindingFlags.Public | BindingFlags.Instance);
        if (prop?.CanWrite != true)
            return;

        object? value = ConvertValue(ValueAsString, prop.PropertyType, Name);
        if (value is null && Nullable.GetUnderlyingType(prop.PropertyType) is null && prop.PropertyType.IsValueType)
            return;

        prop.SetValue(trigger, value);
    }

    private static object? ConvertValue(string text, Type targetType, string propertyName)
    {
        Type t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        string s = text.Trim();

        if (t == typeof(string))
        {
            if (propertyName == nameof(ChangeFilterInstruction.FilterName) && string.IsNullOrWhiteSpace(s))
                return null;
            return text;
        }

        if (string.IsNullOrEmpty(s))
            return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null
                ? Activator.CreateInstance(targetType)
                : null;

        if (t.IsEnum)
            return Enum.Parse(t, s, ignoreCase: true);

        return t switch
        {
            { } when t == typeof(int) => int.Parse(s, CultureInfo.InvariantCulture),
            { } when t == typeof(double) => double.Parse(s, CultureInfo.InvariantCulture),
            { } when t == typeof(float) => float.Parse(s, CultureInfo.InvariantCulture),
            { } when t == typeof(bool) => bool.Parse(s),
            { } when t == typeof(TimeSpan) => TimeSpan.Parse(s, CultureInfo.InvariantCulture),
            _ => Convert.ChangeType(s, t!, CultureInfo.InvariantCulture),
        };
    }
}

/// <summary>Builds <see cref="SequenceNodeViewModel"/> instances and property grids for known instruction types.</summary>
public static class SequenceItemViewModelFactory
{
    public static SequenceNodeViewModel FromTemplate(InstructionTemplate template)
    {
        ISequenceItem item = template.Factory();
        return new SequenceNodeViewModel(item, template.Name, template.Icon, template.Category);
    }

    public static SequenceNodeViewModel FromConditionTemplate(ConditionTemplate template)
    {
        if (template.ConditionFactory is not null)
        {
            ISequenceCondition condition = template.ConditionFactory();
            var item = new ConditionInstruction
            {
                Name = template.Name,
                Description = string.Empty,
                Condition = condition,
            };
            return new SequenceNodeViewModel(item, template.Name, template.Icon, template.Category);
        }

        if (template.TriggerFactory is not null)
        {
            ISequenceTrigger trigger = template.TriggerFactory();
            var item = new TriggerInstruction
            {
                Name = template.Name,
                Description = string.Empty,
                Trigger = trigger,
            };
            return new SequenceNodeViewModel(item, template.Name, template.Icon, template.Category);
        }

        throw new InvalidOperationException("ConditionTemplate must set ConditionFactory or TriggerFactory.");
    }

    public static SequenceNodeViewModel FromContainerTemplate(ContainerTemplate template)
    {
        return template.NodeType switch
        {
            SequenceNodeType.SequentialContainer => FromSequentialContainer(),
            SequenceNodeType.ParallelContainer => FromParallelContainer(),
            SequenceNodeType.DsoContainer => FromDeepSkyObjectContainer(),
            _ => throw new ArgumentOutOfRangeException(nameof(template)),
        };
    }

    public static SequenceNodeViewModel FromSequentialContainer()
    {
        var c = new SequenceContainer
        {
            Name = "Sequential",
            Description = string.Empty,
        };
        return new SequenceNodeViewModel(c, "Sequential", "📋", "Container", SequenceNodeType.SequentialContainer);
    }

    public static SequenceNodeViewModel FromParallelContainer()
    {
        var c = new ParallelContainer
        {
            Name = "Parallel",
            Description = string.Empty,
        };
        return new SequenceNodeViewModel(c, "Parallel", "⚡", "Container", SequenceNodeType.ParallelContainer);
    }

    public static SequenceNodeViewModel FromDeepSkyObjectContainer()
    {
        var c = new DeepSkyObjectContainer
        {
            Name = "DSO Target",
            Description = string.Empty,
        };
        return new SequenceNodeViewModel(c, "DSO Target", "🌟", "Container", SequenceNodeType.DsoContainer);
    }

    public static SequenceNodeViewModel FromItem(ISequenceItem item)
    {
        if (item is SequenceContainer sc)
            return new SequenceNodeViewModel(sc, sc.Name, "📋", "Container", SequenceNodeType.SequentialContainer);
        if (item is ParallelContainer pc)
            return new SequenceNodeViewModel(pc, pc.Name, "⚡", "Container", SequenceNodeType.ParallelContainer);
        if (item is DeepSkyObjectContainer dso)
            return new SequenceNodeViewModel(dso, dso.Name, "🌟", "Container", SequenceNodeType.DsoContainer);
        if (TryGetTemplateMetadata(item, out string? displayName, out string? icon, out string? category))
            return new SequenceNodeViewModel(item, displayName, icon, category);

        return new SequenceNodeViewModel(
            item,
            item.Name,
            "📌",
            "Utility");
    }

    public static SequenceNodeViewModel CloneNode(SequenceNodeViewModel vm)
    {
        vm.ApplyPropertiesToItem();
        return vm.NodeType switch
        {
            SequenceNodeType.Instruction => FromItem(CloneItem(vm.Item)),
            SequenceNodeType.SequentialContainer => CloneContainerNode(vm, () => new SequenceContainer
            {
                Name = vm.Item.Name,
                Description = vm.Item.Description,
            }, SequenceNodeType.SequentialContainer),
            SequenceNodeType.ParallelContainer => CloneContainerNode(vm, () => new ParallelContainer
            {
                Name = vm.Item.Name,
                Description = vm.Item.Description,
            }, SequenceNodeType.ParallelContainer),
            SequenceNodeType.DsoContainer => CloneDsoContainerNode(vm),
            _ => throw new InvalidOperationException(),
        };
    }

    private static SequenceNodeViewModel CloneContainerNode(
        SequenceNodeViewModel vm,
        Func<ISequenceItem> createContainer,
        SequenceNodeType nodeType)
    {
        ISequenceItem c = createContainer();
        var n = new SequenceNodeViewModel(
            c,
            vm.DisplayName,
            vm.Icon,
            vm.Category,
            nodeType);
        foreach (SequenceNodeViewModel child in vm.Children)
            n.Children.Add(CloneNode(child));
        return n;
    }

    private static SequenceNodeViewModel CloneDsoContainerNode(SequenceNodeViewModel vm)
    {
        vm.ApplyPropertiesToItem();
        if (vm.Item is not DeepSkyObjectContainer src)
            throw new InvalidOperationException();

        var c = new DeepSkyObjectContainer
        {
            Name = src.Name,
            Description = src.Description,
            TargetName = src.TargetName,
            RA = src.RA,
            Dec = src.Dec,
            PositionAngle = src.PositionAngle,
            IsEnabled = src.IsEnabled,
        };
        var n = new SequenceNodeViewModel(
            c,
            vm.DisplayName,
            vm.Icon,
            vm.Category,
            SequenceNodeType.DsoContainer);
        foreach (SequenceNodeViewModel child in vm.Children)
            n.Children.Add(CloneNode(child));
        return n;
    }

    public static bool TryGetTemplateMetadata(
        ISequenceItem item,
        out string displayName,
        out string icon,
        out string category)
    {
        displayName = item.Name;
        icon = "📌";
        category = "Utility";

        switch (item)
        {
            case CaptureVideoInstruction:
                displayName = "Capture Video";
                icon = "📹";
                category = "Capture";
                return true;
            case ChangeFilterInstruction:
                displayName = "Change Filter";
                icon = "🔄";
                category = "Equipment";
                return true;
            case RunAutoFocusInstruction:
                displayName = "Run AutoFocus";
                icon = "🎯";
                category = "Equipment";
                return true;
            case StartGuidingInstruction:
                displayName = "Start Guiding";
                icon = "▶";
                category = "Guiding";
                return true;
            case StopGuidingInstruction:
                displayName = "Stop Guiding";
                icon = "⏹";
                category = "Guiding";
                return true;
            case SlewInstruction:
                displayName = "Slew to Target";
                icon = "🔭";
                category = "Mount";
                return true;
            case WaitInstruction:
                displayName = "Wait";
                icon = "⏳";
                category = "Utility";
                return true;
            case AnnotationInstruction:
                displayName = "Annotation";
                icon = "📝";
                category = "Utility";
                return true;
            case OpenFlatCoverInstruction:
                displayName = "Open Flat Cover";
                icon = "🔆";
                category = "Flat Panel";
                return true;
            case CloseFlatCoverInstruction:
                displayName = "Close Flat Cover";
                icon = "🔅";
                category = "Flat Panel";
                return true;
            case SetFlatBrightnessInstruction:
                displayName = "Set Brightness";
                icon = "💡";
                category = "Flat Panel";
                return true;
            case ToggleFlatLightInstruction:
                displayName = "Toggle Light";
                icon = "🔦";
                category = "Flat Panel";
                return true;
            case ToggleSwitchInstruction:
                displayName = "Toggle Switch";
                icon = "🔌";
                category = "Power";
                return true;
            case SetSwitchValueInstruction:
                displayName = "Set Switch Value";
                icon = "🎚";
                category = "Power";
                return true;
            case ParkMountInstruction:
                displayName = "Park Mount";
                icon = "🅿";
                category = "Mount";
                return true;
            case UnparkMountInstruction:
                displayName = "Unpark Mount";
                icon = "▶";
                category = "Mount";
                return true;
            case CoolCameraInstruction:
                displayName = "Cool Camera";
                icon = "❄";
                category = "Camera";
                return true;
            case WarmCameraInstruction:
                displayName = "Warm Camera";
                icon = "🌡";
                category = "Camera";
                return true;
            case MoveRotatorInstruction:
                displayName = "Move Rotator";
                icon = "🔃";
                category = "Rotator";
                return true;
            case DitherGuidingInstruction:
                displayName = "Dither";
                icon = "🎲";
                category = "Guiding";
                return true;
            case WaitForAltitudeInstruction:
                displayName = "Wait for Altitude";
                icon = "⛰";
                category = "Utility";
                return true;
            case WaitForTimeInstruction:
                displayName = "Wait for Time";
                icon = "🕐";
                category = "Utility";
                return true;
            case WaitForTwilightInstruction:
                displayName = "Wait for Twilight";
                icon = "🌅";
                category = "Utility";
                return true;
            case MeridianFlipInstruction:
                displayName = "Meridian Flip";
                icon = "🔀";
                category = "Mount";
                return true;
            case SetTrackingInstruction:
                displayName = "Set Tracking";
                icon = "📡";
                category = "Mount";
                return true;
            case CenterTargetInstruction:
                displayName = "Center Target";
                icon = "🎯";
                category = "Mount";
                return true;
            case ExternalScriptInstruction:
                displayName = "Run Script";
                icon = "⚙";
                category = "Utility";
                return true;
            case SendNotificationInstruction:
                displayName = "Notification";
                icon = "🔔";
                category = "Utility";
                return true;
            case TakeExposureInstruction:
                displayName = "Take Exposure";
                icon = "📸";
                category = "Capture";
                return true;
            case SetReadoutModeInstruction:
                displayName = "Set Readout Mode";
                icon = "🖥";
                category = "Camera";
                return true;
            case MoveFocuserAbsoluteInstruction:
                displayName = "Move Focuser (Abs)";
                icon = "🔭";
                category = "Focuser";
                return true;
            case MoveFocuserRelativeInstruction:
                displayName = "Move Focuser (Rel)";
                icon = "🔭";
                category = "Focuser";
                return true;
            case ConnectEquipmentInstruction:
                displayName = "Connect Equipment";
                icon = "🔗";
                category = "Utility";
                return true;
            case DisconnectEquipmentInstruction:
                displayName = "Disconnect Equipment";
                icon = "🔌";
                category = "Utility";
                return true;
            case SlewScopeToAltAzInstruction:
                displayName = "Slew Alt/Az";
                icon = "📡";
                category = "Telescope";
                return true;
            case WaitForTimeSpanInstruction:
                displayName = "Wait Duration";
                icon = "⏱";
                category = "Utility";
                return true;
            case MessageBoxInstruction:
                displayName = "Message";
                icon = "💬";
                category = "Utility";
                return true;
            case SaveSequenceInstruction:
                displayName = "Save Sequence";
                icon = "💾";
                category = "Utility";
                return true;
            case DeepSkyObjectContainer:
                displayName = "DSO Target";
                icon = "🌟";
                category = "Container";
                return true;
            case TriggerInstruction ti:
                displayName = ti.Name;
                icon = "⚡";
                category = "Trigger";
                switch (ti.Trigger)
                {
                    case AutofocusAfterExposuresTrigger:
                        icon = "🎯";
                        category = "Trigger";
                        break;
                    case AutofocusAfterTimeTrigger:
                        icon = "🕐";
                        category = "Trigger";
                        break;
                    case DitherAfterExposuresTrigger:
                        icon = "🎲";
                        category = "Trigger";
                        break;
                    case MeridianFlipTrigger:
                        icon = "🔀";
                        category = "Trigger";
                        break;
                }
                return true;
            case ConditionInstruction ci:
                displayName = ci.Name;
                icon = "◆";
                category = "Conditions";
                switch (ci.Condition)
                {
                    case LoopCondition:
                        icon = "🔁";
                        category = "Flow";
                        break;
                    case TimeCondition:
                        icon = "⏱";
                        category = "Flow";
                        break;
                    case AltitudeCondition:
                        icon = "📐";
                        category = "Sky";
                        break;
                    case SunAltitudeCondition:
                        icon = "☀";
                        category = "Sky";
                        break;
                    case MoonAltitudeCondition:
                        icon = "🌙";
                        category = "Sky";
                        break;
                    case TwilightCondition:
                        icon = "🌅";
                        category = "Sky";
                        break;
                    case MoonIlluminationCondition:
                        icon = "🌑";
                        category = "Sky";
                        break;
                    case SafetyCondition:
                        icon = "🛡";
                        category = "Safety";
                        break;
                    case MeridianFlipCondition:
                        icon = "🔀";
                        category = "Sky";
                        break;
                }
                return true;
            default:
                return false;
        }
    }

    private static SequenceItemPropertyViewModel SeqProp(string name, object? value, Type type) =>
        new(name, FormatSeqPropValue(value, type), type);

    private static string FormatSeqPropValue(object? value, Type type)
    {
        if (value is null)
            return "";
        Type ut = Nullable.GetUnderlyingType(type) ?? type;
        if (ut == typeof(double) || ut == typeof(float))
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (ut.IsEnum)
            return value.ToString() ?? "";
        return value.ToString() ?? "";
    }

    private static SequenceItemPropertyViewModel[] WithInstructionMeta(ISequenceItem item, SequenceItemPropertyViewModel[] core)
    {
        if (item is SequenceContainer or ParallelContainer or DeepSkyObjectContainer)
            return core;

        var list = new List<SequenceItemPropertyViewModel>(core);
        AppendReflectiveInstructionMeta(item, list);
        return list.ToArray();
    }

    private static void AppendReflectiveInstructionMeta(ISequenceItem item, List<SequenceItemPropertyViewModel> list)
    {
        Type t = item.GetType();
        var existing = new HashSet<string>(list.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        foreach (string name in new[] { "Category", "ErrorBehavior", "Attempts" })
        {
            if (existing.Contains(name))
                continue;
            PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p?.CanRead != true)
                continue;
            list.Add(SeqProp(name, p.GetValue(item), p.PropertyType));
        }
    }

    private static SequenceItemPropertyViewModel[] BuildPropertiesForTrigger(ISequenceTrigger t) =>
        t switch
        {
            AutofocusAfterExposuresTrigger x => new[]
            {
                SeqProp(nameof(AutofocusAfterExposuresTrigger.ExposureCount), x.ExposureCount, typeof(int)),
            },
            AutofocusAfterTimeTrigger x => new[]
            {
                SeqProp(nameof(AutofocusAfterTimeTrigger.IntervalMinutes), x.IntervalMinutes, typeof(double)),
            },
            DitherAfterExposuresTrigger x => new[]
            {
                SeqProp(nameof(DitherAfterExposuresTrigger.ExposureCount), x.ExposureCount, typeof(int)),
            },
            MeridianFlipTrigger x => new[]
            {
                SeqProp(nameof(MeridianFlipTrigger.MinutesAfterMeridian), x.MinutesAfterMeridian, typeof(double)),
            },
            _ => Array.Empty<SequenceItemPropertyViewModel>(),
        };

    public static IReadOnlyList<SequenceItemPropertyViewModel> BuildPropertiesForItem(ISequenceItem item)
    {
        return item switch
        {
            SequenceContainer sc => new[]
            {
                SeqProp(nameof(SequenceContainer.Name), sc.Name, typeof(string)),
                SeqProp(nameof(SequenceContainer.Description), sc.Description, typeof(string)),
                SeqProp(nameof(SequenceContainer.IsEnabled), sc.IsEnabled, typeof(bool)),
            },
            ParallelContainer pc => new[]
            {
                SeqProp(nameof(ParallelContainer.Name), pc.Name, typeof(string)),
                SeqProp(nameof(ParallelContainer.Description), pc.Description, typeof(string)),
                SeqProp(nameof(ParallelContainer.IsEnabled), pc.IsEnabled, typeof(bool)),
            },
            DeepSkyObjectContainer d => new[]
            {
                SeqProp(nameof(DeepSkyObjectContainer.Name), d.Name, typeof(string)),
                SeqProp(nameof(DeepSkyObjectContainer.Description), d.Description, typeof(string)),
                SeqProp(nameof(DeepSkyObjectContainer.TargetName), d.TargetName, typeof(string)),
                SeqProp(nameof(DeepSkyObjectContainer.RA), d.RA, typeof(double)),
                SeqProp(nameof(DeepSkyObjectContainer.Dec), d.Dec, typeof(double)),
                SeqProp(nameof(DeepSkyObjectContainer.PositionAngle), d.PositionAngle, typeof(double)),
                SeqProp(nameof(DeepSkyObjectContainer.IsEnabled), d.IsEnabled, typeof(bool)),
            },
            ConditionInstruction ci => WithInstructionMeta(ci, BuildPropertiesForCondition(ci.Condition)),
            TriggerInstruction ti => WithInstructionMeta(ti, BuildPropertiesForTrigger(ti.Trigger)),
            CaptureVideoInstruction c => WithInstructionMeta(c, new[]
            {
                SeqProp(nameof(CaptureVideoInstruction.FrameLimit), c.FrameLimit, typeof(int)),
                SeqProp(nameof(CaptureVideoInstruction.TimeLimitSeconds), c.TimeLimitSeconds, typeof(double)),
                SeqProp(nameof(CaptureVideoInstruction.Format), c.Format.ToString(), typeof(CaptureFormat)),
                SeqProp(nameof(CaptureVideoInstruction.Gain), c.Gain, typeof(int)),
                SeqProp(nameof(CaptureVideoInstruction.ExposureUs), c.ExposureUs, typeof(double)),
                SeqProp(nameof(CaptureVideoInstruction.BinningX), c.BinningX, typeof(int)),
                SeqProp(nameof(CaptureVideoInstruction.BinningY), c.BinningY, typeof(int)),
                SeqProp(nameof(CaptureVideoInstruction.FilePrefix), c.FilePrefix, typeof(string)),
            }),
            ChangeFilterInstruction c => WithInstructionMeta(c, new[]
            {
                SeqProp(nameof(ChangeFilterInstruction.FilterPosition), c.FilterPosition, typeof(int)),
                SeqProp(nameof(ChangeFilterInstruction.FilterName), c.FilterName ?? "", typeof(string)),
            }),
            RunAutoFocusInstruction r => WithInstructionMeta(r, new[]
            {
                SeqProp(nameof(RunAutoFocusInstruction.StepSize), r.StepSize, typeof(int)),
                SeqProp(nameof(RunAutoFocusInstruction.InitialOffsetSteps), r.InitialOffsetSteps, typeof(int)),
                SeqProp(nameof(RunAutoFocusInstruction.NumberOfFramesPerPoint), r.NumberOfFramesPerPoint, typeof(int)),
                SeqProp(nameof(RunAutoFocusInstruction.FilterName), r.FilterName ?? "", typeof(string)),
            }),
            StartGuidingInstruction s => WithInstructionMeta(s, new[]
            {
                SeqProp(nameof(StartGuidingInstruction.TrackingMode), s.TrackingMode.ToString(), typeof(TrackingMode)),
                SeqProp(nameof(StartGuidingInstruction.ForceCalibration), s.ForceCalibration, typeof(bool)),
                SeqProp(nameof(StartGuidingInstruction.SettlePixels), s.SettlePixels, typeof(double)),
                SeqProp(nameof(StartGuidingInstruction.SettleTimeSeconds), s.SettleTimeSeconds, typeof(int)),
            }),
            StopGuidingInstruction s => WithInstructionMeta(s, Array.Empty<SequenceItemPropertyViewModel>()),
            SlewInstruction s => WithInstructionMeta(s, new[]
            {
                SeqProp(nameof(SlewInstruction.RA), s.RA, typeof(double)),
                SeqProp(nameof(SlewInstruction.Dec), s.Dec, typeof(double)),
                SeqProp(nameof(SlewInstruction.Rotate), s.Rotate, typeof(bool)),
                SeqProp(nameof(SlewInstruction.PositionAngle), s.PositionAngle, typeof(double)),
            }),
            WaitInstruction w => WithInstructionMeta(w, new[]
            {
                SeqProp(nameof(WaitInstruction.Seconds), w.Seconds, typeof(int)),
            }),
            AnnotationInstruction a => WithInstructionMeta(a, new[]
            {
                SeqProp(nameof(AnnotationInstruction.Message), a.Message, typeof(string)),
            }),
            OpenFlatCoverInstruction o => WithInstructionMeta(o, Array.Empty<SequenceItemPropertyViewModel>()),
            CloseFlatCoverInstruction c => WithInstructionMeta(c, Array.Empty<SequenceItemPropertyViewModel>()),
            SetFlatBrightnessInstruction f => WithInstructionMeta(f, new[]
            {
                SeqProp(nameof(SetFlatBrightnessInstruction.Brightness), f.Brightness, typeof(int)),
                SeqProp(nameof(SetFlatBrightnessInstruction.TurnOnLight), f.TurnOnLight, typeof(bool)),
            }),
            ToggleFlatLightInstruction t => WithInstructionMeta(t, new[]
            {
                SeqProp(nameof(ToggleFlatLightInstruction.LightOn), t.LightOn, typeof(bool)),
            }),
            ToggleSwitchInstruction t => WithInstructionMeta(t, new[]
            {
                SeqProp(nameof(ToggleSwitchInstruction.SwitchIndex), t.SwitchIndex, typeof(int)),
                SeqProp(nameof(ToggleSwitchInstruction.State), t.State, typeof(bool)),
            }),
            SetSwitchValueInstruction s => WithInstructionMeta(s, new[]
            {
                SeqProp(nameof(SetSwitchValueInstruction.SwitchIndex), s.SwitchIndex, typeof(int)),
                SeqProp(nameof(SetSwitchValueInstruction.Value), s.Value, typeof(double)),
            }),
            ParkMountInstruction p => WithInstructionMeta(p, Array.Empty<SequenceItemPropertyViewModel>()),
            UnparkMountInstruction u => WithInstructionMeta(u, Array.Empty<SequenceItemPropertyViewModel>()),
            CoolCameraInstruction c => WithInstructionMeta(c, new[]
            {
                SeqProp(nameof(CoolCameraInstruction.TargetTemperature), c.TargetTemperature, typeof(double)),
                SeqProp(nameof(CoolCameraInstruction.DurationMinutes), c.DurationMinutes, typeof(double)),
            }),
            WarmCameraInstruction w => WithInstructionMeta(w, new[]
            {
                SeqProp(nameof(WarmCameraInstruction.TargetTemperature), w.TargetTemperature, typeof(double)),
                SeqProp(nameof(WarmCameraInstruction.DurationMinutes), w.DurationMinutes, typeof(double)),
            }),
            MoveRotatorInstruction m => WithInstructionMeta(m, new[]
            {
                SeqProp(nameof(MoveRotatorInstruction.MechanicalPosition), m.MechanicalPosition, typeof(double)),
                SeqProp(nameof(MoveRotatorInstruction.IsRelative), m.IsRelative, typeof(bool)),
            }),
            DitherGuidingInstruction d => WithInstructionMeta(d, new[]
            {
                SeqProp(nameof(DitherGuidingInstruction.DitherPixels), d.DitherPixels, typeof(double)),
                SeqProp(nameof(DitherGuidingInstruction.RAOnly), d.RAOnly, typeof(bool)),
                SeqProp(nameof(DitherGuidingInstruction.SettleTimeSeconds), d.SettleTimeSeconds, typeof(int)),
            }),
            WaitForAltitudeInstruction w => WithInstructionMeta(w, new[]
            {
                SeqProp(nameof(WaitForAltitudeInstruction.MinAltitude), w.MinAltitude, typeof(double)),
                SeqProp(nameof(WaitForAltitudeInstruction.CheckIntervalSeconds), w.CheckIntervalSeconds, typeof(double)),
            }),
            WaitForTimeInstruction w => WithInstructionMeta(w, new[]
            {
                SeqProp(nameof(WaitForTimeInstruction.TargetTimeUtc), w.TargetTimeUtc, typeof(string)),
            }),
            WaitForTwilightInstruction w => WithInstructionMeta(w, new[]
            {
                SeqProp(nameof(WaitForTwilightInstruction.TargetTwilight), w.TargetTwilight.ToString(), typeof(TwilightType)),
                SeqProp(nameof(WaitForTwilightInstruction.OrDarker), w.OrDarker, typeof(bool)),
            }),
            MeridianFlipInstruction m => WithInstructionMeta(m, new[]
            {
                SeqProp(nameof(MeridianFlipInstruction.PauseBeforeFlipSeconds), m.PauseBeforeFlipSeconds, typeof(int)),
                SeqProp(nameof(MeridianFlipInstruction.Recenter), m.Recenter, typeof(bool)),
                SeqProp(nameof(MeridianFlipInstruction.SettleTimeSeconds), m.SettleTimeSeconds, typeof(int)),
                SeqProp(nameof(MeridianFlipInstruction.AutoFocusAfterFlip), m.AutoFocusAfterFlip, typeof(bool)),
            }),
            SetTrackingInstruction s => WithInstructionMeta(s, new[]
            {
                SeqProp(nameof(SetTrackingInstruction.EnableTracking), s.EnableTracking, typeof(bool)),
            }),
            CenterTargetInstruction c => WithInstructionMeta(c, new[]
            {
                SeqProp(nameof(CenterTargetInstruction.RA), c.RA, typeof(double)),
                SeqProp(nameof(CenterTargetInstruction.Dec), c.Dec, typeof(double)),
                SeqProp(nameof(CenterTargetInstruction.Iterations), c.Iterations, typeof(int)),
                SeqProp(nameof(CenterTargetInstruction.ThresholdArcsec), c.ThresholdArcsec, typeof(double)),
            }),
            ExternalScriptInstruction e => WithInstructionMeta(e, new[]
            {
                SeqProp(nameof(ExternalScriptInstruction.FilePath), e.FilePath, typeof(string)),
                SeqProp(nameof(ExternalScriptInstruction.Arguments), e.Arguments, typeof(string)),
                SeqProp(nameof(ExternalScriptInstruction.TimeoutSeconds), e.TimeoutSeconds, typeof(int)),
            }),
            SendNotificationInstruction s => WithInstructionMeta(s, new[]
            {
                SeqProp(nameof(SendNotificationInstruction.Title), s.Title, typeof(string)),
                SeqProp(nameof(SendNotificationInstruction.Message), s.Message, typeof(string)),
            }),
            TakeExposureInstruction t => WithInstructionMeta(t, new[]
            {
                SeqProp(nameof(TakeExposureInstruction.ExposureSeconds), t.ExposureSeconds, typeof(double)),
                SeqProp(nameof(TakeExposureInstruction.Gain), t.Gain, typeof(int)),
                SeqProp(nameof(TakeExposureInstruction.Offset), t.Offset, typeof(int)),
                SeqProp(nameof(TakeExposureInstruction.BinningX), t.BinningX, typeof(int)),
                SeqProp(nameof(TakeExposureInstruction.BinningY), t.BinningY, typeof(int)),
                SeqProp(nameof(TakeExposureInstruction.ImageType), t.ImageType, typeof(string)),
                SeqProp(nameof(TakeExposureInstruction.FilterName), t.FilterName ?? "", typeof(string)),
                SeqProp(nameof(TakeExposureInstruction.TotalExposureCount), t.TotalExposureCount, typeof(int)),
                SeqProp(nameof(TakeExposureInstruction.FilePrefix), t.FilePrefix, typeof(string)),
            }),
            SetReadoutModeInstruction s => WithInstructionMeta(s, new[]
            {
                SeqProp(nameof(SetReadoutModeInstruction.PixelFormat), s.PixelFormat, typeof(string)),
            }),
            MoveFocuserAbsoluteInstruction m => WithInstructionMeta(m, new[]
            {
                SeqProp(nameof(MoveFocuserAbsoluteInstruction.Position), m.Position, typeof(int)),
            }),
            MoveFocuserRelativeInstruction m => WithInstructionMeta(m, new[]
            {
                SeqProp(nameof(MoveFocuserRelativeInstruction.Steps), m.Steps, typeof(int)),
            }),
            ConnectEquipmentInstruction c => WithInstructionMeta(c, Array.Empty<SequenceItemPropertyViewModel>()),
            DisconnectEquipmentInstruction d => WithInstructionMeta(d, Array.Empty<SequenceItemPropertyViewModel>()),
            SlewScopeToAltAzInstruction s => WithInstructionMeta(s, new[]
            {
                SeqProp(nameof(SlewScopeToAltAzInstruction.Altitude), s.Altitude, typeof(double)),
                SeqProp(nameof(SlewScopeToAltAzInstruction.Azimuth), s.Azimuth, typeof(double)),
            }),
            WaitForTimeSpanInstruction w => WithInstructionMeta(w, new[]
            {
                SeqProp(nameof(WaitForTimeSpanInstruction.Hours), w.Hours, typeof(double)),
                SeqProp(nameof(WaitForTimeSpanInstruction.Minutes), w.Minutes, typeof(double)),
                SeqProp(nameof(WaitForTimeSpanInstruction.Seconds), w.Seconds, typeof(double)),
            }),
            MessageBoxInstruction m => WithInstructionMeta(m, new[]
            {
                SeqProp(nameof(MessageBoxInstruction.Text), m.Text, typeof(string)),
            }),
            SaveSequenceInstruction s => WithInstructionMeta(s, Array.Empty<SequenceItemPropertyViewModel>()),
            _ => Array.Empty<SequenceItemPropertyViewModel>(),
        };

        static SequenceItemPropertyViewModel[] BuildPropertiesForCondition(ISequenceCondition c) =>
            c switch
            {
                LoopCondition l => new[]
                {
                    SeqProp(nameof(LoopCondition.MaxIterations), l.MaxIterations, typeof(int)),
                },
                TimeCondition t => new[]
                {
                    SeqProp(nameof(TimeCondition.MaxDuration), t.MaxDuration.ToString(), typeof(TimeSpan)),
                },
                AltitudeCondition a => new[]
                {
                    SeqProp(nameof(AltitudeCondition.MinAltitude), a.MinAltitude, typeof(double)),
                },
                SunAltitudeCondition s => new[]
                {
                    SeqProp(nameof(SunAltitudeCondition.AboveThreshold), s.AboveThreshold, typeof(bool)),
                    SeqProp(nameof(SunAltitudeCondition.ThresholdAltitude), s.ThresholdAltitude, typeof(double)),
                },
                MoonAltitudeCondition m => new[]
                {
                    SeqProp(nameof(MoonAltitudeCondition.AboveThreshold), m.AboveThreshold, typeof(bool)),
                    SeqProp(nameof(MoonAltitudeCondition.ThresholdAltitude), m.ThresholdAltitude, typeof(double)),
                },
                TwilightCondition tw => new[]
                {
                    SeqProp(nameof(TwilightCondition.RequiredTwilight), tw.RequiredTwilight.ToString(), typeof(TwilightType)),
                    SeqProp(nameof(TwilightCondition.OrDarker), tw.OrDarker, typeof(bool)),
                },
                MoonIlluminationCondition mi => new[]
                {
                    SeqProp(nameof(MoonIlluminationCondition.MaxIllumination), mi.MaxIllumination, typeof(double)),
                },
                SafetyCondition sa => new[]
                {
                    SeqProp(nameof(SafetyCondition.RequireTracking), sa.RequireTracking, typeof(bool)),
                    SeqProp(nameof(SafetyCondition.RequireCameraConnected), sa.RequireCameraConnected, typeof(bool)),
                },
                MeridianFlipCondition mf => new[]
                {
                    SeqProp(nameof(MeridianFlipCondition.HoursThreshold), mf.HoursThreshold, typeof(double)),
                },
                _ => Array.Empty<SequenceItemPropertyViewModel>(),
            };
    }

    public static ISequenceItem CloneItem(ISequenceItem item)
    {
        return item switch
        {
            CaptureVideoInstruction c => new CaptureVideoInstruction
            {
                Name = c.Name,
                Description = c.Description,
                FrameLimit = c.FrameLimit,
                TimeLimitSeconds = c.TimeLimitSeconds,
                Format = c.Format,
                Gain = c.Gain,
                ExposureUs = c.ExposureUs,
                BinningX = c.BinningX,
                BinningY = c.BinningY,
                FilePrefix = c.FilePrefix,
            },
            ChangeFilterInstruction c => new ChangeFilterInstruction
            {
                Name = c.Name,
                Description = c.Description,
                FilterPosition = c.FilterPosition,
                FilterName = c.FilterName,
            },
            RunAutoFocusInstruction c => new RunAutoFocusInstruction
            {
                Name = c.Name,
                Description = c.Description,
                StepSize = c.StepSize,
                InitialOffsetSteps = c.InitialOffsetSteps,
                NumberOfFramesPerPoint = c.NumberOfFramesPerPoint,
                FilterName = c.FilterName,
            },
            StartGuidingInstruction c => new StartGuidingInstruction
            {
                Name = c.Name,
                Description = c.Description,
                TrackingMode = c.TrackingMode,
                ForceCalibration = c.ForceCalibration,
                SettlePixels = c.SettlePixels,
                SettleTimeSeconds = c.SettleTimeSeconds,
            },
            StopGuidingInstruction c => new StopGuidingInstruction { Name = c.Name, Description = c.Description },
            SlewInstruction c => new SlewInstruction
            {
                Name = c.Name,
                Description = c.Description,
                RA = c.RA,
                Dec = c.Dec,
                Rotate = c.Rotate,
                PositionAngle = c.PositionAngle,
            },
            WaitInstruction c => new WaitInstruction
            {
                Name = c.Name,
                Description = c.Description,
                Seconds = c.Seconds,
            },
            AnnotationInstruction c => new AnnotationInstruction
            {
                Name = c.Name,
                Description = c.Description,
                Message = c.Message,
            },
            OpenFlatCoverInstruction c => new OpenFlatCoverInstruction { Name = c.Name, Description = c.Description },
            CloseFlatCoverInstruction c => new CloseFlatCoverInstruction { Name = c.Name, Description = c.Description },
            SetFlatBrightnessInstruction c => new SetFlatBrightnessInstruction
            {
                Name = c.Name,
                Description = c.Description,
                Brightness = c.Brightness,
                TurnOnLight = c.TurnOnLight,
            },
            ToggleFlatLightInstruction c => new ToggleFlatLightInstruction
            {
                Name = c.Name,
                Description = c.Description,
                LightOn = c.LightOn,
            },
            ToggleSwitchInstruction c => new ToggleSwitchInstruction
            {
                Name = c.Name,
                Description = c.Description,
                SwitchIndex = c.SwitchIndex,
                State = c.State,
            },
            SetSwitchValueInstruction c => new SetSwitchValueInstruction
            {
                Name = c.Name,
                Description = c.Description,
                SwitchIndex = c.SwitchIndex,
                Value = c.Value,
            },
            ParkMountInstruction c => new ParkMountInstruction { Name = c.Name, Description = c.Description },
            UnparkMountInstruction c => new UnparkMountInstruction { Name = c.Name, Description = c.Description },
            CoolCameraInstruction c => new CoolCameraInstruction
            {
                Name = c.Name,
                Description = c.Description,
                TargetTemperature = c.TargetTemperature,
                DurationMinutes = c.DurationMinutes,
            },
            WarmCameraInstruction c => new WarmCameraInstruction
            {
                Name = c.Name,
                Description = c.Description,
                TargetTemperature = c.TargetTemperature,
                DurationMinutes = c.DurationMinutes,
            },
            MoveRotatorInstruction c => new MoveRotatorInstruction
            {
                Name = c.Name,
                Description = c.Description,
                MechanicalPosition = c.MechanicalPosition,
                IsRelative = c.IsRelative,
            },
            DitherGuidingInstruction c => new DitherGuidingInstruction
            {
                Name = c.Name,
                Description = c.Description,
                DitherPixels = c.DitherPixels,
                RAOnly = c.RAOnly,
                SettleTimeSeconds = c.SettleTimeSeconds,
            },
            WaitForAltitudeInstruction c => new WaitForAltitudeInstruction
            {
                Name = c.Name,
                Description = c.Description,
                MinAltitude = c.MinAltitude,
                CheckIntervalSeconds = c.CheckIntervalSeconds,
            },
            WaitForTimeInstruction c => new WaitForTimeInstruction
            {
                Name = c.Name,
                Description = c.Description,
                TargetTimeUtc = c.TargetTimeUtc,
            },
            WaitForTwilightInstruction c => new WaitForTwilightInstruction
            {
                Name = c.Name,
                Description = c.Description,
                TargetTwilight = c.TargetTwilight,
                OrDarker = c.OrDarker,
            },
            MeridianFlipInstruction c => new MeridianFlipInstruction
            {
                Name = c.Name,
                Description = c.Description,
                PauseBeforeFlipSeconds = c.PauseBeforeFlipSeconds,
                Recenter = c.Recenter,
                SettleTimeSeconds = c.SettleTimeSeconds,
                AutoFocusAfterFlip = c.AutoFocusAfterFlip,
            },
            SetTrackingInstruction c => new SetTrackingInstruction
            {
                Name = c.Name,
                Description = c.Description,
                EnableTracking = c.EnableTracking,
            },
            CenterTargetInstruction c => new CenterTargetInstruction
            {
                Name = c.Name,
                Description = c.Description,
                RA = c.RA,
                Dec = c.Dec,
                Iterations = c.Iterations,
                ThresholdArcsec = c.ThresholdArcsec,
            },
            ExternalScriptInstruction c => new ExternalScriptInstruction
            {
                Name = c.Name,
                Description = c.Description,
                FilePath = c.FilePath,
                Arguments = c.Arguments,
                TimeoutSeconds = c.TimeoutSeconds,
            },
            SendNotificationInstruction c => new SendNotificationInstruction
            {
                Name = c.Name,
                Description = c.Description,
                Title = c.Title,
                Message = c.Message,
            },
            TakeExposureInstruction c => new TakeExposureInstruction
            {
                Name = c.Name,
                Description = c.Description,
                ExposureSeconds = c.ExposureSeconds,
                Gain = c.Gain,
                Offset = c.Offset,
                BinningX = c.BinningX,
                BinningY = c.BinningY,
                ImageType = c.ImageType,
                FilterName = c.FilterName,
                TotalExposureCount = c.TotalExposureCount,
                FilePrefix = c.FilePrefix,
            },
            SetReadoutModeInstruction c => new SetReadoutModeInstruction
            {
                Name = c.Name,
                Description = c.Description,
                PixelFormat = c.PixelFormat,
            },
            MoveFocuserAbsoluteInstruction c => new MoveFocuserAbsoluteInstruction
            {
                Name = c.Name,
                Description = c.Description,
                Category = c.Category,
                ErrorBehavior = c.ErrorBehavior,
                Attempts = c.Attempts,
                IsEnabled = c.IsEnabled,
                Position = c.Position,
            },
            MoveFocuserRelativeInstruction c => new MoveFocuserRelativeInstruction
            {
                Name = c.Name,
                Description = c.Description,
                Category = c.Category,
                ErrorBehavior = c.ErrorBehavior,
                Attempts = c.Attempts,
                IsEnabled = c.IsEnabled,
                Steps = c.Steps,
            },
            ConnectEquipmentInstruction c => new ConnectEquipmentInstruction
            {
                Name = c.Name,
                Description = c.Description,
                Category = c.Category,
                ErrorBehavior = c.ErrorBehavior,
                Attempts = c.Attempts,
                IsEnabled = c.IsEnabled,
            },
            DisconnectEquipmentInstruction c => new DisconnectEquipmentInstruction
            {
                Name = c.Name,
                Description = c.Description,
                Category = c.Category,
                ErrorBehavior = c.ErrorBehavior,
                Attempts = c.Attempts,
                IsEnabled = c.IsEnabled,
            },
            SlewScopeToAltAzInstruction c => new SlewScopeToAltAzInstruction
            {
                Name = c.Name,
                Description = c.Description,
                Category = c.Category,
                ErrorBehavior = c.ErrorBehavior,
                Attempts = c.Attempts,
                IsEnabled = c.IsEnabled,
                Altitude = c.Altitude,
                Azimuth = c.Azimuth,
            },
            WaitForTimeSpanInstruction c => new WaitForTimeSpanInstruction
            {
                Name = c.Name,
                Description = c.Description,
                Category = c.Category,
                ErrorBehavior = c.ErrorBehavior,
                Attempts = c.Attempts,
                IsEnabled = c.IsEnabled,
                Hours = c.Hours,
                Minutes = c.Minutes,
                Seconds = c.Seconds,
            },
            MessageBoxInstruction c => new MessageBoxInstruction
            {
                Name = c.Name,
                Description = c.Description,
                Category = c.Category,
                ErrorBehavior = c.ErrorBehavior,
                Attempts = c.Attempts,
                IsEnabled = c.IsEnabled,
                Text = c.Text,
            },
            SaveSequenceInstruction c => new SaveSequenceInstruction
            {
                Name = c.Name,
                Description = c.Description,
                Category = c.Category,
                ErrorBehavior = c.ErrorBehavior,
                Attempts = c.Attempts,
                IsEnabled = c.IsEnabled,
            },
            TriggerInstruction c => new TriggerInstruction
            {
                Name = c.Name,
                Description = c.Description,
                Category = c.Category,
                ErrorBehavior = c.ErrorBehavior,
                Attempts = c.Attempts,
                IsEnabled = c.IsEnabled,
                Trigger = CloneTrigger(c.Trigger),
            },
            ConditionInstruction c => new ConditionInstruction
            {
                Name = c.Name,
                Description = c.Description,
                Condition = CloneCondition(c.Condition),
            },
            SequenceContainer sc => CloneSequenceContainer(sc),
            ParallelContainer pc => CloneParallelContainer(pc),
            DeepSkyObjectContainer d => CloneDeepSkyObjectContainer(d),
            _ => throw new NotSupportedException($"Cannot duplicate item type {item.GetType().Name}"),
        };
    }

    private static SequenceContainer CloneSequenceContainer(SequenceContainer sc)
    {
        var n = new SequenceContainer
        {
            Name = sc.Name,
            Description = sc.Description,
            IsEnabled = sc.IsEnabled,
        };
        foreach (ISequenceItem i in sc.Items)
            n.Items.Add(CloneItem(i));
        foreach (ISequenceCondition c in sc.Conditions)
            n.Conditions.Add(CloneCondition(c));
        foreach (ISequenceTrigger t in sc.Triggers)
            n.Triggers.Add(CloneTrigger(t));
        return n;
    }

    private static ParallelContainer CloneParallelContainer(ParallelContainer pc)
    {
        var n = new ParallelContainer
        {
            Name = pc.Name,
            Description = pc.Description,
            IsEnabled = pc.IsEnabled,
        };
        foreach (ISequenceItem i in pc.Items)
            n.Items.Add(CloneItem(i));
        return n;
    }

    private static DeepSkyObjectContainer CloneDeepSkyObjectContainer(DeepSkyObjectContainer d)
    {
        var n = new DeepSkyObjectContainer
        {
            Name = d.Name,
            Description = d.Description,
            TargetName = d.TargetName,
            RA = d.RA,
            Dec = d.Dec,
            PositionAngle = d.PositionAngle,
            IsEnabled = d.IsEnabled,
        };
        foreach (ISequenceItem i in d.Items)
            n.Items.Add(CloneItem(i));
        foreach (ISequenceCondition c in d.Conditions)
            n.Conditions.Add(CloneCondition(c));
        foreach (ISequenceTrigger t in d.Triggers)
            n.Triggers.Add(CloneTrigger(t));
        return n;
    }

    private static ISequenceTrigger CloneTrigger(ISequenceTrigger t) =>
        t switch
        {
            AutofocusAfterExposuresTrigger x => new AutofocusAfterExposuresTrigger
            {
                Name = x.Name,
                ExposureCount = x.ExposureCount,
            },
            AutofocusAfterTimeTrigger x => new AutofocusAfterTimeTrigger
            {
                Name = x.Name,
                IntervalMinutes = x.IntervalMinutes,
            },
            DitherAfterExposuresTrigger x => new DitherAfterExposuresTrigger
            {
                Name = x.Name,
                ExposureCount = x.ExposureCount,
            },
            MeridianFlipTrigger x => new MeridianFlipTrigger
            {
                Name = x.Name,
                MinutesAfterMeridian = x.MinutesAfterMeridian,
            },
            AutofocusOnFilterChangeTrigger x => new AutofocusOnFilterChangeTrigger { Name = x.Name },
            _ => throw new NotSupportedException($"Cannot duplicate trigger type {t.GetType().Name}"),
        };

    private static ISequenceCondition CloneCondition(ISequenceCondition c) =>
        c switch
        {
            LoopCondition l => new LoopCondition { Name = l.Name, MaxIterations = l.MaxIterations },
            TimeCondition t => new TimeCondition { Name = t.Name, MaxDuration = t.MaxDuration },
            AltitudeCondition a => new AltitudeCondition { Name = a.Name, MinAltitude = a.MinAltitude },
            SunAltitudeCondition s => new SunAltitudeCondition
            {
                Name = s.Name,
                AboveThreshold = s.AboveThreshold,
                ThresholdAltitude = s.ThresholdAltitude,
            },
            MoonAltitudeCondition m => new MoonAltitudeCondition
            {
                Name = m.Name,
                AboveThreshold = m.AboveThreshold,
                ThresholdAltitude = m.ThresholdAltitude,
            },
            TwilightCondition tw => new TwilightCondition
            {
                Name = tw.Name,
                RequiredTwilight = tw.RequiredTwilight,
                OrDarker = tw.OrDarker,
            },
            MoonIlluminationCondition mi => new MoonIlluminationCondition
            {
                Name = mi.Name,
                MaxIllumination = mi.MaxIllumination,
            },
            SafetyCondition sa => new SafetyCondition
            {
                Name = sa.Name,
                RequireTracking = sa.RequireTracking,
                RequireCameraConnected = sa.RequireCameraConnected,
            },
            MeridianFlipCondition mf => new MeridianFlipCondition
            {
                Name = mf.Name,
                HoursThreshold = mf.HoursThreshold,
            },
            _ => throw new NotSupportedException($"Cannot duplicate condition type {c.GetType().Name}"),
        };
}

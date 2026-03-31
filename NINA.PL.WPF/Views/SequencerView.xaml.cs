using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NINA.PL.WPF.ViewModels;

namespace NINA.PL.WPF.Views;

public partial class SequencerView
{
    private const double DragThresholdPx = 8;

    private Point _dragStart;
    private SequenceNodeViewModel? _pendingDrag;
    private Point _paletteDragStart;
    private object? _pendingPaletteDrag;
    private bool _paletteDragStarted;

    public SequencerView()
    {
        InitializeComponent();
    }

    private void OnNodeMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        if (sender is FrameworkElement fe && fe.Tag is SequenceNodeViewModel vm)
            _pendingDrag = vm;
    }

    private void OnNodeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pendingDrag is null)
            return;
        Point p = e.GetPosition(null);
        Vector d = p - _dragStart;
        if (d.Length < DragThresholdPx)
            return;

        if (DataContext is not SequencerPanelViewModel panel)
            return;

        panel.DragStartCommand.Execute(_pendingDrag);
        var data = new DataObject(typeof(SequenceNodeViewModel), _pendingDrag);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        panel.ClearDragVisuals();
        _pendingDrag = null;
    }

    private void OnNodeMouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            _pendingDrag = null;
    }

    private void OnNodeDragOver(object sender, DragEventArgs e)
    {
        if (DataContext is not SequencerPanelViewModel panel)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        bool isPaletteDrag = e.Data.GetDataPresent(typeof(InstructionTemplate)) ||
                             e.Data.GetDataPresent(typeof(ConditionTemplate));
        bool isNodeDrag = panel.DraggedNode is not null;

        if (!isPaletteDrag && !isNodeDrag)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (sender is FrameworkElement fe && fe.Tag is SequenceNodeViewModel target)
        {
            e.Effects = isPaletteDrag ? DragDropEffects.Copy : DragDropEffects.Move;
            SequencerDropMode mode = GetDropMode(target, fe, e);
            panel.SetDropIndicator(target, mode);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnNodeDragLeave(object sender, DragEventArgs e)
    {
        if (DataContext is not SequencerPanelViewModel panel)
            return;
        panel.SetDropIndicator(null, SequencerDropMode.Before);
    }

    private void OnNodeDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not SequencerPanelViewModel panel)
        {
            e.Handled = true;
            return;
        }

        if (sender is FrameworkElement fe && fe.Tag is SequenceNodeViewModel target)
        {
            if (e.Data.GetDataPresent(typeof(InstructionTemplate)) &&
                e.Data.GetData(typeof(InstructionTemplate)) is InstructionTemplate it)
            {
                panel.AddInstructionToNode(it, target);
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(typeof(ConditionTemplate)) &&
                e.Data.GetData(typeof(ConditionTemplate)) is ConditionTemplate ct)
            {
                panel.AddConditionToNode(ct, target);
                e.Handled = true;
                return;
            }
        }

        if (panel.DraggedNode is null)
        {
            e.Handled = true;
            return;
        }

        if (sender is not FrameworkElement fe2 || fe2.Tag is not SequenceNodeViewModel target2)
        {
            e.Handled = true;
            return;
        }

        SequencerDropMode mode = GetDropMode(target2, fe2, e);
        panel.DropAt(target2, mode);
        panel.SetDropIndicator(null, SequencerDropMode.Before);
        e.Handled = true;
    }

    private void OnPaletteTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        _paletteDragStart = e.GetPosition(null);
        _paletteDragStarted = false;
        if (sender is FrameworkElement fe)
            _pendingPaletteDrag = fe.Tag;
        e.Handled = true;
    }

    private void OnPaletteTileMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pendingPaletteDrag is null)
            return;
        Point p = e.GetPosition(null);
        Vector d = p - _paletteDragStart;
        if (d.Length < DragThresholdPx)
            return;

        _paletteDragStarted = true;

        DataObject data;
        if (_pendingPaletteDrag is InstructionTemplate it)
            data = new DataObject(typeof(InstructionTemplate), it);
        else if (_pendingPaletteDrag is ConditionTemplate ct)
            data = new DataObject(typeof(ConditionTemplate), ct);
        else
            return;

        if (DataContext is SequencerPanelViewModel panel)
            panel.SetDropIndicator(null, SequencerDropMode.Before);

        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);

        if (DataContext is SequencerPanelViewModel panel2)
            panel2.SetDropIndicator(null, SequencerDropMode.Before);

        _pendingPaletteDrag = null;
    }

    private void OnPaletteTileMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_paletteDragStarted || _pendingPaletteDrag is null)
        {
            _pendingPaletteDrag = null;
            return;
        }

        if (DataContext is SequencerPanelViewModel panel)
            panel.AddInstructionCommand.Execute(_pendingPaletteDrag);

        _pendingPaletteDrag = null;
        e.Handled = true;
    }

    private void OnSequenceAreaDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(InstructionTemplate)) ||
            e.Data.GetDataPresent(typeof(ConditionTemplate)))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (DataContext is SequencerPanelViewModel panel && panel.DraggedNode is not null)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnSequenceAreaDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not SequencerPanelViewModel panel)
        {
            e.Handled = true;
            return;
        }

        string section = (sender as FrameworkElement)?.Tag as string ?? "Target";
        var targetColl = section switch
        {
            "Start" => panel.StartSectionNodes,
            "End" => panel.EndSectionNodes,
            _ => panel.TargetSectionNodes,
        };

        if (e.Data.GetDataPresent(typeof(InstructionTemplate)) &&
            e.Data.GetData(typeof(InstructionTemplate)) is InstructionTemplate it)
        {
            var node = SequenceItemViewModelFactory.FromTemplate(it);
            targetColl.Add(node);
            panel.SelectedNode = node;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(typeof(ConditionTemplate)) &&
            e.Data.GetData(typeof(ConditionTemplate)) is ConditionTemplate ct)
        {
            var node = SequenceItemViewModelFactory.FromConditionTemplate(ct);
            targetColl.Add(node);
            panel.SelectedNode = node;
            e.Handled = true;
        }
    }

    private static SequencerDropMode GetDropMode(SequenceNodeViewModel target, FrameworkElement fe, DragEventArgs e)
    {
        Point pos = e.GetPosition(fe);
        double h = fe.ActualHeight;
        if (h <= 0)
            h = 1;
        double y = pos.Y / h;

        if (target.IsContainer)
        {
            if (y < 0.22)
                return SequencerDropMode.Before;
            if (y > 0.78)
                return SequencerDropMode.After;
            return SequencerDropMode.Inside;
        }

        return y < 0.5 ? SequencerDropMode.Before : SequencerDropMode.After;
    }
}

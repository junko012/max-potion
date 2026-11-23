using Avalonia.Controls;
using Avalonia.Input;
using Exxeguttor.UI.ViewModels;

namespace Exxeguttor.UI.Views;

public partial class EmptyStateView : UserControl
{
    public EmptyStateView()
    {
        InitializeComponent();
    }

    /// <summary>Click en una fila de "Recientes" — delega en RecentSaves.OpenEntryCommand
    /// (mismo comando que dispara MainWindowViewModel.OpenSaveFromPathAsync).</summary>
    private void RecentRow_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control control) return;
        if (control.DataContext is not RecentSaveRowViewModel row) return;
        if (DataContext is not MainWindowViewModel vm) return;

        if (vm.RecentSaves.OpenEntryCommand.CanExecute(row))
            vm.RecentSaves.OpenEntryCommand.Execute(row);
    }
}

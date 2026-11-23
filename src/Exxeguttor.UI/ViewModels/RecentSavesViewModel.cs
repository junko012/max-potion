using System.Collections.ObjectModel;
using System.Windows.Input;
using Exxeguttor.App.Services;
using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Orquesta la zona "Recientes" de la Pantalla 1 (Estado vacío / Carga de save): lee la lista
/// persistida vía <see cref="RecentSavesService"/>, arma las filas para bindear
/// (<see cref="RecentSaveRowViewModel"/>), y expone el evento de "abrir este save" para que
/// <c>MainWindowViewModel</c> reuse el mismo flujo que el botón "Abrir save...".
/// </summary>
public class RecentSavesViewModel
{
    private readonly RecentSavesService _recentSaves;
    private readonly GameCoverService _gameCovers;

    public ObservableCollection<RecentSaveRowViewModel> Entries { get; } = new();

    public bool HasEntries => Entries.Count > 0;

    /// <summary>Se dispara cuando el usuario clickea una fila — MainWindowViewModel decide
    /// cómo abrir ese path (mismo flujo interno que OpenSaveFileCommand).</summary>
    public event Action<string>? OpenRequested;

    public ICommand OpenEntryCommand { get; }

    public RecentSavesViewModel(RecentSavesService recentSaves, GameCoverService gameCovers)
    {
        _recentSaves = recentSaves;
        _gameCovers = gameCovers;
        OpenEntryCommand = new RelayCommand<RecentSaveRowViewModel>(row => OpenRequested?.Invoke(row.FilePath));

        Refresh();
    }

    /// <summary>Vuelve a leer <c>recent.json</c> y reconstruye las filas. Se llama al arrancar
    /// la app y después de cada apertura exitosa de un save (para que la fila nueva/movida al
    /// tope aparezca sin tener que reiniciar).</summary>
    public void Refresh()
    {
        Entries.Clear();
        var saved = _recentSaves.GetAll();
        for (int i = 0; i < saved.Count; i++)
            Entries.Add(new RecentSaveRowViewModel(saved[i], isLatest: i == 0, _gameCovers));
    }

    /// <summary>Registra un save recién abierto exitosamente y refresca la lista. Llamar desde
    /// <c>MainWindowViewModel.OpenSaveFileAsync</c> justo después de abrir con éxito (mismo
    /// código crudo <c>save.Version.ToString()</c> que ya usa <c>TrainerInfo.GameName</c>).</summary>
    public void NotifyOpened(string filePath, string gameVersion)
    {
        _recentSaves.Add(filePath, gameVersion);
        Refresh();
    }

    /// <summary>Saca una entrada de "Recientes" — botón "Quitar de recientes" del modal de
    /// error "Abra se teletransportó" (<see cref="SaveOpenErrorKind.NotFound"/>).</summary>
    public void Remove(string filePath)
    {
        _recentSaves.Remove(filePath);
        Refresh();
    }
}

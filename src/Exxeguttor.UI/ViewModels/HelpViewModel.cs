using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Exxeguttor.App.Services;
using Exxeguttor.UI.i18n;
using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.ViewModels;

public enum HelpPanel { None, About, Shortcuts, Report, Crash }

public sealed record ShortcutRow(string Keys, string Description);
public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutRow> Rows);
public sealed record CreditLine(string Name, string Role, string? Note, string? Url);

/// <summary>Una licencia de terceros en la pestaña «Licencias»: título + texto completo expandible (embebido).</summary>
public sealed class LicenseItemViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    private readonly string? _licenseId;
    private string? _text;
    private bool _expanded;

    public string Name { get; }
    public string By { get; }
    public string LicenseName { get; }
    public string? Url { get; }
    public bool HasText => _licenseId != null;
    public ICommand ToggleCommand { get; }
    public ICommand OpenUrlCommand { get; }

    public LicenseItemViewModel(string name, string by, string? licenseName, string? licenseId, string? url,
        string noLicenseLabel, ICommand openUrl)
    {
        Name = name; By = by; Url = url; _licenseId = licenseId;
        LicenseName = licenseName ?? noLicenseLabel;
        OpenUrlCommand = openUrl;
        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public bool IsExpanded
    {
        get => _expanded;
        set { if (_expanded == value) return; _expanded = value; Raise(); Raise(nameof(Arrow)); Raise(nameof(Text)); }
    }
    public string Arrow => IsExpanded ? "▾" : "▸";

    /// <summary>Texto completo de la licencia; se lee del recurso la primera vez que se expande.</summary>
    public string Text => IsExpanded && _licenseId != null ? _text ??= Load(_licenseId) : "";

    private static string Load(string id)
    {
        try
        {
            using var s = AssetLoader.Open(new Uri($"avares://maxpotion/Assets/legal/{id}.txt"));
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }
        catch { return "(license text unavailable)"; }
    }
}

/// <summary>
/// Menú Ayuda: «Acerca de», Atajos y Reportar un error (mockups/help_about). Un solo panel visible a la vez.
/// La app no envía nada: «Reportar» abre el formulario de GitHub en el navegador, ya prellenado.
/// </summary>
public sealed class HelpViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    private readonly LocalizationService _loc;
    private readonly Func<DiagnosticsInfo> _diag;
    private readonly Action _showTips;
    private readonly Func<string, Task> _copy;
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public LocalizationService Loc => _loc;

    public HelpViewModel(LocalizationService loc, Func<DiagnosticsInfo> diagnostics, Action showTips, Func<string, Task> copyToClipboard)
    {
        _loc = loc; _diag = diagnostics; _showTips = showTips; _copy = copyToClipboard;
        _noticeTimer.Tick += (_, _) => { _noticeTimer.Stop(); Notice = ""; };

        OpenAboutCommand = new RelayCommand(() => { AboutTab = 0; Open(HelpPanel.About); });
        OpenShortcutsCommand = new RelayCommand(() => Open(HelpPanel.Shortcuts));
        OpenReportCommand = new RelayCommand(() => { ReportTitle = ""; Open(HelpPanel.Report); });
        ShowGuideCommand = new RelayCommand(() => { Close(); _showTips(); });
        SuggestFeatureCommand = new RelayCommand(() => Launch(AppLinks.FeatureRequestUrl));
        CheckUpdatesCommand = new RelayCommand(() => Launch(AppLinks.ReleasesUrl));
        OpenConfigFolderCommand = new RelayCommand(() => LaunchFolder(AppPaths.ConfigDir));
        OpenLogsFolderCommand = new RelayCommand(() => LaunchFolder(AppPaths.LogsDir));
        CloseCommand = new RelayCommand(Close);
        SelectTabCommand = new RelayCommand<string>(t => { if (int.TryParse(t, out var i)) AboutTab = i; });
        OpenUrlCommand = new RelayCommand<string>(u => { if (!string.IsNullOrEmpty(u)) Launch(u); });
        SendReportCommand = new RelayCommand(() => { if (Launch(ReportUrl)) Close(); });
        CopyReportLinkCommand = new RelayCommand(() => CopyText(ReportUrl));
        CopySystemCommand = new RelayCommand(() => CopyText(SystemText));
        CopyCrashCommand = new RelayCommand(() => CopyText(SystemText + "\n\nError:\n" + CrashDetail));
        ReportCrashCommand = new RelayCommand(() =>
        {
            var url = IssueUrlBuilder.BuildBugReport(_crashSummary, _diag(), errorDetail: CrashDetail);
            if (Launch(url)) Close();
        });
        CloseAppCommand = new RelayCommand(() =>
        {
            AppLog.Info("Closed by the user from the error dialog.");
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d)
                d.Shutdown();
        });

        Rebuild();
        _loc.PropertyChanged += (_, e) => { if (e.PropertyName is "Item" or "Item[]" or null or "") Rebuild(); };
    }

    // ── Estado ───────────────────────────────────────────────────────────────────────────

    private HelpPanel _panel;
    public HelpPanel Panel => _panel;
    public bool IsAnyVisible => _panel != HelpPanel.None;
    public bool IsAboutVisible => _panel == HelpPanel.About;
    public bool IsShortcutsVisible => _panel == HelpPanel.Shortcuts;
    public bool IsReportVisible => _panel == HelpPanel.Report;
    public bool IsCrashVisible => _panel == HelpPanel.Crash;

    private string _crashDetail = "";
    private string _crashSummary = "";
    /// <summary>Texto técnico del último error mostrado (tipo, mensaje y primeras líneas de la pila, sin rutas personales).</summary>
    public string CrashDetail { get => _crashDetail; private set { _crashDetail = value; Raise(); } }
    public Avalonia.Media.Imaging.Bitmap? RotomGray { get; } = SpriteService.Instance.GetStatusRotomGraySprite();
    public string LogsHint => string.Format(_loc["Crash_LogHint"], AppPaths.LogsDir.Replace(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~"));

    /// <summary>Muestra «Algo salió mal» (siempre desde el hilo de UI). Si ya hay uno abierto, el nuevo solo queda en el registro.</summary>
    public void ShowCrash(Exception ex)
    {
        if (_panel == HelpPanel.Crash) return;
        _crashSummary = CrashReport.Summary(ex);
        CrashDetail = CrashReport.Format(ex);
        Open(HelpPanel.Crash);
    }

    private void Open(HelpPanel p)
    {
        Notice = "";
        _panel = p;
        if (p == HelpPanel.About) { Raise(nameof(SystemText)); }
        if (p == HelpPanel.Report) { Raise(nameof(ReportPreview)); }
        RaisePanel();
    }

    public void Close()
    {
        if (_panel == HelpPanel.None) return;
        _panel = HelpPanel.None;
        foreach (var l in Licenses) l.IsExpanded = false;
        RaisePanel();
    }

    private void RaisePanel()
    {
        Raise(nameof(Panel)); Raise(nameof(IsAnyVisible)); Raise(nameof(IsAboutVisible));
        Raise(nameof(IsShortcutsVisible)); Raise(nameof(IsReportVisible)); Raise(nameof(IsCrashVisible)); Raise(nameof(Notice));
    }

    private int _aboutTab;
    public int AboutTab
    {
        get => _aboutTab;
        set
        {
            if (_aboutTab == value) return;
            _aboutTab = value;
            Raise(); Raise(nameof(IsTabGeneral)); Raise(nameof(IsTabCredits)); Raise(nameof(IsTabLicenses)); Raise(nameof(IsTabSystem));
            if (value == 3) Raise(nameof(SystemText));
        }
    }
    public bool IsTabGeneral => _aboutTab == 0;
    public bool IsTabCredits => _aboutTab == 1;
    public bool IsTabLicenses => _aboutTab == 2;
    public bool IsTabSystem => _aboutTab == 3;

    private string _notice = "";
    /// <summary>Aviso breve (enlace copiado, etc.); se borra solo a los 4 s.</summary>
    public string Notice
    {
        get => _notice;
        private set { _notice = value; Raise(); Raise(nameof(HasNotice)); }
    }
    public bool HasNotice => _notice.Length > 0;

    private void ShowNotice(string text)
    {
        Notice = text;
        _noticeTimer.Stop(); _noticeTimer.Start();
    }

    // ── Textos de «General» ───────────────────────────────────────────────────────────────

    public string ProductName => AppInfo.ProductName;
    public string VersionLine => string.Format(_loc["About_Version"], AppInfo.Version);
    public string BuildLine
    {
        get
        {
            var parts = new[] { AppInfo.Commit is { } c ? "commit " + c : null, AppInfo.BuildDate }.Where(x => !string.IsNullOrEmpty(x));
            return string.Join(" · ", parts);
        }
    }
    public bool HasBuildLine => BuildLine.Length > 0;
    public string CreatorAlias => AppLinks.CreatorAlias;
    public string EngineLine => $"PKHeX.Core {AppInfo.PkhexVersion} · Avalonia {AppInfo.AvaloniaVersion} · {AppInfo.DotNet}";

    // ── Reportar un error ─────────────────────────────────────────────────────────────────

    private string _reportTitle = "";
    public string ReportTitle
    {
        get => _reportTitle;
        set { _reportTitle = value ?? ""; Raise(); }
    }

    private string ReportUrl => IssueUrlBuilder.BuildBugReport(ReportTitle, _diag());
    /// <summary>Lo que se incluiría (mismo texto que «Sistema», con la carpeta personal reemplazada por ~).</summary>
    public string ReportPreview => PathScrubber.Scrub(_diag().ToText());
    public string SystemText => PathScrubber.Scrub(_diag().ToText());

    // ── Listas (dependen del idioma) ─────────────────────────────────────────────────────

    public IReadOnlyList<ShortcutGroup> ShortcutGroups { get; private set; } = [];
    public IReadOnlyList<CreditLine> People { get; private set; } = [];
    public IReadOnlyList<CreditLine> Translators { get; private set; } = [];
    public IReadOnlyList<LicenseItemViewModel> Licenses { get; private set; } = [];
    public IReadOnlyList<LicenseItemViewModel> ThirdParty => Licenses;

    private void Rebuild()
    {
        string L(string k) => _loc[k];
        ShortcutGroups =
        [
            new(L("Shortcuts_Group_General"),
            [
                new("F1", L("Shortcuts_Guide")),
                new("Ctrl+O", L("Shortcuts_Open")),
                new("Ctrl+S", L("Shortcuts_Save")),
                new("Ctrl+Shift+S", L("Shortcuts_Export")),
                new("Ctrl+W", L("Shortcuts_Close")),
            ]),
            new(L("Shortcuts_Group_Review"), [new("←  →", L("Shortcuts_Review_Cards"))]),
            new(L("Shortcuts_Group_Tips"), [new("↑  ↓", L("Shortcuts_Tips_Scroll"))]),
            new(L("Shortcuts_Group_Create"),
            [
                new("←  →", L("Shortcuts_Create_Move")),
                new("Shift+←  →", L("Shortcuts_Create_Jump")),
                new("Home  End", L("Shortcuts_Create_Ends")),
                new("Enter", L("Shortcuts_Create_Confirm")),
            ]),
        ];

        var people = new List<CreditLine>();
        var translators = new List<CreditLine>();
        var licenses = new List<LicenseItemViewModel>();
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://maxpotion/Assets/credits.json"));
            using var doc = JsonDocument.Parse(s);
            var root = doc.RootElement;
            string? Str(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            if (root.TryGetProperty("creator", out var c))
                people.Add(new(Str(c, "name") ?? AppLinks.CreatorAlias, L("Credits_Role_Creator"), null, Str(c, "url")));
            if (root.TryGetProperty("contributors", out var cs))
                foreach (var e in cs.EnumerateArray())
                    people.Add(new(Str(e, "name") ?? "?", L("Credits_Role_Contributor"), null, Str(e, "url")));
            if (root.TryGetProperty("translators", out var ts))
                foreach (var e in ts.EnumerateArray())
                {
                    var noteKey = Str(e, "noteKey");
                    var scope = Str(e, "scope");
                    var note = noteKey != null ? L(noteKey) : null;
                    translators.Add(new(Str(e, "name") ?? "?", scope ?? "", note, null));
                }
            if (root.TryGetProperty("thirdParty", out var tp))
                foreach (var e in tp.EnumerateArray())
                    licenses.Add(new(Str(e, "name") ?? "?", Str(e, "by") ?? "", Str(e, "licenseName"), Str(e, "licenseId"),
                        Str(e, "url"), L("Licenses_NoText"), OpenUrlCommand));
        }
        catch { /* sin credits.json la pestaña queda vacía; nunca rompe la app */ }

        People = people; Translators = translators; Licenses = licenses;
        Raise(nameof(ShortcutGroups)); Raise(nameof(People)); Raise(nameof(Translators)); Raise(nameof(Licenses)); Raise(nameof(ThirdParty));
        Raise(nameof(VersionLine)); Raise(nameof(HasTranslators)); Raise(nameof(LogsHint));
    }

    public bool HasTranslators => Translators.Count > 0;

    // ── Comandos ─────────────────────────────────────────────────────────────────────────

    public ICommand OpenAboutCommand { get; }
    public ICommand OpenShortcutsCommand { get; }
    public ICommand OpenReportCommand { get; }
    public ICommand ShowGuideCommand { get; }
    public ICommand SuggestFeatureCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand OpenConfigFolderCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand SelectTabCommand { get; }
    public ICommand OpenUrlCommand { get; }
    public ICommand SendReportCommand { get; }
    public ICommand CopyReportLinkCommand { get; }
    public ICommand CopySystemCommand { get; }
    public ICommand CopyCrashCommand { get; }
    public ICommand ReportCrashCommand { get; }
    public ICommand CloseAppCommand { get; }

    /// <summary>Abre un enlace en el navegador; si no se puede, lo copia al portapapeles y avisa.</summary>
    private bool Launch(string url)
    {
        if (ExternalLauncher.Open(url)) return true;
        CopyText(url, _loc["Help_LinkCopied"]);
        return false;
    }

    private void LaunchFolder(string path)
    {
        if (!ExternalLauncher.OpenFolder(path))
            CopyText(path, _loc["Help_PathCopied"]);
    }

    private async void CopyText(string text, string? notice = null)
    {
        try { await _copy(text); ShowNotice(notice ?? _loc["Help_Copied"]); }
        catch { ShowNotice(_loc["Help_CopyFailed"]); }
    }
}

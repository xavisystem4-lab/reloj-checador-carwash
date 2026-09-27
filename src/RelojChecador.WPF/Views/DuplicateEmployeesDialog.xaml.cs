using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using RelojChecador.Application.Employees;
using RelojChecador.Domain.Employees;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>Una fila de <see cref="DuplicateEmployeesDialog"/>: un registro dentro de su grupo de
/// repetidos, con "Se queda" (radio, uno por grupo) y "Eliminar" (checkbox).</summary>
public sealed partial class DuplicateRow(int groupIndex, DuplicateGroup group, DuplicateCandidate candidate, bool isKeeper) : ObservableObject
{
    public int GroupIndex { get; } = groupIndex;
    public DuplicateGroup Group { get; } = group;
    public DuplicateCandidate Candidate { get; } = candidate;

    public string GroupLabel => (Group.NeedsReview ? "⚠ Revisar " : "") + $"#{GroupIndex + 1}";
    public string RadioGroup => $"dup-group-{GroupIndex}";
    public string Number => Candidate.Employee.Number.Value;
    public string FullName => Candidate.Employee.FullName;
    public int PunchCount => Candidate.PunchCount;
    public string PinText => Candidate.HasPin ? "✔" : "";
    public string StatusText => Candidate.Employee.Status switch
    {
        EmploymentStatus.Active => "Activo",
        EmploymentStatus.Terminated => "Baja",
        EmploymentStatus.Inactive => "Inactivo",
        _ => Candidate.Employee.Status.ToString(),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private bool _isKeeper = isKeeper;

    [ObservableProperty]
    private bool _toDelete = !isKeeper && !group.NeedsReview;

    public bool CanDelete => !IsKeeper;

    partial void OnIsKeeperChanged(bool value)
    {
        // Quien deja de ser "Se queda" pasa a eliminarse (salvo en grupos a revisar).
        ToDelete = !value && !Group.NeedsReview;
    }
}

/// <summary>
/// "🔍 Buscar repetidos" (pedido explícito del usuario, v1.70.0: "un botón que busque los
/// nombres y apellidos repetidos y con un click eliminarlos"). Busca con
/// <see cref="EmployeeDuplicateFinder"/>, ya deja marcado qué se queda y qué se elimina en cada
/// grupo seguro, y "Eliminar repetidos" lo aplica todo de una vez con
/// <see cref="EmployeesViewModel.MergeDuplicatesAsync"/> — sin perder marcaciones ni PINs.
/// </summary>
public partial class DuplicateEmployeesDialog : Window
{
    private readonly EmployeesViewModel _viewModel;
    private readonly ObservableCollection<DuplicateRow> _rows = [];

    public DuplicateEmployeesDialog(EmployeesViewModel viewModel, IReadOnlyList<DuplicateGroup> groups)
    {
        InitializeComponent();
        _viewModel = viewModel;
        Load(groups);
        RowsGrid.ItemsSource = _rows;
    }

    private void Load(IReadOnlyList<DuplicateGroup> groups)
    {
        foreach (var row in _rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }
        _rows.Clear();

        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            foreach (var candidate in group.All)
            {
                var row = new DuplicateRow(i, group, candidate, candidate == group.Keeper);
                row.PropertyChanged += OnRowChanged;
                _rows.Add(row);
            }
        }

        UpdateSummary();
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateSummary();

    private void UpdateSummary()
    {
        var groupCount = _rows.Select(r => r.GroupIndex).Distinct().Count();
        var toDelete = _rows.Count(r => r.ToDelete && !r.IsKeeper);
        SummaryTextBlock.Text = groupCount == 0
            ? "✅ No se encontraron repetidos."
            : $"{groupCount} grupo(s) · {toDelete} registro(s) marcados para eliminar";
        DeleteButton.Content = toDelete > 0 ? $"🧹 Eliminar {toDelete} repetido(s)" : "🧹 Eliminar repetidos";
        DeleteButton.IsEnabled = toDelete > 0;
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var requests = new List<EmployeesViewModel.DuplicateMergeRequest>();
        foreach (var group in _rows.GroupBy(r => r.GroupIndex))
        {
            var keeper = group.FirstOrDefault(r => r.IsKeeper);
            var remove = group.Where(r => r.ToDelete && !r.IsKeeper).ToList();
            if (keeper is null || remove.Count == 0)
            {
                continue;
            }

            // El nombre más completo entre quien se queda y los que se eliminan (nunca el de alguien
            // que no se está uniendo).
            var fullName = UseFullestNameCheckBox.IsChecked == true
                ? remove.Select(r => r.FullName).Append(keeper.FullName)
                    .OrderByDescending(n => n.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)
                    .ThenBy(n => n == keeper.FullName ? 0 : 1)
                    .First()
                : keeper.FullName;
            requests.Add(new(keeper.Candidate.Employee.Id, [.. remove.Select(r => r.Candidate.Employee.Id)], fullName));
        }

        if (requests.Count == 0)
        {
            return;
        }

        var total = requests.Sum(r => r.RemoveIds.Count);
        var confirmed = MessageBox.Show(this,
            $"¿Eliminar {total} registro(s) repetido(s)?\n\n" +
            "Sus marcaciones, PINs y datos pasan al registro que se queda en cada grupo; después se borran " +
            "(también del sitio web). No se puede deshacer.",
            "Eliminar repetidos", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        DeleteButton.IsEnabled = false;
        DeleteButton.Content = "Eliminando…";
        var outcome = await _viewModel.MergeDuplicatesAsync(requests);
        if (!outcome.Success)
        {
            MessageBox.Show(this, outcome.Error, "No se pudo eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
            UpdateSummary();
            return;
        }

        MessageBox.Show(this,
            $"Listo: {outcome.Deleted} repetido(s) eliminado(s). {outcome.PunchesMoved} marcación(es) y " +
            $"{outcome.PinsMoved} PIN(s) pasaron al registro que se quedó" +
            (outcome.DeductionsDeleted > 0 ? $"; {outcome.DeductionsDeleted} deducción(es) de nómina de los repetidos se borraron" : "") + "." +
            (outcome.CloudCleaned ? "" : "\n\nNo se pudo limpiar el sitio web ahora (sin internet o sin nube configurada): los repetidos pueden seguir apareciendo ahí."),
            "Repetidos eliminados", MessageBoxButton.OK, MessageBoxImage.Information);

        Load(await _viewModel.FindDuplicatesAsync());
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = true;
}

using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Licensing;

public enum ImportStep
{
    File,
    Columns,
    Preview,
    Done,
}

// One line of the "map your columns" step: which column of the file feeds this field.
public sealed partial class FieldMappingRow : ObservableObject
{
    public const string NotInFile = "(not in the file)";

    public FieldMappingRow(ImportFieldDto field, IReadOnlyList<string> options, string? selected)
    {
        Field = field;
        Options = options;
        _selectedHeader = selected ?? NotInFile;
    }

    public ImportFieldDto Field { get; }

    public string Label => Field.Required ? Field.Label + " *" : Field.Label;

    public string Hint => Field.Hint ?? string.Empty;

    public IReadOnlyList<string> Options { get; }

    [ObservableProperty]
    private string _selectedHeader;
}

// File -> map columns -> preview with row errors -> apply -> undo the last import. The file is read once and sent for
// every step, so nothing changes on the server until Apply.
public sealed partial class ImportWizardViewModel : ViewModelBase, INavigationAware
{
    private readonly IImportApi _api;
    private readonly IFilePicker _files;
    private readonly INotificationService _notifications;
    private readonly IDialogService _dialogs;
    private byte[] _content = Array.Empty<byte>();

    public ImportWizardViewModel(IImportApi api, IFilePicker files, INotificationService notifications, IDialogService dialogs)
    {
        _api = api;
        _files = files;
        _notifications = notifications;
        _dialogs = dialogs;
    }

    public IReadOnlyList<ImportKind> Kinds { get; } = new[] { ImportKind.Menu, ImportKind.Customers };

    public ObservableCollection<FieldMappingRow> Mapping { get; } = new();

    public ObservableCollection<ImportRowErrorDto> Errors { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindHelp))]
    private ImportKind _kind = ImportKind.Menu;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFileStep), nameof(IsColumnsStep), nameof(IsPreviewStep), nameof(IsDoneStep), nameof(StepNumber), nameof(CanApply))]
    private ImportStep _step = ImportStep.File;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    private string _fileName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(HasErrors), nameof(CanApply))]
    private ImportPreviewDto? _preview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    private bool _skipInvalid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultText))]
    private ImportResultDto? _result;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLast), nameof(LastText), nameof(CanUndo))]
    private ImportBatchDto? _last;

    public bool IsFileStep => Step == ImportStep.File;

    public bool IsColumnsStep => Step == ImportStep.Columns;

    public bool IsPreviewStep => Step == ImportStep.Preview;

    public bool IsDoneStep => Step == ImportStep.Done;

    public int StepNumber => (int)Step + 1;

    public bool HasFile => FileName.Length > 0;

    public bool HasErrors => Preview is { ErrorRows: > 0 };

    public string KindHelp => Kind == ImportKind.Menu
        ? "Columns: category, item name and price are needed; tax, station, code, description, HSN/SAC and modifiers are optional. Items already on the menu (same category and name) are updated."
        : "Columns: name is needed; phone, email, GSTIN, legal name, state and address are optional. Customers are matched by phone, then GSTIN.";

    public string Summary => Preview is null
        ? string.Empty
        : $"{Preview.TotalRows} rows: {Preview.ValidRows} ready ({Preview.ToCreate} new, {Preview.ToUpdate} to update), {Preview.ErrorRows} with errors.";

    public bool CanApply => IsPreviewStep && Preview is { ValidRows: > 0 } && (!HasErrors || SkipInvalid);

    public string ResultText => Result is null
        ? string.Empty
        : $"Imported: {Result.Created} added, {Result.Updated} updated" + (Result.Skipped > 0 ? $", {Result.Skipped} rows skipped." : ".");

    public bool HasLast => Last is not null;

    public bool CanUndo => Last is { CanUndo: true };

    public string LastText => Last is null
        ? string.Empty
        : Last.Status == ImportStatus.Undone
            ? $"The last import ({Last.FileName}) was undone."
            : $"Last import: {Last.FileName}, {Last.Created} added and {Last.Updated} updated on {Last.AppliedAtUtc.ToLocalTime():dd MMM yyyy HH:mm}.";

    public async Task OnNavigatedToAsync(object? parameter) => await LoadLastAsync();

    public void OnNavigatedFrom()
    {
    }

    private async Task LoadLastAsync()
    {
        var result = await _api.GetLastAsync();
        Last = result.Success ? result.Data : null;
    }

    [RelayCommand]
    private async Task ChooseFileAsync()
    {
        var path = _files.PickImportFile();
        if (path is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            _content = await File.ReadAllBytesAsync(path);
            FileName = Path.GetFileName(path);
            var result = await _api.PreviewAsync(Kind, new MemoryStream(_content), FileName, null);
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = ApiFailures.Describe(result);
                FileName = string.Empty;
                return;
            }

            Mapping.Clear();
            var options = new[] { FieldMappingRow.NotInFile }.Concat(result.Data.Headers).ToList();
            foreach (var field in result.Data.Fields)
            {
                Mapping.Add(new FieldMappingRow(field, options, result.Data.Mapping.GetValueOrDefault(field.Key)));
            }

            Preview = result.Data;
            Step = ImportStep.Columns;
        });
    }

    // Checks the columns again with the chosen mapping and shows the rows that would fail.
    [RelayCommand]
    private async Task ShowPreviewAsync()
    {
        var missing = Mapping.Where(m => m.Field.Required && m.SelectedHeader == FieldMappingRow.NotInFile).Select(m => m.Field.Label).ToList();
        if (missing.Count > 0)
        {
            ErrorMessage = $"Choose the column for: {string.Join(", ", missing)}.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _api.PreviewAsync(Kind, new MemoryStream(_content), FileName, CurrentMapping());
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = ApiFailures.Describe(result);
                return;
            }

            Preview = result.Data;
            Errors.Clear();
            foreach (var error in result.Data.Errors)
            {
                Errors.Add(error);
            }

            SkipInvalid = false;
            Step = ImportStep.Preview;
        });
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!CanApply || Preview is null)
        {
            return;
        }

        var count = Preview.ValidRows;
        if (!await _dialogs.ConfirmAsync("Import now?", $"{count} row(s) will be imported in one step. You can undo this import afterwards.", "Import"))
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _api.ApplyAsync(Kind, new MemoryStream(_content), FileName, CurrentMapping(), SkipInvalid);
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = ApiFailures.Describe(result);
                return;
            }

            Result = result.Data;
            Step = ImportStep.Done;
            await LoadLastAsync();
            _notifications.Success(ResultText);
        });
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (Last is null || !await _dialogs.ConfirmAsync("Undo the last import?", $"Everything imported from {Last.FileName} is removed or put back as it was.", "Undo import", destructive: true))
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _api.UndoAsync();
            if (!result.Success)
            {
                ErrorMessage = ApiFailures.Describe(result);
                return;
            }

            _notifications.Success("The last import was undone.");
            await LoadLastAsync();
        });
    }

    [RelayCommand]
    private void Back()
    {
        ErrorMessage = null;
        Step = Step switch
        {
            ImportStep.Preview => ImportStep.Columns,
            ImportStep.Columns => ImportStep.File,
            _ => ImportStep.File,
        };
    }

    [RelayCommand]
    private void StartOver()
    {
        ErrorMessage = null;
        _content = Array.Empty<byte>();
        FileName = string.Empty;
        Preview = null;
        Result = null;
        Mapping.Clear();
        Errors.Clear();
        Step = ImportStep.File;
    }

    partial void OnLastChanged(ImportBatchDto? value) => UndoCommand.NotifyCanExecuteChanged();

    private Dictionary<string, string> CurrentMapping() =>
        Mapping.Where(m => m.SelectedHeader != FieldMappingRow.NotInFile).ToDictionary(m => m.Field.Key, m => m.SelectedHeader);
}

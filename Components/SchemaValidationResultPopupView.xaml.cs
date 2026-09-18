using System.Windows.Input;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Components;

public partial class SchemaValidationResultPopupView : ContentView
{
    public static readonly BindableProperty ValidationResultProperty =
        BindableProperty.Create(nameof(ValidationResult), typeof(SchemaValidationResult), typeof(SchemaValidationResultPopupView));

    public static readonly BindableProperty CloseCommandProperty =
        BindableProperty.Create(nameof(CloseCommand), typeof(ICommand), typeof(SchemaValidationResultPopupView));

    public static readonly BindableProperty ExportCommandProperty =
        BindableProperty.Create(nameof(ExportCommand), typeof(ICommand), typeof(SchemaValidationResultPopupView));

    public static readonly BindableProperty FixSchemaErrorsCommandProperty =
        BindableProperty.Create(nameof(FixSchemaErrorsCommand), typeof(ICommand), typeof(SchemaValidationResultPopupView));

    public static readonly BindableProperty RefreshCommandProperty =
        BindableProperty.Create(nameof(RefreshCommand), typeof(ICommand), typeof(SchemaValidationResultPopupView));

    public static readonly BindableProperty HasFixableIssuesProperty =
        BindableProperty.Create(nameof(HasFixableIssues), typeof(bool), typeof(SchemaValidationResultPopupView), false);

    public SchemaValidationResult ValidationResult
    {
        get => (SchemaValidationResult)GetValue(ValidationResultProperty);
        set => SetValue(ValidationResultProperty, value);
    }

    public ICommand CloseCommand
    {
        get => (ICommand)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public ICommand ExportCommand
    {
        get => (ICommand)GetValue(ExportCommandProperty);
        set => SetValue(ExportCommandProperty, value);
    }

    public ICommand FixSchemaErrorsCommand
    {
        get => (ICommand)GetValue(FixSchemaErrorsCommandProperty);
        set => SetValue(FixSchemaErrorsCommandProperty, value);
    }

    public ICommand RefreshCommand
    {
        get => (ICommand)GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    public bool HasFixableIssues
    {
        get => (bool)GetValue(HasFixableIssuesProperty);
        set => SetValue(HasFixableIssuesProperty, value);
    }

    public SchemaValidationResultPopupView()
    {
        InitializeComponent();
    }
}

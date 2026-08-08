using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Sati.ViewModels;
using Sati.ViewModels.ClientDocuments;
using System.ComponentModel;

namespace Sati.Views.ClientDocuments
{
    public partial class ComprehensiveAssessmentWorkspace : UserControl
    {
        private NewClientViewModel? _parent;
        public ComprehensiveAssessmentViewModel Workspace { get; }

        public ComprehensiveAssessmentWorkspace()
        {
            var app = (App)System.Windows.Application.Current;
            Workspace = new ComprehensiveAssessmentViewModel(
                app.Services.GetRequiredService<Data.IComprehensiveAssessmentService>(),
                app.Services.GetRequiredService<Data.ISessionService>());
            InitializeComponent();
            DataContextChanged += OnParentDataContextChanged;
            Unloaded += async (_, _) => await Workspace.LoadPersonAsync(null);
        }

        private void OnParentDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (_parent is not null) _parent.PropertyChanged -= OnParentPropertyChanged;
            _parent = e.NewValue as NewClientViewModel;
            if (_parent is not null) _parent.PropertyChanged += OnParentPropertyChanged;
            _ = Workspace.LoadPersonAsync(_parent?.SelectedPerson);
        }

        private void OnParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NewClientViewModel.SelectedPerson))
                _ = Workspace.LoadPersonAsync(_parent?.SelectedPerson);
        }
    }
}

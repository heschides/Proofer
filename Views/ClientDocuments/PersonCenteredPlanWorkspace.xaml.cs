using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Sati.ViewModels;
using Sati.ViewModels.ClientDocuments;
using System.ComponentModel;

namespace Sati.Views.ClientDocuments
{
    public partial class PersonCenteredPlanWorkspace : UserControl
    {
        private NewClientViewModel? _parent;
        public PersonCenteredPlanViewModel Workspace { get; }

        public PersonCenteredPlanWorkspace()
        {
            var app = (App)System.Windows.Application.Current;
            Workspace = new PersonCenteredPlanViewModel(
                app.Services.GetRequiredService<Data.IPersonCenteredPlanSourceService>(),
                app.Services.GetRequiredService<Data.ISessionService>());
            InitializeComponent();
            DataContextChanged += OnParentDataContextChanged;
            IsVisibleChanged += async (_, _) =>
            {
                if (IsVisible)
                    await Workspace.LoadPersonAsync(_parent?.SelectedPerson);
            };
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

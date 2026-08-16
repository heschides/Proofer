using System.Windows;

namespace Sati.Views
{
    /// <summary>
    /// Marks a navigation button as the currently selected one.
    ///
    /// WHY AN ATTACHED PROPERTY. Each tab's selected state comes from a
    /// DIFFERENT view model property — <c>IsBillingActive</c>, <c>IsAdminActive</c>,
    /// and so on — and a WPF Style cannot vary the binding path of its own
    /// triggers. Before this existed, that forced every tab to carry its own
    /// inline <c>Style</c> with its own <c>DataTrigger</c>, which is why changing
    /// how selection looks used to mean editing five (and now nine) places.
    ///
    /// With the state hoisted onto an attached property, each tab binds once
    /// (<c>views:NavTab.IsActive="{Binding IsBillingActive}"</c>) and a single
    /// shared Style in App.xaml owns what selection LOOKS like.
    ///
    /// A ToggleButton or RadioButton would have been the more obvious control,
    /// but both write to their own IsChecked when clicked, and assigning a local
    /// value to a property that carries a OneWay binding replaces that binding.
    /// The view model's Is*Active properties are computed getters over
    /// CurrentViewModel with no setter to bind two-way against, so a control that
    /// mutates its own selected state would fight them. Nothing writes to this
    /// property except the binding.
    /// </summary>
    public static class NavTab
    {
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.RegisterAttached(
                "IsActive",
                typeof(bool),
                typeof(NavTab),
                new PropertyMetadata(false));

        public static bool GetIsActive(DependencyObject element)
        {
            ArgumentNullException.ThrowIfNull(element);
            return (bool)element.GetValue(IsActiveProperty);
        }

        public static void SetIsActive(DependencyObject element, bool value)
        {
            ArgumentNullException.ThrowIfNull(element);
            element.SetValue(IsActiveProperty, value);
        }
    }
}

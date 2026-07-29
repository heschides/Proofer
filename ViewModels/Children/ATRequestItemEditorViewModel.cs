using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models;

namespace Sati.ViewModels.Children
{
    // Observable editing wrapper for one ATRequestItem grid row.
    // BUILD STATE: stub for 1b — full item-grid behavior (write-through
    // ItemCost/Quantity, live LineTotal, parent total notification) lands in 1c.
    public partial class ATRequestItemEditorViewModel : ObservableObject
    {
        private readonly ATRequestItem _item;

        public ATRequestItemEditorViewModel(ATRequestItem item)
        {
            _item = item;
        }

        public ATRequestItem Item => _item;
    }
}
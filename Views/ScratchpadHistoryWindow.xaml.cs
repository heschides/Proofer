using Sati.Data;
using System.Windows;


namespace Sati
{
    public partial class ScratchpadHistoryWindow : Window
    {
        public ScratchpadHistoryWindow(ScratchpadHistoryViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
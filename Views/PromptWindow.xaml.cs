using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Sati.Views
{
    /// <summary>
    /// Interaction logic for PromptWindow.xaml
    /// </summary>
    public partial class PromptWindow : Window
    {
        public string GreetingText { get; }

        public PromptWindow(string displayName)
        {
            InitializeComponent();
            GreetingText = $"Hello, {displayName}!";
            DataContext = this;
            YesButton.Click += (s, e) => { DialogResult = true; Close(); };
            NoButton.Click += (s, e) => { DialogResult = false; Close(); };
        }
    }
}

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class DatePickerTemplateTests
{
    [Fact]
    public void GlobalDatePickerShowsACenteredClickableCalendarButton()
    {
        WpfUiHarness.Run(() =>
        {
            var picker = new DatePicker
            {
                Width = 220,
                SelectedDate = new DateTime(2026, 8, 26),
                Style = Assert.IsType<Style>(Application.Current.FindResource(typeof(DatePicker)))
            };

            WpfUiHarness.Realize(picker);
            picker.ApplyTemplate();

            var button = Assert.IsType<Button>(picker.Template.FindName("PART_Button", picker));
            Assert.True(button.ActualWidth >= 32);
            Assert.True(button.ActualHeight >= 32);
            Assert.True(button.IsHitTestVisible);
            Assert.Equal(VerticalAlignment.Stretch, button.VerticalAlignment);
            Assert.Equal("Open calendar", AutomationProperties.GetName(button));

            var glyph = Assert.IsType<TextBlock>(button.Content);
            Assert.Equal(VerticalAlignment.Center, glyph.VerticalAlignment);
            Assert.Equal(HorizontalAlignment.Center, glyph.HorizontalAlignment);
            Assert.False(string.IsNullOrWhiteSpace(glyph.Text));

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(picker.IsDropDownOpen);
        });
    }
}

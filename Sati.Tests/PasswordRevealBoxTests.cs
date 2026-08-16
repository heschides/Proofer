using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Sati.Views;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The reveal toggle on the password fields.
///
/// The risk this covers is not the eye icon; it is the two-box sync underneath.
/// WPF cannot show a password in plain text, so the control overlays a TextBox on
/// a PasswordBox and swaps which one is visible. If the copy between them is
/// wrong in either direction, a user types a correct password, sees it on screen,
/// and is told their credentials are invalid — with no way to tell why.
///
/// Every test runs on an STA thread because WPF controls cannot be constructed
/// anywhere else.
/// </summary>
public sealed class PasswordRevealBoxTests
{
    [Fact]
    public void TypingWhileMaskedReachesSecurePassword() => OnStaThread(box =>
    {
        Masked(box).Password = "Correct-Horse-42!";

        Assert.Equal("Correct-Horse-42!", Plain(box.SecurePassword));
    });

    [Fact]
    public void RevealingShowsWhatWasAlreadyTyped() => OnStaThread(box =>
    {
        Masked(box).Password = "Correct-Horse-42!";

        Toggle(box).IsChecked = true;

        Assert.True(box.IsRevealed);
        Assert.Equal("Correct-Horse-42!", Revealed(box).Text);
    });

    [Fact]
    public void TypingWhileRevealedStillReachesSecurePassword() => OnStaThread(box =>
    {
        // The failure this guards: characters typed into the visible box never
        // reaching the credential that is actually submitted.
        Toggle(box).IsChecked = true;

        Revealed(box).Text = "typed-in-the-open";

        Assert.Equal("typed-in-the-open", Plain(box.SecurePassword));
    });

    [Fact]
    public void HidingKeepsThePasswordAndDropsThePlainTextCopy() => OnStaThread(box =>
    {
        Toggle(box).IsChecked = true;
        Revealed(box).Text = "typed-in-the-open";

        Toggle(box).IsChecked = false;

        Assert.False(box.IsRevealed);
        Assert.Equal("typed-in-the-open", Plain(box.SecurePassword));
        // The visible copy must not linger behind a Collapsed flag.
        Assert.Empty(Revealed(box).Text);
    });

    [Fact]
    public void TogglingRepeatedlyDoesNotCorruptOrDuplicateThePassword() => OnStaThread(box =>
    {
        // A sync bug that appends rather than replaces shows up here and nowhere
        // else.
        Masked(box).Password = "Correct-Horse-42!";

        for (var i = 0; i < 5; i++)
        {
            Toggle(box).IsChecked = true;
            Toggle(box).IsChecked = false;
        }

        Assert.Equal("Correct-Horse-42!", Plain(box.SecurePassword));
    });

    [Fact]
    public void PasswordChangedFiresForBothBoxes() => OnStaThread(box =>
    {
        var fired = 0;
        box.PasswordChanged += (_, _) => fired++;

        Masked(box).Password = "first";
        var afterMasked = fired;

        Toggle(box).IsChecked = true;
        Revealed(box).Text = "second";

        Assert.True(afterMasked > 0, "Typing while masked did not raise PasswordChanged.");
        Assert.True(fired > afterMasked, "Typing while revealed did not raise PasswordChanged.");
    });

    [Fact]
    public void ClearingEmptiesBothBoxesAndReMasks() => OnStaThread(box =>
    {
        // Clear() runs after a failed sign-in. The next person to type must not
        // find the field already showing their characters.
        Toggle(box).IsChecked = true;
        Revealed(box).Text = "typed-in-the-open";

        box.Clear();

        Assert.False(box.IsRevealed);
        Assert.Empty(Plain(box.SecurePassword));
        Assert.Empty(Revealed(box).Text);
        Assert.Empty(Masked(box).Password);
    });

    [Fact]
    public void OnlyOneEntryBoxIsEverVisible() => OnStaThread(box =>
    {
        Assert.Equal(System.Windows.Visibility.Visible, Masked(box).Visibility);
        Assert.Equal(System.Windows.Visibility.Collapsed, Revealed(box).Visibility);

        Toggle(box).IsChecked = true;

        Assert.Equal(System.Windows.Visibility.Collapsed, Masked(box).Visibility);
        Assert.Equal(System.Windows.Visibility.Visible, Revealed(box).Visibility);
    });

    [Fact]
    public void TheToggleTellsAScreenReaderWhatItWillDo() => OnStaThread(box =>
    {
        // The label has to describe the ACTION, and it changes meaning each press.
        // A static "Show password" would be a lie half the time.
        Assert.Equal("Show password", AutomationProperties.GetName(Toggle(box)));

        Toggle(box).IsChecked = true;

        Assert.Equal("Hide password", AutomationProperties.GetName(Toggle(box)));
    });

    // ---- Harness ----

    private static PasswordBox Masked(PasswordRevealBox box) => Find<PasswordBox>(box, "Masked");
    private static TextBox Revealed(PasswordRevealBox box) => Find<TextBox>(box, "Revealed");
    private static ToggleButton Toggle(PasswordRevealBox box) => Find<ToggleButton>(box, "RevealToggle");

    private static T Find<T>(PasswordRevealBox box, string name) where T : class =>
        box.FindName(name) as T
        ?? throw new InvalidOperationException($"{name} was not found in the control template.");

    // The control stores the password in a SecureString by design; reading it
    // back is a test-only concession so the assertions can be about the value the
    // sign-in path would actually receive.
    private static string Plain(SecureString value)
    {
        if (value.Length == 0)
            return string.Empty;

        var pointer = IntPtr.Zero;
        try
        {
            pointer = Marshal.SecureStringToGlobalAllocUnicode(value);
            return Marshal.PtrToStringUni(pointer) ?? string.Empty;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
                Marshal.ZeroFreeGlobalAllocUnicode(pointer);
        }
    }

    private static void OnStaThread(Action<PasswordRevealBox> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var box = new PasswordRevealBox();
                // Realise the template so the named parts exist.
                box.Measure(new System.Windows.Size(400, 40));
                box.Arrange(new System.Windows.Rect(0, 0, 400, 40));
                body(box);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "PasswordRevealBox test"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The STA test thread did not finish.");
        if (failure is not null)
            throw failure;
    }
}

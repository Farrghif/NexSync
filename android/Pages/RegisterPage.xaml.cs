using NexSync.Android.Services;

namespace NexSync.Android.Pages;

public partial class RegisterPage : ContentPage
{
    private readonly AuthService _auth;

    public RegisterPage(AuthService auth)
    {
        InitializeComponent();
        _auth = auth;
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim();
        var email = EmailEntry.Text?.Trim();
        var password = PasswordEntry.Text;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ShowError("All fields are required.");
            return;
        }

        if (password.Length < 8)
        {
            ShowError("Password must be at least 8 characters.");
            return;
        }

        SetBusy(true);
        try
        {
            await _auth.RegisterAsync(email, password, name);
            await Shell.Current.GoToAsync("//browser");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnGoToSignInClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private void OnTogglePasswordClicked(object sender, EventArgs e)
    {
        PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
        TogglePasswordBtn.Text = PasswordEntry.IsPassword ? "Show" : "Hide";
    }

    private void SetBusy(bool busy)
    {
        RegisterBtn.IsEnabled = !busy;
        RegisterBtn.Opacity = busy ? 0.7 : 1;
        RegisterBtn.Text = busy ? "" : "Create Account";
        BusyIndicator.IsRunning = busy;
        BusyIndicator.IsVisible = busy;
        NameEntry.IsEnabled = !busy;
        EmailEntry.IsEnabled = !busy;
        PasswordEntry.IsEnabled = !busy;
        TogglePasswordBtn.IsEnabled = !busy;
    }

    private void ShowError(string msg)
    {
        ErrorLabel.Text = msg;
        var show = !string.IsNullOrWhiteSpace(msg);
        ErrorLabel.IsVisible = show;
        if (ErrorFrame != null) ErrorFrame.IsVisible = show;
    }
}

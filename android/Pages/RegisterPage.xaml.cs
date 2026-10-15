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

    private void SetBusy(bool busy)
    {
        RegisterBtn.IsEnabled = !busy;
        BusyIndicator.IsRunning = busy;
        BusyIndicator.IsVisible = busy;
    }

    private void ShowError(string msg)
    {
        ErrorLabel.Text = msg;
        ErrorLabel.IsVisible = !string.IsNullOrWhiteSpace(msg);
    }
}

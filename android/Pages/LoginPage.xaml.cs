using NexSync.Android.Services;

namespace NexSync.Android.Pages;

public partial class LoginPage : ContentPage
{
    private readonly AuthService _auth;

    public LoginPage(AuthService auth)
    {
        InitializeComponent();
        _auth = auth;
        ServerUrlEntry.Text = ApiConfig.BaseUrl;
    }

    private async void OnSignInClicked(object sender, EventArgs e)
    {
        var email = EmailEntry.Text?.Trim();
        var password = PasswordEntry.Text;
        var server = ServerUrlEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ShowError("Please enter email and password.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(server))
            ApiConfig.BaseUrl = server;

        SetBusy(true);
        try
        {
            await _auth.LoginAsync(email, password);
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

    private async void OnGoToRegisterClicked(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new RegisterPage(_auth));
    }

    private void SetBusy(bool busy)
    {
        SignInBtn.IsEnabled = !busy;
        BusyIndicator.IsRunning = busy;
        BusyIndicator.IsVisible = busy;
    }

    private void ShowError(string msg)
    {
        ErrorLabel.Text = msg;
        ErrorLabel.IsVisible = !string.IsNullOrWhiteSpace(msg);
    }
}

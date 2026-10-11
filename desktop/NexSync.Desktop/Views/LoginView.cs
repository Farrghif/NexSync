using System;
using System.Net.Http;
using System.Windows.Controls;
using NexSync.Desktop.Auth;

namespace NexSync.Desktop.Views;

public sealed class LoginView : System.Windows.Controls.UserControl
{
    public LoginView(AppState state, DesktopAuthService auth)
    {
        var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(24) };
        panel.Children.Add(new System.Windows.Controls.Label { Content = "NexSync — Sign in" });
        var email = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(0, 4, 0, 4) };
        var password = new System.Windows.Controls.PasswordBox { Margin = new System.Windows.Thickness(0, 4, 0, 4) };
        var error = new System.Windows.Controls.TextBlock { TextWrapping = System.Windows.TextWrapping.Wrap };
        var button = new System.Windows.Controls.Button { Content = "Sign in", Margin = new System.Windows.Thickness(0, 8, 0, 0) };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            error.Text = string.Empty;
            try
            {
                await auth.LoginAsync(email.Text.Trim(), password.Password);
                await auth.EnsureDeviceRegisteredAsync(Environment.MachineName + " (Windows)");
                state.GoTo(AppScreen.Setup);
            }
            catch (UnauthorizedException ex)
            {
                error.Text = ex.Message;
            }
            catch (HttpRequestException ex)
            {
                error.Text = "Cannot reach the NexSync server: " + ex.Message;
            }
            catch (Exception ex)
            {
                error.Text = "Sign-in failed: " + ex.Message;
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        panel.Children.Add(new System.Windows.Controls.Label { Content = "Email" });
        panel.Children.Add(email);
        panel.Children.Add(new System.Windows.Controls.Label { Content = "Password" });
        panel.Children.Add(password);
        panel.Children.Add(error);
        panel.Children.Add(button);
        Content = panel;
    }
}

using System;
using System.Windows.Controls;

namespace NexSync.Desktop.Views;

public sealed class LoginView : System.Windows.Controls.UserControl
{
    public LoginView(AppState state)
    {
        var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(24) };
        panel.Children.Add(new System.Windows.Controls.Label { Content = "NexSync — Sign in" });
        var email = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(0, 4, 0, 4) };
        var password = new System.Windows.Controls.PasswordBox { Margin = new System.Windows.Thickness(0, 4, 0, 4) };
        var error = new System.Windows.Controls.TextBlock();
        var button = new System.Windows.Controls.Button { Content = "Sign in (stub — Task 10)", Margin = new System.Windows.Thickness(0, 8, 0, 0) };
        button.Click += (_, _) => state.GoTo(AppScreen.Setup);
        panel.Children.Add(new System.Windows.Controls.Label { Content = "Email" });
        panel.Children.Add(email);
        panel.Children.Add(new System.Windows.Controls.Label { Content = "Password" });
        panel.Children.Add(password);
        panel.Children.Add(error);
        panel.Children.Add(button);
        Content = panel;
    }
}



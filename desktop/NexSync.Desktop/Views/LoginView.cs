using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NexSync.Desktop.Auth;

namespace NexSync.Desktop.Views;

public sealed class LoginView : UserControl
{
    private bool _busy;
    private static SolidColorBrush Br(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static readonly SolidColorBrush BgLight = Br("#F6F7FB");
    private static readonly SolidColorBrush BgWhite = Br("#FFFFFF");
    private static readonly SolidColorBrush BorderLight = Br("#E6E8EF");
    private static readonly SolidColorBrush BorderInput = Br("#D5D8E3");
    private static readonly SolidColorBrush Brand = Br("#5B5BD6");
    private static readonly SolidColorBrush TextDark = Br("#14161D");
    private static readonly SolidColorBrush TextMuted = Br("#8B90A0");
    private static readonly SolidColorBrush Danger = Br("#C62B3C");

    public LoginView(AppState state, DesktopAuthService auth)
    {
        Background = BgLight;
        FontFamily = new FontFamily("Segoe UI");

        var outer = new Grid { Background = BgLight };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var card = new Border
        {
            Background = BgWhite,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(28),
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24)
        };
        // subtle shadow via effect would need extra dep; skip for code-behind simplicity

        var stack = new StackPanel();

        // badge
        var badge = new Border
        {
            Width = 44, Height = 44,
            Background = Brand,
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        badge.Child = new TextBlock
        {
            Text = "☁",
            Foreground = Brushes.White,
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -2, 0, 0)
        };
        stack.Children.Add(badge);

        stack.Children.Add(new TextBlock
        {
            Text = "NexSync",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = TextDark,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Private sync between Windows and browser.",
            FontSize = 12,
            Foreground = TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 22)
        });

        // Email label
        stack.Children.Add(new TextBlock
        {
            Text = "Email",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMuted,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var emailBorder = new Border
        {
            BorderBrush = BorderInput,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Background = BgWhite,
            Padding = new Thickness(2),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var email = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 12,
            Foreground = TextDark
        };
        email.GotFocus += (_, _) => { emailBorder.BorderBrush = Brand; emailBorder.BorderThickness = new Thickness(1.4); };
        email.LostFocus += (_, _) => { emailBorder.BorderBrush = BorderInput; emailBorder.BorderThickness = new Thickness(1); };
        emailBorder.Child = email;
        stack.Children.Add(emailBorder);

        // Password label
        stack.Children.Add(new TextBlock
        {
            Text = "Password",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMuted,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var passBorder = new Border
        {
            BorderBrush = BorderInput,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Background = BgWhite,
            Padding = new Thickness(2),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var passGrid = new Grid();
        var password = new PasswordBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 12,
            Foreground = TextDark,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var passwordVisible = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = 12,
            Foreground = TextDark,
            Visibility = Visibility.Collapsed,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        // keep them in sync
        password.PasswordChanged += (_, _) =>
        {
            if (passwordVisible.Visibility != Visibility.Visible)
                return;
            if (passwordVisible.Text != password.Password)
                passwordVisible.Text = password.Password;
        };
        passwordVisible.TextChanged += (_, _) =>
        {
            if (passwordVisible.Visibility == Visibility.Visible && password.Password != passwordVisible.Text)
                password.Password = passwordVisible.Text;
        };
        passGrid.Children.Add(password);
        passGrid.Children.Add(passwordVisible);
        password.GotFocus += (_, _) => { passBorder.BorderBrush = Brand; passBorder.BorderThickness = new Thickness(1.4); };
        password.LostFocus += (_, _) => { if (!passwordVisible.IsFocused) { passBorder.BorderBrush = BorderInput; passBorder.BorderThickness = new Thickness(1); } };
        passwordVisible.GotFocus += (_, _) => { passBorder.BorderBrush = Brand; passBorder.BorderThickness = new Thickness(1.4); };
        passwordVisible.LostFocus += (_, _) => { if (!password.IsFocused) { passBorder.BorderBrush = BorderInput; passBorder.BorderThickness = new Thickness(1); } };
        passBorder.Child = passGrid;
        stack.Children.Add(passBorder);

        var showCheck = new CheckBox
        {
            Content = "Show password",
            FontSize = 11,
            Foreground = TextMuted,
            Margin = new Thickness(2, 0, 0, 14),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        showCheck.Checked += (_, _) =>
        {
            passwordVisible.Text = password.Password;
            password.Visibility = Visibility.Collapsed;
            passwordVisible.Visibility = Visibility.Visible;
            passwordVisible.Focus();
            passwordVisible.SelectAll();
        };
        showCheck.Unchecked += (_, _) =>
        {
            password.Password = passwordVisible.Text;
            passwordVisible.Visibility = Visibility.Collapsed;
            password.Visibility = Visibility.Visible;
            password.Focus();
        };
        stack.Children.Add(showCheck);

        var error = new TextBlock
        {
            Foreground = Danger,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 4),
            MinHeight = 0
        };
        void SetError(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg))
            {
                error.Text = string.Empty;
                error.Visibility = Visibility.Collapsed;
            }
            else
            {
                error.Text = msg;
                error.Visibility = Visibility.Visible;
            }
        }
        stack.Children.Add(error);

        var btnBorder = new Border
        {
            Background = Brand,
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 10, 0, 0),
            Height = 44
        };
        var button = new Button
        {
            Content = "Sign in",
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        // make button fill border
        btnBorder.Child = button;
        stack.Children.Add(btnBorder);

        // footer hint
        var footer = new TextBlock
        {
            FontSize = 11,
            Foreground = TextMuted,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0)
        };
        footer.Inlines.Add(new System.Windows.Documents.Run("Don't have an account? "));
        footer.Inlines.Add(new System.Windows.Documents.Run("Create one in the browser at ") { Foreground = TextMuted });
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("http://localhost:5173/register"))
        {
            Foreground = Brand,
            TextDecorations = null
        };
        // hyperlink navigate would need RequestNavigate; just show as styled text for simplicity
        link.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("http://localhost:5173/register") { UseShellExecute = true }); } catch { }
        };
        footer.Inlines.Add(link);
        footer.Inlines.Add(new System.Windows.Documents.Run("."));
        stack.Children.Add(footer);

        card.Child = stack;
        scroll.Content = card;
        outer.Children.Add(scroll);
        Content = outer;

        // focus first field
        Loaded += (_, _) => email.Focus();

        async void DoLogin()
        {
            if (_busy) return;
            _busy = true;
            SetError(string.Empty);
            button.IsEnabled = false;
            btnBorder.Opacity = 0.6;
            button.Content = "Signing in…";
            try
            {
                var pwd = passwordVisible.Visibility == Visibility.Visible ? passwordVisible.Text : password.Password;
                await auth.LoginAsync(email.Text.Trim(), pwd);
                await auth.EnsureDeviceRegisteredAsync(Environment.MachineName + " (Windows)");
                state.GoTo(AppScreen.Setup);
            }
            catch (UnauthorizedException ex)
            {
                SetError(ex.Message);
            }
            catch (HttpRequestException ex)
            {
                SetError("Cannot reach the NexSync server: " + ex.Message);
            }
            catch (Exception ex)
            {
                SetError("Sign-in failed: " + ex.Message);
            }
            finally
            {
                _busy = false;
                button.IsEnabled = true;
                btnBorder.Opacity = 1;
                button.Content = "Sign in";
            }
        }

        button.Click += (_, _) => DoLogin();
        // Enter submits
        email.KeyDown += (_, e) => { if (e.Key == Key.Enter) { DoLogin(); e.Handled = true; } };
        password.KeyDown += (_, e) => { if (e.Key == Key.Enter) { DoLogin(); e.Handled = true; } };
        passwordVisible.KeyDown += (_, e) => { if (e.Key == Key.Enter) { DoLogin(); e.Handled = true; } };
    }
}

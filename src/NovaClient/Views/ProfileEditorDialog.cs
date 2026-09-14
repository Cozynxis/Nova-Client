using System.Windows;
using System.Windows.Controls;
using NovaClient.Models;

namespace NovaClient.Views;

public sealed class ProfileEditorDialog : Window
{
    private readonly TextBox _name = new();
    private readonly TextBox _description = new();
    private readonly ComboBox _version = new();
    private readonly ComboBox _loader = new();
    private readonly Slider _memory = new() { Minimum = 2, Maximum = 24, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBlock _memoryText = new();
    private readonly TextBox _icon = new();
    private readonly TextBox _server = new();
    private readonly CheckBox _favorite = new() { Content = "Show in Jump in" };
    private readonly CheckBox _autoUpdate = new() { Content = "Suggest mod updates" };

    public Profile Result { get; }

    public ProfileEditorDialog(Profile profile, bool creating)
    {
        Result = profile;
        Owner = Application.Current.MainWindow;
        Title = creating ? "New instance — Nova Client" : "Edit instance — Nova Client";
        Width = 620;
        Height = 700;
        MinWidth = 560;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("BgBrush");
        Foreground = System.Windows.Media.Brushes.White;

        var root = new Grid { Margin = new Thickness(26) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new TextBlock { Text = creating ? "Create instance" : "Instance settings", FontSize = 27, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Give every Minecraft setup its own version, loader and memory.", Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("MutedBrush"), Margin = new Thickness(0, 4, 0, 0) });
        root.Children.Add(header);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        var form = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        scroll.Content = form;

        _name.Text = profile.Name;
        AddField(form, "Name", _name);
        _description.Text = profile.Description;
        AddField(form, "Description", _description);

        var twoColumns = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        twoColumns.ColumnDefinitions.Add(new ColumnDefinition());
        twoColumns.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { Margin = new Thickness(0, 0, 7, 0) };
        left.Children.Add(new TextBlock { Text = "Minecraft version", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        _version.IsEditable = true;
        foreach (var item in new[] { "1.21.1", "1.21", "1.20.6", "1.20.4", "1.20.1", "1.19.4", "1.19.2", "1.18.2" }) _version.Items.Add(item);
        _version.Text = profile.MinecraftVersion;
        left.Children.Add(_version);
        twoColumns.Children.Add(left);
        var right = new StackPanel { Margin = new Thickness(7, 0, 0, 0) };
        right.Children.Add(new TextBlock { Text = "Loader", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var item in new[] { "Fabric", "NeoForge", "Forge", "Quilt", "Vanilla" }) _loader.Items.Add(item);
        _loader.SelectedItem = profile.Loader;
        if (_loader.SelectedIndex < 0) _loader.SelectedIndex = 0;
        right.Children.Add(_loader);
        Grid.SetColumn(right, 1);
        twoColumns.Children.Add(right);
        form.Children.Add(twoColumns);

        var memoryHeader = new Grid();
        memoryHeader.Children.Add(new TextBlock { Text = "Memory", FontWeight = FontWeights.SemiBold });
        _memoryText.HorizontalAlignment = HorizontalAlignment.Right;
        _memoryText.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("AccentBrush");
        memoryHeader.Children.Add(_memoryText);
        form.Children.Add(memoryHeader);
        _memory.Value = Math.Clamp(profile.MemoryMb / 1024d, 2, 24);
        _memoryText.Text = $"{(int)_memory.Value} GB";
        _memory.ValueChanged += (_, _) => _memoryText.Text = $"{(int)_memory.Value} GB";
        _memory.Margin = new Thickness(0, 7, 0, 18);
        form.Children.Add(_memory);

        _icon.Text = profile.Icon;
        AddField(form, "Icon / emoji", _icon);
        _server.Text = profile.ServerAddress ?? string.Empty;
        AddField(form, "Quick-connect server (optional)", _server);
        _favorite.IsChecked = profile.Favorite;
        _autoUpdate.IsChecked = profile.AutoUpdateMods;
        form.Children.Add(_favorite);
        form.Children.Add(_autoUpdate);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 95, Style = Application.Current.FindResource("SecondaryButton") as Style, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var save = new Button { Content = creating ? "Create instance" : "Save changes", MinWidth = 120, Style = Application.Current.FindResource("PrimaryButton") as Style };
        save.Click += Save_Click;
        footer.Children.Add(cancel);
        footer.Children.Add(save);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
    }

    private static void AddField(Panel panel, string label, Control control)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        stack.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        stack.Children.Add(control);
        panel.Children.Add(stack);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_version.Text))
        {
            MessageBox.Show(this, "Enter an instance name and Minecraft version.", "Nova Client", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Result.Name = _name.Text.Trim();
        Result.Description = _description.Text.Trim();
        Result.MinecraftVersion = _version.Text.Trim();
        Result.Loader = _loader.SelectedItem?.ToString() ?? "Fabric";
        Result.MemoryMb = (int)_memory.Value * 1024;
        Result.Icon = string.IsNullOrWhiteSpace(_icon.Text) ? "⛏" : _icon.Text.Trim();
        Result.ServerAddress = string.IsNullOrWhiteSpace(_server.Text) ? null : _server.Text.Trim();
        Result.Favorite = _favorite.IsChecked == true;
        Result.AutoUpdateMods = _autoUpdate.IsChecked == true;
        Result.Accent = Result.Loader.ToLowerInvariant() switch { "fabric" => "#DBB86B", "forge" => "#5B84C7", "neoforge" => "#E26E44", "quilt" => "#A98CE8", "vanilla" => "#58B86A", _ => "#20DC72" };
        DialogResult = true;
    }
}

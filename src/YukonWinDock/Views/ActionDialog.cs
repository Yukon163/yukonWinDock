using System.Windows;
using System.Windows.Controls;
using YukonWinDock.Core;
using YukonWinDock.Services;

namespace YukonWinDock.Views;

public sealed class ActionDialog : Window
{
    public QuickAction? Result { get; private set; }
    public ActionDialog()
    {
        Title = "添加快捷操作"; Width = 420; Height = 300; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 36, 41));
        var body = new StackPanel { Margin = new Thickness(22) };
        body.Children.Add(new TextBlock { Text = "操作名称", Margin = new Thickness(0, 0, 0, 6) });
        var name = new TextBox { MaxLength = 60 }; body.Children.Add(name);
        body.Children.Add(new TextBlock { Text = "程序 / 文件夹路径 / http(s) 网址", Margin = new Thickness(0, 14, 0, 6) });
        var target = new TextBox(); body.Children.Add(target);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Salmon, Margin = new Thickness(0, 8, 0, 8) };
        body.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var browse = new Button { Content = "选择文件", Margin = new Thickness(0, 0, 8, 0) };
        browse.Click += (_, _) => { var picker = new Microsoft.Win32.OpenFileDialog { Title = "选择快捷操作目标", CheckFileExists = true }; if (picker.ShowDialog(this) == true) { target.Text = picker.FileName; if (name.Text.Length == 0) name.Text = Path.GetFileNameWithoutExtension(picker.FileName); } };
        var add = new Button { Content = "添加", IsDefault = true };
        add.Click += (_, _) =>
        {
            string? validation = QuickActions.Validate(target.Text.Trim());
            if (string.IsNullOrWhiteSpace(name.Text)) { error.Text = "请填写操作名称。"; return; }
            if (validation != null) { error.Text = validation; return; }
            Result = new QuickAction { Name = name.Text.Trim(), Target = target.Text.Trim() }; DialogResult = true;
        };
        buttons.Children.Add(browse); buttons.Children.Add(add); body.Children.Add(buttons); Content = body;
    }
}

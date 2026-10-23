using System.Diagnostics;
using YukonWinDock.Core;

namespace YukonWinDock.Services;

public static class QuickActions
{
    public static string? Validate(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return "请输入程序、文件夹路径或 http/https 网址。";
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return null;
        if (Path.IsPathFullyQualified(target) && (File.Exists(target) || Directory.Exists(target))) return null;
        return "路径不存在，或网址不是 http/https 地址。";
    }
    public static void Run(QuickAction action)
    {
        var error = Validate(action.Target);
        if (error != null) throw new InvalidOperationException(error);
        Process.Start(new ProcessStartInfo(action.Target) { UseShellExecute = true });
    }
}
